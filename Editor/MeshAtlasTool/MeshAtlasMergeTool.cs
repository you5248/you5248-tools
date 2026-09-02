using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace you5248.MeshAtlasTool
{

    /// <summary>
    /// 複数のメッシュ・マテリアルを 1 つに統合し、テクスチャをアトラス化するエディタツール。
    /// Albedo / Normal / Emission / Metallic+Smoothness をまとめて 1 枚のアトラスにパックする。
    /// マテリアルごとに SubMesh を分けたまま統合する「Multi SubMesh」モードも備える。
    /// </summary>
    public class MeshAtlasMergeTool : EditorWindow
    {
        private enum ToolMode    { MergeAndAtlas, AtlasOnly }
        private enum DiffMatMode { Atlas, MultiSubMesh }
        private enum OutputMode  { CreateNew, ReplaceInPlace }

        private ToolMode    toolMode     = ToolMode.MergeAndAtlas;
        private DiffMatMode diffMode     = DiffMatMode.Atlas;
        private OutputMode  outputMode   = OutputMode.CreateNew;
        private int         atlasSize    = 2048;
        private string      outputFolder = "Assets/you5248/MeshAtlasTool/Generated";
        private string      outputName   = "AtlasMesh";
        private bool        saveAsPng        = true;
        private bool        disableReadWrite = true;
        private bool        includeChildren  = false;
        private bool        disableOriginals = true;
        private bool        setEditorOnly    = true;
        private Vector2     scroll;

        // アトラス プレビュー（非破壊。アセットは生成しない）
        private Texture2D    previewAtlas;
        private Rect[]       previewRects;
        private Material[]   previewMats;
        private Vector2Int[] previewSrcSizes;        // 各マテリアルの元 albedo 解像度（0,0=テクスチャ無し）
        private int          previewAtlasSize;
        private int          previewRecommendedSize; // 元解像度・占有面積から算出した推奨サイズ
        private string       previewTilingReport;     // UVが[0,1]外（タイリング）のメッシュ一覧。空=無し
        private bool         previewRebuildRequested;

        // マテリアル共有チェック用
        private Dictionary<Material, List<GameObject>> sharedUsers;
        private bool shareCheckFoldout           = true;
        private bool shareCheckIncludeEditorOnly = false;

        // リバート
        private AtlasRevertData revertTarget;
        private bool revertFoldout = true;

        // タブ
        private int activeTab = 0;
        private static readonly string[] TabLabels = { "Atlas Merge", "Utility" };

        // Utility: Missing 探索
        private List<GameObject> missingMeshResults   = new List<GameObject>();
        private List<GameObject> missingScriptResults = new List<GameObject>();
        private Vector2          utilityScroll;
        private bool             missingMeshFoldout   = true;
        private bool             missingScriptFoldout = true;

        private static readonly string[] SizeLabels = { "512", "1024", "2048", "4096" };
        private static readonly int[]    SizeValues = {  512,   1024,   2048,   4096 };

        private const int   AtlasPadding = 2;     // アトラス内テクスチャ間の余白(px)
        private const float NoLossScale  = 0.97f; // この倍率以上で収まれば実質ロスなしとみなす

        [MenuItem("you5248/Mesh Atlas Merge")]
        static void Open() => GetWindow<MeshAtlasMergeTool>("Atlas Merge");

        void OnSelectionChange() { sharedUsers = null; ClearPreview(); Repaint(); }

        void OnDisable() => ClearPreview();

        // ========================= GUI =========================

        void OnGUI()
        {
            activeTab = GUILayout.Toolbar(activeTab, TabLabels);

            if (activeTab == 1) { DrawUtilityTab(); return; }

            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("Mesh Atlas Merge Tool", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // ── 選択状況 ──
            var targets    = GetSelectedMeshObjects();
            int uniqueMats = targets.SelectMany(go => go.GetComponent<MeshRenderer>().sharedMaterials)
                                    .Where(m => m != null).Distinct().Count();
            bool enoughToMerge = TotalSubmeshSlots(targets) >= 2;

            EditorGUILayout.HelpBox(
                $"選択中のメッシュ: {targets.Count} 個（マテリアルスロット計: {TotalSubmeshSlots(targets)}）\n" +
                $"ユニークマテリアル数: {uniqueMats}",
                targets.Count == 0 ? MessageType.None : MessageType.Info);

            EditorGUI.BeginChangeCheck();
            includeChildren = EditorGUILayout.Toggle(
                new GUIContent("子オブジェクトを含める", "選択したオブジェクトの子階層にある MeshFilter もまとめて対象にします。"),
                includeChildren);
            if (EditorGUI.EndChangeCheck()) sharedUsers = null;

            EditorGUILayout.Space();

            // ── モード ──
            EditorGUILayout.LabelField("モード", EditorStyles.boldLabel);
            toolMode = (ToolMode)EditorGUILayout.EnumPopup(
                new GUIContent("統合モード", "MergeAndAtlas: メッシュを 1 つに統合。  AtlasOnly: 統合せずアトラス化のみ。"),
                toolMode);

            bool multiMat = uniqueMats >= 2;

            if (toolMode == ToolMode.MergeAndAtlas)
            {
                EditorGUILayout.HelpBox(
                    "選択したメッシュを 1 つのメッシュに統合します。\n" +
                    "複数マテリアルがある場合の扱いは下の「差分マテリアル」で選びます。\n" +
                    "  Atlas        … 全テクスチャを 1 枚のアトラスにまとめ、1 マテリアルに統合\n" +
                    "  MultiSubMesh … テクスチャはそのまま、マテリアルごとに SubMesh として統合\n\n" +
                    "タイル状（UV が 0–1 を超える）マテリアルでは UV マッピングが変わる場合があります。",
                    MessageType.Info);
            }
            else
            {
                if (outputMode == OutputMode.CreateNew)
                    EditorGUILayout.HelpBox(
                        "メッシュは統合せず、各メッシュのアトラス化（UV リマップ）のみ行います。\n" +
                        "元のオブジェクトと同じ位置・回転・スケールで新規 GameObject を生成します。\n" +
                        "マテリアルは共有アトラスに 1 つにまとめられます。\n\n" +
                        "UV マッピングが変わる場合があります。",
                        MessageType.Info);
                else
                    EditorGUILayout.HelpBox(
                        "元のオブジェクトの Mesh と Material をアトラス結果に直接置き換えます。\n" +
                        "新規 GameObject は作成されません。\n\n" +
                        "UV マッピングが変わる場合があります。",
                        MessageType.Info);
            }

            EditorGUILayout.Space();

            // ── 設定 ──
            EditorGUILayout.LabelField("設定", EditorStyles.boldLabel);
            outputName = EditorGUILayout.TextField("出力名", outputName);

            EditorGUILayout.BeginHorizontal();
            outputFolder = EditorGUILayout.TextField("出力フォルダ", outputFolder);
            if (GUILayout.Button("...", GUILayout.Width(28)))
            {
                string picked = EditorUtility.OpenFolderPanel("出力フォルダを選択", Application.dataPath, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    string rel = ToAssetsRelativePath(picked);
                    if (rel != null) outputFolder = rel;
                    else EditorUtility.DisplayDialog("無効なフォルダ", "Assets フォルダ内を選択してください。", "OK");
                }
            }
            EditorGUILayout.EndHorizontal();

            bool folderValid = IsValidAssetsFolderPath(outputFolder);
            if (!folderValid)
                EditorGUILayout.HelpBox("出力フォルダは Assets/ 以下のパスを指定してください。", MessageType.Error);

            atlasSize = EditorGUILayout.IntPopup("アトラスサイズ", atlasSize, SizeLabels, SizeValues);

            saveAsPng        = EditorGUILayout.Toggle("テクスチャを PNG で保存", saveAsPng);
            disableReadWrite = EditorGUILayout.Toggle(new GUIContent(
                "Disable Read/Write",
                "生成メッシュの CPU コピーを破棄してメモリを削減します。"),
                disableReadWrite);

            EditorGUILayout.Space();

            // ── 差分マテリアル（MergeAndAtlas かつ複数マテリアル時のみ有効） ──
            bool diffRelevant = toolMode == ToolMode.MergeAndAtlas && multiMat;
            using (new EditorGUI.DisabledScope(!diffRelevant))
            {
                diffMode = (DiffMatMode)EditorGUILayout.EnumPopup(
                    new GUIContent("差分マテリアル", "複数マテリアルの統合方法。Atlas=1 枚にまとめる / MultiSubMesh=SubMesh で保持。"),
                    diffMode);
            }
            if (toolMode == ToolMode.MergeAndAtlas && !multiMat)
                EditorGUILayout.LabelField("    （マテリアルが 1 種類のため差分設定は不要）", EditorStyles.miniLabel);
            else if (toolMode == ToolMode.AtlasOnly)
                EditorGUILayout.LabelField("    （AtlasOnly では使用しません）", EditorStyles.miniLabel);

            // ── 出力方法（AtlasOnly 時のみ有効） ──
            using (new EditorGUI.DisabledScope(toolMode != ToolMode.AtlasOnly))
            {
                outputMode = (OutputMode)EditorGUILayout.EnumPopup(
                    new GUIContent("出力方法", "CreateNew=新規 GameObject を生成 / ReplaceInPlace=元オブジェクトを直接置換。"),
                    outputMode);
            }
            if (toolMode == ToolMode.MergeAndAtlas)
                EditorGUILayout.LabelField("    （MergeAndAtlas は常に新規生成）", EditorStyles.miniLabel);

            EditorGUILayout.Space();

            // ── 元オブジェクトの後処理 ──
            bool showOriginalOptions = !(toolMode == ToolMode.AtlasOnly && outputMode == OutputMode.ReplaceInPlace);
            using (new EditorGUI.DisabledScope(!showOriginalOptions))
            {
                disableOriginals = EditorGUILayout.Toggle(
                    new GUIContent("元メッシュを無効化", "統合後、元のオブジェクトを SetActive(false) にします。"),
                    disableOriginals);
                setEditorOnly = EditorGUILayout.Toggle(
                    new GUIContent("元メッシュを EditorOnly に", "統合後、元のオブジェクトのタグを EditorOnly にしてビルドから除外します。"),
                    setEditorOnly);
            }
            if (!showOriginalOptions)
                EditorGUILayout.LabelField("    （ReplaceInPlace は元オブジェクトをそのまま置換します）", EditorStyles.miniLabel);

            EditorGUILayout.Space();

            // ── 実行 ──
            string buttonLabel = toolMode == ToolMode.MergeAndAtlas ? "Merge" : "Apply Atlas";
            using (new EditorGUI.DisabledScope(!enoughToMerge || !folderValid))
            {
                if (GUILayout.Button(buttonLabel, GUILayout.Height(32)))
                    Execute(targets);
            }

            if (!enoughToMerge)
                EditorGUILayout.HelpBox("メッシュを 2 つ以上、またはサブメッシュ（複数マテリアル）を持つメッシュを選択してください。", MessageType.Warning);

            DrawAtlasPreview(targets, multiMat);

            EditorGUILayout.Space();
            DrawSharedMaterialCheck(targets);

            EditorGUILayout.Space();
            DrawRevertSection();

            EditorGUILayout.EndScrollView();
        }

        // ========================= Utility タブ =========================

        void DrawUtilityTab()
        {
            utilityScroll = EditorGUILayout.BeginScrollView(utilityScroll);

            EditorGUILayout.LabelField("Utility", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // ── Missing 探索 ──
            EditorGUILayout.LabelField("Missing 探索", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "シーン内の全オブジェクトから Missing を探索します（非アクティブを含む）。",
                MessageType.Info);

            if (GUILayout.Button("Missing を探索", GUILayout.Height(30)))
                RunMissingSearch();

            EditorGUILayout.Space();

            // ── Missing sharedMesh 一覧 ──
            missingMeshFoldout = EditorGUILayout.Foldout(missingMeshFoldout,
                $"MeshFilter の sharedMesh が Missing ({missingMeshResults.Count})", true);

            if (missingMeshFoldout)
            {
                if (missingMeshResults.Count == 0)
                {
                    EditorGUILayout.LabelField("  該当なし", EditorStyles.miniLabel);
                }
                else
                {
                    if (GUILayout.Button($"すべて選択 ({missingMeshResults.Count})"))
                        Selection.objects = missingMeshResults.ToArray();

                    foreach (var go in missingMeshResults)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.ObjectField(go, typeof(GameObject), true);
                        if (GUILayout.Button("Ping", GUILayout.Width(44)))
                            EditorGUIUtility.PingObject(go);
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }

            EditorGUILayout.Space();

            // ── Missing Script 一覧 ──
            missingScriptFoldout = EditorGUILayout.Foldout(missingScriptFoldout,
                $"Missing Script を持つオブジェクト ({missingScriptResults.Count})", true);

            if (missingScriptFoldout)
            {
                if (missingScriptResults.Count == 0)
                {
                    EditorGUILayout.LabelField("  該当なし", EditorStyles.miniLabel);
                }
                else
                {
                    if (GUILayout.Button($"すべて選択 ({missingScriptResults.Count})"))
                        Selection.objects = missingScriptResults.ToArray();

                    foreach (var go in missingScriptResults)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.ObjectField(go, typeof(GameObject), true);
                        if (GUILayout.Button("Ping", GUILayout.Width(44)))
                            EditorGUIUtility.PingObject(go);
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }

            EditorGUILayout.Space();

            // ── Prefab から復元 ──
            EditorGUILayout.LabelField("Prefab から復元", EditorStyles.boldLabel);

            var selectedMeshObjs = Selection.gameObjects
                .Where(go => go.GetComponent<MeshFilter>() != null || go.GetComponent<MeshRenderer>() != null)
                .ToList();

            int prefabCount   = selectedMeshObjs.Count(go => PrefabUtility.GetCorrespondingObjectFromSource(go) != null);
            int noPrefabCount = selectedMeshObjs.Count - prefabCount;

            EditorGUILayout.HelpBox(
                $"選択中: {selectedMeshObjs.Count} オブジェクト\n" +
                $"  Prefab 接続あり: {prefabCount} 個 … Prefab から復元できます\n" +
                $"  Prefab 接続なし: {noPrefabCount} 個 … スキップされます",
                selectedMeshObjs.Count == 0 ? MessageType.None : MessageType.Info);

            using (new EditorGUI.DisabledScope(prefabCount == 0))
            {
                if (GUILayout.Button($"選択中のメッシュを Prefab から復元 ({prefabCount} 個)", GUILayout.Height(28)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Restore from Prefab",
                        $"{prefabCount} 個のオブジェクトを Prefab から復元します。",
                        "実行", "キャンセル"))
                    {
                        RestoreFromPrefabBatch(selectedMeshObjs);
                        Debug.Log($"[Utility] Prefab 復元完了: {prefabCount} 個");
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        void RunMissingSearch()
        {
            missingMeshResults.Clear();
            missingScriptResults.Clear();

            var allObjects = FindObjectsOfType<GameObject>(includeInactive: true);

            foreach (var go in allObjects)
            {
                // MeshFilter の sharedMesh が null
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh == null)
                    missingMeshResults.Add(go);

                // Missing Script（コンポーネントが null）
                var components = go.GetComponents<Component>();
                if (components.Any(c => c == null))
                    missingScriptResults.Add(go);
            }

            missingMeshResults   = missingMeshResults.OrderBy(go => go.name).ToList();
            missingScriptResults = missingScriptResults.OrderBy(go => go.name).ToList();

            Debug.Log($"[Utility] Missing Mesh: {missingMeshResults.Count} 個 / Missing Script: {missingScriptResults.Count} 個");
        }

        void DrawRevertSection()
        {
            revertFoldout = EditorGUILayout.Foldout(revertFoldout, "Revert", true);
            if (!revertFoldout) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            revertTarget = (AtlasRevertData)EditorGUILayout.ObjectField(
                "リバートデータ", revertTarget, typeof(AtlasRevertData), false);

            if (revertTarget != null)
            {
                EditorGUILayout.LabelField($"モード: {revertTarget.mode}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"対象数: {revertTarget.originals.Count} オブジェクト", EditorStyles.miniLabel);
            }

            using (new EditorGUI.DisabledScope(revertTarget == null))
            {
                if (GUILayout.Button("Execute Revert", GUILayout.Height(28)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Confirm Revert",
                        "リバートを実行しますか？",
                        "実行", "キャンセル"))
                    {
                        DoRevert(revertTarget);
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        void DoRevert(AtlasRevertData data)
        {
            bool hasEditorOnly = HasEditorOnlyTag();
            int restored = 0;
            int missing  = 0;

            switch (data.mode)
            {
                case AtlasRevertData.ModeAtTime.AtlasOnly_ReplaceInPlace:
                    foreach (var entry in data.originals)
                    {
                        var target = ResolveTarget(entry);
                        if (target == null) { missing++; continue; }
                        var fallbackMesh = entry.originalMeshes?.Length > 0 ? entry.originalMeshes[0] : null;
                        RestoreMeshAndMaterial(target, fallbackMesh, entry.originalMaterials);
                        restored++;
                    }
                    break;

                case AtlasRevertData.ModeAtTime.AtlasOnly_CreateNew:
                case AtlasRevertData.ModeAtTime.MergeAndAtlas_CreateNew:
                    var createdRoot = ResolveCreatedRoot(data);
                    if (createdRoot != null)
                        Undo.DestroyObjectImmediate(createdRoot);
                    else
                        Debug.LogWarning($"[AtlasMerge] 生成オブジェクト '{data.createdRootName}' が見つからないため削除をスキップしました。");

                    foreach (var entry in data.originals)
                    {
                        var target = ResolveTarget(entry);
                        if (target == null) { missing++; continue; }
                        Undo.RecordObject(target, "Revert Original");
                        target.SetActive(entry.wasActive);
                        if (hasEditorOnly || entry.originalTag != "EditorOnly")
                            target.tag = entry.originalTag;
                        restored++;
                    }
                    break;
            }

            AssetDatabase.SaveAssets();

            if (restored == 0 && data.originals.Count > 0)
                Debug.LogWarning($"[AtlasMerge] リバート失敗: {data.name} — 対象オブジェクトが1件も見つかりませんでした（欠落 {missing}件）。変換時と別のシーンを開いているか、対象が削除・リネームされています。");
            else if (missing > 0)
                Debug.LogWarning($"[AtlasMerge] リバート一部完了: {data.name} — 復元 {restored}件 / 欠落 {missing}件");
            else
                Debug.Log($"[AtlasMerge] リバート完了: {data.name} — 復元 {restored}件");
        }

        /// <summary>
        /// リバート対象を解決する。直接参照が生きていればそれを、
        /// 失われていれば GlobalObjectId 文字列から復元する。
        /// </summary>
        static GameObject ResolveTarget(AtlasRevertData.OriginalEntry entry)
        {
            if (entry.target != null) return entry.target;
            return ResolveByGlobalId(entry.targetGlobalId);
        }

        /// <summary>
        /// CreateNew 系モードで生成した親を解決する。
        /// 生成直後はシーン未保存で GlobalObjectId が確定しないことがあるため、名前でもフォールバックする。
        /// </summary>
        static GameObject ResolveCreatedRoot(AtlasRevertData data)
        {
            if (data.createdRoot != null) return data.createdRoot;

            var byId = ResolveByGlobalId(data.createdRootGlobalId);
            if (byId != null) return byId;

            if (!string.IsNullOrEmpty(data.createdRootName))
            {
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    if (root.name == data.createdRootName)
                        return root;
            }
            return null;
        }

        static GameObject ResolveByGlobalId(string idString)
        {
            if (string.IsNullOrEmpty(idString)) return null;
            if (!GlobalObjectId.TryParse(idString, out var id)) return null;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
        }

        /// <summary>
        /// 選択オブジェクトを Prefab 接続元の Mesh/Material に一括復元する。
        /// Undo に対応しており、まとめて元に戻せる。
        /// </summary>
        void RestoreFromPrefabBatch(List<GameObject> targets)
        {
            // Prefab 接続があるものだけを対象に、復元する値を集める
            var ops = new List<(MeshFilter mf, Mesh mesh, MeshRenderer mr, Material[] mats)>();

            foreach (var go in targets)
            {
                var prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (prefabSource == null) continue;

                var mf = go.GetComponent<MeshFilter>();
                var mr = go.GetComponent<MeshRenderer>();
                var restoreMesh = prefabSource.GetComponent<MeshFilter>()?.sharedMesh;
                var restoreMats = prefabSource.GetComponent<MeshRenderer>()?.sharedMaterials;

                ops.Add((mf, restoreMesh, mr, restoreMats));
            }

            if (ops.Count == 0) return;

            // 全コンポーネントをまとめて Undo に記録してから復元
            var toRecord = ops
                .SelectMany(op => new Object[] { op.mf, op.mr })
                .Where(o => o != null)
                .ToArray();

            Undo.RecordObjects(toRecord, "Revert from Prefab");

            foreach (var (mf, mesh, mr, mats) in ops)
            {
                if (mf != null) mf.sharedMesh       = mesh;
                if (mr != null) mr.sharedMaterials  = mats;
            }
        }

        /// <summary>
        /// Prefab 接続があればその Mesh/Material で、なければ fallback で復元する。
        /// </summary>
        void RestoreMeshAndMaterial(GameObject target, Mesh fallbackMesh, Material[] fallbackMaterials)
        {
            var mf = target.GetComponent<MeshFilter>();
            var mr = target.GetComponent<MeshRenderer>();
            if (mf == null && mr == null) return;

            var prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(target);

            Mesh       restoreMesh;
            Material[] restoreMaterials;

            if (prefabSource != null)
            {
                restoreMesh      = prefabSource.GetComponent<MeshFilter>()?.sharedMesh ?? fallbackMesh;
                restoreMaterials = prefabSource.GetComponent<MeshRenderer>()?.sharedMaterials ?? fallbackMaterials;
                Debug.Log($"[Revert] {target.name}: Prefab から復元");
            }
            else
            {
                restoreMesh      = fallbackMesh;
                restoreMaterials = fallbackMaterials;
                Debug.Log($"[Revert] {target.name}: RevertData から復元");
            }

            if (mf != null) { Undo.RecordObject(mf, "Revert Mesh");     mf.sharedMesh      = restoreMesh; }
            if (mr != null) { Undo.RecordObject(mr, "Revert Material"); mr.sharedMaterials = restoreMaterials; }
        }

        void DrawSharedMaterialCheck(List<GameObject> targets)
        {
            shareCheckFoldout = EditorGUILayout.Foldout(shareCheckFoldout, "Material Share Check", true);
            if (!shareCheckFoldout) return;

            if (targets.Count == 0)
            {
                EditorGUILayout.LabelField("  対象メッシュが選択されていません。", EditorStyles.miniLabel);
                return;
            }

            // 統合対象のマテリアルを、対象外のオブジェクトが共有していないか調べる
            EditorGUI.BeginChangeCheck();
            shareCheckIncludeEditorOnly = EditorGUILayout.Toggle("Include EditorOnly", shareCheckIncludeEditorOnly);
            if (EditorGUI.EndChangeCheck()) sharedUsers = null;

            // キャッシュ生成
            if (sharedUsers == null)
                sharedUsers = BuildSharedUsersMap(targets);

            bool anyConflict = false;
            foreach (var kvp in sharedUsers)
            {
                var mat   = kvp.Key;
                var users = kvp.Value;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // マテリアル表示
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(mat, typeof(Material), false, GUILayout.Width(200));
                if (users.Count == 0)
                {
                    var prevColor = GUI.color;
                    GUI.color = Color.green;
                    EditorGUILayout.LabelField("✔ 選択対象のみで使用");
                    GUI.color = prevColor;
                }
                else
                {
                    anyConflict = true;
                    var prevColor = GUI.color;
                    GUI.color = new Color(1f, 0.6f, 0.2f);
                    EditorGUILayout.LabelField($"⚠ 共有: {users.Count} 個の対象外オブジェクトで使用中");
                    GUI.color = prevColor;
                }
                EditorGUILayout.EndHorizontal();

                // 使用中オブジェクト一覧
                foreach (var go in users)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(16);
                    EditorGUILayout.ObjectField(go, typeof(GameObject), true);
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
            }

            if (anyConflict)
            {
                EditorGUILayout.HelpBox(
                    "⚠ 対象外のオブジェクトが同じマテリアルを共有しています。\n" +
                    "統合後はそれらのマテリアル参照を手動で差し替える必要があります。",
                    MessageType.Warning);

                var allConflicting = sharedUsers.Values
                    .SelectMany(list => list)
                    .Distinct()
                    .ToArray();

                if (GUILayout.Button($"共有先オブジェクトを選択に追加 ({allConflicting.Length} 個)"))
                    Selection.objects = Selection.objects.Union(allConflicting).ToArray();
            }
        }

        Dictionary<Material, List<GameObject>> BuildSharedUsersMap(List<GameObject> targets)
        {
            var targetSet  = new HashSet<GameObject>(targets);
            var targetMats = targets
                .SelectMany(go => go.GetComponent<MeshRenderer>().sharedMaterials)
                .Where(m => m != null)
                .Distinct()
                .ToList();

            var allRenderers = FindObjectsOfType<MeshRenderer>(includeInactive: true);

            var result = new Dictionary<Material, List<GameObject>>();
            foreach (var mat in targetMats)
            {
                result[mat] = allRenderers
                    .Where(mr => !targetSet.Contains(mr.gameObject)
                              && mr.sharedMaterials.Contains(mat)
                              && (shareCheckIncludeEditorOnly || mr.gameObject.tag != "EditorOnly"))
                    .Select(mr => mr.gameObject)
                    .OrderBy(go => go.name)
                    .ToList();
            }
            return result;
        }

        List<GameObject> GetSelectedMeshObjects()
        {
            IEnumerable<GameObject> candidates;

            if (includeChildren)
            {
                candidates = Selection.gameObjects
                    .SelectMany(go => go.GetComponentsInChildren<MeshFilter>(includeInactive: true))
                    .Select(mf => mf.gameObject)
                    .Distinct();
            }
            else
            {
                candidates = Selection.gameObjects;
            }

            return candidates
                .Where(go =>
                {
                    var mf = go.GetComponent<MeshFilter>();
                    var mr = go.GetComponent<MeshRenderer>();
                    return mf != null && mf.sharedMesh != null
                        && mr != null && mr.sharedMaterials.Any(m => m != null);
                })
                .ToList();
        }

        // ========================= アトラス プレビュー =========================

        // 生成前に「各テクスチャがアトラスのどこに・どの解像度で並ぶか」を非破壊で確認する。
        // 本番と同じ PackAlbedoAtlas を通すので、ここで見える配置＝実際に生成される配置。
        void DrawAtlasPreview(List<GameObject> targets, bool multiMat)
        {
            // アトラスを実際に作るモードのときだけ意味がある
            bool atlasWillBuild =
                toolMode == ToolMode.AtlasOnly
                || (toolMode == ToolMode.MergeAndAtlas && multiMat && diffMode == DiffMatMode.Atlas);

            if (!atlasWillBuild) { ClearPreview(); previewRebuildRequested = false; return; }

            // ボタン押下はフラグ化し、Layout イベントで実ビルドする
            // （イベント途中でレイアウト要素を増やして不整合を起こさないため）
            if (previewRebuildRequested && Event.current.type == EventType.Layout)
            {
                previewRebuildRequested = false;
                BuildPreview(targets);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("アトラス プレビュー", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(targets.Count < 1))
            {
                if (GUILayout.Button("プレビュー生成 / 更新", GUILayout.Height(26)))
                {
                    previewRebuildRequested = true;
                    Repaint();
                }
            }
            EditorGUILayout.LabelField(
                "アセットは生成しません。アトラスサイズを変えて何度でも試せます。",
                EditorStyles.miniLabel);

            if (previewAtlas == null) return;

            if (previewAtlasSize != atlasSize)
                EditorGUILayout.HelpBox(
                    $"アトラスサイズが変更されています（プレビューは {previewAtlasSize}）。「更新」で反映します。",
                    MessageType.Warning);

            float fill = ComputeFillRatio(previewRects);
            EditorGUILayout.HelpBox(
                $"マテリアル数: {previewMats.Length}\n" +
                $"アトラス: {previewAtlasSize}×{previewAtlasSize}\n" +
                $"充填率: {fill * 100f:F1}%   余白: {(1f - fill) * 100f:F1}%",
                MessageType.Info);

            DrawRecommendation();

            if (!string.IsNullOrEmpty(previewTilingReport))
                EditorGUILayout.HelpBox(
                    "次のメッシュは UV が [0,1] の外＝メッシュ自体にタイリングが焼き込まれています。\n" +
                    "（マテリアルの Tiling が 1,1 でも、メッシュUVが 0〜N なら繰り返します）\n" +
                    "アトラス化では UV が枠外へはみ出して崩れます:\n" + previewTilingReport,
                    MessageType.Warning);

            DrawPreviewImage();
            EditorGUILayout.Space();
            DrawPreviewLegend();
        }

        // 元解像度に対して過不足のないサイズを提案し、ワンクリックで適用できるようにする。
        void DrawRecommendation()
        {
            int rec = previewRecommendedSize;
            if (rec == previewAtlasSize)
            {
                EditorGUILayout.HelpBox("このアトラスサイズは元テクスチャの解像度に対して適切です。", MessageType.None);
                return;
            }

            string msg = rec < previewAtlasSize
                ? "元テクスチャの解像度に対してアトラスが大きく、余白が多い状態です。\n" +
                  $"{rec}×{rec} で十分です（画質はほぼ同等のままメモリを削減できます）。"
                : "現在のサイズでは一部テクスチャがアトラス内で縮小され、画質が落ちます。\n" +
                  $"{rec}×{rec} 以上を推奨します。";

            EditorGUILayout.HelpBox(msg, rec < previewAtlasSize ? MessageType.Info : MessageType.Warning);

            using (new EditorGUI.DisabledScope(rec == atlasSize))
            {
                if (GUILayout.Button($"推奨サイズ {rec}×{rec} を適用してプレビュー更新", GUILayout.Height(24)))
                {
                    atlasSize = rec;
                    previewRebuildRequested = true;
                    Repaint();
                }
            }
        }

        void DrawPreviewImage()
        {
            float size = Mathf.Clamp(EditorGUIUtility.currentViewWidth - 40f, 64f, 320f);
            Rect  box  = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));

            // 余白が分かるよう、背景を暗色で塗ってからアトラスを重ねる
            EditorGUI.DrawRect(box, new Color(0.12f, 0.12f, 0.12f, 1f));
            EditorGUI.DrawPreviewTexture(box, previewAtlas, null, ScaleMode.ScaleToFit);

            for (int i = 0; i < previewRects.Length; i++)
            {
                var r = previewRects[i];
                // PackTextures の Rect は左下原点(UV)。GUI は左上原点なので y を反転する。
                float x = box.x + r.x * box.width;
                float y = box.y + (1f - r.y - r.height) * box.height;
                var   gr = new Rect(x, y, r.width * box.width, r.height * box.height);

                Color c = IndexColor(i);
                DrawRectOutline(gr, c, 1.5f);

                // 番号ラベル（暗背景＋色文字で視認性を確保）
                var lr = new Rect(gr.x + 1f, gr.y + 1f, 16f, 14f);
                EditorGUI.DrawRect(lr, new Color(0f, 0f, 0f, 0.65f));
                var st = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = c;
                GUI.Label(lr, i.ToString(), st);
            }
        }

        void DrawPreviewLegend()
        {
            for (int i = 0; i < previewMats.Length; i++)
            {
                var r  = previewRects[i];
                int pw = Mathf.RoundToInt(r.width  * previewAtlasSize);
                int ph = Mathf.RoundToInt(r.height * previewAtlasSize);
                string matName = previewMats[i] != null ? previewMats[i].name : "(null)";

                // 元解像度より目立って小さい枠に押し込まれている＝縮小（画質低下）。
                // パディング由来の数px減を誤検知しないよう許容幅(NoLossScale)を持たせる。
                var  src        = previewSrcSizes != null ? previewSrcSizes[i] : Vector2Int.zero;
                bool downscaled = src.x > 0 && (pw < src.x * NoLossScale || ph < src.y * NoLossScale);
                string right    = downscaled ? $"{pw}×{ph} px  ⚠縮小(元 {src.x}×{src.y})" : $"{pw}×{ph} px";

                EditorGUILayout.BeginHorizontal();
                var sw = GUILayoutUtility.GetRect(14f, 14f, GUILayout.Width(14f), GUILayout.Height(14f));
                EditorGUI.DrawRect(sw, IndexColor(i));
                EditorGUILayout.LabelField($"[{i}] {matName}", right);
                EditorGUILayout.EndHorizontal();
            }
        }

        void BuildPreview(List<GameObject> targets)
        {
            ClearPreview();

            var mats = targets
                .SelectMany(go => go.GetComponent<MeshRenderer>().sharedMaterials)
                .Where(m => m != null)
                .Distinct()
                .ToList();
            if (mats.Count == 0) return;

            // 元 albedo の実解像度を控える（推奨サイズ算出・縮小判定に使う）
            var srcSizes = new Vector2Int[mats.Count];
            for (int i = 0; i < mats.Count; i++)
            {
                var t = (mats[i].HasProperty("_MainTex") ? mats[i].GetTexture("_MainTex") : null) as Texture2D;
                srcSizes[i] = t != null ? new Vector2Int(t.width, t.height) : Vector2Int.zero;
            }

            int defSize = Mathf.Max(32, atlasSize / Mathf.CeilToInt(Mathf.Sqrt(mats.Count)));
            var (atlas, rects) = PackAlbedoAtlas(mats, defSize);

            previewAtlas           = atlas;
            previewRects           = rects;
            previewMats            = mats.ToArray();
            previewSrcSizes        = srcSizes;
            previewAtlasSize       = atlasSize;
            previewRecommendedSize = RecommendAtlasSize(srcSizes);
            previewTilingReport    = BuildTilingReport(targets);
        }

        void ClearPreview()
        {
            if (previewAtlas != null) Object.DestroyImmediate(previewAtlas);
            previewAtlas       = null;
            previewRects       = null;
            previewMats        = null;
            previewSrcSizes     = null;
            previewTilingReport = null;
        }

        // 充填率＝各 Rect の面積合計（正規化座標なのでそのまま占有率になる。パディングは含まない）
        static float ComputeFillRatio(Rect[] rects)
        {
            float s = 0f;
            foreach (var r in rects) s += r.width * r.height;
            return Mathf.Clamp01(s);
        }

        // 実際のシェルフパッカーで候補サイズを試し、元解像度をほぼ保てる（縮小 ≲3%）
        // 最小のアトラスサイズを推奨する。タイトに詰めるので「512×4 なら 1024」を正しく返す。
        static int RecommendAtlasSize(Vector2Int[] srcSizes)
        {
            int matCount = Mathf.Max(1, srcSizes.Length);
            foreach (int size in SizeValues)   // { 512, 1024, 2048, 4096 } 昇順
            {
                int defSize = Mathf.Max(32, size / Mathf.CeilToInt(Mathf.Sqrt(matCount)));
                var eff = new Vector2Int[srcSizes.Length];
                for (int i = 0; i < srcSizes.Length; i++)
                    eff[i] = srcSizes[i].x > 0 ? srcSizes[i] : new Vector2Int(defSize, defSize);

                var (_, scale) = ComputeShelfRects(eff, size, AtlasPadding);
                if (scale >= NoLossScale) return size;   // この大きさで実質ロスなく収まる
            }
            return 4096;   // 4096 でも収まらない極端ケースは最大で
        }

        // シェルフ(行)パッキング。各テクスチャをネイティブ解像度で行に敷き詰め、
        // アトラスに収まらない場合のみ全体を一様スケールで縮小する。
        // 返り値の scale は採用した倍率（1=無縮小）。Rect は元の順序・正規化(左下原点)。
        static (Rect[] rects, float scale) ComputeShelfRects(Vector2Int[] sizes, int atlasSize, int padding)
        {
            var order = Enumerable.Range(0, sizes.Length)
                .OrderByDescending(i => sizes[i].y).ThenByDescending(i => sizes[i].x)
                .ToArray();

            // 1) 指定パディングでネイティブ解像度のまま入るか
            if (TryShelfPack(sizes, order, 1f, atlasSize, padding, out var native))
                return (native, 1f);

            // 2) パディングが詰まりの原因なだけなら、パディングを 0 にしてでもネイティブを優先する。
            //    （アトラスがちょうど埋まる 512×4→1024 のようなケースを原寸で通すため）
            if (padding > 0 && TryShelfPack(sizes, order, 1f, atlasSize, 0, out var tight))
                return (tight, 1f);

            // 3) それでも入らない → 収まる最大倍率を二分探索（指定パディングで）
            float  lo = 0f, hi = 1f;
            Rect[] best = null;
            for (int it = 0; it < 24; it++)
            {
                float mid = (lo + hi) * 0.5f;
                if (TryShelfPack(sizes, order, mid, atlasSize, padding, out var r)) { lo = mid; best = r; }
                else hi = mid;
            }
            if (best == null) TryShelfPack(sizes, order, lo, atlasSize, padding, out best);
            return (best, lo);
        }

        static bool TryShelfPack(Vector2Int[] sizes, int[] order, float scale, int atlasSize, int padding, out Rect[] rects)
        {
            rects = new Rect[sizes.Length];
            int cursorX = padding, cursorY = padding, shelfH = 0;

            foreach (int idx in order)
            {
                int w = Mathf.Max(1, Mathf.CeilToInt(sizes[idx].x * scale));
                int h = Mathf.Max(1, Mathf.CeilToInt(sizes[idx].y * scale));

                if (cursorX + w + padding > atlasSize)   // 行の右端を超える → 次の棚へ
                {
                    cursorY += shelfH + padding;
                    cursorX  = padding;
                    shelfH   = 0;
                }
                if (cursorX + w + padding > atlasSize) return false;  // 1 枚で幅オーバー
                if (cursorY + h + padding > atlasSize) return false;  // 縦オーバー

                // top-left 原点で配置 → bottom-left 原点(UV) に反転して正規化
                rects[idx] = new Rect(
                    cursorX / (float)atlasSize,
                    (atlasSize - cursorY - h) / (float)atlasSize,
                    w / (float)atlasSize,
                    h / (float)atlasSize);

                cursorX += w + padding;
                shelfH   = Mathf.Max(shelfH, h);
            }
            return true;
        }

        // 視認しやすい色を黄金比でずらして巡回割り当てする。
        static Color IndexColor(int i) => Color.HSVToRGB((i * 0.61803398875f) % 1f, 0.65f, 1f);

        static void DrawRectOutline(Rect r, Color c, float t)
        {
            EditorGUI.DrawRect(new Rect(r.x,        r.y,        r.width, t),        c); // 上
            EditorGUI.DrawRect(new Rect(r.x,        r.yMax - t, r.width, t),        c); // 下
            EditorGUI.DrawRect(new Rect(r.x,        r.y,        t,       r.height), c); // 左
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y,        t,       r.height), c); // 右
        }

        // ========================= Execute =========================

        void Execute(List<GameObject> targets)
        {
            EnsureFolder(outputFolder);

            // 実行ごとにユニークなサブフォルダを作り、全アセットをそこにまとめる
            string subFolderPath = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/{outputName}");
            string resolvedName  = Path.GetFileName(subFolderPath);
            AssetDatabase.CreateFolder(outputFolder, resolvedName);

            // サブフォルダを種類ごとに整理
            var folders = CreateSubFolders(subFolderPath);

            var uniqueMats = targets
                .SelectMany(go => go.GetComponent<MeshRenderer>().sharedMaterials)
                .Where(m => m != null)
                .Distinct()
                .ToList();

            // 復元データを作る（撤回時に元へ戻すため、変換前の状態を控える）
            // シーンが未保存だと GlobalObjectId が確定せず、ドメインリロード後のリバートが効かない
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                Debug.LogWarning("[AtlasMerge] シーンが未保存です。Unity再起動やスクリプト再コンパイル後にリバートできなくなる可能性があります。");

            var revertData = ScriptableObject.CreateInstance<AtlasRevertData>();
            foreach (var go in targets)
            {
                var mf = go.GetComponent<MeshFilter>();
                var mr = go.GetComponent<MeshRenderer>();
                revertData.originals.Add(new AtlasRevertData.OriginalEntry
                {
                    target            = go,
                    targetGlobalId    = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString(),
                    originalMeshes    = new Mesh[]     { mf.sharedMesh },
                    originalMaterials = mr.sharedMaterials.ToArray(),
                    wasActive         = go.activeSelf,
                    originalTag       = go.tag,
                });
            }

            var resultGO = new GameObject(resolvedName);
            Undo.RegisterCreatedObjectUndo(resultGO, "Atlas Merge");

            try
            {
                if (toolMode == ToolMode.AtlasOnly && outputMode == OutputMode.ReplaceInPlace)
                {
                    revertData.mode = AtlasRevertData.ModeAtTime.AtlasOnly_ReplaceInPlace;
                    Undo.DestroyObjectImmediate(resultGO);
                    resultGO = null;
                    AtlasOnlyReplace(targets, uniqueMats, resolvedName, folders);
                }
                else if (toolMode == ToolMode.AtlasOnly)
                {
                    revertData.mode        = AtlasRevertData.ModeAtTime.AtlasOnly_CreateNew;
                    revertData.createdRoot = resultGO;
                    AtlasOnly(targets, uniqueMats, resultGO, resolvedName, folders);
                }
                else
                {
                    revertData.mode        = AtlasRevertData.ModeAtTime.MergeAndAtlas_CreateNew;
                    revertData.createdRoot = resultGO;
                    if (uniqueMats.Count == 1)
                        MergeSingleMaterial(targets, uniqueMats[0], resultGO, resolvedName, folders);
                    else if (diffMode == DiffMatMode.Atlas)
                        MergeWithAtlas(targets, uniqueMats, resultGO, resolvedName, folders);
                    else
                        MergeSubMesh(targets, uniqueMats, resultGO, resolvedName, folders);
                }

                if (revertData.createdRoot != null)
                {
                    // 生成直後のオブジェクトはシーン保存まで GlobalObjectId が確定しないため、名前も控えておく
                    revertData.createdRootGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(revertData.createdRoot).ToString();
                    revertData.createdRootName     = revertData.createdRoot.name;
                }

                // 元メッシュの後処理（ReplaceInPlace 時はスキップ）
                bool isReplace = toolMode == ToolMode.AtlasOnly && outputMode == OutputMode.ReplaceInPlace;
                bool hasEditorOnly = HasEditorOnlyTag();
                foreach (var go in targets)
                {
                    if (!isReplace && disableOriginals)
                    {
                        Undo.RecordObject(go, "Disable Original");
                        go.SetActive(false);
                    }
                    if (!isReplace && setEditorOnly && hasEditorOnly)
                    {
                        Undo.RecordObject(go, "Set EditorOnly");
                        go.tag = "EditorOnly";
                    }
                }

                // リバートデータを保存
                AssetDatabase.CreateAsset(revertData, $"{folders.root}/RevertData.asset");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                if (resultGO != null) Selection.activeGameObject = resultGO;

                Debug.Log($"[AtlasMerge] 完了: {folders.root}");
            }
            catch (System.Exception e)
            {
                if (resultGO != null) Undo.DestroyObjectImmediate(resultGO);
                EditorUtility.DisplayDialog("エラー", e.Message, "OK");
                Debug.LogException(e);
            }
        }

        // ========================= アトラスのみ =========================

        /// <summary>
        /// 元のオブジェクトの Mesh/Material をアトラス結果に直接置き換える（ReplaceInPlace）。
        /// </summary>
        void AtlasOnlyReplace(List<GameObject> targets, List<Material> mats, string n, SaveFolders f)
        {
            int defSize = Mathf.Max(32, atlasSize / Mathf.CeilToInt(Mathf.Sqrt(mats.Count)));

            var (albedoAtlas, normalAtlas, emissionAtlas, metallicAtlas, rects) = BuildAtlas(mats, defSize);
            var newMat = BuildAtlasMaterial(mats, albedoAtlas, normalAtlas, emissionAtlas, metallicAtlas);

            // 同じ (元メッシュ, 各サブメッシュのマテリアル枠) なら統合結果も同一なのでキャッシュ共有
            var meshCache = new Dictionary<string, Mesh>();

            foreach (var src in targets)
            {
                var mf   = src.GetComponent<MeshFilter>();
                var mr   = src.GetComponent<MeshRenderer>();
                var flat = BuildFlattenedAtlasMesh(src, mats, rects, n, f, meshCache);

                Undo.RecordObject(mf, "Atlas Replace Mesh");
                Undo.RecordObject(mr, "Atlas Replace Material");
                mf.sharedMesh      = flat;
                mr.sharedMaterials = new Material[] { newMat };   // 全サブメッシュを 1 スロットに
            }

            SaveAtlasAssets(albedoAtlas, normalAtlas, emissionAtlas, metallicAtlas, newMat, n, f);
        }

        void AtlasOnly(List<GameObject> targets, List<Material> mats, GameObject resultGO, string n, SaveFolders f)
        {
            int defSize = Mathf.Max(32, atlasSize / Mathf.CeilToInt(Mathf.Sqrt(mats.Count)));

            var (albedoAtlas, normalAtlas, emissionAtlas, metallicAtlas, rects) = BuildAtlas(mats, defSize);
            var newMat = BuildAtlasMaterial(mats, albedoAtlas, normalAtlas, emissionAtlas, metallicAtlas);

            var meshCache = new Dictionary<string, Mesh>();

            foreach (var src in targets)
            {
                var flat = BuildFlattenedAtlasMesh(src, mats, rects, n, f, meshCache);

                var child = new GameObject(src.name);
                Undo.RegisterCreatedObjectUndo(child, "Atlas Only");
                child.transform.SetParent(resultGO.transform, worldPositionStays: false);
                child.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
                child.transform.localScale = src.transform.lossyScale;

                child.AddComponent<MeshFilter>().sharedMesh       = flat;
                child.AddComponent<MeshRenderer>().sharedMaterial = newMat;

                GameObjectUtility.SetStaticEditorFlags(child, GameObjectUtility.GetStaticEditorFlags(src));
            }

            SaveAtlasAssets(albedoAtlas, normalAtlas, emissionAtlas, metallicAtlas, newMat, n, f);
        }

        // ========================= 統合（単一マテリアル） =========================

        void MergeSingleMaterial(List<GameObject> targets, Material mat, GameObject resultGO, string n, SaveFolders f)
        {
            var mesh  = CombineWorldSpace(targets);
            mesh.name = n;
            SaveMesh(mesh, $"{f.meshes}/{n}.asset");

            resultGO.AddComponent<MeshFilter>().sharedMesh       = mesh;
            resultGO.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ========================= SubMesh 分割 =========================

        void MergeSubMesh(List<GameObject> targets, List<Material> mats, GameObject resultGO, string n, SaveFolders f)
        {
            var subCombines = new List<CombineInstance>();
            var tempMeshes  = new List<Mesh>();

            // マテリアルごとに、全オブジェクトの該当サブメッシュをワールド空間で 1 つにまとめ、
            // それを 1 サブメッシュとして並べる（テクスチャは分けたまま）。
            foreach (var mat in mats)
            {
                var combines = new List<CombineInstance>();
                foreach (var go in targets)
                {
                    var mesh   = go.GetComponent<MeshFilter>().sharedMesh;
                    var goMats = go.GetComponent<MeshRenderer>().sharedMaterials;
                    var xform  = go.transform.localToWorldMatrix;
                    int sub    = Mathf.Max(1, mesh.subMeshCount);
                    for (int s = 0; s < sub; s++)
                    {
                        Material sm = goMats.Length == 0 ? null
                                    : (s < goMats.Length ? goMats[s] : goMats[goMats.Length - 1]);
                        if (sm != mat) continue;
                        var ext = ExtractSubmesh(mesh, s, remap: false, rect: default);
                        tempMeshes.Add(ext);
                        combines.Add(new CombineInstance { mesh = ext, subMeshIndex = 0, transform = xform });
                    }
                }

                var sub1 = new Mesh { indexFormat = IndexFormat.UInt32 };
                sub1.CombineMeshes(combines.ToArray(), mergeSubMeshes: true, useMatrices: true);
                tempMeshes.Add(sub1);
                subCombines.Add(new CombineInstance { mesh = sub1, transform = Matrix4x4.identity });
            }

            var mesh2 = new Mesh { name = n, indexFormat = IndexFormat.UInt32 };
            mesh2.CombineMeshes(subCombines.ToArray(), mergeSubMeshes: false, useMatrices: false);
            mesh2.RecalculateBounds();

            foreach (var t in tempMeshes) Object.DestroyImmediate(t);

            SaveMesh(mesh2, $"{f.meshes}/{n}.asset");

            resultGO.AddComponent<MeshFilter>().sharedMesh        = mesh2;
            resultGO.AddComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
        }

        // ========================= アトラス統合 =========================

        void MergeWithAtlas(List<GameObject> targets, List<Material> mats, GameObject resultGO, string n, SaveFolders f)
        {
            int defSize = Mathf.Max(32, atlasSize / Mathf.CeilToInt(Mathf.Sqrt(mats.Count)));

            var (albedoAtlas, normalAtlas, emissionAtlas, metallicAtlas, rects) = BuildAtlas(mats, defSize);

            // 各オブジェクトの全サブメッシュを、対応マテリアルのアトラス枠へリマップしてワールド統合
            var combines   = new List<CombineInstance>();
            var tempMeshes = new List<Mesh>();

            foreach (var go in targets)
            {
                var mesh   = go.GetComponent<MeshFilter>().sharedMesh;
                var xform  = go.transform.localToWorldMatrix;
                int[] idxs = SubmeshMatIndices(go, mats);
                for (int s = 0; s < idxs.Length; s++)
                {
                    var ext = ExtractSubmesh(mesh, s, remap: true, rect: rects[idxs[s]]);
                    tempMeshes.Add(ext);
                    combines.Add(new CombineInstance { mesh = ext, subMeshIndex = 0, transform = xform });
                }
            }

            var finalMesh = new Mesh { name = n, indexFormat = IndexFormat.UInt32 };
            finalMesh.CombineMeshes(combines.ToArray(), mergeSubMeshes: true, useMatrices: true);
            finalMesh.RecalculateBounds();

            foreach (var t in tempMeshes) Object.DestroyImmediate(t);

            var newMat = BuildAtlasMaterial(mats, albedoAtlas, normalAtlas, emissionAtlas, metallicAtlas);

            SaveMesh(finalMesh, $"{f.meshes}/{n}.asset");
            SaveAtlasAssets(albedoAtlas, normalAtlas, emissionAtlas, metallicAtlas, newMat, n, f);

            resultGO.AddComponent<MeshFilter>().sharedMesh       = finalMesh;
            resultGO.AddComponent<MeshRenderer>().sharedMaterial = newMat;
        }

        // ========================= アトラス生成・保存 =========================

        // albedo を実際にパックして (アトラス, 各マテリアルの Rect) を返す。
        // BuildAtlas（本番）もプレビューもこれを共有するので、見える配置＝実際の配置になる。
        // Unity 標準の PackTextures は詰め込みが緩く必要以上に縮小するため、
        // 自前のシェルフ(行)パッカーでネイティブ解像度のままタイトに敷き詰める。
        (Texture2D albedoAtlas, Rect[] rects) PackAlbedoAtlas(List<Material> mats, int defSize)
        {
            var albedos = mats.Select(m => GetTex(m, "_MainTex", defSize, Color.white, sRGB: true)).ToArray();

            var sizes = new Vector2Int[albedos.Length];
            for (int i = 0; i < albedos.Length; i++)
                sizes[i] = new Vector2Int(albedos[i].width, albedos[i].height);

            var (rects, _)  = ComputeShelfRects(sizes, atlasSize, AtlasPadding);
            var albedoAtlas = PackByRects(albedos, rects, atlasSize, linear: false, defaultColor: Color.clear);

            // ピクセルはコピー済みなので中間テクスチャは破棄してよい。
            foreach (var t in albedos) if (t != null) Object.DestroyImmediate(t);

            return (albedoAtlas, rects);
        }

        (Texture2D albedo, Texture2D normal, Texture2D emission, Texture2D metallic, Rect[] rects)
            BuildAtlas(List<Material> mats, int defSize)
        {
            var (albedoAtlas, rects) = PackAlbedoAtlas(mats, defSize);

            var normals   = mats.Select(m => GetTex(m, "_BumpMap",     defSize, new Color(0.5f,0.5f,1f), sRGB: false)).ToArray();
            var emissions = mats.Select(m => GetTex(m, "_EmissionMap", defSize, Color.black,             sRGB: true )).ToArray();
            var metallics = mats.Select(m => GetMetallicTex(m, defSize)).ToArray();

            var normalAtlas   = PackByRects(normals,   rects, atlasSize, linear: true,  defaultColor: new Color(0.5f, 0.5f, 1f, 1f));
            var emissionAtlas = PackByRects(emissions, rects, atlasSize, linear: false, defaultColor: Color.black);
            var metallicAtlas = PackByRects(metallics, rects, atlasSize, linear: true,  defaultColor: Color.black);

            return (albedoAtlas, normalAtlas, emissionAtlas, metallicAtlas, rects);
        }

        Material BuildAtlasMaterial(List<Material> mats,
            Texture2D albedoAtlas, Texture2D normalAtlas, Texture2D emissionAtlas, Texture2D metallicAtlas)
        {
            var mat = new Material(mats[0].shader);
            if (mats[0].HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            mat.SetTexture("_MainTex",          albedoAtlas);
            mat.SetTexture("_BumpMap",          normalAtlas);
            mat.SetTexture("_EmissionMap",      emissionAtlas);
            mat.SetTexture("_MetallicGlossMap", metallicAtlas);
            mat.SetFloat("_Metallic",   1f);
            mat.SetFloat("_Glossiness", 1f);
            mat.EnableKeyword("_NORMALMAP");
            mat.EnableKeyword("_EMISSION");
            mat.EnableKeyword("_METALLICGLOSSMAP");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            return mat;
        }

        void SaveAtlasAssets(Texture2D albedo, Texture2D normal, Texture2D emission, Texture2D metallic, Material mat, string n, SaveFolders f)
        {
            if (saveAsPng)
            {
                var albedoTex   = SaveTexAsPng(albedo,   $"{f.textures}/{n}_Albedo.png",   isLinear: false, isNormalMap: false);
                var normalTex   = SaveTexAsPng(normal,   $"{f.textures}/{n}_Normal.png",   isLinear: true,  isNormalMap: true);
                var emissionTex = SaveTexAsPng(emission, $"{f.textures}/{n}_Emission.png", isLinear: false, isNormalMap: false);
                var metallicTex = SaveTexAsPng(metallic, $"{f.textures}/{n}_Metallic.png", isLinear: true,  isNormalMap: false);

                mat.SetTexture("_MainTex",          albedoTex);
                mat.SetTexture("_BumpMap",          normalTex);
                mat.SetTexture("_EmissionMap",      emissionTex);
                mat.SetTexture("_MetallicGlossMap", metallicTex);
            }
            else
            {
                SaveTex(albedo,   $"{f.textures}/{n}_Albedo.asset");
                SaveTex(normal,   $"{f.textures}/{n}_Normal.asset");
                SaveTex(emission, $"{f.textures}/{n}_Emission.asset");
                SaveTex(metallic, $"{f.textures}/{n}_Metallic.asset");
            }

            AssetDatabase.CreateAsset(mat, $"{f.materials}/{n}_Material.mat");
        }

        Texture2D SaveTexAsPng(Texture2D tex, string assetPath, bool isLinear, bool isNormalMap)
        {
            string fullPath = Application.dataPath + assetPath.Substring("Assets".Length);
            File.WriteAllBytes(fullPath, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture   = !isLinear;
                importer.textureType   = isNormalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        // ========================= テクスチャ操作 =========================

        Texture2D GetTex(Material mat, string prop, int defSize, Color defColor, bool sRGB)
        {
            if (mat.HasProperty(prop))
            {
                var t = mat.GetTexture(prop) as Texture2D;
                if (t != null) return MakeReadable(t, !sRGB);
            }
            return SolidTex(defSize, defColor);
        }

        Texture2D GetMetallicTex(Material mat, int defSize)
        {
            if (mat.HasProperty("_MetallicGlossMap"))
            {
                var t = mat.GetTexture("_MetallicGlossMap") as Texture2D;
                if (t != null) return MakeReadable(t, linear: true);
            }
            float m = mat.HasProperty("_Metallic")   ? mat.GetFloat("_Metallic")   : 0f;
            float s = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.5f;
            return SolidTex(defSize, new Color(m, m, m, s));
        }

        /// <summary>各テクスチャを Rect の位置にスケールして配置し、1 枚のアトラスにまとめる。</summary>
        Texture2D PackByRects(Texture2D[] textures, Rect[] rects, int size, bool linear, Color defaultColor)
        {
            var atlas = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true, linear: linear);
            atlas.SetPixels(Enumerable.Repeat(defaultColor, size * size).ToArray());

            for (int i = 0; i < textures.Length; i++)
            {
                int x = Mathf.RoundToInt(rects[i].x * size);
                int y = Mathf.RoundToInt(rects[i].y * size);
                int w = Mathf.Max(1, Mathf.RoundToInt(rects[i].width  * size));
                int h = Mathf.Max(1, Mathf.RoundToInt(rects[i].height * size));

                var scaled = ScaleTex(textures[i], w, h, linear);
                atlas.SetPixels(x, y, w, h, scaled.GetPixels());
                Object.DestroyImmediate(scaled);
            }

            atlas.Apply();
            return atlas;
        }

        // 1 つのサブメッシュだけを取り出し、参照頂点のみで独立メッシュを作る（肥大化しない）。
        // remap=true のとき UV を atlas 内 Rect へ変換（タイリングは frac で 0–1 に丸め）。
        static Mesh ExtractSubmesh(Mesh src, int sub, bool remap, Rect rect)
        {
            int[] tris = src.GetTriangles(sub);
            var sV  = src.vertices;
            var sN  = src.normals;
            var sT  = src.tangents;
            var sU0 = src.uv;
            var sU2 = src.uv2;
            var sC  = src.colors;
            bool hN  = sN.Length  == sV.Length;
            bool hT  = sT.Length  == sV.Length;
            bool hU0 = sU0.Length == sV.Length;
            bool hU2 = sU2.Length == sV.Length;
            bool hC  = sC.Length  == sV.Length;

            var map = new Dictionary<int, int>(tris.Length);
            var V  = new List<Vector3>();
            var N  = new List<Vector3>();
            var T  = new List<Vector4>();
            var U0 = new List<Vector2>();
            var U2 = new List<Vector2>();
            var C  = new List<Color>();
            var nt = new int[tris.Length];

            for (int i = 0; i < tris.Length; i++)
            {
                int oi = tris[i];
                if (!map.TryGetValue(oi, out int ni))
                {
                    ni = V.Count;
                    map[oi] = ni;
                    V.Add(sV[oi]);
                    if (hN) N.Add(sN[oi]);
                    if (hT) T.Add(sT[oi]);
                    if (hU0)
                    {
                        var uv = sU0[oi];
                        if (remap)
                            // [0,1] のUVをそのまま atlas 枠へ線形変換する。
                            // frac で 0–1 に丸めると u/v=1.0 等の整数境界が枠の端へ潰れ、
                            // 端の三角形のテクスチャが崩れる（タイリング前提の誤った処理だった）。
                            uv = new Vector2(rect.x + uv.x * rect.width, rect.y + uv.y * rect.height);
                        U0.Add(uv);
                    }
                    if (hU2) U2.Add(sU2[oi]);
                    if (hC)  C.Add(sC[oi]);
                }
                nt[i] = ni;
            }

            var m = new Mesh { indexFormat = V.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(V);
            if (hN)  m.SetNormals(N);
            if (hT)  m.SetTangents(T);
            if (hU0) m.SetUVs(0, U0);
            if (hU2) m.SetUVs(1, U2);
            if (hC)  m.SetColors(C);
            m.SetTriangles(nt, 0);
            return m;
        }

        // 各サブメッシュのマテリアルを allMats のインデックスに変換した配列を返す。
        static int[] SubmeshMatIndices(GameObject go, List<Material> allMats)
        {
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var mats = go.GetComponent<MeshRenderer>().sharedMaterials;
            int sub  = Mathf.Max(1, mesh.subMeshCount);
            var idxs = new int[sub];
            for (int s = 0; s < sub; s++)
            {
                Material mat = mats.Length == 0 ? null
                             : (s < mats.Length ? mats[s] : mats[mats.Length - 1]);
                idxs[s] = Mathf.Max(0, allMats.IndexOf(mat));
            }
            return idxs;
        }

        // 選択全体のサブメッシュ(=マテリアルスロット)合計。実行可否の判定に使う。
        static int TotalSubmeshSlots(List<GameObject> targets)
        {
            int total = 0;
            foreach (var go in targets)
            {
                var mesh = go.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh != null) total += Mathf.Max(1, mesh.subMeshCount);
            }
            return total;
        }

        // UV が [0,1] の外に出ている（＝メッシュUVにタイリングが焼き込まれた）メッシュを
        // 実UV範囲つきで列挙する。アトラス化前の警告に使う。空文字なら問題なし。
        static string BuildTilingReport(List<GameObject> targets)
        {
            const float eps = 0.001f;
            var sb = new System.Text.StringBuilder();
            foreach (var go in targets)
            {
                var mesh = go.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var uv = mesh.uv;
                if (uv.Length == 0) continue;

                float minU = float.MaxValue, maxU = float.MinValue;
                float minV = float.MaxValue, maxV = float.MinValue;
                for (int i = 0; i < uv.Length; i++)
                {
                    minU = Mathf.Min(minU, uv[i].x); maxU = Mathf.Max(maxU, uv[i].x);
                    minV = Mathf.Min(minV, uv[i].y); maxV = Mathf.Max(maxV, uv[i].y);
                }
                if (minU < -eps || maxU > 1f + eps || minV < -eps || maxV > 1f + eps)
                    sb.AppendLine($"・{go.name}: U {minU:F1}〜{maxU:F1} / V {minV:F1}〜{maxV:F1}");
            }
            return sb.ToString().TrimEnd();
        }

        // オブジェクトの全サブメッシュを各マテリアルのアトラス枠へリマップし、
        // 1 サブメッシュのメッシュに統合してアセット保存（同一形状はキャッシュで共有）。
        Mesh BuildFlattenedAtlasMesh(GameObject go, List<Material> allMats, Rect[] rects,
                                     string n, SaveFolders f, Dictionary<string, Mesh> cache)
        {
            var mesh   = go.GetComponent<MeshFilter>().sharedMesh;
            int[] idxs = SubmeshMatIndices(go, allMats);
            string key = mesh.GetInstanceID() + ":" + string.Join(",", idxs);
            if (cache.TryGetValue(key, out var cached)) return cached;

            var combines = new List<CombineInstance>(idxs.Length);
            var temp     = new List<Mesh>(idxs.Length);
            for (int s = 0; s < idxs.Length; s++)
            {
                var ext = ExtractSubmesh(mesh, s, remap: true, rect: rects[idxs[s]]);
                temp.Add(ext);
                combines.Add(new CombineInstance { mesh = ext, subMeshIndex = 0, transform = Matrix4x4.identity });
            }

            var flat = new Mesh { name = $"{n}_{mesh.name}", indexFormat = IndexFormat.UInt32 };
            flat.CombineMeshes(combines.ToArray(), mergeSubMeshes: true, useMatrices: false);
            flat.RecalculateBounds();
            foreach (var t in temp) Object.DestroyImmediate(t);

            SaveMesh(flat, AssetDatabase.GenerateUniqueAssetPath($"{f.meshes}/{flat.name}.asset"));
            cache[key] = flat;
            return flat;
        }

        static Texture2D MakeReadable(Texture2D src, bool linear)
        {
            var rwMode = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
            var rt     = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, rwMode);
            Graphics.Blit(src, rt);
            var prev   = RenderTexture.active;
            RenderTexture.active = rt;
            var result = new Texture2D(src.width, src.height, TextureFormat.RGBA32, mipChain: false, linear: linear);
            result.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            result.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        static Texture2D ScaleTex(Texture2D src, int w, int h, bool linear)
        {
            var rwMode = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
            var rt     = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, rwMode);
            Graphics.Blit(src, rt);
            var prev   = RenderTexture.active;
            RenderTexture.active = rt;
            var result = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false, linear: linear);
            result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            result.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        static Texture2D SolidTex(int size, Color color)
        {
            var t = new Texture2D(size, size);
            t.SetPixels(Enumerable.Repeat(color, size * size).ToArray());
            t.Apply();
            return t;
        }

        void SaveMesh(Mesh mesh, string path)
        {
            if (disableReadWrite) mesh.UploadMeshData(markNoLongerReadable: true);
            AssetDatabase.CreateAsset(mesh, path);
        }

        static void SaveTex(Texture2D tex, string path) => AssetDatabase.CreateAsset(tex, path);

        // ========================= メッシュ統合 =========================

        static Mesh CombineWorldSpace(List<GameObject> targets)
        {
            var combines = targets.Select(go => new CombineInstance
            {
                mesh      = go.GetComponent<MeshFilter>().sharedMesh,
                transform = go.GetComponent<MeshFilter>().transform.localToWorldMatrix
            }).ToArray();

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(combines, mergeSubMeshes: true, useMatrices: true);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ========================= フォルダユーティリティ =========================

        struct SaveFolders
        {
            public string root;
            public string meshes;
            public string textures;
            public string materials;
        }

        static SaveFolders CreateSubFolders(string root)
        {
            AssetDatabase.CreateFolder(root, "Meshes");
            AssetDatabase.CreateFolder(root, "Textures");
            AssetDatabase.CreateFolder(root, "Materials");
            return new SaveFolders
            {
                root      = root,
                meshes    = $"{root}/Meshes",
                textures  = $"{root}/Textures",
                materials = $"{root}/Materials",
            };
        }

        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace("\\",  "/");
            string name   = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        /// <summary>パスが Assets/ 以下を指しているか検証する。</summary>
        static bool IsValidAssetsFolderPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = path.Replace("\\", "/");
            return path == "Assets" || path.StartsWith("Assets/");
        }

        /// <summary>絶対パスを Assets/ 相対パスに変換する。プロジェクト外なら null。</summary>
        static string ToAssetsRelativePath(string absolutePath)
        {
            absolutePath = absolutePath.Replace("\\", "/");
            string dataPath = Application.dataPath.Replace("\\", "/"); // .../Assets
            if (absolutePath == dataPath) return "Assets";
            if (absolutePath.StartsWith(dataPath + "/"))
                return "Assets" + absolutePath.Substring(dataPath.Length);
            return null;
        }

        static bool HasEditorOnlyTag() =>
            UnityEditorInternal.InternalEditorUtility.tags.Contains("EditorOnly");
    }
}
