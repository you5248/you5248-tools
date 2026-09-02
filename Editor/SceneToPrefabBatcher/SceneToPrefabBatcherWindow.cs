// SceneToPrefabBatcherWindow.cs
// Unity 2022.3.22f1 compatible
//
// Tools > you5248 > Scene -> Prefab Batcher
//
// - Converts scene GameObjects (e.g. unprefabbed props from purchased asset packs)
//   into self-contained prefabs under Assets/you5248/Prefabs/<category>/.
// - Vendors mesh / material / texture dependencies into a sibling _Deps/ folder
//   so the source vendor folder can be deleted without breaking the prefab.
// - Auto-categorizes by user-editable keyword rules, falling back to the mesh's
//   top-level Assets folder name.
// - Leaf mode: each MeshRenderer/SkinnedMeshRenderer becomes its own flat prefab.
// - Branch mode: each Selection root becomes one prefab keeping its full subtree.
// - Prefab asset transform is reset to (0,0,0)/(0,0,0)/(1,1,1); the scene
//   GameObject is left untouched at its original world position.
// - JP/EN UI to match other you5248 tools.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace you5248.SceneToPrefabBatcher
{
    public class SceneToPrefabBatcherWindow : EditorWindow
    {
        private enum Language { Japanese, English }
        private enum Mode { Leaf, Branch }
        private enum DuplicatePolicy { AutoNumber, Overwrite, Skip }

        [SerializeField] private Language uiLanguage = Language.Japanese;
        [SerializeField] private Mode mode = Mode.Leaf;
        [SerializeField] private string prefabRoot = "Assets/you5248/Prefabs";
        [SerializeField] private string namePrefix = "";
        [SerializeField] private DuplicatePolicy duplicatePolicy = DuplicatePolicy.AutoNumber;
        [SerializeField] private bool vendorDependencies = true;
        [SerializeField] private bool replaceWithInstance = false;
        [SerializeField] private bool dedupByFingerprint = true;
        [SerializeField] private bool dedupAcrossExisting = true;
        [SerializeField] private bool dryRun = false;
        [SerializeField] private bool verboseLog = true;

        private SceneToPrefabBatcherSettings settings;
        private Vector2 scroll;
        private SerializedObject settingsSO;

        // Dedup state 遯ｶ繝ｻreset per Execute() run.
        private readonly Dictionary<string, string> fingerprintToPrefabPath = new Dictionary<string, string>();
        private readonly Dictionary<int, string> meshFingerprintCache = new Dictionary<int, string>();

        // Dry-run dedup group tracking: fingerprint 遶翫・list of GameObject names
        private readonly Dictionary<string, List<string>> dryRunDedupGroups = new Dictionary<string, List<string>>();

        // Cleanup state
        private List<DupGroup> cleanupGroups = null;
        private Vector2 cleanupScroll;
        private bool cleanupScanned = false;

        // Folder-creation cache 遯ｶ繝ｻprevents duplicate folders during
        // StartAssetEditing() batches where IsValidFolder returns stale results.
        private readonly HashSet<string> createdFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [MenuItem("you5248/Scene -> Prefab Batcher")]
        public static void Open()
        {
            var w = GetWindow<SceneToPrefabBatcherWindow>(false, "Scene -> Prefab Batcher", true);
            w.minSize = new Vector2(440, 520);
            w.Show();
        }

        private void OnEnable()
        {
            settings = SceneToPrefabBatcherSettings.LoadOrCreate();
            settingsSO = new SerializedObject(settings);
        }

        // ------------------------------------------------------------
        // GUI
        // ------------------------------------------------------------
        private void OnGUI()
        {
            if (settings == null) settings = SceneToPrefabBatcherSettings.LoadOrCreate();
            if (settingsSO == null || settingsSO.targetObject != settings)
                settingsSO = new SerializedObject(settings);

            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawHeader();
            EditorGUILayout.Space();

            DrawTargetSection();
            EditorGUILayout.Space();

            DrawOutputSection();
            EditorGUILayout.Space();

            DrawCategorySection();
            EditorGUILayout.Space();

            DrawOptionsSection();
            EditorGUILayout.Space();

            DrawApplyButtons();

            EditorGUILayout.Space();
            DrawSeparator();
            EditorGUILayout.Space();

            DrawCleanupSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Scene -> Prefab Batcher",
                    EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                uiLanguage = (Language)EditorGUILayout.EnumPopup(uiLanguage, GUILayout.Width(90));
            }
            EditorGUILayout.HelpBox(
                Tr("郢ｧ・ｷ郢晢ｽｼ郢晢ｽｳ闕ｳ鄙ｫ縲帝ｩ包ｽｸ隰壽ｧｭ・邵ｺ繝ｻGameObject 郢ｧ繝ｻAssets/you5248/Prefabs/ 鬩溷ｺ・ｸ荵昶・鬮ｮ繝ｻ・ｴ繝ｻPrefab陋ｹ謔ｶ・邵ｺ・ｾ邵ｺ蜷ｶﾂ繝ｻ" +
                   "Vendor dependencies can be deleted.",
                   "Converts selected scene GameObjects into prefabs under Assets/you5248/Prefabs/. " +
                   "Mesh/Material/Texture dependencies are vendored, so deleting the source vendor folder won't break the prefab."),
                MessageType.Info);
        }

        private void DrawTargetSection()
        {
            EditorGUILayout.LabelField("1. Target", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                mode = (Mode)EditorGUILayout.EnumPopup(
                    new GUIContent("Mode",
                        "Leaf: Each MeshRenderer to Prefab\nBranch: Entire hierarchy to Prefab"),
                    mode);

                var sel = Selection.gameObjects;
                EditorGUILayout.LabelField(
                    Tr($"Selected: {sel.Length} objects", $"Selected: {sel.Length} objects"));
                int planned = CountPlannedTargets();
                EditorGUILayout.LabelField(
                    Tr($"陷・ｽｦ騾・・・ｺ莠･・ｮ繝ｻ 驍上・{planned} 闔会ｽｶ", $"Planned: ~{planned} prefabs"));
            }
        }

        private void DrawOutputSection()
        {
            EditorGUILayout.LabelField("2. Output", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    prefabRoot = EditorGUILayout.TextField("Prefab root", prefabRoot);
                    if (GUILayout.Button("...", GUILayout.Width(28)))
                    {
                        var picked = EditorUtility.OpenFolderPanel(
                            "Pick prefab root folder",
                            Application.dataPath, "");
                        if (!string.IsNullOrEmpty(picked))
                        {
                            var rel = MakeProjectRelative(picked);
                            if (rel != null) prefabRoot = rel;
                            else EditorUtility.DisplayDialog(
                                "Invalid",
                                "Pick a folder under Assets/.", "OK");
                        }
                    }
                }

                namePrefix = EditorGUILayout.TextField(
                    new GUIContent("Name prefix",
                        "Optional. e.g. SM_, Prop_"),
                    namePrefix);

                duplicatePolicy = (DuplicatePolicy)EditorGUILayout.EnumPopup(
                    "Duplicate name policy", duplicatePolicy);
            }
        }

        private void DrawCategorySection()
        {
            EditorGUILayout.LabelField("3. Auto category rules",
                EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.HelpBox(
                    Tr("郢ｧ・ｭ郢晢ｽｼ郢晢ｽｯ郢晢ｽｼ郢晏ｳｨ窶ｲGameObject陷ｷ髦ｪ竏ｪ邵ｺ貅倥・Mesh陷医・繝ｱ郢ｧ・ｹ邵ｺ・ｫ陷ｷ・ｫ邵ｺ・ｾ郢ｧ蠕娯ｻ邵ｺ繝ｻ・檎ｸｺ・ｰ邵ｲ竏壺落邵ｺ・ｮ郢ｧ・ｫ郢昴・縺也ｹ晢ｽｪ邵ｺ・ｮ郢晁ｼ斐°郢晢ｽｫ郢敖邵ｺ・ｫ闖ｫ譎擾ｽｭ蛟･・・ｹｧ蠕娯穐邵ｺ蜷ｶﾂ繝ｻ" +
                       "Fallback to top level folder if no match.",
                       "If a keyword appears in the GameObject name or mesh asset path, that category folder is used. " +
                       "Otherwise the top-level folder under Assets/ where the mesh lives is used."),
                    MessageType.None);

                settingsSO.Update();
                var rulesProp = settingsSO.FindProperty("rules");
                for (int i = 0; i < rulesProp.arraySize; i++)
                {
                    var elem = rulesProp.GetArrayElementAtIndex(i);
                    var kw = elem.FindPropertyRelative("keyword");
                    var cat = elem.FindPropertyRelative("category");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(kw, GUIContent.none);
                        EditorGUILayout.LabelField("->", GUILayout.Width(20));
                        EditorGUILayout.PropertyField(cat, GUIContent.none);
                        if (GUILayout.Button("-", GUILayout.Width(24)))
                        {
                            rulesProp.DeleteArrayElementAtIndex(i);
                            settingsSO.ApplyModifiedProperties();
                            return;
                        }
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Add rule", GUILayout.Width(120)))
                    {
                        rulesProp.InsertArrayElementAtIndex(rulesProp.arraySize);
                    }
                    GUILayout.FlexibleSpace();
                    var fb = settingsSO.FindProperty("fallbackCategoryWhenUnknown");
                    EditorGUILayout.LabelField("Fallback folder", GUILayout.Width(120));
                    EditorGUILayout.PropertyField(fb, GUIContent.none, GUILayout.Width(140));
                }
                settingsSO.ApplyModifiedProperties();
            }
        }

        private void DrawOptionsSection()
        {
            EditorGUILayout.LabelField("4. Options", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                vendorDependencies = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Vendor dependencies (copy Mesh / Material / Texture)",
                        "If ON, original vendor folders can be deleted."),
                    vendorDependencies);

                replaceWithInstance = EditorGUILayout.ToggleLeft(
                    "Replace scene GameObject with prefab instance (default OFF)",
                    replaceWithInstance);

                dedupByFingerprint = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Detect duplicates (Dedup)",
                        Tr("Mesh 郢昴・繝ｻ郢ｧ・ｿ郢晢ｽｻ郢晄ｧｭ繝ｦ郢晢ｽｪ郢ｧ・｢郢晢ｽｫ郢晢ｽｻScale郢晢ｽｻRotation 邵ｺ蠕｡・ｸﾂ髢ｾ・ｴ邵ｺ蜷ｶ・狗ｹｧ繧・・郢ｧ雋樣・闕ｳﾂ髫募・・ｦ螢ｹ縺・ｹｧ・ｻ郢昴・繝ｨ邵ｺ・ｨ陋ｻ・､陞ｳ螢ｹ・邵ｲ繝ｻ" +
                           "2陋溽距蟯ｼ闔会ｽ･鬮ｯ髦ｪ繝ｻ隴・ｽｰ髫戊･ｲrefab郢ｧ蜑・ｽｽ諛奇ｽ臥ｸｺ螢ｹ竊楢ｭ鯉ｽ｢陝・ｦ排efab郢ｧ雋槭・陋ｻ・ｩ騾包ｽｨ邵ｺ蜉ｱ竏ｪ邵ｺ蜷ｶﾂ繧九裟髫阪・・邵ｺ貅倥☆郢晢ｽｼ郢晢ｽｳGameObject邵ｺ・ｯ" +
                           "(Even if ON, instances will be replaced if existing prefab matches).",
                           "Items with matching mesh data, materials, scale and rotation are treated as the same " +
                           "visual asset. Duplicates do not produce new prefabs 遯ｶ繝ｻthey reuse the first one. If " +
                           "'replace scene' is also ON, duplicate scene GameObjects become instances of the first prefab.")),
                    dedupByFingerprint);

                if (dedupByFingerprint)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        dedupAcrossExisting = EditorGUILayout.ToggleLeft(
                            new GUIContent(
                                "Also check existing prefabs in output (cross-run)",
                                Tr("Prefab郢晢ｽｫ郢晢ｽｼ郢晉｣ｯ繝ｻ闕ｳ荵昴・隴鯉ｽ｢陝・ｦ排efab郢ｧ蛛ｵ笘・ｸｺ・ｹ邵ｺ・ｦ郢ｧ・ｹ郢ｧ・ｭ郢晢ｽ｣郢晢ｽｳ邵ｺ蜉ｱ窶ｻ郢晁ｼ斐≦郢晢ｽｳ郢ｧ・ｬ郢晢ｽｼ郢晏干ﾎ懃ｹ晢ｽｳ郢晞メ・ｾ讓雁ｶ檎ｸｺ・ｫ騾具ｽｻ鬪ｭ・ｲ邵ｺ蜉ｱ竏ｪ邵ｺ蜷ｶﾂ繝ｻ" +
                                   "Can dedup prefabs created in past batches.",
                                   "Scans all existing prefabs under the prefab root and adds them to the fingerprint " +
                                   "registry, so duplicates of earlier batches are also detected.")),
                            dedupAcrossExisting);
                    }
                }

                dryRun = EditorGUILayout.ToggleLeft(
                    "Dry Run (log planned actions, don't write files)",
                    dryRun);

                verboseLog = EditorGUILayout.ToggleLeft(
                    "Verbose log", verboseLog);
            }
        }

        private void DrawApplyButtons()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply", GUILayout.Height(34)))
                    Execute();

                if (GUILayout.Button("Refresh", GUILayout.Width(120), GUILayout.Height(34)))
                    Repaint();
            }
        }

        // ------------------------------------------------------------
        // Pre-flight
        // ------------------------------------------------------------
        private int CountPlannedTargets()
        {
            int count = 0;
            foreach (var go in Selection.gameObjects)
            {
                if (mode == Mode.Branch) { count++; continue; }
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is MeshRenderer || r is SkinnedMeshRenderer) count++;
                }
            }
            return count;
        }

        // ------------------------------------------------------------
        // Execute
        // ------------------------------------------------------------
        private void Execute()
        {
            var sel = Selection.gameObjects;
            if (sel == null || sel.Length == 0)
            {
                EditorUtility.DisplayDialog("Nothing selected",
                    "Please select a GameObject in the scene.", "OK");
                return;
            }
            if (!prefabRoot.StartsWith("Assets/"))
            {
                EditorUtility.DisplayDialog("Invalid root",
                    "Prefab root must be under Assets/.", "OK");
                return;
            }

            var targets = new List<GameObject>();
            foreach (var root in sel)
            {
                if (mode == Mode.Branch)
                {
                    targets.Add(root);
                }
                else
                {
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r is MeshRenderer || r is SkinnedMeshRenderer)
                            targets.Add(r.gameObject);
                    }
                }
            }

            fingerprintToPrefabPath.Clear();
            meshFingerprintCache.Clear();
            vendoredMeshes.Clear();
            vendoredMaterials.Clear();
            vendoredTextures.Clear();
            dryRunDedupGroups.Clear();

            if (dedupByFingerprint && dedupAcrossExisting)
            {
                try
                {
                    EditorUtility.DisplayProgressBar("Scene -> Prefab Batcher",
                        "Scanning existing prefabs...", 0f);
                    ScanExistingPrefabs();
                }
                finally { EditorUtility.ClearProgressBar(); }
            }

            int saved = 0, skipped = 0, failed = 0;
            int dedup = 0;

            // Pre-create all needed folders BEFORE StartAssetEditing(), because
            // AssetDatabase.IsValidFolder() returns stale data during a batch.
            // This prevents Unity from auto-numbering duplicate folders like
            // "Prefabs 1", "Furniture 2", etc.
            createdFolders.Clear();
            if (!dryRun)
            {
                var neededFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var go in targets)
                {
                    if (go == null) continue;
                    string meshPath = FindFirstMeshAssetPath(go);
                    string cat = settings.ResolveCategory(go.name, meshPath);
                    string catFolder = $"{prefabRoot.TrimEnd('/')}/{SanitizeFolder(cat)}";
                    neededFolders.Add(catFolder);
                    if (vendorDependencies)
                    {
                        string deps = $"{catFolder}/_Deps";
                        neededFolders.Add(deps);
                        neededFolders.Add($"{deps}/Meshes");
                        neededFolders.Add($"{deps}/Materials");
                        neededFolders.Add($"{deps}/Textures");
                    }
                }
                foreach (var f in neededFolders)
                    EnsureFolder(f);
            }

            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < targets.Count; i++)
                {
                    var go = targets[i];
                    EditorUtility.DisplayProgressBar("Scene -> Prefab Batcher",
                        $"[{i + 1}/{targets.Count}] {go.name}",
                        (i + 1) / (float)targets.Count);
                    try
                    {
                        var result = ProcessOne(go);
                        if (result == ProcessResult.Saved) saved++;
                        else if (result == ProcessResult.Duplicate) dedup++;
                        else if (result == ProcessResult.Skipped) skipped++;
                    }
                    catch (Exception e)
                    {
                        failed++;
                        Debug.LogError($"[SceneToPrefabBatcher] {go.name}: {e}");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                if (!dryRun) AssetDatabase.SaveAssets();
            }

            Debug.Log($"[SceneToPrefabBatcher] done. saved={saved}, dedup={dedup}, skipped={skipped}, failed={failed}, dryRun={dryRun}");

            // Dry-run: print dedup group summary
            if (dryRun && dedupByFingerprint && dryRunDedupGroups.Count > 0)
            {
                var groupSb = new StringBuilder();
                groupSb.AppendLine($"[SceneToPrefabBatcher][dry] === {"Duplicate Groups"} ===");
                int groupIdx = 0;
                foreach (var kvp in dryRunDedupGroups)
                {
                    if (kvp.Value.Count < 2) continue;
                    groupIdx++;
                    groupSb.AppendLine($"  Group {groupIdx} ({kvp.Value.Count} {"objects"}):");
                    for (int n = 0; n < kvp.Value.Count; n++)
                    {
                        string label = n == 0
                            ? " 遶翫・Save as prefab"
                            : " 遶翫・Duplicate (skip)";
                        groupSb.AppendLine($"    - {kvp.Value[n]}{label}");
                    }
                }
                if (groupIdx > 0) Debug.Log(groupSb.ToString());
            }
        }

        private enum ProcessResult { Saved, Skipped, Failed, Duplicate }

        private ProcessResult ProcessOne(GameObject sourceInScene)
        {
            if (sourceInScene == null) return ProcessResult.Skipped;
            // Skip only when the source is already a hand-authored prefab (Regular/Variant).
            // Model prefab instances (FBX/OBJ dragged into the scene) are exactly what we
            // want to convert into standalone .prefab files, so don't skip those.
            if (PrefabUtility.IsPartOfPrefabInstance(sourceInScene))
            {
                var assetType = PrefabUtility.GetPrefabAssetType(sourceInScene);
                if (assetType == PrefabAssetType.Regular || assetType == PrefabAssetType.Variant)
                {
                    if (verboseLog) Debug.Log($"[SceneToPrefabBatcher] skip (already prefab instance, type={assetType}): {sourceInScene.name}");
                    return ProcessResult.Skipped;
                }
            }

            // Dedup check 遯ｶ繝ｻdata-based fingerprint catches both "same FBX placed many times"
            // and "different FBX with identical mesh data".
            string fingerprint = null;
            if (dedupByFingerprint)
            {
                fingerprint = ComputeFingerprint(sourceInScene, mode == Mode.Leaf);

                // Track dedup groups for dry-run summary
                if (!string.IsNullOrEmpty(fingerprint))
                {
                    if (!dryRunDedupGroups.TryGetValue(fingerprint, out var nameList))
                    {
                        nameList = new List<string>();
                        dryRunDedupGroups[fingerprint] = nameList;
                    }
                    nameList.Add(sourceInScene.name);
                }

                if (!string.IsNullOrEmpty(fingerprint) &&
                    fingerprintToPrefabPath.TryGetValue(fingerprint, out string existingPath))
                {
                    var existingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(existingPath);
                    if (existingAsset != null)
                    {
                        if (verboseLog) Debug.Log($"[SceneToPrefabBatcher] duplicate of {existingPath}: {sourceInScene.name}");
                        if (replaceWithInstance && !dryRun)
                            ReplaceSceneWithPrefabInstance(sourceInScene, existingAsset);
                        return ProcessResult.Duplicate;
                    }
                }
            }

            string firstMeshPath = FindFirstMeshAssetPath(sourceInScene);
            string category = settings.ResolveCategory(sourceInScene.name, firstMeshPath);
            string categoryFolder = $"{prefabRoot.TrimEnd('/')}/{SanitizeFolder(category)}";
            string depsFolder = $"{categoryFolder}/_Deps";

            string baseName = string.IsNullOrEmpty(namePrefix)
                ? sourceInScene.name
                : namePrefix + sourceInScene.name;
            baseName = SanitizeFileName(baseName);

            string targetPath = $"{categoryFolder}/{baseName}.prefab";

            if (AssetDatabase.LoadAssetAtPath<GameObject>(targetPath) != null)
            {
                switch (duplicatePolicy)
                {
                    case DuplicatePolicy.Skip:
                        if (verboseLog) Debug.Log($"[SceneToPrefabBatcher] skip (already exists): {targetPath}");
                        return ProcessResult.Skipped;
                    case DuplicatePolicy.Overwrite:
                        break;
                    case DuplicatePolicy.AutoNumber:
                        targetPath = AssetDatabase.GenerateUniqueAssetPath(targetPath);
                        break;
                }
            }

            if (dryRun)
            {
                Debug.Log($"[SceneToPrefabBatcher][dry] {sourceInScene.name} -> {targetPath} (category={category})");
                return ProcessResult.Saved;
            }

            EnsureFolder(categoryFolder);
            if (vendorDependencies) EnsureFolder(depsFolder);

            // Capture the source's WORLD rotation and scale BEFORE cloning / re-parenting.
            // For FBX content the import scale (0.01 for Blender etc.) is baked into ancestor
            // transforms and only visible via lossyScale. If we reset everything to identity
            // the saved prefab renders ~100x its intended size 遯ｶ繝ｻwhich is what was happening
            // before this fix. Only the position is zero-ed; visual orientation and size
            // are preserved so the prefab looks like the scene instance.
            Quaternion captureRotation = sourceInScene.transform.rotation;
            Vector3 captureScale = sourceInScene.transform.lossyScale;

            var clone = UnityEngine.Object.Instantiate(sourceInScene);

            try
            {
                clone.transform.SetParent(null, false);

                // If the source was a Model Prefab instance (FBX), the clone inherits the
                // prefab connection. Unpack so the saved prefab is fully independent and
                // not a Variant of the source FBX.
                if (PrefabUtility.IsPartOfPrefabInstance(clone))
                {
                    PrefabUtility.UnpackPrefabInstance(clone, PrefabUnpackMode.Completely,
                        InteractionMode.AutomatedAction);
                }

                // After SetParent(null), local == world. Position to origin, but keep the
                // captured world rotation and scale so the prefab looks visually correct.
                clone.transform.localPosition = Vector3.zero;
                clone.transform.localRotation = captureRotation;
                clone.transform.localScale = captureScale;

                // Strip children first in Leaf mode so we don't vendor deps that
                // belong to descendants (each descendant is its own prefab in Leaf).
                if (mode == Mode.Leaf)
                {
                    for (int i = clone.transform.childCount - 1; i >= 0; i--)
                    {
                        UnityEngine.Object.DestroyImmediate(clone.transform.GetChild(i).gameObject);
                    }
                }

                if (vendorDependencies) VendorAssetsOn(clone, depsFolder);

                clone.name = Path.GetFileNameWithoutExtension(targetPath);

                bool success;
                PrefabUtility.SaveAsPrefabAsset(clone, targetPath, out success);
                if (!success)
                {
                    Debug.LogError($"[SceneToPrefabBatcher] failed to save: {targetPath}");
                    return ProcessResult.Failed;
                }
                if (verboseLog) Debug.Log($"[SceneToPrefabBatcher] saved: {targetPath} (category={category})");

                if (dedupByFingerprint && !string.IsNullOrEmpty(fingerprint))
                    fingerprintToPrefabPath[fingerprint] = targetPath;

                if (replaceWithInstance)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
                    if (asset != null) ReplaceSceneWithPrefabInstance(sourceInScene, asset);
                }
                return ProcessResult.Saved;
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
            }
        }

        // ------------------------------------------------------------
        // Dependency vendoring
        // ------------------------------------------------------------
        // Cache keyed by original asset GUID, reused within a single batch
        // so prefabs in the same run share the same vendored copies.
        private readonly Dictionary<string, Mesh> vendoredMeshes = new Dictionary<string, Mesh>();
        private readonly Dictionary<string, Material> vendoredMaterials = new Dictionary<string, Material>();
        private readonly Dictionary<string, Texture> vendoredTextures = new Dictionary<string, Texture>();

        private void VendorAssetsOn(GameObject root, string depsFolder)
        {
            EnsureFolder($"{depsFolder}/Meshes");
            EnsureFolder($"{depsFolder}/Materials");
            EnsureFolder($"{depsFolder}/Textures");

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                mf.sharedMesh = VendorMesh(mf.sharedMesh, depsFolder);
            }
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh != null) smr.sharedMesh = VendorMesh(smr.sharedMesh, depsFolder);
                smr.sharedMaterials = smr.sharedMaterials
                    .Select(m => VendorMaterial(m, depsFolder)).ToArray();
            }
            foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (mc.sharedMesh == null) continue;
                mc.sharedMesh = VendorMesh(mc.sharedMesh, depsFolder);
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer) continue;
                r.sharedMaterials = r.sharedMaterials
                    .Select(m => VendorMaterial(m, depsFolder)).ToArray();
            }

            // Lightweight script-dependency warning
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var script = MonoScript.FromMonoBehaviour(mb);
                if (script == null) continue;
                var p = AssetDatabase.GetAssetPath(script);
                if (string.IsNullOrEmpty(p)) continue;
                if (p.StartsWith("Assets/") && !p.StartsWith("Assets/you5248/"))
                    Debug.LogWarning($"[SceneToPrefabBatcher] script not vendored: {p} (will break if vendor folder is deleted): {mb.GetType().Name}");
            }
        }

        private Mesh VendorMesh(Mesh orig, string depsFolder)
        {
            if (orig == null) return null;
            string origPath = AssetDatabase.GetAssetPath(orig);
            if (!IsVendorable(origPath)) return orig;
            if (IsUnderPrefabRoot(origPath)) return orig;

            if (!TryGetUniqueAssetKey(orig, out string cacheKey, out string suffix)) return orig;
            if (vendoredMeshes.TryGetValue(cacheKey, out var cached) && cached != null) return cached;

            string safe = SanitizeFileName(orig.name);
            string targetPath = $"{depsFolder}/Meshes/{safe}__{suffix}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(targetPath);
            if (existing != null)
            {
                vendoredMeshes[cacheKey] = existing;
                return existing;
            }

            // Use CopyAsset only when the source is the main asset of a standalone .asset file.
            // FBX-embedded meshes (and any sub-asset) must be cloned with Instantiate + CreateAsset
            // 遯ｶ繝ｻotherwise we'd copy the wrong file or the entire container.
            bool isStandaloneMainAsset =
                origPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                AssetDatabase.LoadMainAssetAtPath(origPath) == (UnityEngine.Object)orig;

            if (isStandaloneMainAsset)
            {
                if (!AssetDatabase.CopyAsset(origPath, targetPath))
                {
                    Debug.LogWarning($"[SceneToPrefabBatcher] CopyAsset failed for mesh: {origPath} -> {targetPath}");
                    return orig;
                }
                var copy = AssetDatabase.LoadAssetAtPath<Mesh>(targetPath);
                vendoredMeshes[cacheKey] = copy;
                return copy;
            }
            else
            {
                var clone = UnityEngine.Object.Instantiate(orig);
                clone.name = orig.name;
                AssetDatabase.CreateAsset(clone, targetPath);
                vendoredMeshes[cacheKey] = clone;
                return clone;
            }
        }

        private Material VendorMaterial(Material orig, string depsFolder)
        {
            if (orig == null) return null;
            string origPath = AssetDatabase.GetAssetPath(orig);
            if (!IsVendorable(origPath)) return orig;
            if (IsUnderPrefabRoot(origPath)) return orig;

            if (!TryGetUniqueAssetKey(orig, out string cacheKey, out string suffix)) return orig;
            if (vendoredMaterials.TryGetValue(cacheKey, out var cached) && cached != null) return cached;

            string safe = SanitizeFileName(orig.name);
            string targetPath = $"{depsFolder}/Materials/{safe}__{suffix}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
            Material copy = existing;
            if (copy == null)
            {
                bool isStandaloneMainAsset =
                    origPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) &&
                    AssetDatabase.LoadMainAssetAtPath(origPath) == (UnityEngine.Object)orig;

                if (isStandaloneMainAsset)
                {
                    if (!AssetDatabase.CopyAsset(origPath, targetPath))
                    {
                        Debug.LogWarning($"[SceneToPrefabBatcher] CopyAsset failed for material: {origPath} -> {targetPath}");
                        return orig;
                    }
                    copy = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
                }
                else
                {
                    // FBX-embedded or sub-asset material 遯ｶ繝ｻclone via new Material(orig)
                    var clone = new Material(orig) { name = orig.name };
                    AssetDatabase.CreateAsset(clone, targetPath);
                    copy = clone;
                }
            }
            if (copy == null) return orig;

            RerouteMaterialTextures(copy, depsFolder);
            vendoredMaterials[cacheKey] = copy;
            return copy;
        }

        private void RerouteMaterialTextures(Material mat, string depsFolder)
        {
            var so = new SerializedObject(mat);
            var texEnvs = so.FindProperty("m_SavedProperties.m_TexEnvs");
            if (texEnvs == null || !texEnvs.isArray) return;

            bool dirty = false;
            for (int i = 0; i < texEnvs.arraySize; i++)
            {
                var entry = texEnvs.GetArrayElementAtIndex(i);
                var second = entry.FindPropertyRelative("second");
                if (second == null) continue;
                var texProp = second.FindPropertyRelative("m_Texture");
                if (texProp == null) continue;
                var tex = texProp.objectReferenceValue as Texture;
                if (tex == null) continue;
                var vended = VendorTexture(tex, depsFolder);
                if (vended != null && vended != tex)
                {
                    texProp.objectReferenceValue = vended;
                    dirty = true;
                }
            }
            if (dirty) so.ApplyModifiedPropertiesWithoutUndo();
        }

        private Texture VendorTexture(Texture orig, string depsFolder)
        {
            if (orig == null) return null;
            string origPath = AssetDatabase.GetAssetPath(orig);
            if (!IsVendorable(origPath)) return orig;
            if (IsUnderPrefabRoot(origPath)) return orig;

            if (!TryGetUniqueAssetKey(orig, out string cacheKey, out string suffix)) return orig;
            if (vendoredTextures.TryGetValue(cacheKey, out var cached) && cached != null) return cached;

            string ext = Path.GetExtension(origPath);
            string safe = SanitizeFileName(orig.name);
            string targetPath = $"{depsFolder}/Textures/{safe}__{suffix}{ext}";

            var existing = AssetDatabase.LoadAssetAtPath<Texture>(targetPath);
            if (existing != null)
            {
                vendoredTextures[cacheKey] = existing;
                return existing;
            }

            if (AssetDatabase.CopyAsset(origPath, targetPath))
            {
                var copy = AssetDatabase.LoadAssetAtPath<Texture>(targetPath);
                vendoredTextures[cacheKey] = copy;
                return copy;
            }
            return orig;
        }

        // ------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------
        // ------------------------------------------------------------
        // Dedup
        // ------------------------------------------------------------
        private void ReplaceSceneWithPrefabInstance(GameObject sourceInScene, GameObject prefabAsset)
        {
            if (sourceInScene == null || prefabAsset == null) return;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, sourceInScene.scene);
            if (inst == null) return;
            inst.transform.SetParent(sourceInScene.transform.parent, false);
            inst.transform.localPosition = sourceInScene.transform.localPosition;
            inst.transform.localRotation = sourceInScene.transform.localRotation;
            inst.transform.localScale = sourceInScene.transform.localScale;
            inst.transform.SetSiblingIndex(sourceInScene.transform.GetSiblingIndex());
            Undo.RegisterCreatedObjectUndo(inst, "Replace with Prefab Instance");
            Undo.DestroyObjectImmediate(sourceInScene);
        }

        private void ScanExistingPrefabs()
        {
            var root = prefabRoot.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(root)) return;
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { root });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                // Skip anything under _Deps/ (vendored sub-assets shouldn't be considered as canonical prefabs)
                if (path.Replace("\\",  "/").Contains("/_Deps/")) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                // For existing prefabs we don't know whether they were saved in Leaf or
                // Branch mode. We register BOTH fingerprint scopes so candidates in either
                // mode can match. (Leaf-mode prefabs have no children 遶翫・both scopes produce
                // the same fingerprint; Branch-mode prefabs produce two distinct entries.)
                string fpLeaf = ComputeFingerprint(prefab, leafScopeOnly: true);
                string fpBranch = ComputeFingerprint(prefab, leafScopeOnly: false);
                if (!string.IsNullOrEmpty(fpLeaf) && !fingerprintToPrefabPath.ContainsKey(fpLeaf))
                    fingerprintToPrefabPath[fpLeaf] = path;
                if (!string.IsNullOrEmpty(fpBranch) && fpBranch != fpLeaf &&
                    !fingerprintToPrefabPath.ContainsKey(fpBranch))
                    fingerprintToPrefabPath[fpBranch] = path;
            }
        }

        // Data-based fingerprint. Invariant under vendoring: the vendored mesh is an
        // Instantiate'd copy of the original so its data fingerprint is identical, and
        // material fingerprint uses asset GUID for texture identity.
        // leafScopeOnly=true 遶翫・only the root's own renderers (matches what Leaf mode saves).
        // leafScopeOnly=false 遶翫・full subtree (matches Branch mode).
        private string ComputeFingerprint(GameObject root, bool leafScopeOnly)
        {
            if (root == null) return null;
            var sb = new StringBuilder(512);
            var rootT = root.transform;

            sb.Append(leafScopeOnly ? "LEAF#" : "BRANCH#");

            // We intentionally do NOT include rootT.rotation or rootT.lossyScale here.
            // A Prefab is an asset definition; its root scale and rotation in the scene
            // are instance properties. Baking them into the fingerprint causes duplicates
            // like Chair.001, Chair.002 just because they face different directions.

            var renderers = leafScopeOnly
                ? root.GetComponents<Renderer>()
                : root.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                Mesh m = null;
                if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    m = mf != null ? mf.sharedMesh : null;
                }
                else if (r is SkinnedMeshRenderer smr)
                {
                    m = smr.sharedMesh;
                }
                sb.Append("R|").Append(ComputeMeshFingerprint(m)).Append('|');

                foreach (var mat in r.sharedMaterials)
                {
                    sb.Append(ComputeMaterialFingerprint(mat)).Append('|');
                }

                // Local transform of the renderer relative to root (matters for Branch mode)
                if (!leafScopeOnly)
                {
                    var local = rootT.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    var pos = local.GetColumn(3);
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "p:{0:F3}_{1:F3}_{2:F3}#", pos.x, pos.y, pos.z);
                }
            }
            return sb.ToString();
        }

        // Normalize an euler angle vector to a rounded string, handling the 0/360 boundary.
        private static string NormalizeEuler(Vector3 euler)
        {
            float x = NormalizeAngle(euler.x);
            float y = NormalizeAngle(euler.y);
            float z = NormalizeAngle(euler.z);
            return string.Format(CultureInfo.InvariantCulture,
                "{0:F0}_{1:F0}_{2:F0}", x, y, z);
        }

        private static float NormalizeAngle(float a)
        {
            a = a % 360f;
            if (a > 180f) a -= 360f;
            if (a < -180f) a += 360f;
            return Mathf.Round(a);
        }

        private string ComputeMeshFingerprint(Mesh m)
        {
            if (m == null) return "Mnull";
            int key = m.GetInstanceID();
            if (meshFingerprintCache.TryGetValue(key, out var cached)) return cached;

            // Raw byte hashing (SHA256) causes mismatches because Unity's Instantiate(mesh) 
            // when vendoring can change the internal binary layout compared to the FBX importer.
            // Instead, we use an extremely precise structural fingerprint that works regardless
            // of binary layout or m.isReadable:
            var sb = new StringBuilder(192);
            sb.Append("M:").Append(m.vertexCount).Append(':').Append(m.subMeshCount);

            // Append exact triangle counts for every submesh
            for (int i = 0; i < m.subMeshCount; i++)
            {
                sb.Append(':').Append(m.GetIndexCount(i));
            }

            // Append exact bounds down to 4 decimals
            var b = m.bounds;
            sb.AppendFormat(CultureInfo.InvariantCulture,
                ":{0:F4}_{1:F4}_{2:F4}:{3:F4}_{4:F4}_{5:F4}",
                b.size.x, b.size.y, b.size.z, b.center.x, b.center.y, b.center.z);

            string fp = sb.ToString();
            meshFingerprintCache[key] = fp;
            return fp;
        }

        private static string ComputeMaterialFingerprint(Material mat)
        {
            if (mat == null) return "matNULL";
            var sb = new StringBuilder(256);
            sb.Append("mat:");
            sb.Append(mat.shader != null ? mat.shader.name : "noshader");

            // Color properties
            foreach (var prop in new[] { "_Color", "_BaseColor", "_EmissionColor" })
            {
                if (!mat.HasProperty(prop)) continue;
                var c = mat.GetColor(prop);
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "|{0}:{1:F2}_{2:F2}_{3:F2}_{4:F2}", prop, c.r, c.g, c.b, c.a);
            }

            // Texture properties 遯ｶ繝ｻuse GUID for identity so vendored copies match originals
            foreach (var prop in new[] { "_MainTex", "_BaseMap", "_BumpMap",
                                          "_MetallicGlossMap", "_EmissionMap",
                                          "_OcclusionMap", "_ParallaxMap",
                                          "_DetailAlbedoMap", "_DetailNormalMap" })
            {
                if (!mat.HasProperty(prop)) continue;
                var tex = mat.GetTexture(prop);
                if (tex == null)
                {
                    sb.Append('|').Append(prop).Append(":null");
                    continue;
                }
                // Try to use asset GUID for vendoring-invariant comparison
                var texPath = AssetDatabase.GetAssetPath(tex);
                if (!string.IsNullOrEmpty(texPath) &&
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tex, out string guid, out long _))
                {
                    sb.Append('|').Append(prop).Append(":g:").Append(guid);
                }
                else
                {
                    sb.Append('|').Append(prop).Append(':').Append(StripDepsSuffix(tex.name));
                }
            }

            // Float properties
            foreach (var prop in new[] { "_Metallic", "_Glossiness", "_Smoothness",
                                          "_BumpScale", "_Cutoff" })
            {
                if (!mat.HasProperty(prop)) continue;
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "|{0}:{1:F2}", prop, mat.GetFloat(prop));
            }

            return sb.ToString();
        }

        // Strip the "__<guid8>_<localId>" suffix from a vendored asset's name so that the
        // vendored copy and the original asset both fingerprint to the same string.
        private static string StripDepsSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int idx = name.LastIndexOf("__", StringComparison.Ordinal);
            return idx > 0 ? name.Substring(0, idx) : name;
        }

        // ------------------------------------------------------------
        // Path helpers
        // ------------------------------------------------------------
        private bool IsUnderPrefabRoot(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            var root = prefabRoot.TrimEnd('/') + "/";
            return assetPath.Replace("\\",  "/").StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        // Only paths physically under Assets/ are safe to CopyAsset / CreateAsset against.
        // Skip empty paths (built-in primitives), Packages/, Resources/unity_builtin_extra,
        // Library/unity default resources, etc. Trying to CopyAsset those crashes the editor.
        private static bool IsVendorable(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            var p = assetPath.Replace("\\",  "/");
            return p.StartsWith("Assets/", StringComparison.Ordinal);
        }

        // GUID alone is the file's identity. For FBX-embedded sub-assets (each mesh in
        // a multi-mesh FBX, each embedded material, etc.) the GUID is shared across
        // siblings and we must also include LocalFileIdentifier to get a per-sub-asset key.
        private static bool TryGetUniqueAssetKey(UnityEngine.Object obj, out string cacheKey, out string filenameSuffix)
        {
            cacheKey = null;
            filenameSuffix = null;
            if (obj == null) return false;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId))
                return false;
            if (string.IsNullOrEmpty(guid)) return false;
            cacheKey = $"{guid}_{localId}";
            string guid8 = guid.Length >= 8 ? guid.Substring(0, 8) : guid;
            filenameSuffix = $"{guid8}_{(uint)localId:x8}";
            return true;
        }

        private string FindFirstMeshAssetPath(GameObject go)
        {
            var mf = go.GetComponentInChildren<MeshFilter>(true);
            if (mf != null && mf.sharedMesh != null)
            {
                var p = AssetDatabase.GetAssetPath(mf.sharedMesh);
                if (!string.IsNullOrEmpty(p)) return p;
            }
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null && smr.sharedMesh != null)
            {
                var p = AssetDatabase.GetAssetPath(smr.sharedMesh);
                if (!string.IsNullOrEmpty(p)) return p;
            }
            return null;
        }

        private void EnsureFolder(string folderPath)
        {
            folderPath = folderPath.Replace("\\",  "/").TrimEnd('/');
            // Check our in-memory cache first (covers the StartAssetEditing batch
            // where IsValidFolder returns stale data).
            if (createdFolders.Contains(folderPath)) return;
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                createdFolders.Add(folderPath);
                return;
            }
            var parent = Path.GetDirectoryName(folderPath)?.Replace("\\",  "/");
            var leaf = Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            if (!createdFolders.Contains(folderPath) && !AssetDatabase.IsValidFolder(folderPath))
                AssetDatabase.CreateFolder(parent, leaf);
            createdFolders.Add(folderPath);
        }

        private static string SanitizeFolder(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Unknown";
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            s = s.Replace('/', '_').Replace('\\', '_').Trim();
            return string.IsNullOrEmpty(s) ? "Unknown" : s;
        }

        private static string SanitizeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Unnamed";
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            s = s.Replace(' ', '_').Trim();
            return string.IsNullOrEmpty(s) ? "Unnamed" : s;
        }

        private static string MakeProjectRelative(string absolute)
        {
            absolute = absolute.Replace("\\",  "/");
            var dataPath = Application.dataPath.Replace("\\",  "/");
            if (absolute == dataPath) return "Assets";
            if (absolute.StartsWith(dataPath + "/")) return "Assets" + absolute.Substring(dataPath.Length);
            return null;
        }

        private string Tr(string ja, string en) => uiLanguage == Language.Japanese ? ja : en;

        private static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 2);
            rect.height = 1;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }

        // ============================================================
        // Existing-Prefab Cleanup
        // ============================================================
        private class DupGroup
        {
            public string fingerprint;
            public List<string> prefabPaths = new List<string>();
            public string keepPath; // the one to keep
        }

        private void DrawCleanupSection()
        {
            EditorGUILayout.LabelField(
                "5. Clean up duplicate prefabs",
                EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.HelpBox(
                    Tr("Prefab郢晢ｽｫ郢晢ｽｼ郢晉｣ｯ繝ｻ闕ｳ荵昴・隴鯉ｽ｢陝・ｦ排efab郢ｧ蛛ｵ縺帷ｹｧ・ｭ郢晢ｽ｣郢晢ｽｳ邵ｺ蜉ｱﾂ竏晞・闕ｳﾂ郢晢ｽ｡郢昴・縺咏ｹ晢ｽ･/郢晄ｧｭ繝ｦ郢晢ｽｪ郢ｧ・｢郢晢ｽｫ隶貞玄繝ｻ邵ｺ・ｮ鬩･蟠趣ｽ､繝ｻ・定ｮ諛ｷ繝ｻ邵ｺ蜉ｱ竏ｪ邵ｺ蜷ｶﾂ繝ｻ" +
                       "Deletes duplicates and replaces scene references.",
                       "Scans existing prefabs under the prefab root for duplicate mesh/material compositions. " +
                       "Cleanup deletes duplicates and re-routes scene references to the kept prefab."),
                    MessageType.None);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Scan", GUILayout.Height(28)))
                    {
                        RunCleanupScan();
                    }

                    using (new EditorGUI.DisabledScope(!cleanupScanned || cleanupGroups == null || cleanupGroups.Count == 0))
                    {
                        if (GUILayout.Button("Clean up", GUILayout.Height(28)))
                        {
                            RunCleanup();
                        }
                    }
                }

                // Show scan results
                if (cleanupScanned && cleanupGroups != null)
                {
                    if (cleanupGroups.Count == 0)
                    {
                        EditorGUILayout.HelpBox(
                            "No duplicates found.",
                            MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.LabelField(
                            Tr($"{cleanupGroups.Count} 郢ｧ・ｰ郢晢ｽｫ郢晢ｽｼ郢晏干繝ｻ鬩･蟠趣ｽ､繝ｻ・定ｮ諛ｷ繝ｻ",
                               $"Found {cleanupGroups.Count} duplicate groups"),
                            EditorStyles.boldLabel);

                        int totalDeletable = 0;
                        foreach (var g in cleanupGroups) totalDeletable += g.prefabPaths.Count - 1;
                        EditorGUILayout.LabelField(
                            Tr($"陷台ｼ∝求闔莠･・ｮ繝ｻ {totalDeletable} 陋滉ｹ昴・Prefab",
                               $"Will delete: {totalDeletable} prefabs"));

                        cleanupScroll = EditorGUILayout.BeginScrollView(cleanupScroll,
                            GUILayout.MaxHeight(300));

                        for (int gi = 0; gi < cleanupGroups.Count; gi++)
                        {
                            var g = cleanupGroups[gi];
                            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                            EditorGUILayout.LabelField(
                                $"Group {gi + 1}: {g.prefabPaths.Count} prefabs",
                                EditorStyles.miniBoldLabel);
                            for (int pi = 0; pi < g.prefabPaths.Count; pi++)
                            {
                                var path = g.prefabPaths[pi];
                                bool isKeep = path == g.keepPath;
                                var fileName = Path.GetFileNameWithoutExtension(path);

                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    var style = isKeep ? EditorStyles.boldLabel : EditorStyles.label;
                                    var color = GUI.color;
                                    if (!isKeep) GUI.color = new Color(1f, 0.6f, 0.6f);
                                    EditorGUILayout.LabelField(
                                        isKeep ? $"隨ｨ繝ｻ{fileName}" : $"  隨ｨ繝ｻ{fileName}",
                                        style);
                                    GUI.color = color;

                                    if (!isKeep && GUILayout.Button(
                                        "Keep this",
                                        GUILayout.Width(90)))
                                    {
                                        g.keepPath = path;
                                    }

                                    if (GUILayout.Button("Ping", GUILayout.Width(40)))
                                    {
                                        var obj = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                                        if (obj != null) EditorGUIUtility.PingObject(obj);
                                    }
                                }
                            }
                            EditorGUILayout.EndVertical();
                        }

                        EditorGUILayout.EndScrollView();
                    }
                }
            }
        }

        private void RunCleanupScan()
        {
            cleanupGroups = new List<DupGroup>();
            cleanupScanned = true;
            meshFingerprintCache.Clear();

            var root = prefabRoot.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(root))
            {
                Debug.LogWarning($"[SceneToPrefabBatcher] Prefab root not found: {root}");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { root });
            var fpToGroup = new Dictionary<string, DupGroup>();

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (string.IsNullOrEmpty(path)) continue;
                    // Skip _Deps
                    if (path.Replace("\\",  "/").Contains("/_Deps/")) continue;

                    EditorUtility.DisplayProgressBar("Scanning prefabs",
                        $"[{i + 1}/{guids.Length}] {Path.GetFileName(path)}",
                        (i + 1) / (float)guids.Length);

                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;

                    // Compute a cleanup-specific fingerprint that ignores rotation and scale
                    // since we only care about mesh/material identity for cleanup.
                    string fp = ComputeCleanupFingerprint(prefab);
                    if (string.IsNullOrEmpty(fp)) continue;

                    if (!fpToGroup.TryGetValue(fp, out var group))
                    {
                        group = new DupGroup { fingerprint = fp };
                        fpToGroup[fp] = group;
                    }
                    group.prefabPaths.Add(path);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // Only keep groups with 2+ entries (actual duplicates)
            foreach (var kvp in fpToGroup)
            {
                if (kvp.Value.prefabPaths.Count < 2) continue;
                // Default: keep the shortest-named one (likely the original without .001 suffix)
                kvp.Value.keepPath = kvp.Value.prefabPaths
                    .OrderBy(p => Path.GetFileNameWithoutExtension(p).Length)
                    .ThenBy(p => p)
                    .First();
                cleanupGroups.Add(kvp.Value);
            }

            // Sort groups by size descending
            cleanupGroups.Sort((a, b) => b.prefabPaths.Count.CompareTo(a.prefabPaths.Count));

            int totalDup = 0;
            foreach (var g in cleanupGroups) totalDup += g.prefabPaths.Count - 1;
            Debug.Log($"[SceneToPrefabBatcher] Cleanup scan: {cleanupGroups.Count} duplicate groups, " +
                      $"{totalDup} prefabs to delete.");
        }

        // Fingerprint for cleanup: ignores rotation, scale, and local position.
        // Only compares mesh data + material identity.
        private string ComputeCleanupFingerprint(GameObject prefab)
        {
            if (prefab == null) return null;
            var sb = new StringBuilder(512);

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            // Sort by mesh fingerprint to ensure consistent ordering
            var entries = new List<string>();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var entrySb = new StringBuilder();
                Mesh m = null;
                if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    m = mf != null ? mf.sharedMesh : null;
                }
                else if (r is SkinnedMeshRenderer smr)
                {
                    m = smr.sharedMesh;
                }
                entrySb.Append(ComputeMeshFingerprint(m)).Append('|');
                foreach (var mat in r.sharedMaterials)
                {
                    entrySb.Append(ComputeMaterialFingerprint(mat)).Append('|');
                }
                entries.Add(entrySb.ToString());
            }
            entries.Sort(StringComparer.Ordinal);
            foreach (var e in entries) sb.Append(e);

            return sb.Length > 0 ? sb.ToString() : null;
        }

        private void RunCleanup()
        {
            if (cleanupGroups == null || cleanupGroups.Count == 0) return;

            int totalDel = 0;
            foreach (var g in cleanupGroups) totalDel += g.prefabPaths.Count - 1;

            bool confirmed = EditorUtility.DisplayDialog(
                "Clean up duplicate prefabs",
                Tr($"Deleting {totalDel} duplicate prefabs across {cleanupGroups.Count} groups.\n" +
                   $"Scene references will be replaced.\n\nThis cannot be undone. Continue?", $"Deleting {totalDel} duplicate prefabs across {cleanupGroups.Count} groups.\n" +
                   $"Scene references will be replaced.\n\nThis cannot be undone. Continue?"),
                "Execute",
                "Cancel");

            if (!confirmed) return;

            int deleted = 0;
            int rerouted = 0;

            try
            {
                for (int gi = 0; gi < cleanupGroups.Count; gi++)
                {
                    var g = cleanupGroups[gi];
                    EditorUtility.DisplayProgressBar(
                        "Cleaning up...",
                        $"Group {gi + 1}/{cleanupGroups.Count}",
                        (gi + 1) / (float)cleanupGroups.Count);

                    var keepAsset = AssetDatabase.LoadAssetAtPath<GameObject>(g.keepPath);
                    if (keepAsset == null)
                    {
                        Debug.LogWarning($"[SceneToPrefabBatcher] Keep target not found: {g.keepPath}");
                        continue;
                    }

                    foreach (var dupPath in g.prefabPaths)
                    {
                        if (dupPath == g.keepPath) continue;

                        // Re-route scene instances before deleting
                        rerouted += RerouteSceneInstances(dupPath, keepAsset);

                        // Delete the duplicate prefab
                        if (AssetDatabase.DeleteAsset(dupPath))
                        {
                            deleted++;
                            if (verboseLog)
                                Debug.Log($"[SceneToPrefabBatcher] Deleted duplicate: {dupPath} (kept: {g.keepPath})");
                        }
                        else
                        {
                            Debug.LogWarning($"[SceneToPrefabBatcher] Failed to delete: {dupPath}");
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"[SceneToPrefabBatcher] Cleanup done. Deleted {deleted} prefabs, re-routed {rerouted} scene instances.");

            // Clear scan results
            cleanupGroups = null;
            cleanupScanned = false;
        }

        // Find all instances of a prefab in open scenes and replace them with instances of the keep target.
        private int RerouteSceneInstances(string deletePrefabPath, GameObject keepAsset)
        {
            int count = 0;
            var deleteAsset = AssetDatabase.LoadAssetAtPath<GameObject>(deletePrefabPath);
            if (deleteAsset == null) return 0;

            // Search all open scenes
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;

                foreach (var rootGO in scene.GetRootGameObjects())
                {
                    foreach (var t in rootGO.GetComponentsInChildren<Transform>(true))
                    {
                        var go = t.gameObject;
                        if (!PrefabUtility.IsPartOfPrefabInstance(go)) continue;
                        var nearestRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                        if (nearestRoot != go) continue; // only process prefab roots

                        var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                        if (source == null) continue;
                        var sourcePath = AssetDatabase.GetAssetPath(source);
                        if (sourcePath != deletePrefabPath) continue;

                        // This is an instance of the prefab we're deleting 遯ｶ繝ｻreroute it
                        var parent = go.transform.parent;
                        var siblingIndex = go.transform.GetSiblingIndex();
                        var localPos = go.transform.localPosition;
                        var localRot = go.transform.localRotation;
                        var localScale = go.transform.localScale;
                        var wasActive = go.activeSelf;

                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(keepAsset, scene);
                        if (inst == null) continue;

                        inst.transform.SetParent(parent, false);
                        inst.transform.localPosition = localPos;
                        inst.transform.localRotation = localRot;
                        inst.transform.localScale = localScale;
                        inst.transform.SetSiblingIndex(siblingIndex);
                        inst.SetActive(wasActive);
                        inst.name = go.name; // preserve original name in scene

                        Undo.RegisterCreatedObjectUndo(inst, "Reroute prefab instance");
                        Undo.DestroyObjectImmediate(go);
                        count++;
                    }
                }
            }
            return count;
        }
    }
}

