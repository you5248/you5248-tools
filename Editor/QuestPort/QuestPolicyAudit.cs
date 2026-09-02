using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace you5248.QuestPort
{
    /// <summary>
    /// Quest の固定方針（MonoSH 不使用、植生の風なし）を、実シーン参照に対して監査する。
    /// アセットは変更しない。
    /// </summary>
    public static class QuestPolicyAudit
    {
        const string ScenePath = "Assets/Forest test/washitu.unity";
        const string AutomationRequestRelativePath = "Library/QuestPolicyAudit.request";
        const string AutomationResultRelativePath = "Library/QuestPolicyAudit.result";

        [InitializeOnLoadMethod]
        static void RunRequestedAuditAfterReload()
        {
            string requestPath = ProjectPath(AutomationRequestRelativePath);
            if (!File.Exists(requestPath)) return;
            File.Delete(requestPath);
            EditorApplication.delayCall += () =>
            {
                string result;
                try
                {
                    result = BuildReport();
                    Debug.Log(result);
                }
                catch (Exception exception)
                {
                    result = "[QuestPolicyAudit] FAILED\n" + exception;
                    Debug.LogError(result);
                }
                File.WriteAllText(ProjectPath(AutomationResultRelativePath), result);
            };
        }
        [MenuItem("you5248/Quest Port/Audit MonoSH and Vegetation Wind")]
        public static void Run()
        {
            Debug.Log(BuildReport());
        }

        static string BuildReport()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedForAudit = !scene.IsValid() || !scene.isLoaded;
            if (openedForAudit)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                return BuildReport(scene);
            }
            finally
            {
                if (openedForAudit && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        static string BuildReport(Scene scene)
        {
            QuestPortConfig config = AssetDatabase.LoadAssetAtPath<QuestPortConfig>(
                QuestPortConfig.DefaultAssetPath);

            var swaps = new Dictionary<Material, Material>();
            if (config != null && config.materialSwaps != null)
            {
                foreach (QuestPortConfig.MaterialSwap swap in config.materialSwaps)
                {
                    if (swap == null || swap.sourceMat == null || swap.questMat == null) continue;
                    swaps[swap.sourceMat] = swap.questMat;
                }
            }

            var materials = new HashSet<Material>();
            foreach (Renderer renderer in CollectInScene<Renderer>(scene))
            {
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null) materials.Add(material);
            }
            foreach (Terrain terrain in CollectInScene<Terrain>(scene))
            {
                if (terrain.materialTemplate != null) materials.Add(terrain.materialTemplate);
            }

            var shaderCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var monoSh = new List<string>();
            var wind = new List<string>();
            var missingSwapTarget = new List<string>();

            foreach (Material source in materials)
            {
                Material effective = source;
                if (swaps.TryGetValue(source, out Material quest))
                {
                    if (quest == null)
                    {
                        missingSwapTarget.Add(AssetDatabase.GetAssetPath(source));
                        continue;
                    }
                    effective = quest;
                }

                string shaderName = effective.shader != null ? effective.shader.name : "<missing>";
                shaderCounts.TryGetValue(shaderName, out int count);
                shaderCounts[shaderName] = count + 1;

                if (UsesMonoSh(effective))
                    monoSh.Add(Describe(source, effective, shaderName));
                if (UsesVegetationWind(effective, shaderName))
                    wind.Add(Describe(source, effective, shaderName));
            }

            int l1References = CountBakeryL1References(scene);
            var log = new StringBuilder();
            log.AppendLine("[QuestPolicyAudit] scene=" + ScenePath);
            log.AppendLine($"  scene materials={materials.Count}, effective shaders={shaderCounts.Count}");
            foreach (var pair in shaderCounts)
                log.AppendLine($"  SHADER {pair.Value,3}  {pair.Key}");
            log.AppendLine($"  MonoSH enabled materials={monoSh.Count}");
            foreach (string item in monoSh) log.AppendLine("    MONOSH " + item);
            log.AppendLine($"  Vegetation wind enabled materials={wind.Count}");
            foreach (string item in wind) log.AppendLine("    WIND " + item);
            log.AppendLine($"  Bakery _L1 serialized references={l1References}");
            foreach (string item in missingSwapTarget) log.AppendLine("    MISSING SWAP " + item);
            return log.ToString();
        }

        // Unity -executeMethod entry point.
        public static void RunFromCommandLine()
        {
            Run();
        }

        static bool UsesMonoSh(Material material)
        {
            return IsEnabled(material, "_UseMonoSH", "_MONOSH_ON")
                || IsEnabled(material, "_UseMonoSHSpec", "_MONOSHSPEC_ON")
                || (material.HasProperty("_BakeryMode") && material.GetFloat("_BakeryMode") == 3f)
                || material.IsKeywordEnabled("_BAKERY_MONOSH");
        }

        static bool UsesVegetationWind(Material material, string shaderName)
        {
            if (material.HasProperty("_WindOff") && material.GetFloat("_WindOff") < 0.5f)
                return true;
            if (material.HasProperty("_WindToggle") && material.GetFloat("_WindToggle") > 0.5f)
                return true;
            if (material.HasProperty("_VertexWindEnabled") && material.GetFloat("_VertexWindEnabled") > 0.5f)
                return true;

            bool knownWindShader = shaderName == "BK/Grass"
                || shaderName == "BK/Impostor"
                || shaderName.StartsWith("BK/Vegetation", StringComparison.Ordinal)
                || shaderName == "you5248/SoyogiCleanroom";
            if (!knownWindShader) return false;

            return !material.HasProperty("_WindMultiplier")
                || Math.Abs(material.GetFloat("_WindMultiplier")) > 0.0001f;
        }

        static bool IsEnabled(Material material, string property, string keyword)
        {
            return (material.HasProperty(property) && material.GetFloat(property) > 0.5f)
                || material.IsKeywordEnabled(keyword);
        }

        static string Describe(Material source, Material effective, string shaderName)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string effectivePath = AssetDatabase.GetAssetPath(effective);
            return source == effective
                ? $"{sourcePath} [{shaderName}]"
                : $"{sourcePath} -> {effectivePath} [{shaderName}]";
        }

        static int CountBakeryL1References(Scene scene)
        {
            int count = 0;
            foreach (MonoBehaviour component in CollectInScene<MonoBehaviour>(scene))
            {
                if (component == null) continue;
                try
                {
                    var serialized = new SerializedObject(component);
                    SerializedProperty property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        if (!(property.objectReferenceValue is Texture2D texture)) continue;
                        string path = AssetDatabase.GetAssetPath(texture).Replace('\\', '/');
                        if (!path.StartsWith("Assets/BakeryLightmaps/", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (Path.GetFileNameWithoutExtension(path)
                            .EndsWith("_L1", StringComparison.OrdinalIgnoreCase)) count++;
                    }
                }
                catch (Exception)
                {
                    // Some third-party components cannot be serialized during an audit.
                }
            }
            return count;
        }

        static IEnumerable<T> CollectInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T component in root.GetComponentsInChildren<T>(true))
                    yield return component;
            }
        }

        static string ProjectPath(string relativePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

    }
}
