using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace you5248.LODGenerator
{
    /// <summary>
    /// Core logic: mesh collection, simplification, LODGroup construction, asset saving.
    /// </summary>
    public static class LODGeneratorCore
    {
        // 間引き後のLOD生成結果（採用メッシュ＋遷移高さ）
        private struct LodBuildResult
        {
            public Mesh[] meshes;
            public float[] transitions;
            public Material[][] materials;   // per-kept-level override (null = use original materials; billboard levels set this)
        }

        // 同一メッシュの重複簡略化を防ぐキャッシュ（バッチ実行中のみ有効）
                    private static Dictionary<Mesh, LodBuildResult> _meshCache;

        /// <summary>
        /// Information about one mesh target to process.
        /// </summary>
        public struct MeshTarget
        {
            public GameObject gameObject;
            public MeshFilter meshFilter;           // null for SkinnedMeshRenderer
            public MeshRenderer meshRenderer;       // null for SkinnedMeshRenderer
            public SkinnedMeshRenderer skinnedMesh;  // null for MeshFilter
            public Mesh sharedMesh;
        }

        /// <summary>
        /// Collect all mesh targets from the given selection.
        /// </summary>
        public static List<MeshTarget> CollectTargets(GameObject[] selectedObjects, bool includeChildren)
        {
            var targets = new List<MeshTarget>();
            var visited = new HashSet<GameObject>();

            foreach (var go in selectedObjects)
            {
                if (go == null) continue;
                CollectFromGameObject(go, includeChildren, targets, visited);
            }

            return targets;
        }

        private static void CollectFromGameObject(GameObject go, bool includeChildren,
            List<MeshTarget> targets, HashSet<GameObject> visited)
        {
            if (!visited.Add(go)) return;

            // LOD生成済みの子オブジェクト（_LOD suffix）はスキップ
            if (IsLODChild(go))
                return;

            // MeshFilter + MeshRenderer
            var mf = go.GetComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>();
            if (mf != null && mr != null && mf.sharedMesh != null)
            {
                targets.Add(new MeshTarget
                {
                    gameObject = go,
                    meshFilter = mf,
                    meshRenderer = mr,
                    skinnedMesh = null,
                    sharedMesh = mf.sharedMesh
                });
            }

            // SkinnedMeshRenderer
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null)
            {
                targets.Add(new MeshTarget
                {
                    gameObject = go,
                    meshFilter = null,
                    meshRenderer = null,
                    skinnedMesh = smr,
                    sharedMesh = smr.sharedMesh
                });
            }

            // Recurse children (LOD子オブジェクトは上のガードでスキップされる)
            if (includeChildren)
            {
                for (int i = 0; i < go.transform.childCount; i++)
                    CollectFromGameObject(go.transform.GetChild(i).gameObject, true, targets, visited);
            }
        }

        /// <summary>
        /// Generate LODs for a single target.
        /// Returns true on success, false on failure.
        /// </summary>
        public static bool GenerateLOD(MeshTarget target, LODGeneratorPreset preset, out string errorMessage)
        {
            errorMessage = null;
            var go = target.gameObject;
            var sourceMesh = target.sharedMesh;

            if (sourceMesh == null)
            {
                errorMessage = LODGeneratorLocalization.Get("ErrorNoMesh", go.name);
                return false;
            }

            // Determine save folder（元メッシュの隣 or 指定フォルダ直下）
            string savePath = ResolveSavePath(preset, sourceMesh);

            // 保存先が Assets 配下でなければ中断（CreateAsset の空パスクラッシュを防ぐ安全網）
            if (!IsUnderAssets(savePath))
            {
                errorMessage = $"保存先フォルダを解決できませんでした (savePath='{savePath}', mesh='{sourceMesh.name}')。" +
                               "「同じフォルダに保存」を OFF にして Assets 配下のフォルダを指定してください。";
                return false;
            }

            // Undo時に消すために生成内容を記録
            var cleanup = new PendingCleanup();

            // Ensure folder exists
            if (!string.IsNullOrEmpty(savePath) && !AssetDatabase.IsValidFolder(savePath))
                CreateFolderRecursive(savePath, cleanup.createdFolders);

            // Register undo
            Undo.RegisterCompleteObjectUndo(go, "LOD Generator");
            if (target.meshFilter != null) Undo.RegisterCompleteObjectUndo(target.meshFilter, "LOD Generator");
            if (target.meshRenderer != null) Undo.RegisterCompleteObjectUndo(target.meshRenderer, "LOD Generator");
            if (target.skinnedMesh != null) Undo.RegisterCompleteObjectUndo(target.skinnedMesh, "LOD Generator");

            // Remove existing LODGroup and LOD child objects
            var existingLODGroup = go.GetComponent<LODGroup>();
            if (existingLODGroup != null)
                Undo.DestroyObjectImmediate(existingLODGroup);
            CleanupLODChildren(go);

            // Generate simplified meshes
            int lodCount = preset.lodCount;
            preset.SyncLevels();

            int srcTriCount = sourceMesh.triangles.Length / 3;

            Mesh[] keptMeshes;
            float[] keptTransitions;
            Material[][] keptMaterials;

            // 同一メッシュのキャッシュ（間引き結果ごと再利用）
            if (_meshCache != null && _meshCache.TryGetValue(sourceMesh, out var cached))
            {
                keptMeshes = cached.meshes;
                keptTransitions = cached.transitions;
                keptMaterials = cached.materials;
                Debug.Log($"[LOD Generator] Reusing cached LOD meshes for '{sourceMesh.name}' ({go.name})");
            }
            else
            {
                // 1) 全レベルをメモリ上で簡略化（保存はまだしない）
                var simplifiedMeshes = new Mesh[lodCount];
                var tris = new int[lodCount];
                for (int i = 0; i < lodCount; i++)
                {
                    try
                    {
                        // Billboard / Cube レベルは簡略化せず、保存フェーズで置換メッシュ化する
                        if (preset.levels[i].mode == LODLevelMode.Billboard ||
                            preset.levels[i].mode == LODLevelMode.Cube)
                        {
                            simplifiedMeshes[i] = null;
                            tris[i] = -1;
                            continue;
                        }

                        EditorUtility.DisplayProgressBar(
                            "LOD Generator",
                            $"LOD{i + 1}: {go.name} (simplifying...)",
                            (float)i / Mathf.Max(1, lodCount));

                        simplifiedMeshes[i] = SimplifyOnce(sourceMesh, preset.levels[i].quality, preset);
                        tris[i] = simplifiedMeshes[i].triangles.Length / 3;
                    }
                    catch (System.Exception ex)
                    {
                        EditorUtility.ClearProgressBar();
                        errorMessage = LODGeneratorLocalization.Get("ErrorSimplify", ex.Message);
                        for (int j = 0; j <= i; j++)
                            if (simplifiedMeshes[j] != null) Object.DestroyImmediate(simplifiedMeshes[j]);
                        return false;
                    }
                }
                EditorUtility.ClearProgressBar();

                // 2) これ以上ポリゴンが減らないレベル（前段と同数以上）を間引く
                var keep = new List<int>();
                int prevTris = srcTriCount;
                for (int i = 0; i < lodCount; i++)
                {
                    // Billboard / Cube レベルは常に採用（ポリ数比較の対象外。prevTris も更新しない）
                    if (preset.levels[i].mode == LODLevelMode.Billboard ||
                        preset.levels[i].mode == LODLevelMode.Cube)
                    {
                        keep.Add(i);
                        continue;
                    }
                    if (preset.skipDuplicateLODs && tris[i] >= prevTris)
                    {
                        Debug.Log($"[LOD Generator] LOD{i + 1} skipped: {tris[i]} tris " +
                            $"(no reduction vs previous level's {prevTris}).");
                        continue;
                    }
                    keep.Add(i);
                    prevTris = tris[i];
                }

                // 3) 採用レベルのみ保存。遷移高さは「次の採用レベルの1つ手前」の screenPercent を使い、
                //    間引かれた区間は前のレベルがそのまま引き継ぐ（見た目を変えない）。
                keptMeshes = new Mesh[keep.Count];
                keptTransitions = new float[keep.Count];
                keptMaterials = new Material[keep.Count][];
                for (int k = 0; k < keep.Count; k++)
                {
                    int g = keep[k];

                    // Billboard レベル: 板ポリ + カットアウトテクスチャ + マテリアルを生成
                    if (preset.levels[g].mode == LODLevelMode.Billboard)
                    {
                        var srcMats = target.skinnedMesh != null
                            ? target.skinnedMesh.sharedMaterials
                            : target.meshRenderer.sharedMaterials;
                        var bbOpts = new LODBillboardBaker.Options
                        {
                            resolution = preset.billboardResolution,
                            cutoff = preset.billboardCutoff,
                            padding = preset.billboardPadding,
                            twoSided = preset.billboardTwoSided,
                            axisOverride = -1
                        };
                        var bb = LODBillboardBaker.Bake(sourceMesh, srcMats,
                            sourceMesh.name + "_LOD" + (k + 1), savePath, bbOpts, cleanup.assetPaths);
                        if (!bb.ok)
                        {
                            errorMessage = $"板ポリ生成に失敗しました ({go.name}): {bb.error}";
                            foreach (var p in cleanup.assetPaths) AssetDatabase.DeleteAsset(p);
                            for (int j = 0; j < lodCount; j++)
                                if (simplifiedMeshes[j] != null) Object.DestroyImmediate(simplifiedMeshes[j]);
                            return false;
                        }
                        keptMeshes[k] = bb.quad;
                        keptMaterials[k] = new[] { bb.material };
                        int bbBoundary = (k + 1 < keep.Count) ? keep[k + 1] - 1 : (lodCount - 1);
                        keptTransitions[k] = preset.levels[bbBoundary].screenPercent;
                        continue;
                    }

                    // Cube レベル: バウンズにフィットした 12tri キューブ + 元マテリアル（先頭）流用。
                    // マテリアル配列を明示するので Billboard と同じ「静的 MeshRenderer 化」経路に乗る
                    if (preset.levels[g].mode == LODLevelMode.Cube)
                    {
                        var cubeMats = target.skinnedMesh != null
                            ? target.skinnedMesh.sharedMaterials
                            : target.meshRenderer.sharedMaterials;
                        var cubeMesh = BuildCubeMesh(sourceMesh, sourceMesh.name + "_LOD" + (k + 1));
                        string cubePath = BuildAssetPath(savePath, cubeMesh.name);
                        AssetDatabase.CreateAsset(cubeMesh, cubePath);
                        cleanup.assetPaths.Add(cubePath);
                        keptMeshes[k] = cubeMesh;
                        keptMaterials[k] = new[] { cubeMats != null && cubeMats.Length > 0 ? cubeMats[0] : null };
                        int cbBoundary = (k + 1 < keep.Count) ? keep[k + 1] - 1 : (lodCount - 1);
                        keptTransitions[k] = preset.levels[cbBoundary].screenPercent;
                        continue;
                    }

                    var mesh = simplifiedMeshes[g];
                    mesh.name = sourceMesh.name + "_LOD" + (k + 1);

                    int targetTris = Mathf.RoundToInt(srcTriCount * preset.levels[g].quality);
                    if (tris[g] > targetTris * 1.1f) // 目標に10%以上届かない場合は警告
                    {
                        float actualReduction = srcTriCount > 0 ? 1f - (float)tris[g] / srcTriCount : 0f;
                        Debug.LogWarning($"[LOD Generator] {mesh.name}: target not fully reached. " +
                            $"target={targetTris} actual={tris[g]} (reduction={actualReduction:P1}). " +
                            "Try disabling Preserve Border / UV Seam Edges if you need more reduction.");
                    }

                    string assetPath = BuildAssetPath(savePath, mesh.name);
                    AssetDatabase.CreateAsset(mesh, assetPath);
                    cleanup.assetPaths.Add(assetPath);

                    keptMeshes[k] = mesh;
                    int boundary = (k + 1 < keep.Count) ? keep[k + 1] - 1 : (lodCount - 1);
                    keptTransitions[k] = preset.levels[boundary].screenPercent;
                }

                // 4) 間引いたレベルの（保存していない）メッシュは破棄
                for (int i = 0; i < lodCount; i++)
                    if (!keep.Contains(i) && simplifiedMeshes[i] != null)
                        Object.DestroyImmediate(simplifiedMeshes[i]);

                if (_meshCache != null)
                    _meshCache[sourceMesh] = new LodBuildResult { meshes = keptMeshes, transitions = keptTransitions, materials = keptMaterials };
            }

            int keptCount = keptMeshes.Length;

            // 採用LODが0個 = これ以上ポリゴンを減らせなかった場合。
            // 「LOD0 → Culled」だけの“消えるLODGroup”を作らず、元メッシュをそのまま（常に表示）にする。
            if (keptCount == 0)
            {
                Debug.LogWarning($"[LOD Generator] '{go.name}': 生成できたLODがありません" +
                    "（メッシュをこれ以上削減できませんでした）。LODGroup は作成せず、元メッシュをそのまま使用します。");
                return true;
            }

            // Create child GameObjects for each kept LOD level
            var generatedRenderers = new Renderer[keptCount];
            for (int k = 0; k < keptCount; k++)
            {
                var lodChild = new GameObject(go.name + "_LOD" + (k + 1));
                Undo.RegisterCreatedObjectUndo(lodChild, "LOD Generator");
                lodChild.transform.SetParent(go.transform, false);

                bool isBillboard = keptMaterials != null && keptMaterials[k] != null;

                // Billboard は素の板ポリ（ボーンを持たない）なので、元がSkinnedでも静的MeshRendererにする
                if (target.skinnedMesh != null && !isBillboard)
                {
                    var smr = lodChild.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = keptMeshes[k];
                    smr.sharedMaterials = target.skinnedMesh.sharedMaterials;
                    smr.rootBone = target.skinnedMesh.rootBone;
                    smr.bones = target.skinnedMesh.bones;
                    generatedRenderers[k] = smr;
                }
                else
                {
                    var mf = lodChild.AddComponent<MeshFilter>();
                    mf.sharedMesh = keptMeshes[k];
                    var mr = lodChild.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = isBillboard ? keptMaterials[k] : target.meshRenderer.sharedMaterials;
                    generatedRenderers[k] = mr;
                }
            }

            // Build LOD array
            int totalLodLevels = 1 + keptCount + (preset.enableCulled ? 1 : 0);
            var lods = new LOD[totalLodLevels];

            // LOD0 (original)
            Renderer originalRenderer = target.skinnedMesh != null
                ? (Renderer)target.skinnedMesh
                : (Renderer)target.meshRenderer;

            lods[0] = new LOD(preset.lod0ScreenPercent, new Renderer[] { originalRenderer });

            // 採用された生成レベル
            for (int k = 0; k < keptCount; k++)
            {
                float transition = keptTransitions[k];
                // 最終LODは Culled を明示していない限りしきい値0で残す
                // （遠距離でも消えず、ローポリ表示を維持する）
                if (k == keptCount - 1 && !preset.enableCulled)
                    transition = 0f;
                lods[k + 1] = new LOD(transition,
                    new Renderer[] { generatedRenderers[k] });
            }

            // Culled
            if (preset.enableCulled)
                lods[totalLodLevels - 1] = new LOD(0f, new Renderer[0]);

            // Cross-fade 遷移幅は SetLODs より前に各LODへ設定する必要がある
            if (preset.enableCrossFade)
            {
                for (int i = 0; i < lods.Length; i++)
                    lods[i].fadeTransitionWidth = preset.crossFadeWidth;
            }

            // Apply LODGroup
            var lodGroup = Undo.AddComponent<LODGroup>(go);
            lodGroup.SetLODs(lods);

            // LODGroup を sentinel として cleanup を登録 (Undo で消えると asset/folder も削除)
            cleanup.sentinel = lodGroup;
            cleanup.scene = go.scene;
            if (cleanup.assetPaths.Count > 0 || cleanup.createdFolders.Count > 0)
                _pendingCleanups.Add(cleanup);

            // Cross-fade（遷移幅は上で各LODのfadeTransitionWidthに設定済み）
            if (preset.enableCrossFade)
            {
                lodGroup.fadeMode = preset.animateCrossFading
                    ? LODFadeMode.CrossFade
                    : LODFadeMode.SpeedTree;
                lodGroup.animateCrossFading = preset.animateCrossFading;
            }
            else
            {
                lodGroup.fadeMode = LODFadeMode.None;
            }

            lodGroup.RecalculateBounds();

            return true;
        }

        /// <summary>
        /// Process all targets with the given preset.
        /// Returns number of successfully processed targets.
        /// </summary>
        public static int GenerateAll(List<MeshTarget> targets, LODGeneratorPreset preset,
            out List<string> errors, out List<string> skipped)
        {
            errors = new List<string>();
            skipped = new List<string>();
            int processed = 0;

            _meshCache = new Dictionary<Mesh, LodBuildResult>();

            Undo.SetCurrentGroupName("LOD Generator Batch");
            int undoGroup = Undo.GetCurrentGroup();

            // 既存LODGroupの上書き判定: 0=未決定, 1=すべて上書き, -1=すべてスキップ
            int overwriteAll = 0;

            // 事前に既存LODGroup数をカウント（一括判断ダイアログの表示判定用）
        int existingCount = 0;
            for (int i = 0; i < targets.Count; i++)
                if (targets[i].gameObject.GetComponent<LODGroup>() != null)
                    existingCount++;

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];

                EditorUtility.DisplayProgressBar(
                    LODGeneratorLocalization.Get("WindowTitle"),
                    LODGeneratorLocalization.Get("GenerateProgress", i + 1, targets.Count),
                    (float)(i + 1) / targets.Count);

                if (target.gameObject.GetComponent<LODGroup>() != null)
                {
                    if (overwriteAll == 1)
                    {
                        // すべて上書き: そのまま続行
                    }
                    else if (overwriteAll == -1)
                    {
                        skipped.Add(target.gameObject.name);
                        continue;
                    }
                    else if (existingCount >= 2)
                    {
                        int choice = EditorUtility.DisplayDialogComplex(
                            LODGeneratorLocalization.Get("WindowTitle"),
                            LODGeneratorLocalization.Get("GenerateOverwriteBatch",
                                target.gameObject.name, existingCount),
                            LODGeneratorLocalization.Get("OverwriteAll"),
                            LODGeneratorLocalization.Get("SkipAll"),
                            LODGeneratorLocalization.Get("OverwriteOne"));

                        if (choice == 0) { overwriteAll = 1; }
                        else if (choice == 1) { overwriteAll = -1; skipped.Add(target.gameObject.name); continue; }
                        // choice == 2: この1件だけ上書き
                    }
                    else
                    {
                        bool overwrite = EditorUtility.DisplayDialog(
                            LODGeneratorLocalization.Get("WindowTitle"),
                            LODGeneratorLocalization.Get("GenerateOverwrite", target.gameObject.name),
                            "OK", "Skip");

                        if (!overwrite)
                        {
                            skipped.Add(target.gameObject.name);
                            continue;
                        }
                    }
                }

                if (GenerateLOD(target, preset, out string err))
                {
                    processed++;
                }
                else
                {
                    errors.Add(err);
                }
            }

            EditorUtility.ClearProgressBar();
            _meshCache = null;
            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return processed;
        }

        /// <summary>
        /// Simplify a mesh to a single quality level using the preset's preserve options.
        /// Returns a new Mesh (not saved as an asset).
        /// </summary>
        private static Mesh SimplifyOnce(Mesh sourceMesh, float quality, LODGeneratorPreset preset)
        {
            if (!sourceMesh.isReadable)
            {
                Debug.LogWarning($"[LOD Generator] Mesh '{sourceMesh.name}' is not readable. " +
                    "Enable Read/Write in the mesh import settings.");
            }

            var simplifier = new UnityMeshSimplifier.MeshSimplifier();
            var opts = UnityMeshSimplifier.SimplificationOptions.Default;
            opts.PreserveBorderEdges = preset.preserveBorderEdges;
            opts.PreserveUVSeamEdges = preset.preserveUVSeamEdges;
            opts.PreserveUVFoldoverEdges = preset.preserveUVFoldoverEdges;
            opts.PreserveSurfaceCurvature = preset.preserveSurfaceCurvature;
            opts.EnableSmartLink = true;
            simplifier.SimplificationOptions = opts;
            simplifier.Initialize(sourceMesh);
            simplifier.SimplifyMesh(quality);
            return simplifier.ToMesh();
        }

        /// <summary>
        /// Simplify a mesh for preview only (no asset is created). The caller owns the
        /// returned Mesh and is responsible for destroying it.
        /// </summary>
        /// <summary>ソースメッシュのローカルバウンズにフィットした 24頂点/12tri のキューブを作る。
        /// 頂点はソースのローカル空間（LOD子は親と同トランスフォームのため）。
        /// UV は「8隅へのリメッシュ」方式（v3・ユーザー発案）: 各面の4隅について、
        /// **その面と同方向(±60°)を向く元三角形の頂点のうち3D的に最も近い実頂点のUVをそのまま継承**する。
        /// 面単位では同方向ジオメトリ＝同じUVアイランドに収まるため、箱メッシュなら
        /// 元の面テクスチャがほぼそのまま復元される（合成UVのぐちゃぐちゃ・平色化の両方を回避）。</summary>
        public static Mesh BuildCubeMesh(Mesh source, string name)
        {
            Bounds b = source.bounds;
            Vector3 mn = b.min, mx = b.max;

            var sv = source.vertices;
            var suv = source.uv;
            bool hasUv = suv != null && suv.Length == sv.Length;
            int[] stris = source.GetTriangles(0);   // 保持するのは materials[0] なのでサブメッシュ0

            Vector3[] dirs = { Vector3.back, Vector3.forward, Vector3.left, Vector3.right, Vector3.up, Vector3.down };
            var faceVerts = new HashSet<int>[6];
            for (int f = 0; f < 6; f++) faceVerts[f] = new HashSet<int>();
            Vector2 globalCenter = Vector2.zero; float globalW = 0f;

            if (hasUv)
            {
                for (int t = 0; t + 2 < stris.Length; t += 3)
                {
                    int i0 = stris[t], i1 = stris[t + 1], i2 = stris[t + 2];
                    Vector3 gn = Vector3.Cross(sv[i1] - sv[i0], sv[i2] - sv[i0]);   // 幾何法線
                    float area = gn.magnitude * 0.5f;
                    if (area < 1e-10f) continue;
                    gn.Normalize();
                    globalCenter += (suv[i0] + suv[i1] + suv[i2]) / 3f * area; globalW += area;
                    for (int f = 0; f < 6; f++)
                    {
                        if (Vector3.Dot(gn, dirs[f]) <= 0.5f) continue;
                        faceVerts[f].Add(i0); faceVerts[f].Add(i1); faceVerts[f].Add(i2);
                    }
                }
            }
            if (globalW > 0f) globalCenter /= globalW; else globalCenter = new Vector2(0.5f, 0.5f);

            Vector2 CornerUv(int f, Vector3 corner)
            {
                if (!hasUv || faceVerts[f].Count == 0) return globalCenter;
                float best = float.MaxValue; Vector2 uvBest = globalCenter;
                foreach (int idx in faceVerts[f])
                {
                    float d = (sv[idx] - corner).sqrMagnitude;
                    if (d < best) { best = d; uvBest = suv[idx]; }
                }
                return uvBest;
            }

            var m = new Mesh { name = name };
            var v = new List<Vector3>(24);
            var n = new List<Vector3>(24);
            var uv = new List<Vector2>(24);
            var tan = new List<Vector4>(24);
            var tris = new List<int>(36);

            void Face(int fi, Vector3 a, Vector3 bb, Vector3 c, Vector3 d, Vector3 normal, Vector3 tangent)
            {
                int i0 = v.Count;
                v.Add(a); v.Add(bb); v.Add(c); v.Add(d);
                var t4 = new Vector4(tangent.x, tangent.y, tangent.z, -1f);
                for (int i = 0; i < 4; i++) { n.Add(normal); tan.Add(t4); }
                uv.Add(CornerUv(fi, a)); uv.Add(CornerUv(fi, bb));
                uv.Add(CornerUv(fi, c)); uv.Add(CornerUv(fi, d));
                tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 1);
                tris.Add(i0); tris.Add(i0 + 3); tris.Add(i0 + 2);
            }

            Face(0, new Vector3(mn.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mn.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mn.x, mx.y, mn.z), Vector3.back, Vector3.right);
            Face(1, new Vector3(mx.x, mn.y, mx.z), new Vector3(mn.x, mn.y, mx.z), new Vector3(mn.x, mx.y, mx.z), new Vector3(mx.x, mx.y, mx.z), Vector3.forward, Vector3.left);
            Face(2, new Vector3(mn.x, mn.y, mx.z), new Vector3(mn.x, mn.y, mn.z), new Vector3(mn.x, mx.y, mn.z), new Vector3(mn.x, mx.y, mx.z), Vector3.left, Vector3.back);
            Face(3, new Vector3(mx.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mx.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mx.x, mx.y, mn.z), Vector3.right, Vector3.forward);
            Face(4, new Vector3(mn.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mn.x, mx.y, mx.z), Vector3.up, Vector3.right);
            Face(5, new Vector3(mn.x, mn.y, mx.z), new Vector3(mx.x, mn.y, mx.z), new Vector3(mx.x, mn.y, mn.z), new Vector3(mn.x, mn.y, mn.z), Vector3.down, Vector3.right);

            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTangents(tan);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        public static Mesh SimplifyPreview(Mesh sourceMesh, float quality, LODGeneratorPreset preset)
        {
            if (sourceMesh == null) return null;

            // 構造簡略化が有効なら、Replace と同じ手順でプレビューにも反映する
            Mesh baseMesh = sourceMesh;
            Mesh structural = null;
            if (preset.replaceStructuralSimplify)
            {
                var sOpts = new LODStructuralSimplifier.Options
                {
                    coplanarAngleDeg = preset.structuralCoplanarAngle,
                    bevelLength = preset.structuralBevelLength,
                    flatShadeRegions = preset.structuralFlatShade
                };
                structural = LODStructuralSimplifier.Simplify(sourceMesh, sOpts, out _);
                if (structural != null) baseMesh = structural;
            }

            Mesh m;
            if (quality < 0.999f)
            {
                m = SimplifyOnce(baseMesh, quality, preset);
                if (structural != null) Object.DestroyImmediate(structural);
            }
            else
            {
                m = structural != null ? structural : SimplifyOnce(baseMesh, quality, preset);
            }
            m.name = sourceMesh.name + "_Preview";
            return m;
        }

        /// <summary>Resolve the asset save folder for a source mesh, honoring the preset's save settings.</summary>
        private static string ResolveSavePath(LODGeneratorPreset preset, Mesh sourceMesh)
        {
            if (preset.saveSameFolder)
            {
                string meshAssetPath = AssetDatabase.GetAssetPath(sourceMesh);
                if (!string.IsNullOrEmpty(meshAssetPath))
                {
                    string dir = Path.GetDirectoryName(meshAssetPath).Replace('\\', '/');
                    // ビルトインのプリミティブやパッケージ内のメッシュは Assets 外（例: Library/unity default
                    // resources）を指すため、そのまま使うと CreateAsset が空パスで失敗する。Assets 配下のみ採用。
                    if (IsUnderAssets(dir)) return dir;
                }
                // 同フォルダ保存が使えない（非アセット／ビルトイン／パッケージ）→ フォールバック
                return FallbackSavePath(preset);
            }
            // 指定フォルダ直下に保存（タイムスタンプ・サブフォルダは作らない）
            return IsUnderAssets(preset.customSavePath) ? preset.customSavePath : FallbackSavePath(preset);
        }

        /// <summary>同フォルダ保存が使えない場合の保存先。指定フォルダが有効ならそれ、無ければ既定フォルダ。</summary>
        private static string FallbackSavePath(LODGeneratorPreset preset)
        {
            return IsUnderAssets(preset.customSavePath) ? preset.customSavePath : "Assets/GeneratedLODs";
        }

        /// <summary>パスが Assets/ 以下を指しているか。</summary>
        private static bool IsUnderAssets(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            p = p.Replace('\\', '/');
            return p == "Assets" || p.StartsWith("Assets/");
        }

        /// <summary>
        /// saveFolder とメッシュ名から、保存可能で一意なアセットパスを作る。
        /// メッシュ名にファイル名として使えない文字（/ : * など。FBX 内のメッシュ名に紛れがち）が
        /// 含まれると生成パスが壊れ GenerateUniqueAssetPath が空を返すため、名前をサニタイズする。
        /// それでも空が返った場合は候補パスをそのまま使い、原因のメッシュ名をログに出す。
        /// </summary>
        private static string BuildAssetPath(string saveFolder, string meshName)
        {
            string fileName  = SanitizeFileName(meshName) + ".asset";
            string candidate = saveFolder.TrimEnd('/') + "/" + fileName;
            string unique    = AssetDatabase.GenerateUniqueAssetPath(candidate);
            if (string.IsNullOrEmpty(unique))
            {
                Debug.LogWarning($"[LOD Generator] GenerateUniqueAssetPath が空を返しました。" +
                    $"候補 '{candidate}' をそのまま使用します（元メッシュ名='{meshName}'）。");
                return candidate;
            }
            return unique;
        }

        /// <summary>ファイル名に使えない文字を '_' に置換する。空になる場合は "Mesh"。</summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Mesh";
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append((System.Array.IndexOf(invalid, c) >= 0 || c == '/' || c == '\\' || c == ':') ? '_' : c);
            string result = sb.ToString().Trim();
            return string.IsNullOrEmpty(result) ? "Mesh" : result;
        }

        /// <summary>
        /// Replace each target's mesh directly with a simplified low-poly version.
        /// No LODGroup or child objects are created — only the renderer's sharedMesh reference
        /// is swapped to a newly generated "_LowPoly" asset. The original mesh asset is left intact.
        /// Targets sharing the same source mesh reuse a single generated asset.
        /// Returns the number of successfully processed targets.
        /// </summary>
        public static int ReplaceAll(List<MeshTarget> targets, LODGeneratorPreset preset,
            float quality, out List<string> errors)
        {
            errors = new List<string>();
            int processed = 0;

            // 同一元メッシュ → 同一ローポリアセットを共有
            var cache = new Dictionary<Mesh, Mesh>();

            // Undo時に生成アセット/フォルダを消すための記録（Generateと同様の仕組み）
            var cleanup = new PendingCleanup { meshRefs = new List<Mesh>() };

            Undo.SetCurrentGroupName("LOD Generator Replace");
            int undoGroup = Undo.GetCurrentGroup();

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                var sourceMesh = target.sharedMesh;

                if (sourceMesh == null)
                {
                    errors.Add(LODGeneratorLocalization.Get("ErrorNoMesh", target.gameObject.name));
                    continue;
                }

                EditorUtility.DisplayProgressBar(
                    LODGeneratorLocalization.Get("WindowTitle"),
                    LODGeneratorLocalization.Get("ReplaceProgress", i + 1, targets.Count),
                    (float)(i + 1) / targets.Count);

                Mesh lowPoly;
                if (!cache.TryGetValue(sourceMesh, out lowPoly))
                {
                    try
                    {
                        string savePath = ResolveSavePath(preset, sourceMesh);
                        if (!string.IsNullOrEmpty(savePath) && !AssetDatabase.IsValidFolder(savePath))
                            CreateFolderRecursive(savePath, cleanup.createdFolders);

                        int srcTriCount = sourceMesh.triangles.Length / 3;

                        // Cube 置換: バウンズフィットの 12tri キューブへ丸ごと差し替え（quality / 構造簡略化は無視）
                        if (preset.replaceWithCube)
                        {
                            lowPoly = BuildCubeMesh(sourceMesh, sourceMesh.name + "_CubeLP");
                            Debug.Log($"[LOD Generator] Replace(Cube): '{sourceMesh.name}' tris {srcTriCount} → 12");
                            string cubeAssetPath = BuildAssetPath(savePath, lowPoly.name);
                            AssetDatabase.CreateAsset(lowPoly, cubeAssetPath);
                            cache[sourceMesh] = lowPoly;
                            cleanup.assetPaths.Add(cubeAssetPath);
                            cleanup.meshRefs.Add(lowPoly);
                            goto swapReference;
                        }

                        // 構造簡略化（平面結合＋ベベル溶接）を先に通す。元UV/法線マップは維持される。
                        Mesh baseMesh = sourceMesh;
                        Mesh structural = null;
                        if (preset.replaceStructuralSimplify)
                        {
                            var sOpts = new LODStructuralSimplifier.Options
                            {
                                coplanarAngleDeg = preset.structuralCoplanarAngle,
                                bevelLength = preset.structuralBevelLength,
                                flatShadeRegions = preset.structuralFlatShade
                            };
                            structural = LODStructuralSimplifier.Simplify(sourceMesh, sOpts, out string sReport);
                            if (structural != null)
                            {
                                baseMesh = structural;
                                Debug.Log($"[LOD Generator] Replace: '{sourceMesh.name}' {sReport}");
                            }
                            else if (!string.IsNullOrEmpty(sReport))
                            {
                                Debug.Log($"[LOD Generator] Replace: structural skipped — {sReport}");
                            }
                        }

                        // quality < 1 のときだけ追加でメッシュ簡略化（構造簡略化のみなら reduction 0%＝quality 1）
                        if (quality < 0.999f)
                        {
                            lowPoly = SimplifyOnce(baseMesh, quality, preset);
                            if (structural != null) Object.DestroyImmediate(structural);
                        }
                        else
                        {
                            lowPoly = structural != null ? structural : SimplifyOnce(baseMesh, quality, preset);
                        }
                        lowPoly.name = sourceMesh.name + "_LowPoly";

                        int resultTris = lowPoly.triangles.Length / 3;
                        Debug.Log($"[LOD Generator] Replace: '{sourceMesh.name}' " +
                            $"tris {srcTriCount} → {resultTris} (quality={quality:F2}, structural={preset.replaceStructuralSimplify})");

                        string assetPath = BuildAssetPath(savePath, lowPoly.name);
                        AssetDatabase.CreateAsset(lowPoly, assetPath);

                        cache[sourceMesh] = lowPoly;
                        cleanup.assetPaths.Add(assetPath);
                        cleanup.meshRefs.Add(lowPoly);
                    }
                    catch (System.Exception ex)
                    {
                        errors.Add(LODGeneratorLocalization.Get("ErrorSimplify", ex.Message));
                        continue;
                    }
                }

                // レンダラーの参照を差し替え
                swapReference:
                if (target.meshFilter != null)
                {
                    Undo.RegisterCompleteObjectUndo(target.meshFilter, "LOD Generator Replace");
                    target.meshFilter.sharedMesh = lowPoly;
                }
                else if (target.skinnedMesh != null)
                {
                    Undo.RegisterCompleteObjectUndo(target.skinnedMesh, "LOD Generator Replace");
                    target.skinnedMesh.sharedMesh = lowPoly;
                }

                // Cube 置換はサブメッシュが1本になる。余った材質が最終サブメッシュを重ね描きする
                // Unity の仕様があるため、マテリアル配列も先頭1本へ整理する
                if (preset.replaceWithCube)
                {
                    var rend = target.meshFilter != null
                        ? (Renderer)target.meshFilter.GetComponent<MeshRenderer>()
                        : target.skinnedMesh;
                    if (rend != null && rend.sharedMaterials != null && rend.sharedMaterials.Length > 1)
                    {
                        Undo.RegisterCompleteObjectUndo(rend, "LOD Generator Replace");
                        rend.sharedMaterials = new[] { rend.sharedMaterials[0] };
                    }
                }

                cleanup.scene = target.gameObject.scene;
                processed++;
            }

            EditorUtility.ClearProgressBar();
            Undo.CollapseUndoOperations(undoGroup);

            // 生成物があれば追跡（Undoで全参照が戻ったらアセットも削除される）
            if (cleanup.assetPaths.Count > 0)
                _pendingCleanups.Add(cleanup);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return processed;
        }

        /// <summary>
        /// LOD Generatorが生成した子オブジェクトかどうかを判定。
        /// 親オブジェクト名 + "_LOD" + 数字のパターンにマッチするものを対象とする。
        internal static bool IsLODChild(GameObject go)
        {
            if (go.transform.parent == null) return false;
            string parentName = go.transform.parent.name;
            string goName = go.name;
            // "ParentName_LOD1", "ParentName_LOD2" etc.
            if (!goName.StartsWith(parentName + "_LOD")) return false;
            string suffix = goName.Substring(parentName.Length + 4); // "_LOD" = 4 chars
            return int.TryParse(suffix, out _);
        }

        /// <summary>
        /// 既存のLOD子オブジェクトとそのメッシュアセットを削除する。
        /// 他オブジェクトが参照中のメッシュアセットは削除しない。
        private static void CleanupLODChildren(GameObject go)
        {
            var toDestroy = new List<GameObject>();
            var meshesToCheck = new HashSet<Mesh>();

            for (int i = 0; i < go.transform.childCount; i++)
            {
                var child = go.transform.GetChild(i).gameObject;
                if (!IsLODChildOf(go, child)) continue;

                toDestroy.Add(child);

            // 削除候補のメッシュを収集
        var mf = child.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    meshesToCheck.Add(mf.sharedMesh);

                var smr = child.GetComponent<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null)
                    meshesToCheck.Add(smr.sharedMesh);
            }

            // 先に子オブジェクトを破棄（参照を消す）
        foreach (var child in toDestroy)
                Undo.DestroyObjectImmediate(child);

            // 他に参照がないメッシュアセットだけ削除
            foreach (var mesh in meshesToCheck)
                DeleteMeshAssetIfUnused(mesh);
        }

        /// <summary>go の直接の子で、go.name + "_LOD" + 数字のパターンに一致するか。</summary>
        private static bool IsLODChildOf(GameObject parent, GameObject child)
        {
            string expected = parent.name + "_LOD";
            if (!child.name.StartsWith(expected)) return false;
            string suffix = child.name.Substring(expected.Length);
            return int.TryParse(suffix, out _);
        }

        /// <summary>
        /// シーン内で誰も参照していないLOD生成メッシュアセットを削除する。
        /// 他オブジェクトが使用中なら削除せずログを出す。
        private static void DeleteMeshAssetIfUnused(Mesh mesh)
        {
            string path = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(path)) return;
            if (!path.EndsWith(".asset") || !mesh.name.Contains("_LOD")) return;

            if (IsMeshReferencedInScene(mesh))
            {
                Debug.Log($"[LOD Generator] Mesh '{mesh.name}' is still referenced by other objects. Keeping asset.");
                return;
            }

            AssetDatabase.DeleteAsset(path);
        }

        /// <summary>シーン内のいずれかのレンダラーがこのメッシュを参照しているか。</summary>
        private static bool IsMeshReferencedInScene(Mesh mesh)
        {
            foreach (var mf in Object.FindObjectsOfType<MeshFilter>(true))
            {
                if (mf.sharedMesh == mesh) return true;
            }
            foreach (var smr in Object.FindObjectsOfType<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == mesh) return true;
            }
            return false;
        }

        private static void CreateFolderRecursive(string path)
        {
            CreateFolderRecursive(path, null);
        }

        // newly-created folder paths are appended to createdFolders (deepest last)
        private static void CreateFolderRecursive(string path, System.Collections.Generic.List<string> createdFolders)
        {
            path = path.Replace('\\', '/');
            var parts = path.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                    createdFolders?.Add(next);
                }
                current = next;
            }
        }

        // ===================================================================
        //  Undo cleanup: 生成した .asset と新規作成フォルダを Ctrl+Z で削除
        // ===================================================================

        private class PendingCleanup
        {
            public Component sentinel;        // LODGroup — undoで消える（Generate用）
            public List<Mesh> meshRefs;       // 生成メッシュ（Replace用。全て未参照になればundoとみなす）
            public List<string> assetPaths = new List<string>();
            public List<string> createdFolders = new List<string>();
            public System.DateTime createdAt = System.DateTime.UtcNow;
            public UnityEngine.SceneManagement.Scene scene;

            /// <summary>この生成物がまだシーンに存在するか（= Undoされていない）。</summary>
            public bool StillPresent()
            {
                if (sentinel != null) return true;
                if (meshRefs != null)
                {
                    foreach (var m in meshRefs)
                        if (m != null && IsMeshReferencedInScene(m)) return true;
                }
                return false;
            }
        }

        // この秒数以内の Undo だけ自動削除の対象 (経過後はLOD asset 残留)
        private const double CleanupFreshnessSeconds = 60.0;

        private static readonly System.Collections.Generic.List<PendingCleanup> _pendingCleanups
            = new System.Collections.Generic.List<PendingCleanup>();

        [UnityEditor.InitializeOnLoadMethod]
        private static void HookUndo()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= OnSceneSaved;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += OnSceneSaved;
        }

        private static void OnUndoRedo()
        {
            bool changed = false;
            var now = System.DateTime.UtcNow;
            for (int i = _pendingCleanups.Count - 1; i >= 0; i--)
            {
                var c = _pendingCleanups[i];
                if (c.StillPresent()) continue;

                double age = (now - c.createdAt).TotalSeconds;
                if (age <= CleanupFreshnessSeconds)
                {
                    DeleteCleanup(c);
                    changed = true;
                }
                // 古い場合は削除せず、追跡だけ外す (asset/folder は残す)
                _pendingCleanups.RemoveAt(i);
            }
            if (changed)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        // シーン保存 = ユーザーが LOD を確定 → 該当シーンの cleanup を解除
        private static void OnSceneSaved(UnityEngine.SceneManagement.Scene scene)
        {
            for (int i = _pendingCleanups.Count - 1; i >= 0; i--)
            {
                if (_pendingCleanups[i].scene == scene)
                    _pendingCleanups.RemoveAt(i);
            }
        }

        private static void DeleteCleanup(PendingCleanup c)
        {
            int delAsset = 0, delFolder = 0;
            foreach (var path in c.assetPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                {
                    if (AssetDatabase.DeleteAsset(path)) delAsset++;
                }
            }
            // 深い順に削除 (空フォルダのみ)
            c.createdFolders.Sort((a, b) => b.Length.CompareTo(a.Length));
            foreach (var folder in c.createdFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                var inside = AssetDatabase.FindAssets("", new[] { folder });
                if (inside.Length == 0)
                {
                    if (AssetDatabase.DeleteAsset(folder)) delFolder++;
                }
            }
            if (delAsset > 0 || delFolder > 0)
                Debug.Log($"[LOD Generator] Undo cleanup: removed {delAsset} asset(s), {delFolder} folder(s).");
        }
    }
}

