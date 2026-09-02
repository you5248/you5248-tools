using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace you5248.QuestPort
{
    /// <summary>
    /// QuestPortConfig の確認・編集用ウィンドウ。
    /// </summary>
    public class QuestPortConfigWindow : EditorWindow
    {
        QuestPortConfig _config;
        SerializedObject _so;
        Vector2 _scroll;

        [MenuItem("you5248/Quest Port Config")]
        static void Open() => GetWindow<QuestPortConfigWindow>("Quest Port");

        void OnEnable() => EnsureConfig();

        void EnsureConfig()
        {
            if (_config == null)
                _config = LoadOrCreate();
            if (_config != null && (_so == null || _so.targetObject != _config))
                _so = new SerializedObject(_config);
        }

        static QuestPortConfig LoadOrCreate()
        {
            var config = AssetDatabase.LoadAssetAtPath<QuestPortConfig>(QuestPortConfig.DefaultAssetPath);
            if (config != null) return config;

            foreach (var guid in AssetDatabase.FindAssets("t:QuestPortConfig"))
            {
                config = AssetDatabase.LoadAssetAtPath<QuestPortConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (config != null) return config;
            }

            EnsureFolder("Assets/you5248/QuestPort");
            config = CreateInstance<QuestPortConfig>();
            AssetDatabase.CreateAsset(config, QuestPortConfig.DefaultAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[QuestPort] 設定アセットを作成: {QuestPortConfig.DefaultAssetPath}");
            return config;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace("\\", "/");
            string leaf = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(leaf))
                AssetDatabase.CreateFolder(parent, leaf);
        }

        void OnGUI()
        {
            EnsureConfig();
            if (_config == null || _so == null)
            {
                EditorGUILayout.HelpBox("QuestPortConfig を読み込めませんでした。", MessageType.Error);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Quest Port Config", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "モバイル（Android / iOS）ビルド時に IProcessSceneWithReport が適用します。\n" +
                "シーンファイルは保存されません（インメモリ差し替え）。\n" +
                "destroyInsteadOfDisable=ON かつ実ビルド時は DestroyImmediate。\n" +
                "PlayMode（report=null）では常に SetActive(false)。",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            var next = (QuestPortConfig)EditorGUILayout.ObjectField("Config", _config, typeof(QuestPortConfig), false);
            if (EditorGUI.EndChangeCheck() && next != null)
            {
                _config = next;
                _so = new SerializedObject(_config);
            }

            EditorGUILayout.Space();
            _so.Update();
            DrawPropertiesExcluding(_so, "m_Script");
            _so.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("補助", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(Selection.gameObjects == null || Selection.gameObjects.Length == 0))
            {
                if (GUILayout.Button("選択中の GameObject を無効化リストに追加（階層パス）"))
                    AddSelectedPaths();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("検証（現在のシーン・保存なし）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"Active Build Target: {EditorUserBuildSettings.activeBuildTarget}\n" +
                $"無効化/破棄パス数: {(_config.disableObjectPaths != null ? _config.disableObjectPaths.Length : 0)}\n" +
                $"Destroy モード: {_config.destroyInsteadOfDisable}\n" +
                $"マテ差し替え数: {(_config.materialSwaps != null ? _config.materialSwaps.Length : 0)}\n" +
                $"PPS除去: {_config.removePostProcessVolumes}",
                MessageType.None);

            if (GUILayout.Button("無効化パスの存在チェック（アクティブシーン）"))
                ValidateDisablePaths();

            if (GUILayout.Button("Ping Config Asset"))
                EditorGUIUtility.PingObject(_config);

            EditorGUILayout.EndScrollView();
        }

        static void DrawPropertiesExcluding(SerializedObject so, params string[] exclude)
        {
            var prop = so.GetIterator();
            bool enter = true;
            while (prop.NextVisible(enter))
            {
                enter = false;
                bool skip = false;
                foreach (var e in exclude)
                {
                    if (prop.name == e) { skip = true; break; }
                }
                if (skip) continue;
                EditorGUILayout.PropertyField(prop, true);
            }
        }

        void AddSelectedPaths()
        {
            EnsureConfig();
            var scene = SceneManager.GetActiveScene();
            var existing = new HashSet<string>(
                _config.disableObjectPaths ?? System.Array.Empty<string>());
            var added = new List<string>();

            foreach (var go in Selection.gameObjects)
            {
                if (go == null || go.scene != scene) continue;
                string path = QuestBuildProcessor.GetHierarchyPath(go.transform);
                if (string.IsNullOrEmpty(path) || existing.Contains(path)) continue;
                existing.Add(path);
                added.Add(path);
            }

            if (added.Count == 0)
            {
                EditorUtility.DisplayDialog("Quest Port",
                    "追加できるパスがありません（アクティブシーン内の選択が必要／既に登録済み）。", "OK");
                return;
            }

            Undo.RecordObject(_config, "Add Quest Disable Paths");
            var list = new List<string>(_config.disableObjectPaths ?? System.Array.Empty<string>());
            list.AddRange(added);
            _config.disableObjectPaths = list.ToArray();
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
            _so = new SerializedObject(_config);
            Debug.Log($"[QuestPort] 無効化パスを {added.Count} 件追加:\n  " + string.Join("\n  ", added));
        }

        void ValidateDisablePaths()
        {
            EnsureConfig();
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                EditorUtility.DisplayDialog("Quest Port", "有効なアクティブシーンがありません。", "OK");
                return;
            }

            int ok = 0, miss = 0;
            var paths = _config.disableObjectPaths ?? System.Array.Empty<string>();
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (QuestBuildProcessor.FindByHierarchyPath(scene, path.Trim()) != null) ok++;
                else
                {
                    miss++;
                    Debug.LogWarning($"[QuestPort] パス未検出: {path}");
                }
            }

            EditorUtility.DisplayDialog("Quest Port",
                $"シーン '{scene.name}' でのパス検証\n見つかった: {ok}\n未検出: {miss}", "OK");
        }
    }
}
