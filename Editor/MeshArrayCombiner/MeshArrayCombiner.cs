using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Unity.Collections;

namespace you5248.MeshArrayCombiner
{

    /// <summary>
    /// 高タイリングのメッシュを「テクスチャ縮小なし・タイリング保持」で 1 ドローにまとめるツール。
    /// 各マテリアルのテクスチャを Texture2DArray の 1 レイヤーに格納し、メッシュを 1 サブメッシュに
    /// 統合（UV はタイリングのまま）、サブメッシュごとのレイヤー番号を UV3.x に焼く。
    /// 出力マテリアルは you5248/MeshArrayPBR（PBR + Light Volumes + LTCGI）。
    /// ※ アトラスとは別物。[0,1]/低タイルはアトラスツール、タイリングはこちら。
    /// </summary>
    public class MeshArrayCombiner : EditorWindow
    {
        private enum OutMode { CreateNew, ReplaceInPlace }

        private int      arraySize        = 1024;
        private string   outputFolder     = "Assets/you5248/MeshArrayCombiner/Generated";
        private string   outputName       = "ArrayMesh";
        private bool     includeChildren  = false;
        private OutMode  outMode          = OutMode.CreateNew;
        private bool     disableOriginals = true;
        private bool     compress         = true;
        private MeshArrayRevertData revertTarget;
        private Vector2  scroll;

        // Quest 再生成（既存 BC7/RGBA32 配列 → ASTC *_Quest。原本・通常 Combine は不変）
        private enum QuestAstcFormat { ASTC_6x6, ASTC_5x5, ASTC_4x4, ASTC_8x8 }
        private QuestAstcFormat questColorFormat  = QuestAstcFormat.ASTC_6x6;
        private QuestAstcFormat questNormalFormat = QuestAstcFormat.ASTC_5x5;
        private int             questMaxColorSize  = 0; // 0 = 無制限
        private int             questMaxNormalSize = 0;
        private DefaultAsset    questSourceFolder;

        private static readonly string[] SizeLabels = { "512", "1024", "2048" };
        private static readonly int[]    SizeValues = {  512,   1024,   2048 };
        private static readonly string[] QuestMaxSizeLabels = { "無制限", "2048", "1024", "512" };
        private static readonly int[]    QuestMaxSizeValues = { 0, 2048, 1024, 512 };
        private const string ShaderName = "you5248/MeshArrayPBR";
        private const string QuestSuffix = "_Quest";

        [MenuItem("you5248/Mesh Array Combiner")]
        static void Open() => GetWindow<MeshArrayCombiner>("Array Combiner");

        void OnSelectionChange() => Repaint();

        // ========================= GUI =========================

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("Mesh Array Combiner", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "タイリング素材を縮小せず 1 ドローにまとめます。各マテリアルを Texture2DArray の\n" +
                "レイヤーにし、UV はタイリングのまま維持します。出力マテリアルは " + ShaderName + "。",
                MessageType.Info);

            var targets   = GetTargets();
            int slotCount = targets.Sum(go => Mathf.Max(1, GetMesh(go).subMeshCount));
            var mats      = CollectMaterials(targets);

            EditorGUILayout.HelpBox(
                $"選択メッシュ: {targets.Count} 個（サブメッシュ計: {slotCount}）\n" +
                $"ユニークマテリアル(=配列レイヤー数): {mats.Count}",
                targets.Count == 0 ? MessageType.None : MessageType.Info);

            includeChildren = EditorGUILayout.Toggle("子オブジェクトを含める", includeChildren);
            if (GUILayout.Button(new GUIContent("選択を同じメッシュ+マテリアルに拡張",
                "選択中と同じ sharedMesh かつ同じ sharedMaterials を使うシーン内オブジェクトを全て選択します。")))
                SelectSameMeshMaterial();
            if (GUILayout.Button(new GUIContent("選択中のマテリアルを使うメッシュを全選択",
                "選択中のいずれかの sharedMaterial を共有するシーン内オブジェクトを全て選択します（メッシュは問わない）。")))
                SelectSameMaterial();

            EditorGUILayout.Space();
            outputName   = EditorGUILayout.TextField("出力名", outputName);
            outputFolder = EditorGUILayout.TextField("出力フォルダ", outputFolder);
            arraySize    = EditorGUILayout.IntPopup("最大サイズ(上限)", arraySize, SizeLabels, SizeValues);
            compress     = EditorGUILayout.Toggle(new GUIContent("BC7 圧縮",
                "配列を BC7 圧縮して容量を約1/4に（PC向け。Quest は非対応）。"), compress);
            outMode      = (OutMode)EditorGUILayout.EnumPopup("出力方法", outMode);
            using (new EditorGUI.DisabledScope(outMode == OutMode.ReplaceInPlace))
                disableOriginals = EditorGUILayout.Toggle("元メッシュを無効化", disableOriginals);

            if (mats.Count > 0)
                EditorGUILayout.HelpBox(
                    "実際の配列サイズ（元解像度に合わせ自動・上限内）:\n" +
                    $"  Albedo {ArraySizeFor(mats, "_MainTex", arraySize)} / Normal {NormalArraySize(mats, arraySize)} / " +
                    $"Metallic {MetallicArraySize(mats, arraySize)} / Emission {ArraySizeFor(mats, "_EmissionMap", arraySize)}\n" +
                    $"  形式: {(compress ? "BC7（圧縮）" : "RGBA32（無圧縮）")}",
                    MessageType.Info);

            if (mats.Count > 0 && mats.Count > 256)
                EditorGUILayout.HelpBox("レイヤー数が多すぎます（256以下推奨）。", MessageType.Warning);

            EditorGUILayout.Space();
            bool folderOK = outputFolder.StartsWith("Assets");
            if (!folderOK) EditorGUILayout.HelpBox("出力フォルダは Assets/ 以下にしてください。", MessageType.Error);

            using (new EditorGUI.DisabledScope(slotCount < 1 || mats.Count == 0 || !folderOK))
            {
                if (GUILayout.Button("Combine to Texture Array", GUILayout.Height(32)))
                    Execute(targets, mats);
            }

            if (Shader.Find(ShaderName) == null)
                EditorGUILayout.HelpBox($"シェーダー '{ShaderName}' が見つかりません（未インポート/コンパイルエラー）。", MessageType.Warning);

            // ── 元に戻す ──
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("元に戻す (Revert)", EditorStyles.boldLabel);

            if (GUILayout.Button(new GUIContent("シーンの選択から変換を自動検出",
                "変換後オブジェクト（CreateNewのルート/子）か、置換した元オブジェクトを選択して押すと、対応する RevertData を割り当てます。")))
            {
                var found = FindRevertForSelection();
                if (found != null) revertTarget = found;
                else EditorUtility.DisplayDialog("自動検出",
                    "選択中のオブジェクトに対応する RevertData が見つかりませんでした。\n変換後のオブジェクト、または置換した元オブジェクトを選択してください。", "OK");
            }

            revertTarget = (MeshArrayRevertData)EditorGUILayout.ObjectField(
                "RevertData", revertTarget, typeof(MeshArrayRevertData), false);

            if (revertTarget != null)
                EditorGUILayout.HelpBox(
                    $"対象: {revertTarget.name}\n方式: {revertTarget.mode} / 元オブジェクト {revertTarget.originals.Count} 個\n" +
                    $"フォルダ: {revertTarget.generatedFolder}",
                    MessageType.Info);

            using (new EditorGUI.DisabledScope(revertTarget == null))
                if (GUILayout.Button(new GUIContent("選択を名前照合で元に戻す（復旧用）",
                    "RevertData のシーン参照が切れている場合用（フォルダを復元した等）。選択中のオブジェクトの現在メッシュ名から元メッシュ名を割り出し、RevertData の元メッシュ/マテリアルへ戻します（ReplaceInPlace向け）。アセット削除は行いません。")))
                    RestoreSelectionByMeshName(revertTarget);

            using (new EditorGUI.DisabledScope(revertTarget == null))
                if (GUILayout.Button("選択した変換を元に戻す", GUILayout.Height(26)))
                {
                    if (EditorUtility.DisplayDialog("元に戻す",
                        "元のメッシュ/マテリアル/有効状態を復元し、生成された配列・メッシュ・マテリアルを削除します。\n" +
                        "（アセット削除は Undo で戻せません）よろしいですか？", "戻す", "キャンセル"))
                    {
                        RevertConversion(revertTarget);
                        revertTarget = null;
                    }
                }

            // ── Quest ASTC 再生成（原本 BC7 は不変。既存 *_Quest は GUID 維持で上書き）──
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Quest 再生成 (ASTC)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "既存の Texture2DArray（BC7/RGBA32）から _Quest サフィックス付きの配列を ASTC で再生成します。\n" +
                "原本は上書きしません。既に *_Quest.asset がある場合はその中身を上書き更新します（GUID 維持）。\n" +
                "通常の Combine（BC7）のデフォルト挙動は変わりません。",
                MessageType.Info);

            questColorFormat = (QuestAstcFormat)EditorGUILayout.EnumPopup(
                new GUIContent("Albedo/Metallic/Emission", "目安: ASTC 6x6"), questColorFormat);
            questNormalFormat = (QuestAstcFormat)EditorGUILayout.EnumPopup(
                new GUIContent("Normal", "目安: ASTC 5x5 または 6x6"), questNormalFormat);
            // 文字列ラベル配列を渡すため、ラベルも string オーバーロードを使う
            questMaxColorSize = EditorGUILayout.IntPopup(
                "最大解像度 Color", questMaxColorSize, QuestMaxSizeLabels, QuestMaxSizeValues);
            questMaxNormalSize = EditorGUILayout.IntPopup(
                "最大解像度 Normal", questMaxNormalSize, QuestMaxSizeLabels, QuestMaxSizeValues);

            questSourceFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                new GUIContent("生成フォルダ（任意）", "指定時はフォルダ内の *Array.asset を一括処理"),
                questSourceFolder, typeof(DefaultAsset), false);

            if (GUILayout.Button(new GUIContent("選択中の Texture2DArray から _Quest を生成",
                "Project で選んだ Texture2DArray を ASTC 再圧縮し、同フォルダに *_Quest.asset を新規作成"), GUILayout.Height(26)))
                RegenerateQuestFromSelection();

            using (new EditorGUI.DisabledScope(questSourceFolder == null))
            {
                if (GUILayout.Button(new GUIContent("指定フォルダ内の配列から _Quest を一括生成",
                    "フォルダ直下の Texture2DArray（名前に Array を含み _Quest でないもの）を再生成"), GUILayout.Height(26)))
                    RegenerateQuestFromFolder(questSourceFolder);
            }

            EditorGUILayout.EndScrollView();
        }

        // ========================= Collect =========================

        static Mesh GetMesh(GameObject go) => go.GetComponent<MeshFilter>() ? go.GetComponent<MeshFilter>().sharedMesh : null;

        List<GameObject> GetTargets()
        {
            IEnumerable<GameObject> cands = includeChildren
                ? Selection.gameObjects.SelectMany(go => go.GetComponentsInChildren<MeshFilter>(true)).Select(mf => mf.gameObject).Distinct()
                : Selection.gameObjects;

            return cands.Where(go =>
            {
                if (!IsMutableSceneObject(go)) return false;
                var mf = go.GetComponent<MeshFilter>();
                var mr = go.GetComponent<MeshRenderer>();
                return mf && mf.sharedMesh && mr && mr.sharedMaterials.Any(m => m != null);
            }).ToList();
        }

        static List<Material> CollectMaterials(List<GameObject> targets) =>
            targets.SelectMany(go => go.GetComponent<MeshRenderer>().sharedMaterials)
                   .Where(m => m != null).Distinct().ToList();

        // 選択中と同じ (sharedMesh, sharedMaterials) を使うシーン内オブジェクトを全選択する補助。
        static string MeshMatKey(GameObject go)
        {
            var mesh = GetMesh(go);
            var mats = go.GetComponent<MeshRenderer>().sharedMaterials;
            return (mesh ? mesh.GetInstanceID() : 0) + "|" +
                   string.Join(",", mats.Select(m => m ? m.GetInstanceID() : 0));
        }

        static void SelectSameMeshMaterial()
        {
            var sel = Selection.gameObjects
                .Where(go => GetMesh(go) && go.GetComponent<MeshRenderer>())
                .ToList();
            if (sel.Count == 0) return;

            var keys = new HashSet<string>(sel.Select(MeshMatKey));
            Selection.objects = Object.FindObjectsOfType<MeshRenderer>(true)
                .Select(mr => mr.gameObject)
                .Where(go => GetMesh(go) && keys.Contains(MeshMatKey(go)))
                .Distinct()
                .ToArray();
        }

        // 選択中のいずれかの sharedMaterial を共有するシーン内オブジェクトを全選択（メッシュは問わない）
        static void SelectSameMaterial()
        {
            var matSet = new HashSet<Material>(Selection.gameObjects
                .Where(go => go.GetComponent<MeshRenderer>())
                .SelectMany(go => go.GetComponent<MeshRenderer>().sharedMaterials)
                .Where(m => m != null));
            if (matSet.Count == 0) return;

            Selection.objects = Object.FindObjectsOfType<MeshRenderer>(true)
                .Where(mr => mr.sharedMaterials.Any(m => m != null && matSet.Contains(m)))
                .Select(mr => mr.gameObject)
                .Distinct()
                .ToArray();
        }

        // 復旧用：選択オブジェクトの「現在のメッシュ名」（"{変換名}_{元メッシュ名}"）から元メッシュ名を割り出し、
        // RevertData の元メッシュ/マテリアルへ戻す。RevertData のシーン参照(target)が切れている時用（ReplaceInPlace向け）。
        static void RestoreSelectionByMeshName(MeshArrayRevertData data)
        {
            if (data == null) return;
            string prefix = (string.IsNullOrEmpty(data.generatedFolder) ? "" : Path.GetFileName(data.generatedFolder)) + "_";

            // 同名でも「同じメッシュ・同じマテリアル」なら曖昧ではない（同一部材の使い回しは正常ケース）。
            // 実体の異なる同名だけを曖昧として復元対象から外す（誤ったメッシュを割り当てないため）。
            var byName   = new Dictionary<string, MeshArrayRevertData.Entry>();
            var dupNames = new HashSet<string>();
            foreach (var e in data.originals)
            {
                if (e.originalMesh == null) continue;
                string nm = e.originalMesh.name;
                if (byName.TryGetValue(nm, out var prev))
                {
                    if (prev.originalMesh != e.originalMesh ||
                        !SameMaterials(prev.originalMaterials, e.originalMaterials))
                        dupNames.Add(nm);
                    continue;
                }
                byName[nm] = e;
            }

            int n = 0, ambiguous = 0;
            foreach (var go in Selection.gameObjects)
            {
                if (!IsMutableSceneObject(go)) continue;   // Prefabアセット/Prefab Mode を書き換えない
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                string cur = mf.sharedMesh.name;
                string origName = cur.StartsWith(prefix) ? cur.Substring(prefix.Length) : cur;
                if (dupNames.Contains(origName)) { ambiguous++; continue; }
                if (!byName.TryGetValue(origName, out var e)) continue;

                var mr = go.GetComponent<MeshRenderer>();
                Undo.RecordObject(mf, "Restore Mesh"); mf.sharedMesh = e.originalMesh;
                if (mr != null) { Undo.RecordObject(mr, "Restore Material"); mr.sharedMaterials = e.originalMaterials; }
                RecordPrefabOverride(mf, mr);
                n++;
            }
            Debug.Log($"[MeshArrayCombiner] 選択から {n} 個を元メッシュ/マテリアルに復元しました。" +
                      (ambiguous > 0 ? $" 同名で実体の異なる元メッシュがあるため {ambiguous} 個はスキップ（誤復元防止）。" : ""));
            if (ambiguous > 0)
                EditorUtility.DisplayDialog("復元（同名スキップあり）",
                    $"{n} 個を復元しました。\n元メッシュ名が重複していて実体が異なるため、{ambiguous} 個は復元をスキップしました" +
                    "（誤ったメッシュ/マテリアルを割り当てないための安全策）。\n該当分は Inspector から手動で戻してください。", "OK");
            else if (n == 0)
                EditorUtility.DisplayDialog("復元",
                    "一致するオブジェクトがありませんでした。\n選択中のメッシュ名がこの RevertData の変換（" + prefix + "…）に対応しているか確認してください。", "OK");
        }

        static bool SameMaterials(Material[] a, Material[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // シーンの選択（変換後ルート/子、または置換した元オブジェクト）から対応する RevertData を探す
        static MeshArrayRevertData FindRevertForSelection()
        {
            var sel = Selection.gameObjects;
            if (sel.Length == 0) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:MeshArrayRevertData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<MeshArrayRevertData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                bool match = sel.Any(s =>
                    (data.createdRoot != null && s.transform.IsChildOf(data.createdRoot.transform)) ||
                    data.originals.Any(e => e.target == s));
                if (match) return data;
            }
            return null;
        }

        // 変換を元に戻す。安全第一：シーン参照が切れている箇所がある場合は生成物を削除しない（Missing防止）。
        static void RevertConversion(MeshArrayRevertData data)
        {
            if (data == null) return;
            bool createNew = data.mode == MeshArrayRevertData.Mode.CreateNew;

            int restored = 0, nullTargets = 0;
            foreach (var e in data.originals)
            {
                if (e.target == null) { nullTargets++; continue; }
                if (!createNew)
                {
                    var mf = e.target.GetComponent<MeshFilter>();
                    var mr = e.target.GetComponent<MeshRenderer>();
                    if (mf) { Undo.RecordObject(mf, "Revert Mesh");     mf.sharedMesh      = e.originalMesh; }
                    if (mr) { Undo.RecordObject(mr, "Revert Material");  mr.sharedMaterials = e.originalMaterials; }
                    RecordPrefabOverride(mf, mr);
                }
                else   // CreateNew：無効化していた元を元の有効状態へ戻す
                {
                    Undo.RecordObject(e.target, "Revert Active");
                    e.target.SetActive(e.wasActive);
                    RecordPrefabOverride(e.target);
                }
                restored++;
            }

            // 削除してよいのは「この RevertData 自身が直接入っているフォルダ」だけ（親ディレクトリ完全一致）。
            // StartsWith（子孫判定）だと generatedFolder が "Assets" 等の祖先に化けた場合も通ってしまい、
            // 無関係な配下全体をゴミ箱送りにできてしまう。判定は破棄より前に済ませる。
            string folder   = string.IsNullOrEmpty(data.generatedFolder) ? "" : data.generatedFolder.Replace("\\", "/").TrimEnd('/');
            string dataPath = AssetDatabase.GetAssetPath(data).Replace("\\", "/");
            string dataDir  = string.IsNullOrEmpty(dataPath) ? "" : Path.GetDirectoryName(dataPath).Replace("\\", "/");
            bool folderOurs = folder.Length > 0 && folder != "Assets" &&
                              string.Equals(dataDir, folder, System.StringComparison.Ordinal);

            bool hadRoot = data.createdRoot != null;   // Destroy 後は fake-null になるため先に控える
            if (hadRoot) Undo.DestroyObjectImmediate(data.createdRoot);

            // 生成物を「参照している側」を確実に片付けられた場合のみ削除する。
            //   CreateNew      … createdRoot を破棄できた（=生成メッシュを参照する子も消えた）
            //   ReplaceInPlace … 全ターゲットを復元できた（null=参照切れが1つも無い）
            bool restoredAll = createNew ? hadRoot : (nullTargets == 0 && data.originals.Count > 0);

            if (restoredAll && folderOurs && AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.MoveAssetToTrash(folder);   // 永久削除ではなくゴミ箱へ（復元可能）
                Debug.Log($"[MeshArrayCombiner] 元に戻しました（{restored}件）。生成物はゴミ箱へ移動: {folder}");
            }
            else
            {
                string reason = !restoredAll
                    ? $"シーンオブジェクトへの参照が切れている箇所があるため（別セッションで開き直した等。参照切れ {nullTargets} 件）"
                    : !folderOurs
                        ? "RevertData の generatedFolder がこの RevertData の保存場所と一致しないため（データ破損・誤編集の疑い）"
                        : "生成フォルダが見つからないため";
                EditorUtility.DisplayDialog("元に戻す（生成物は保持）",
                    $"{restored}件を復元しました。\n{reason}、" +
                    "Missing 防止のため生成アセットは削除しませんでした。\n" +
                    "問題なければ生成フォルダを手動で削除してください。", "OK");
                Debug.LogWarning($"[MeshArrayCombiner] 生成物は未削除: {folder}（{reason}）");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static int[] SubmeshLayers(GameObject go, List<Material> allMats)
        {
            var mesh = GetMesh(go);
            var mats = go.GetComponent<MeshRenderer>().sharedMaterials;
            int sub  = Mathf.Max(1, mesh.subMeshCount);
            var idxs = new int[sub];
            for (int s = 0; s < sub; s++)
            {
                Material m = mats.Length == 0 ? null : (s < mats.Length ? mats[s] : mats[mats.Length - 1]);
                idxs[s] = Mathf.Max(0, allMats.IndexOf(m));
            }
            return idxs;
        }

        // 変更してよい「通常シーン上の」オブジェクトか。
        // Project上のPrefabアセット本体（persistent）と、Prefab Mode で編集中の内容
        // （プレビューシーン上。オートセーブONだと変更が即アセットへ書かれる）はどちらも対象外。
        internal static bool IsMutableSceneObject(GameObject go) =>
            go != null &&
            !EditorUtility.IsPersistent(go) &&
            UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(go) == null;

        // プレハブインスタンスへの直接変更は Undo.RecordObject だけでは
        // シーン保存・再読込で失われうるため、明示的にオーバーライドとして記録する
        // （PrefabUtility.RecordPrefabInstancePropertyModifications の公式要求）。
        static void RecordPrefabOverride(params Object[] objs)
        {
            foreach (var o in objs)
                if (o != null && PrefabUtility.IsPartOfPrefabInstance(o))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        }

        // ========================= 実行前チェック =========================

        static string Preview(List<string> names) =>
            string.Join(", ", names.Take(3)) + (names.Count > 3 ? " …" : "");

        static readonly string[] AlphaKeywords = { "_ALPHATEST_ON", "_ALPHABLEND_ON", "_ALPHAPREMULTIPLY_ON" };

        static bool IsCutoutOrTransparent(Material m)
        {
            if (m.renderQueue >= 2450) return true;   // AlphaTest(2450) 以上 = Cutout/Transparent 帯
            // DHK 等は Queue=Geometry のままキーワード/トグルで切り抜くため、queue だけでは見逃す
            foreach (var kw in AlphaKeywords)
                if (m.IsKeywordEnabled(kw)) return true;
            if (m.HasProperty("_AlphaTest") && m.GetFloat("_AlphaTest") > 0f) return true;
            string rt = m.GetTag("RenderType", true, "");
            return rt.IndexOf("Transparent", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rt.IndexOf("Cutout",      System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // 「黙って壊れる」既知条件を実行前に列挙し、あればユーザーの確認を取る。
        bool PreflightConfirm(List<GameObject> targets, List<Material> mats)
        {
            var warns = new List<string>();

            var stMats = mats.Where(m => m.HasProperty("_MainTex") &&
                    (m.GetTextureScale("_MainTex") != Vector2.one ||
                     m.GetTextureOffset("_MainTex") != Vector2.zero))
                .Select(m => m.name).ToList();
            if (stMats.Count > 0)
                warns.Add($"・Tiling/Offset が既定値でないマテリアル {stMats.Count} 件（{Preview(stMats)}）\n" +
                          "  → _MainTex_ST は引き継がれないため、変換後にタイリングが変わります");

            var cutMats = mats.Where(IsCutoutOrTransparent).Select(m => m.name).ToList();
            if (cutMats.Count > 0)
                warns.Add($"・Cutout/Transparent のマテリアル {cutMats.Count} 件（{Preview(cutMats)}）\n" +
                          "  → 出力マテリアルは Opaque 既定のため、切り抜き・半透明は自動では移行されません");

            var badTopo = targets.Where(go =>
            {
                var mesh = GetMesh(go);
                for (int s = 0; s < mesh.subMeshCount; s++)
                    if (mesh.GetTopology(s) != MeshTopology.Triangles) return true;
                return false;
            }).Select(go => go.name).ToList();
            if (badTopo.Count > 0)
                warns.Add($"・三角形以外のトポロジ（Keep Quads 等）を含むメッシュ {badTopo.Count} 件（{Preview(badTopo)}）\n" +
                          "  → 三角形以外のサブメッシュは結合時にスキップされます（結合結果に含まれません）");

            if (outMode == OutMode.CreateNew && disableOriginals)
            {
                var colGo = targets.Where(go => go.GetComponentsInChildren<Collider>(true).Length > 0)
                    .Select(go => go.name).ToList();
                if (colGo.Count > 0)
                    warns.Add($"・Collider を持つ対象 {colGo.Count} 件（{Preview(colGo)}）\n" +
                              "  → 元の無効化で当たり判定も消えます（すり抜け注意）");
            }

            if (warns.Count == 0) return true;
            return EditorUtility.DisplayDialog("実行前の確認",
                "以下の既知の注意点があります:\n\n" + string.Join("\n", warns) + "\n\nこのまま実行しますか？",
                "実行する", "キャンセル");
        }

        // ========================= Execute =========================

        void Execute(List<GameObject> targets, List<Material> mats)
        {
            if (!PreflightConfirm(targets, mats)) return;
            EnsureFolder(outputFolder);
            string subPath = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/{outputName}");
            string name    = Path.GetFileName(subPath);
            AssetDatabase.CreateFolder(outputFolder, name);

            try
            {
                // 各マップの実ソース最大解像度に合わせて配列サイズを決める（arraySize は上限）
                int aSz = ArraySizeFor(mats, "_MainTex",          arraySize);
                int nSz = NormalArraySize(mats,                   arraySize);
                int mSz = MetallicArraySize(mats,                 arraySize);
                int eSz = ArraySizeFor(mats, "_EmissionMap",      arraySize);

                // --- 4 つの Texture2DArray を構築 ---
                var albedoArr = BuildArray(mats, m => GetAlbedoPixels(m, aSz), false, aSz, compress);
                var normalArr = BuildArray(mats, m => GetNormalPixels(m, nSz),                              true,  nSz, compress);
                var metalArr  = BuildArray(mats, m => GetMetallicPixels(m, mSz),                            true,  mSz, compress);
                var emisArr   = BuildArray(mats, m => GetEmissionPixels(m, eSz),                            false, eSz, compress);

                AssetDatabase.CreateAsset(albedoArr, $"{subPath}/{name}_AlbedoArray.asset");
                AssetDatabase.CreateAsset(normalArr, $"{subPath}/{name}_NormalArray.asset");
                AssetDatabase.CreateAsset(metalArr,  $"{subPath}/{name}_MetallicArray.asset");
                AssetDatabase.CreateAsset(emisArr,   $"{subPath}/{name}_EmissionArray.asset");

                // --- マテリアル ---
                var shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    EditorUtility.DisplayDialog("エラー", $"シェーダー '{ShaderName}' が見つかりません。先にコンパイルを通してください。", "OK");
                    return;
                }
                var mat = new Material(shader) { name = name + "_Material" };
                mat.SetTexture("_MainTexArray",          albedoArr);
                mat.SetTexture("_BumpMapArray",          normalArr);
                mat.SetTexture("_MetallicGlossMapArray", metalArr);
                mat.SetTexture("_EmissionMapArray",      emisArr);
                AssetDatabase.CreateAsset(mat, $"{subPath}/{name}_Material.mat");

                // --- メッシュ統合（レイヤー番号付き） ---
                var meshCache = new Dictionary<string, Mesh>();
                GameObject root = null;
                if (outMode == OutMode.CreateNew)
                {
                    root = new GameObject(name);
                    Undo.RegisterCreatedObjectUndo(root, "Mesh Array Combine");
                }

                // 元に戻すための復元データ
                var revert = ScriptableObject.CreateInstance<MeshArrayRevertData>();
                revert.mode = outMode == OutMode.ReplaceInPlace
                    ? MeshArrayRevertData.Mode.ReplaceInPlace : MeshArrayRevertData.Mode.CreateNew;
                revert.generatedFolder = subPath;
                revert.createdRoot     = root;

                // 親子とも対象の場合、先に親を無効化すると子の activeInHierarchy が変わるため先に控える
                var wasVisible = targets.ToDictionary(t => t, t => t.activeInHierarchy);

                foreach (var go in targets)
                {
                    var flat = GetFlattened(go, mats, name, subPath, meshCache);

                    // 変換前の状態を控える（ReplaceInPlace=メッシュ/マテリアル、CreateNew=有効状態）
                    var mf0 = go.GetComponent<MeshFilter>();
                    var mr0 = go.GetComponent<MeshRenderer>();
                    revert.originals.Add(new MeshArrayRevertData.Entry
                    {
                        target            = go,
                        originalMesh      = mf0 ? mf0.sharedMesh : null,
                        originalMaterials = mr0 ? mr0.sharedMaterials : new Material[0],
                        wasActive         = go.activeSelf,
                    });

                    if (outMode == OutMode.ReplaceInPlace)
                    {
                        var mf = go.GetComponent<MeshFilter>();
                        var mr = go.GetComponent<MeshRenderer>();
                        Undo.RecordObject(mf, "Replace Mesh");
                        Undo.RecordObject(mr, "Replace Material");
                        mf.sharedMesh      = flat;
                        mr.sharedMaterials = new Material[] { mat };
                        RecordPrefabOverride(mf, mr);
                    }
                    else
                    {
                        var srcMR = go.GetComponent<MeshRenderer>();
                        var child = new GameObject(go.name);
                        Undo.RegisterCreatedObjectUndo(child, "Mesh Array Combine");
                        child.layer = go.layer;
                        child.tag   = go.tag;
                        child.SetActive(wasVisible[go]);   // 元が非表示なら複製も非表示のまま
                        child.transform.SetParent(root.transform, false);
                        child.transform.SetPositionAndRotation(go.transform.position, go.transform.rotation);
                        child.transform.localScale = go.transform.lossyScale;
                        child.AddComponent<MeshFilter>().sharedMesh = flat;
                        var childMR = child.AddComponent<MeshRenderer>();
                        childMR.sharedMaterial = mat;
                        // 見た目に効く Renderer 設定を元から引き継ぐ（影・プローブ・オクルージョン）
                        childMR.enabled                    = srcMR.enabled;
                        childMR.shadowCastingMode          = srcMR.shadowCastingMode;
                        childMR.receiveShadows             = srcMR.receiveShadows;
                        childMR.lightProbeUsage            = srcMR.lightProbeUsage;
                        childMR.reflectionProbeUsage       = srcMR.reflectionProbeUsage;
                        childMR.probeAnchor                = srcMR.probeAnchor;
                        childMR.motionVectorGenerationMode = srcMR.motionVectorGenerationMode;
                        childMR.allowOcclusionWhenDynamic  = srcMR.allowOcclusionWhenDynamic;
                        // ライトマップのベイク結果を引き継ぐ（統合メッシュは uv2 を温存しているので
                        // 同じ index + scaleOffset でそのまま同じライトマップ領域を参照できる）
                        childMR.lightmapIndex               = srcMR.lightmapIndex;
                        childMR.lightmapScaleOffset         = srcMR.lightmapScaleOffset;
                        childMR.realtimeLightmapIndex       = srcMR.realtimeLightmapIndex;
                        childMR.realtimeLightmapScaleOffset = srcMR.realtimeLightmapScaleOffset;
                        GameObjectUtility.SetStaticEditorFlags(child, GameObjectUtility.GetStaticEditorFlags(go));
                        if (disableOriginals) { Undo.RecordObject(go, "Disable Original"); go.SetActive(false); RecordPrefabOverride(go); }
                    }
                }

                AssetDatabase.CreateAsset(revert, $"{subPath}/{name}_RevertData.asset");
                revertTarget = revert;   // 直後にワンクリックで戻せるよう自動セット

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                if (root != null) Selection.activeGameObject = root;
                Debug.Log($"[MeshArrayCombiner] 完了: {subPath}");
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("エラー", e.Message, "OK");
                Debug.LogException(e);
            }
        }

        Mesh GetFlattened(GameObject go, List<Material> allMats, string name, string folder, Dictionary<string, Mesh> cache)
        {
            var mesh = GetMesh(go);
            int[] layers = SubmeshLayers(go, allMats);
            string key = mesh.GetInstanceID() + ":" + string.Join(",", layers);
            if (cache.TryGetValue(key, out var cached)) return cached;

            var flat = FlattenMeshWithLayers(mesh, layers);
            flat.name = $"{name}_{mesh.name}";
            SaveMesh(flat, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{flat.name}.asset"));
            cache[key] = flat;
            return flat;
        }

        // ========================= Mesh 統合 =========================

        // 各サブメッシュを取り出して 1 サブメッシュに連結。UV0=タイリングのまま、UV1=ライトマップ、
        // UV3.x=レイヤー番号。法線/接線/頂点色も保持。参照頂点のみ取り出すので肥大化しない。
        static Mesh FlattenMeshWithLayers(Mesh src, int[] layers)
        {
            var sV = src.vertices;
            var sN = src.normals;
            var sT = src.tangents;
            var sU0 = src.uv;
            var sU1 = src.uv2;   // ライトマップ
            var sC = src.colors;
            bool hN = sN.Length == sV.Length, hT = sT.Length == sV.Length;
            bool hU0 = sU0.Length == sV.Length, hU1 = sU1.Length == sV.Length, hC = sC.Length == sV.Length;

            var V = new List<Vector3>(); var N = new List<Vector3>(); var T = new List<Vector4>();
            var U0 = new List<Vector2>(); var U1 = new List<Vector2>();
            var L  = new List<Vector4>(); var C = new List<Color>();
            var tris = new List<int>();

            int subCount = Mathf.Max(1, src.subMeshCount);
            for (int s = 0; s < subCount; s++)
            {
                // 三角形以外（Quads/Lines/Points）は GetTriangles の挙動が保証されないため明示的にスキップ
                //（黙って欠落・例外での中途状態を防ぐ。Preflight の警告文と対応）
                if (src.subMeshCount > 0 && src.GetTopology(s) != MeshTopology.Triangles)
                {
                    Debug.LogWarning($"[MeshArrayCombiner] '{src.name}' のサブメッシュ {s} は三角形トポロジではないためスキップします（{src.GetTopology(s)}）");
                    continue;
                }
                int layer = (s < layers.Length) ? layers[s] : 0;
                int[] st  = src.GetTriangles(s);
                var map = new Dictionary<int, int>(st.Length);
                foreach (int oi in st)
                {
                    if (!map.TryGetValue(oi, out int ni))
                    {
                        ni = V.Count; map[oi] = ni;
                        V.Add(sV[oi]);
                        if (hN) N.Add(sN[oi]);
                        if (hT) T.Add(sT[oi]);
                        U0.Add(hU0 ? sU0[oi] : Vector2.zero);   // タイリングUVをそのまま
                        U1.Add(hU1 ? sU1[oi] : Vector2.zero);
                        L.Add(new Vector4(layer, 0, 0, 0));
                        if (hC) C.Add(sC[oi]);
                    }
                    tris.Add(map[oi]);
                }
            }

            var m = new Mesh { indexFormat = V.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(V);
            if (hN) m.SetNormals(N);
            if (hT) m.SetTangents(T);
            m.SetUVs(0, U0);
            if (hU1) m.SetUVs(1, U1);
            m.SetUVs(3, L);                 // TEXCOORD3.x = レイヤー番号
            if (hC) m.SetColors(C);
            m.SetTriangles(tris, 0);
            if (!hN) m.RecalculateNormals();
            m.RecalculateTangents();   // 常に再計算（元の壊れた/欠損タンジェントを引き継がない）
            m.RecalculateBounds();
            return m;
        }

        void SaveMesh(Mesh mesh, string path) => AssetDatabase.CreateAsset(mesh, path);

        // ========================= Quest ASTC 再生成 =========================

        static TextureFormat ToTextureFormat(QuestAstcFormat f)
        {
            switch (f)
            {
                case QuestAstcFormat.ASTC_4x4: return TextureFormat.ASTC_4x4;
                case QuestAstcFormat.ASTC_5x5: return TextureFormat.ASTC_5x5;
                case QuestAstcFormat.ASTC_8x8: return TextureFormat.ASTC_8x8;
                default:                       return TextureFormat.ASTC_6x6;
            }
        }

        // ファイル名から線形空間とフォーマット種別を推定（既存命名: *_NormalArray / *_MetallicArray）
        static bool InferLinear(string assetName)
        {
            string n = assetName.ToLowerInvariant();
            return n.Contains("normal") || n.Contains("metallic") || n.Contains("metal");
        }

        static bool InferIsNormal(string assetName) =>
            assetName.ToLowerInvariant().Contains("normal");

        void RegenerateQuestFromSelection()
        {
            var arrays = Selection.GetFiltered<Texture2DArray>(SelectionMode.Assets);
            if (arrays == null || arrays.Length == 0)
            {
                EditorUtility.DisplayDialog("Quest 再生成",
                    "Project ウィンドウで Texture2DArray アセットを選択してください。", "OK");
                return;
            }
            int n = 0;
            foreach (var arr in arrays)
                if (RegenerateQuestArray(arr)) n++;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[MeshArrayCombiner] Quest 再生成完了: {n}/{arrays.Length} 件");
            EditorUtility.DisplayDialog("Quest 再生成", $"{n} 件の _Quest 配列を生成しました。", "OK");
        }

        void RegenerateQuestFromFolder(DefaultAsset folderAsset)
        {
            string folder = AssetDatabase.GetAssetPath(folderAsset);
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                EditorUtility.DisplayDialog("Quest 再生成", "有効なフォルダを指定してください。", "OK");
                return;
            }

            var list = new List<Texture2DArray>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2DArray", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // 直下のみ（サブフォルダの別 ArrayMesh を巻き込まない）
                string parent = System.IO.Path.GetDirectoryName(path)?.Replace("\\", "/");
                if (parent != folder) continue;
                var arr = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
                if (arr == null) continue;
                if (arr.name.EndsWith(QuestSuffix, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!arr.name.ToLowerInvariant().Contains("array")) continue;
                list.Add(arr);
            }

            if (list.Count == 0)
            {
                EditorUtility.DisplayDialog("Quest 再生成",
                    "対象の Texture2DArray が見つかりませんでした。", "OK");
                return;
            }

            int n = 0;
            foreach (var arr in list)
                if (RegenerateQuestArray(arr)) n++;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[MeshArrayCombiner] Quest 一括再生成完了: {n}/{list.Count} 件（{folder}）");
            EditorUtility.DisplayDialog("Quest 再生成",
                $"{folder}\n{n}/{list.Count} 件の _Quest 配列を生成しました。", "OK");
        }

        /// <summary>
        /// 既存配列を読み取り可能なピクセルから ASTC 再圧縮し、同ディレクトリに *_Quest.asset を作成/上書き。
        /// 既に _Quest の場合や読み取り不可の場合はスキップ。ウィンドウの Quest フォーマット・最大解像度設定を使用。
        /// </summary>
        bool RegenerateQuestArray(Texture2DArray src) =>
            RegenerateQuestArrayCore(src, ToTextureFormat(questColorFormat), ToTextureFormat(questNormalFormat),
                questMaxColorSize, questMaxNormalSize);

        /// <summary>
        /// エディタウィンドウを開かず外部から呼べる静的 API（最大解像度は無制限）。
        /// 指定パスの Texture2DArray を ASTC 再圧縮し *_Quest.asset を作成/上書きする。
        /// </summary>
        public static int RegenerateQuestArraysAtPaths(string[] assetPaths, TextureFormat colorFmt, TextureFormat normalFmt) =>
            RegenerateQuestArraysAtPaths(assetPaths, colorFmt, normalFmt, 0, 0);

        /// <summary>
        /// エディタウィンドウを開かず外部から呼べる静的 API。
        /// 指定パスの Texture2DArray を ASTC 再圧縮し *_Quest.asset を作成/上書きする。
        /// maxColorSize / maxNormalSize が 0 のとき無制限（元解像度のまま）。超える場合のみ縮小。
        /// ウィンドウ状態に依存しない。戻り値は成功件数。
        /// </summary>
        public static int RegenerateQuestArraysAtPaths(string[] assetPaths, TextureFormat colorFmt, TextureFormat normalFmt,
            int maxColorSize, int maxNormalSize)
        {
            if (assetPaths == null || assetPaths.Length == 0) return 0;

            int n = 0;
            for (int i = 0; i < assetPaths.Length; i++)
            {
                string path = assetPaths[i];
                if (string.IsNullOrEmpty(path)) continue;
                var arr = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
                if (arr == null)
                {
                    Debug.LogWarning($"[MeshArrayCombiner] Texture2DArray ではない/見つからない: {path}");
                    continue;
                }
                if (RegenerateQuestArrayCore(arr, colorFmt, normalFmt, maxColorSize, maxNormalSize)) n++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[MeshArrayCombiner] Quest 静的再生成完了: {n}/{assetPaths.Length} 件");
            return n;
        }

        /// <summary>
        /// 既存配列を読み取り可能なピクセルから指定フォーマットで再圧縮し、同ディレクトリに *_Quest.asset を作成/上書き。
        /// 既に _Quest の場合や読み取り不可の場合はスキップ。ウィンドウ状態に非依存。
        /// maxSize=0 は無制限。
        /// </summary>
        static bool RegenerateQuestArrayCore(Texture2DArray src, TextureFormat colorFmt, TextureFormat normalFmt,
            int maxColorSize, int maxNormalSize)
        {
            if (src == null) return false;
            string srcPath = AssetDatabase.GetAssetPath(src);
            if (string.IsNullOrEmpty(srcPath))
            {
                Debug.LogWarning($"[MeshArrayCombiner] アセットパスなし: {src.name}");
                return false;
            }
            if (src.name.EndsWith(QuestSuffix, System.StringComparison.OrdinalIgnoreCase) ||
                srcPath.EndsWith(QuestSuffix + ".asset", System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[MeshArrayCombiner] 既に _Quest のためスキップ: {srcPath}");
                return false;
            }

            bool linear = InferLinear(src.name);
            bool isNormal = InferIsNormal(src.name);
            var fmt = isNormal ? normalFmt : colorFmt;
            int maxSize = isNormal ? maxNormalSize : maxColorSize;

            Texture2DArray quest;
            try
            {
                quest = RecompressArray(src, fmt, linear, maxSize);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MeshArrayCombiner] Quest 再生成失敗: {srcPath}\n{e.Message}");
                return false;
            }

            quest.name = src.name.EndsWith("Array", System.StringComparison.OrdinalIgnoreCase)
                ? src.name + QuestSuffix
                : src.name + "Array" + QuestSuffix;

            string dir = System.IO.Path.GetDirectoryName(srcPath)?.Replace("\\", "/");
            string baseName = System.IO.Path.GetFileNameWithoutExtension(srcPath);
            if (baseName.EndsWith(QuestSuffix, System.StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, baseName.Length - QuestSuffix.Length);
            string dstPath = $"{dir}/{baseName}{QuestSuffix}.asset";

            // 既存 *_Quest は GUID 維持のため上書き（GenerateUniqueAssetPath で別名にしない）
            var existing = AssetDatabase.LoadAssetAtPath<Texture2DArray>(dstPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(quest, existing);
                existing.name = quest.name;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(quest);
                Debug.Log($"[MeshArrayCombiner] Quest 配列上書き: {srcPath} → {dstPath} " +
                          $"({fmt}, {existing.width}x{existing.height}x{existing.depth}, linear={linear}, maxSize={(maxSize > 0 ? maxSize.ToString() : "無制限")})");
            }
            else
            {
                AssetDatabase.CreateAsset(quest, dstPath);
                Debug.Log($"[MeshArrayCombiner] Quest 配列生成: {srcPath} → {dstPath} " +
                          $"({fmt}, {quest.width}x{quest.height}x{quest.depth}, linear={linear}, maxSize={(maxSize > 0 ? maxSize.ToString() : "無制限")})");
            }
            return true;
        }

        /// <summary>
        /// 既存 Texture2DArray の mip0 を Color[] で読み出し、指定フォーマットで再圧縮した新規配列を返す。
        /// maxSize &gt; 0 かつ src.width &gt; maxSize のとき、スライスを maxSize へバイリニア縮小してから圧縮する。
        /// ミップは縮小後サイズで再生成。maxSize=0 は無制限（従来どおり元解像度）。
        /// </summary>
        static Texture2DArray RecompressArray(Texture2DArray src, TextureFormat fmt, bool linear, int maxSize)
        {
            int srcSize = src.width;
            int depth = src.depth;
            if (src.height != srcSize)
                throw new System.InvalidOperationException(
                    $"非正方形配列は未対応: {src.width}x{src.height}");

            int size = srcSize;
            if (maxSize > 0 && size > maxSize)
                size = maxSize;

            var arr = new Texture2DArray(size, size, depth, fmt, true, linear)
            {
                wrapMode   = src.wrapMode,
                filterMode = src.filterMode,
                anisoLevel = src.anisoLevel,
                name       = src.name + QuestSuffix,
            };

            var rw = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;

            for (int i = 0; i < depth; i++)
            {
                Color[] pixels = src.GetPixels(i, 0);
                Texture2D slice;

                if (size != srcSize)
                {
                    // 元解像度へ読み出し → Graphics.Blit でバイリニア縮小 → 縮小後サイズでミップ再生成
                    var srcSlice = new Texture2D(srcSize, srcSize, TextureFormat.RGBA32, false, linear);
                    srcSlice.SetPixels(pixels, 0);
                    srcSlice.Apply(false);

                    var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, rw);
                    Graphics.Blit(srcSlice, rt);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    slice = new Texture2D(size, size, TextureFormat.RGBA32, true, linear);
                    slice.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    slice.Apply(true);
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    Object.DestroyImmediate(srcSlice);
                }
                else
                {
                    slice = new Texture2D(size, size, TextureFormat.RGBA32, true, linear);
                    slice.SetPixels(pixels, 0);
                    slice.Apply(true);
                }

                if (fmt != TextureFormat.RGBA32)
                    EditorUtility.CompressTexture(slice, fmt, TextureCompressionQuality.Normal);
                for (int mip = 0; mip < slice.mipmapCount; mip++)
                    arr.SetPixelData(slice.GetPixelData<byte>(mip), mip, i);
                Object.DestroyImmediate(slice);
            }

            arr.Apply(false, false);
            return arr;
        }

        // ========================= Texture2DArray 構築 =========================

        // 各マップの実ソース最大解像度に合わせた配列サイズ（cap 上限・16 下限・2の冪）。
        // 元が 512 なら 512、ソース無し(ソリッド)なら 16 で済むので容量を無駄にしない。
        // ※ Texture2DArray の性質上、全レイヤーが同一サイズになる。バッチ内に 1 枚でも
        //    大きいテクスチャがあると単色レイヤーもそのサイズで確保される点に注意。
        static int ArraySizeFor(List<Material> mats, string prop, int cap)
        {
            int maxDim = 0;
            foreach (var mat in mats)
                if (mat.HasProperty(prop) && mat.GetTexture(prop) is Texture2D t && t != null)
                    maxDim = Mathf.Max(maxDim, t.width, t.height);
            int p = 16;
            int target = Mathf.Max(16, maxDim);
            while (p < target && p < cap) p <<= 1;
            return Mathf.Clamp(p, 16, cap);
        }

        // ノーマルはプロパティ名が様々なので FindNormalTexture 基準で最大解像度を取る
        // （ArraySizeFor("_BumpMap") だと別名のとき 0→32px に潰れてしまう）
        static int NormalArraySize(List<Material> mats, int cap) => Pow2For(mats, cap, FindNormalTexture);

        // metallic/smoothness/roughness/packed の各マップから最大解像度を取る
        static readonly string[] MetalSmoothProps =
            { "_MetallicGlossMap", "_MetallicMap", "_MetallicTex", "_SmoothnessTex", "_SmoothnessMap", "_RoughnessMap", "_PackedMap" };
        static int MetallicArraySize(List<Material> mats, int cap) => Pow2For(mats, cap, m =>
        {
            Texture2D best = null; int bw = 0;
            foreach (var n in MetalSmoothProps)
                if (m.HasProperty(n) && m.GetTexture(n) is Texture2D t && t != null && t.width > bw) { best = t; bw = t.width; }
            return best;
        });

        static int Pow2For(List<Material> mats, int cap, System.Func<Material, Texture2D> finder)
        {
            int maxDim = 0;
            foreach (var mat in mats)
            {
                var t = finder(mat);
                if (t != null) maxDim = Mathf.Max(maxDim, t.width, t.height);
            }
            int p = 16;
            int target = Mathf.Max(16, maxDim);
            while (p < target && p < cap) p <<= 1;
            return Mathf.Clamp(p, 16, cap);
        }

        static Texture2DArray BuildArray(List<Material> mats, System.Func<Material, Color[]> getPixels,
                                         bool linear, int size, bool compress)
        {
            var fmt = compress ? TextureFormat.BC7 : TextureFormat.RGBA32;
            var arr = new Texture2DArray(size, size, mats.Count, fmt, true, linear)
            {
                wrapMode   = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };

            for (int i = 0; i < mats.Count; i++)
            {
                // いったん RGBA32 スライスを作りミップ生成→必要なら圧縮→ミップ毎に生バイトを配列へ転送。
                // （CreateAsset で正しく保存されるよう SetPixelData を使う）
                var slice = new Texture2D(size, size, TextureFormat.RGBA32, true, linear);
                slice.SetPixels(getPixels(mats[i]), 0);
                slice.Apply(true);
                if (compress) EditorUtility.CompressTexture(slice, fmt, TextureCompressionQuality.Normal);
                for (int mip = 0; mip < slice.mipmapCount; mip++)
                    arr.SetPixelData(slice.GetPixelData<byte>(mip), mip, i);
                Object.DestroyImmediate(slice);
            }

            arr.Apply(false, false);   // ミップは設定済み・読み取り可能のまま保存
            return arr;
        }

        static Color[] GetMapPixels(Material mat, string prop, Color fallback, bool linear, int size)
        {
            if (mat.HasProperty(prop))
            {
                var t = mat.GetTexture(prop) as Texture2D;
                if (t != null) return ScaleAndReadPixels(t, size, linear);
            }
            return Solid(size, fallback);
        }

        // アルベドは _MainTex × メインカラー で焼く。
        // テクスチャ無しの単色マテリアルはカラーそのものが焼かれる（従来は白になっていた）。
        // 乗算はシェーダーと同じくリニア空間で行い、sRGB に戻して格納する。
        // ※ HDR(>1)/負値のカラーは RGBA32/BC7 格納時に 0-1 へクランプされる（発光と同様）。
        static Color[] GetAlbedoPixels(Material mat, int size)
        {
            Color tint = GetAlbedoTint(mat);
            Color[] px = GetMapPixels(mat, "_MainTex", Color.white, false, size);
            if (tint == Color.white) return px;

            Color tintLin = tint.linear;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i].linear;
                c = new Color(c.r * tintLin.r, c.g * tintLin.g, c.b * tintLin.b).gamma;
                c.a = px[i].a * tint.a;
                px[i] = c;
            }
            return px;
        }

        // アルベド用メインカラーの決定。[MainColor] 属性付きプロパティを最優先し、
        // 無ければ通例の名前を探す。_Color を先頭にすると「_MainColor=アルベド色・
        // _Color=別用途（例: CVSDissolve のディゾルブ境界色）」のシェーダーで誤爆するため、
        // より「main」を明示する名前から順に見る。
        static Color GetAlbedoTint(Material mat)
        {
            var sh = mat.shader;
            int cnt = sh.GetPropertyCount();
            for (int i = 0; i < cnt; i++)
                if (sh.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color &&
                    (sh.GetPropertyFlags(i) & UnityEngine.Rendering.ShaderPropertyFlags.MainColor) != 0)
                    return mat.GetColor(sh.GetPropertyNameId(i));
            return GetColorAny(mat, Color.white, "_MainColor", "_BaseColor", "_Color");
        }

        static Color GetColorAny(Material mat, Color def, params string[] names)
        {
            foreach (var n in names)
                if (mat.HasColor(n)) return mat.GetColor(n);
            return def;
        }

        // 候補名から最初に見つかったテクスチャ / float を返す汎用ヘルパー
        static Texture2D FindTex(Material mat, params string[] names)
        {
            foreach (var n in names)
                if (mat.HasProperty(n) && mat.GetTexture(n) is Texture2D t && t != null) return t;
            return null;
        }
        static float GetFloatAny(Material mat, float def, params string[] names)
        {
            foreach (var n in names)
                if (mat.HasProperty(n)) return mat.GetFloat(n);
            return def;
        }

        static float Ch(Color c, int i) => i == 0 ? c.r : i == 1 ? c.g : i == 2 ? c.b : c.a;

        // metallic(R) + smoothness(A) を焼く。Standard / lilToon / Mochie / Filamented の各ワークフロー対応。
        //   metallic   : _MetallicGlossMap.r（そのまま） / _MetallicMap.r×strength / _PackedMap[ch]×strength
        //   smoothness : _SmoothnessTex.r×_Smoothness（lilToon） / 1-_RoughnessMap.r（Mochie ラフネス反転）
        //                / 1-_PackedMap[ch]（パック） / _MetallicGlossMap.a×_GlossMapScale（Standard）
        static Color[] GetMetallicPixels(Material mat, int size)
        {
            var px = new Color[size * size];

            var     packed   = FindTex(mat, "_PackedMap");
            Color[] packedPx = packed != null ? ScaleAndReadPixels(packed, size, true) : null;
            int     metCh    = (int)GetFloatAny(mat, 0f, "_MetallicChannel");
            int     rghCh    = (int)GetFloatAny(mat, 1f, "_RoughnessChannel");

            var     stdMet   = FindTex(mat, "_MetallicGlossMap");                     // Standard/Filamented/lilToon
            var     mochMet  = FindTex(mat, "_MetallicMap", "_MetallicTex");          // Mochie
            Color[] stdMetPx = stdMet  != null ? ScaleAndReadPixels(stdMet,  size, true) : null;
            Color[] mochMetPx= mochMet != null ? ScaleAndReadPixels(mochMet, size, true) : null;
            float   metStr   = GetFloatAny(mat, 1f, "_MetallicStrength", "_MetallicIntensity", "_Metallic");
            float   metNoMap = GetFloatAny(mat, 0f, "_Metallic", "_MetallicStrength");

            var     smTex    = FindTex(mat, "_SmoothnessTex", "_SmoothnessMap");
            var     rghMap   = FindTex(mat, "_RoughnessMap");
            Color[] smTexPx  = smTex  != null ? ScaleAndReadPixels(smTex,  size, true) : null;
            Color[] rghMapPx = rghMap != null ? ScaleAndReadPixels(rghMap, size, true) : null;
            float   smStr    = GetFloatAny(mat, 1f,   "_Smoothness", "_SmoothnessScale");
            float   rghStr   = GetFloatAny(mat, 1f,   "_RoughnessStrength");
            float   glossScl = GetFloatAny(mat, 1f,   "_GlossMapScale");
            float   smNoMap  = GetFloatAny(mat, 0.5f, "_Smoothness", "_Glossiness");

            for (int i = 0; i < px.Length; i++)
            {
                float met;
                if      (stdMetPx  != null) met = stdMetPx[i].r;                       // Standard はそのまま
                else if (mochMetPx != null) met = mochMetPx[i].r * metStr;            // Mochie は strength 乗算
                else if (packedPx  != null) met = Ch(packedPx[i], metCh) * metStr;
                else                        met = metNoMap;

                float sm;
                if      (smTexPx  != null) sm = smTexPx[i].r * smStr;                  // lilToon/別smoothnessテクスチャ
                else if (rghMapPx != null) sm = 1f - rghMapPx[i].r * rghStr;          // ラフネス→smoothnessに反転
                else if (packedPx != null) sm = 1f - Ch(packedPx[i], rghCh) * rghStr; // パックドはラフネス想定
                else if (stdMetPx != null) sm = stdMetPx[i].a * glossScl;             // Standard: metallic.a
                else                       sm = smNoMap;

                px[i] = new Color(met, met, met, Mathf.Clamp01(sm));
            }
            return px;
        }

        // 発光は _EmissionMap × _EmissionColor で焼く（元マテリアルの発光色・強度を反映）。
        // 発光色が黒なら無発光。HDR(>1) は RGBA32/BC7 が 0-1 のためクランプされる。
        static Color[] GetEmissionPixels(Material mat, int size)
        {
            Color tint = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
            if (tint.maxColorComponent <= 0f) return Solid(size, Color.black);

            Color[] px;
            if (mat.HasProperty("_EmissionMap") && mat.GetTexture("_EmissionMap") is Texture2D t && t != null)
                px = ScaleAndReadPixels(t, size, false);
            else
                px = Solid(size, Color.white);

            for (int i = 0; i < px.Length; i++) px[i] *= tint;   // map × EmissionColor
            return px;
        }

        // ノーマルは元アセットを一時的に Default/linear で読み直し、正しい RGB 法線を取得する。
        // （ランタイムの DXT5nm を生で読むと壊れるため）。シェーダー側は xy*2-1 で展開。
        static readonly string[] NormalProps = { "_BumpMap", "_NormalMap", "_NormalTex", "_Normal", "_BumpTex", "_MainNormal" };

        // プロパティ名に依存せずノーマルマップを探す（_BumpMap 以外に入っているシェーダー対策）。
        static Texture2D FindNormalTexture(Material mat)
        {
            foreach (var p in NormalProps)
                if (mat.HasProperty(p) && mat.GetTexture(p) is Texture2D t0 && t0 != null) return t0;

            var sh = mat.shader;
            int cnt = ShaderUtil.GetPropertyCount(sh);
            for (int i = 0; i < cnt; i++)
            {
                if (ShaderUtil.GetPropertyType(sh, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                string low = ShaderUtil.GetPropertyName(sh, i).ToLowerInvariant();
                if ((low.Contains("normal") || low.Contains("bump")) &&
                    mat.GetTexture(ShaderUtil.GetPropertyName(sh, i)) is Texture2D t1 && t1 != null)
                    return t1;
            }
            return null;
        }

        static Material s_unpackMat;
        static Color[] GetNormalPixels(Material mat, int size)
        {
            Texture2D t = FindNormalTexture(mat);
            if (t == null)
            {
                Debug.LogWarning($"[MeshArrayCombiner] '{mat.name}' にノーマルマップが見つかりません（平坦法線で焼きます）");
                return Solid(size, new Color(0.5f, 0.5f, 1f, 1f));
            }

            // 1) 画像ファイルなら Import 設定(NormalMap/Default)に依存せず元画像を直接デコード（最も確実）
            string path = AssetDatabase.GetAssetPath(t);
            string ext  = string.IsNullOrEmpty(path) ? "" : Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga")
            {
                try
                {
                    var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                    if (raw.LoadImage(File.ReadAllBytes(path)))
                    {
                        var p = ScaleAndReadPixels(raw, size, true);
                        Object.DestroyImmediate(raw);
                        return p;
                    }
                    Object.DestroyImmediate(raw);
                }
                catch { }
            }

            // 2) フォールバック：Blit で UnpackNormal（埋め込みテクスチャ等）
            if (s_unpackMat == null)
            {
                var sh = Shader.Find("Hidden/you5248/UnpackNormalBlit");
                if (sh != null) s_unpackMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }

            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            if (s_unpackMat != null) Graphics.Blit(t, rt, s_unpackMat);
            else                     Graphics.Blit(t, rt);   // フォールバック（最悪でも生コピー）

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            var px = tex.GetPixels();
            Object.DestroyImmediate(tex);
            return px;
        }

        static Color[] ScaleAndReadPixels(Texture src, int size, bool linear)
        {
            var rw = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, rw);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, linear);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels();
            Object.DestroyImmediate(tex);
            return px;
        }

        static Color[] Solid(int size, Color c)
        {
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            return px;
        }

        // ========================= フォルダ =========================

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            string leaf   = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
