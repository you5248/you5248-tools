using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Linq;

namespace you5248.MeshMergeAndReplace
{

    public class MeshMergeAndReplaceWindow : EditorWindow
    {
        // Options
        bool disableOriginal = true;
        bool copyStaticFlags = true;
        bool autoName = true;
        bool saveAsAsset = true;

        // Inherit baked lightmaps (only when all sources share one lightmap index)
        bool inheritLightmaps = true;

        // Include children
        bool includeChildren = true;

        // EditorOnly option (instead of HideFlags)
        bool setOriginalEditorOnly = true;

        // Anchor (position/transform reference)
        enum AnchorMode
        {
            Parent = 0,
            FirstSelectedRoot = 1,
        }
        AnchorMode anchorMode = AnchorMode.FirstSelectedRoot;

        // SubMesh policy
        bool allowSubMeshes = false;          // ON: SubMesh を保持して結合 / OFF: SubMesh 検出で警告してスキップ
        bool requireSameMaterials = true;     // ON推奨: 許可ON時に sharedMaterials の構成が一致しない場合はスキップ

        // Preview
        MeshMergePreview _preview;
        bool _showPreview = true;
        float _previewHeight = 240f;
        bool _resizingPreview;
        const string PreviewHeightKey = "MeshMerge_PreviewHeight";
        const string ShowPreviewKey = "MeshMerge_ShowPreview";
        const string LanguageKey = "MeshMerge_Language";
        readonly List<MeshMergePreview.Item> _previewItems = new List<MeshMergePreview.Item>();
        int _statTargets, _statVerts, _statSubMeshes, _statMaterials;
        bool _statUInt32;
        bool _prevIncludeChildren;

        [MenuItem("you5248/Mesh Merge And Replace")]
        static void Open()
        {
            GetWindow<MeshMergeAndReplaceWindow>("Mesh Merge");
        }

        void OnEnable()
        {
            _previewHeight = EditorPrefs.GetFloat(PreviewHeightKey, 240f);
            _showPreview = EditorPrefs.GetBool(ShowPreviewKey, true);
            MeshMergeLocalization.Current = (MeshMergeLang)EditorPrefs.GetInt(LanguageKey, (int)MeshMergeLang.Japanese);
            _prevIncludeChildren = includeChildren;
            Selection.selectionChanged += OnSelectionChanged;
            RefreshPreview();
        }

        void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            if (_preview != null) { _preview.Cleanup(); _preview = null; }
        }

        void OnSelectionChanged()
        {
            RefreshPreview();
            Repaint();
        }

        // Rebuilds the preview item list and the merge stats from the current selection.
        void RefreshPreview()
        {
            _previewItems.Clear();
            _statTargets = _statVerts = _statSubMeshes = _statMaterials = 0;
            _statUInt32 = false;

            var roots = Selection.gameObjects;
            if (roots == null || roots.Length == 0) return;

            var pairs = CollectTargets(roots, includeChildren);
            var matSet = new HashSet<Material>();
            foreach (var p in pairs)
            {
                var mesh = p.MF.sharedMesh;
                if (mesh == null) continue;
                _previewItems.Add(new MeshMergePreview.Item
                {
                    mesh = mesh,
                    matrix = p.MF.transform.localToWorldMatrix,
                    mats = p.MR.sharedMaterials
                });
                _statTargets++;
                _statVerts += mesh.vertexCount;
                _statSubMeshes += Mathf.Max(1, mesh.subMeshCount);
                var ms = p.MR.sharedMaterials;
                if (ms != null)
                    foreach (var mm in ms) if (mm != null) matSet.Add(mm);
            }
            _statMaterials = matSet.Count;
            _statUInt32 = _statVerts > 65535;
        }

        void DrawLanguageBar()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            int cur = (int)MeshMergeLocalization.Current;
            int neu = GUILayout.Toolbar(cur, new[] { "EN", "日本語" }, GUILayout.Width(120));
            if (neu != cur)
            {
                MeshMergeLocalization.Current = (MeshMergeLang)neu;
                EditorPrefs.SetInt(LanguageKey, neu);
            }
            EditorGUILayout.EndHorizontal();
        }

        static string L(string key) => MeshMergeLocalization.Get(key);
        static string L(string key, params object[] args) => MeshMergeLocalization.Get(key, args);

        // ── Asset saving helpers (crash-proof paths, mirror of LOD Generator) ──
        static string ResolveSaveFolder(UnityEngine.SceneManagement.Scene scene)
        {
            string scenePath = scene.path;
            if (!string.IsNullOrEmpty(scenePath))
            {
                string dir = System.IO.Path.GetDirectoryName(scenePath).Replace('\\', '/');
                if (IsUnderAssets(dir)) return dir;
            }
            return "Assets/MergedMeshes";
        }

        static bool IsUnderAssets(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            p = p.Replace('\\', '/');
            return p == "Assets" || p.StartsWith("Assets/");
        }

        static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Mesh";
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append((System.Array.IndexOf(invalid, c) >= 0 || c == '/' || c == '\\' || c == ':') ? '_' : c);
            string result = sb.ToString().Trim();
            return string.IsNullOrEmpty(result) ? "Mesh" : result;
        }

        static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            var parts = folder.Split('/');
            string cur = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        static string BuildAssetPath(string folder, string baseName)
        {
            string fileName = SanitizeFileName(baseName) + ".asset";
            string candidate = folder.TrimEnd('/') + "/" + fileName;
            string unique = AssetDatabase.GenerateUniqueAssetPath(candidate);
            return string.IsNullOrEmpty(unique) ? candidate : unique;
        }

        void OnGUI()
        {
            DrawLanguageBar();

            EditorGUILayout.LabelField(L("Title"), EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(L("Info"), MessageType.Info);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField(L("SectionOptions"), EditorStyles.boldLabel);
            disableOriginal = EditorGUILayout.Toggle(L("DisableOriginal"), disableOriginal);
            copyStaticFlags = EditorGUILayout.Toggle(L("CopyStaticFlags"), copyStaticFlags);
            autoName = EditorGUILayout.Toggle(L("AutoName"), autoName);

            saveAsAsset = EditorGUILayout.Toggle(
                new GUIContent(L("SaveAsAsset"), L("SaveAsAssetTip")), saveAsAsset);
            if (saveAsAsset)
            {
                string folder = ResolveSaveFolder(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                EditorGUILayout.LabelField(L("SaveFolderNote", folder), EditorStyles.miniLabel);
            }

            inheritLightmaps = EditorGUILayout.Toggle(
                new GUIContent(L("InheritLightmaps"), L("InheritLightmapsTip")),
                inheritLightmaps);
            if (inheritLightmaps)
            {
                EditorGUILayout.HelpBox(L("InheritLightmapsNote"), MessageType.None);
            }

            EditorGUILayout.Space();
            includeChildren = EditorGUILayout.Toggle(L("IncludeChildren"), includeChildren);
            setOriginalEditorOnly = EditorGUILayout.Toggle(L("SetEditorOnly"), setOriginalEditorOnly);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(L("SectionAnchor"), EditorStyles.boldLabel);
            anchorMode = (AnchorMode)EditorGUILayout.EnumPopup(L("AnchorMode"), anchorMode);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(L("SectionSubMesh"), EditorStyles.boldLabel);
            allowSubMeshes = EditorGUILayout.Toggle(L("AllowSubMeshes"), allowSubMeshes);
            using (new EditorGUI.DisabledScope(!allowSubMeshes))
            {
                requireSameMaterials = EditorGUILayout.Toggle(L("RequireMatMatch"), requireSameMaterials);
            }

            EditorGUILayout.Space();

            // Refresh preview/stats when "include children" changes.
            if (_prevIncludeChildren != includeChildren)
            {
                _prevIncludeChildren = includeChildren;
                RefreshPreview();
            }

            DrawPreviewSection();

            var selected = Selection.gameObjects;
            EditorGUILayout.LabelField(L("SectionSelection"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(L("SelectionCount", selected.Length));

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(selected.Length == 0))
            {
                if (GUILayout.Button(L("ApplyButton"), GUILayout.Height(34)))
                {
                    MergeSelected();
                }
            }
        }

        void DrawPreviewSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(L("SectionPreview"), EditorStyles.boldLabel);

            if (_previewItems.Count == 0)
            {
                EditorGUILayout.HelpBox(L("PreviewSelectHint"), MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                L("PreviewStats", _statTargets, _statVerts, _statSubMeshes, _statMaterials));
            if (_statUInt32)
                EditorGUILayout.HelpBox(L("PreviewUInt32"), MessageType.None);

            bool sp = EditorGUILayout.ToggleLeft(L("PreviewToggle"), _showPreview);
            if (sp != _showPreview)
            {
                _showPreview = sp;
                EditorPrefs.SetBool(ShowPreviewKey, sp);
            }
            if (!_showPreview) return;

            if (_preview == null) _preview = new MeshMergePreview();

            Rect r = EditorGUILayout.GetControlRect(false, _previewHeight);
            if (_preview.HandleInput(r)) Repaint();
            _preview.DrawViewport(r, _previewItems);

            DrawPreviewResizeHandle();
        }

        void DrawPreviewResizeHandle()
        {
            Rect handle = EditorGUILayout.GetControlRect(false, 6f);
            EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeVertical);

            var e = Event.current;
            if (e.type == EventType.MouseDown && handle.Contains(e.mousePosition))
            {
                _resizingPreview = true;
                e.Use();
            }
            else if (_resizingPreview && e.type == EventType.MouseDrag)
            {
                _previewHeight = Mathf.Clamp(_previewHeight + e.delta.y, 140f, 900f);
                Repaint();
                e.Use();
            }
            else if (_resizingPreview && e.type == EventType.MouseUp)
            {
                _resizingPreview = false;
                EditorPrefs.SetFloat(PreviewHeightKey, _previewHeight);
                e.Use();
            }

            if (e.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(handle.x, handle.y + 2f, handle.width, 2f),
                    new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }

        void MergeSelected()
        {
            var selectedRoots = Selection.gameObjects;
            if (selectedRoots == null || selectedRoots.Length == 0)
            {
                Debug.LogWarning("No GameObjects selected.");
                return;
            }

            // 「同階層生成」の前提：選択ルートの親が混在している場合は弾く
            Transform parent = selectedRoots[0].transform.parent;
            for (int i = 1; i < selectedRoots.Length; i++)
            {
                if (selectedRoots[i].transform.parent != parent)
                {
                    Debug.LogError("選択ルートの親が混在しています。同じ親の直下にあるルートだけを選択してください。");
                    return;
                }
            }

            // Anchor 決定
            Transform anchor = (anchorMode == AnchorMode.FirstSelectedRoot)
                ? selectedRoots[0].transform
                : parent; // Parent mode

            // anchor が null（= Parent mode で parent==null）の場合はワールド基準
            Matrix4x4 anchorWorldToLocal = (anchor != null) ? anchor.worldToLocalMatrix : Matrix4x4.identity;

            // 対象 MeshRenderer/MeshFilter を収集
            var targetPairs = CollectTargets(selectedRoots, includeChildren);

            if (targetPairs.Count == 0)
            {
                Debug.LogWarning("No valid MeshFilters found on selection (and children).");
                return;
            }

            // ── Lightmap inheritance analysis ──
            // A renderer can reference only ONE lightmap, so baked lightmaps can be carried
            // only when every source is lightmapped with the same lightmap index.
            var uv2Temps = new List<Mesh>();
            int sharedLightmapIndex = -1;
            bool doLightmap = false;
            if (inheritLightmaps)
            {
                var lit = new List<TargetPair>();
                var indices = new HashSet<int>();
                foreach (var p in targetPairs)
                    if (IsLightmapped(p.MR)) { lit.Add(p); indices.Add(p.MR.lightmapIndex); }

                if (lit.Count == 0)
                {
                    doLightmap = false; // nothing lightmapped -> just merge normally
                }
                else if (indices.Count > 1)
                {
                    if (!EditorUtility.DisplayDialog(L("WindowTitle"), L("DlgMixedLightmap"),
                        L("BtnMergeNoLightmap"), L("BtnCancel"))) return;
                    doLightmap = false;
                }
                else if (lit.Count < targetPairs.Count)
                {
                    if (!EditorUtility.DisplayDialog(L("WindowTitle"), L("DlgPartialLightmap"),
                        L("BtnMergeNoLightmap"), L("BtnCancel"))) return;
                    doLightmap = false;
                }
                else
                {
                    doLightmap = true;
                    sharedLightmapIndex = indices.First();
                }
            }

            // CombineInstance リスト（単一化用）
            var combine = new List<CombineInstance>(targetPairs.Count);

            // SubMesh許可時: マテリアル単位で結合する
            var matToCombines = new Dictionary<int, List<CombineInstance>>();
            var materialOrder = new List<int>(16);
            var idToMaterial = new Dictionary<int, Material>(16);

            foreach (var p in targetPairs)
            {
                var mf = p.MF;
                var mr = p.MR;
                var mesh = mf.sharedMesh;

                if (mesh == null)
                    continue;

                // サブメッシュ判定
                if (mesh.subMeshCount > 1 && !allowSubMeshes)
                {
                    Debug.LogWarning(
                        $"[MeshMerge] SubMesh を含む Mesh を検出しました。「Allow SubMeshes」が OFF のため処理を中止します。\n" +
                        $"  Object: {GetHierarchyPath(mf.transform)}\n" +
                        $"  Mesh  : {mesh.name} (subMeshCount={mesh.subMeshCount})"
                    );
                    return;
                }

                // Use a UV2-baked copy when carrying lightmaps; otherwise the shared mesh.
                Mesh meshForCombine = ResolveCombineMesh(p, doLightmap, uv2Temps);

                if (allowSubMeshes)
                {
                    var mats = mr.sharedMaterials;

                    if (requireSameMaterials)
                    {
                        // subMeshCount と materials の整合性だけは必須（描画事故の抑止）
                        int matLen = mats != null ? mats.Length : 0;
                        if (matLen < mesh.subMeshCount)
                        {
                            Debug.LogWarning(
                                $"[MeshMerge] materials 数が subMeshCount 未満のため中止します（事故防止）。\n" +
                                $"  materials={matLen}, subMeshes={mesh.subMeshCount}\n" +
                                $"  Object: {GetHierarchyPath(mf.transform)}"
                            );
                            return;
                        }
                    }

                    // マテリアル単位で CombineInstance を積む
                    Matrix4x4 xform = anchorWorldToLocal * mf.transform.localToWorldMatrix;
                    for (int smi = 0; smi < mesh.subMeshCount; smi++)
                    {
                        Material mat = (mats != null && smi < mats.Length) ? mats[smi] : null;
                        int matKey = (mat != null) ? mat.GetInstanceID() : 0;

                        if (!matToCombines.TryGetValue(matKey, out var list))
                        {
                            list = new List<CombineInstance>(32);
                            matToCombines.Add(matKey, list);
                            materialOrder.Add(matKey);
                            idToMaterial[matKey] = mat;
                        }

                        list.Add(new CombineInstance
                        {
                            mesh = meshForCombine,
                            subMeshIndex = smi,
                            transform = xform
                        });
                    }
                }
                else
                {
                    // Anchor ローカル空間に焼き込み
                    //   anchorWorldToLocal * (meshObject localToWorld)
                    combine.Add(new CombineInstance
                    {
                        mesh = meshForCombine,
                        transform = anchorWorldToLocal * mf.transform.localToWorldMatrix
                    });
                }
            }

            if (!allowSubMeshes && combine.Count == 0)
            {
                Debug.LogWarning("No valid Mesh data found (Combine list empty).");
                return;
            }
            if (allowSubMeshes && matToCombines.Count == 0)
            {
                Debug.LogWarning("No valid Mesh data found (Material groups empty).");
                return;
            }

            // 出力 Mesh 生成（vertexCount が 65535 を超える場合の対策として UInt32 を使う）
            // ※ まずは合算頂点数から IndexFormat を決める
            int totalVertexCount = 0;
            if (!allowSubMeshes)
            {
                foreach (var c in combine)
                {
                    if (c.mesh != null) totalVertexCount += c.mesh.vertexCount;
                }
            }
            else
            {
                foreach (var kv in matToCombines)
                {
                    var list = kv.Value;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].mesh != null) totalVertexCount += list[i].mesh.vertexCount;
                    }
                }
            }

            // GO 作成(同階層)
            string newName = autoName ? $"MergedMesh_{selectedRoots[0].name}" : "MergedMesh";
            var mergedGO = new GameObject(newName);
            Undo.RegisterCreatedObjectUndo(mergedGO, "Create Merged Mesh");

            // 「同層」の維持：parent 直下に配置
            // ただし座標系は Anchor に合わせたいので SetParent(true) を使い、あとで明示的に合わせる
            mergedGO.transform.SetParent(parent, true);

            // Anchor mode に応じて transform を揃える
            if (anchorMode == AnchorMode.FirstSelectedRoot)
            {
                // 見た目を anchor と一致させる（親配下のまま）
                mergedGO.transform.position = selectedRoots[0].transform.position;
                mergedGO.transform.rotation = selectedRoots[0].transform.rotation;

                // スケールは階層事情で難しいので、基本は anchor の lossyScale に合わせる
                // （親にスケールが入っていると localScale だけで一致しない場合があるが、まずは崩れ対策を優先）
                mergedGO.transform.localScale = selectedRoots[0].transform.lossyScale;
            }
            else
            {
                // Parent 基準：親ローカル原点に置く
                mergedGO.transform.localPosition = Vector3.zero;
                mergedGO.transform.localRotation = Quaternion.identity;
                mergedGO.transform.localScale = Vector3.one;
            }

            var newMF = mergedGO.AddComponent<MeshFilter>();
            var newMR = mergedGO.AddComponent<MeshRenderer>();

            // Mesh 生成
            Mesh mergedMesh = new Mesh();
            mergedMesh.name = allowSubMeshes ? "MergedMesh_SubMeshes" : "MergedMesh";

            if (totalVertexCount > 65535)
                mergedMesh.indexFormat = IndexFormat.UInt32;

            // Combine 実行
            if (!allowSubMeshes)
            {
                // allowSubMeshes:
                //   false -> mergeSubMeshes=true
                mergedMesh.CombineMeshes(combine.ToArray(), true, true);
            }
            else
            {
                // マテリアルごとに一度まとめて、最終的に「1マテリアル=1サブメッシュ」にする
                var finalCombine = new List<CombineInstance>(materialOrder.Count);
                var tempMeshes = new List<Mesh>(materialOrder.Count);

                foreach (int matKey in materialOrder)
                {
                    var list = matToCombines[matKey];

                    var temp = new Mesh();
                    temp.name = "MergedTemp_ByMaterial";
                    int groupVertexCount = 0;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].mesh != null) groupVertexCount += list[i].mesh.vertexCount;
                    }
                    if (groupVertexCount > 65535)
                        temp.indexFormat = IndexFormat.UInt32;

                    temp.CombineMeshes(list.ToArray(), true, true);
                    tempMeshes.Add(temp);

                    finalCombine.Add(new CombineInstance
                    {
                        mesh = temp,
                        transform = Matrix4x4.identity
                    });
                }

                mergedMesh.CombineMeshes(finalCombine.ToArray(), false, true);

                // temp Mesh 破棄
                foreach (var tm in tempMeshes)
                {
                    if (tm != null) DestroyImmediate(tm);
                }
            }

            newMF.sharedMesh = mergedMesh;

            // Temporary UV2-baked copies are no longer needed.
            foreach (var tm in uv2Temps)
                if (tm != null) DestroyImmediate(tm);

            // materials 設定
            if (!allowSubMeshes)
            {
                // 単一マテリアルで統合（代表を選ぶ）
                Material singleMat = FindFirstMaterial(targetPairs);
                if (singleMat == null)
                {
                    Debug.LogWarning("[MeshMerge] Material を取得できませんでした。");
                    Undo.DestroyObjectImmediate(mergedGO);
                    return;
                }
                newMR.sharedMaterial = singleMat;
            }
            else
            {
                // マテリアルの重複は 1 スロットに統合済み
                var mats = new Material[materialOrder.Count];
                for (int i = 0; i < materialOrder.Count; i++)
                {
                    mats[i] = idToMaterial[materialOrder[i]];
                }
                newMR.sharedMaterials = mats;
            }

            // Static flags コピー
            if (copyStaticFlags)
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(selectedRoots[0]);
                GameObjectUtility.SetStaticEditorFlags(mergedGO, flags);
            }

            // Lightmap inheritance: assign the shared lightmap with identity scale/offset
            // (each source's lightmapScaleOffset was baked into UV2 above).
            if (doLightmap && sharedLightmapIndex >= 0)
            {
                newMR.lightmapIndex = sharedLightmapIndex;
                newMR.lightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);

                // Keep ContributeGI so Unity does not reset the assigned lightmap index.
                var lmFlags = GameObjectUtility.GetStaticEditorFlags(mergedGO);
                GameObjectUtility.SetStaticEditorFlags(mergedGO, lmFlags | StaticEditorFlags.ContributeGI);
            }

            // コピー元の処理
            if (setOriginalEditorOnly)
            {
                foreach (var root in selectedRoots)
                {
                    SetTagEditorOnlyRecursive(root, includeChildren);
                }
            }

            if (disableOriginal)
            {
                foreach (var root in selectedRoots)
                {
                    Undo.RecordObject(root, "Disable Original");
                    root.SetActive(false);
                }
            }

            // Save the merged mesh as a reusable .asset (instead of scene-embedded).
            if (saveAsAsset)
            {
                string folder = ResolveSaveFolder(mergedGO.scene);
                EnsureFolder(folder);
                string assetPath = BuildAssetPath(folder, newName);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    mergedMesh.name = newName;
                    AssetDatabase.CreateAsset(mergedMesh, assetPath);
                    AssetDatabase.SaveAssets();
                    MeshMergeAssetCleanup.Track(newMR, assetPath); // delete the asset if undone
                    Debug.Log($"[MeshMerge] Saved mesh asset: {assetPath}");
                }
            }

            Selection.activeGameObject = mergedGO;

            int mergedCount = targetPairs.Count;
            Debug.Log(
                $"[MeshMerge] Merged {mergedCount} meshes into '{mergedGO.name}'. " +
                $"allowSubMeshes={allowSubMeshes}, includeChildren={includeChildren}, anchorMode={anchorMode}, " +
                $"vertexCount={totalVertexCount} (indexFormat={(mergedMesh.indexFormat == IndexFormat.UInt32 ? "UInt32" : "UInt16")}), " +
                $"lightmap={(doLightmap && sharedLightmapIndex >= 0 ? "inherited #" + sharedLightmapIndex : "none")}"
            );
        }

        // --- helpers ---

        class TargetPair
        {
            public MeshFilter MF;
            public MeshRenderer MR;
        }

        static List<TargetPair> CollectTargets(GameObject[] selectedRoots, bool includeChildren)
        {
            var list = new List<TargetPair>(256);

            foreach (var root in selectedRoots)
            {
                if (root == null) continue;

                if (!includeChildren)
                {
                    var mf = root.GetComponent<MeshFilter>();
                    var mr = root.GetComponent<MeshRenderer>();
                    if (mf != null && mr != null && mf.sharedMesh != null)
                        list.Add(new TargetPair { MF = mf, MR = mr });
                    continue;
                }

                // 子階層も対象
                var mfs = root.GetComponentsInChildren<MeshFilter>(true);
                foreach (var mf in mfs)
                {
                    if (mf == null || mf.sharedMesh == null) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null) continue;

                    list.Add(new TargetPair { MF = mf, MR = mr });
                }
            }

            return list;
        }

        static Material FindFirstMaterial(List<TargetPair> targets)
        {
            foreach (var t in targets)
            {
                if (t == null || t.MR == null) continue;
                var mats = t.MR.sharedMaterials;
                if (mats != null && mats.Length > 0 && mats[0] != null) return mats[0];
                if (t.MR.sharedMaterial != null) return t.MR.sharedMaterial;
            }
            return null;
        }

        // True when the renderer references a valid baked lightmap.
        static bool IsLightmapped(MeshRenderer mr)
        {
            if (mr == null) return false;
            int idx = mr.lightmapIndex;
            return idx >= 0 && idx < 65534; // 65535 = none, 65534 = special/none
        }

        // Returns the mesh to feed into CombineMeshes. When carrying lightmaps and the
        // source is lightmapped, returns a temp copy whose UV2 has the renderer's
        // lightmapScaleOffset baked in, so the merged renderer can use scaleOffset (1,1,0,0).
        // The shared source mesh is never mutated.
        static Mesh ResolveCombineMesh(TargetPair p, bool doLightmap, List<Mesh> uv2Temps)
        {
            var mesh = p.MF.sharedMesh;
            if (!doLightmap || !IsLightmapped(p.MR)) return mesh;

            var uv2 = mesh.uv2;
            if (uv2 == null || uv2.Length == 0)
            {
                Debug.LogWarning($"[MeshMerge] Lightmapped object has no UV2; lightmap UVs left unscaled: {GetHierarchyPath(p.MF.transform)}");
                return mesh;
            }

            Vector4 so = p.MR.lightmapScaleOffset; // x,y = scale  z,w = offset
            var baked = new Vector2[uv2.Length];
            for (int i = 0; i < uv2.Length; i++)
                baked[i] = new Vector2(uv2[i].x * so.x + so.z, uv2[i].y * so.y + so.w);

            var copy = Object.Instantiate(mesh);
            copy.name = mesh.name + "_lmBaked";
            copy.uv2 = baked;
            uv2Temps.Add(copy);
            return copy;
        }

        static void SetTagEditorOnlyRecursive(GameObject root, bool includeChildren)
        {
            if (root == null) return;

            // Tag が存在しないと例外になるので、EditorOnly タグの存在を確認する。
            // Unity 標準では EditorOnly タグはデフォルトで存在することが多いが、ない場合はスキップする。
            bool hasEditorOnlyTag = UnityEditorInternal.InternalEditorUtility.tags.Contains("EditorOnly");
            if (!hasEditorOnlyTag)
            {
                Debug.LogWarning("[MeshMerge] 'EditorOnly' タグが存在しません。Tag Manager で追加してください。処理をスキップします。");
                return;
            }

            if (!includeChildren)
            {
                Undo.RecordObject(root, "Set EditorOnly Tag");
                root.tag = "EditorOnly";
                return;
            }

            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (tr == null) continue;
                Undo.RecordObject(tr.gameObject, "Set EditorOnly Tag");
                tr.gameObject.tag = "EditorOnly";
            }
        }

        static string MaterialName(Material m) => m != null ? m.name : "(null)";

        static string GetHierarchyPath(Transform t)
        {
            if (t == null) return "(null)";
            var stack = new Stack<string>();
            while (t != null)
            {
                stack.Push(t.name);
                t = t.parent;
            }
            return string.Join("/", stack);
        }
    }
}
