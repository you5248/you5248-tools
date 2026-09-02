using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace you5248.QuestPort
{
    /// <summary>
    /// モバイル（Android / iOS）ビルド時はシーン差し替え、非モバイル実ビルド時は destroyOnPcPaths を適用する。
    /// シーンファイルへの保存は行わない（決定的かつ可逆なインメモリ変更）。
    /// </summary>
    public class QuestBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 100;

        static bool IsMobileTarget(BuildTarget t) =>
            t == BuildTarget.Android || t == BuildTarget.iOS;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            var config = LoadConfig();
            // 実ビルドでは BuildReport を正とする。SDK の複数プラットフォーム処理で
            // activeBuildTarget と実際の出力先が一時的にずれても誤分岐しないようにする。
            var target = report != null
                ? report.summary.platform
                : EditorUserBuildSettings.activeBuildTarget;

            // 非モバイルの実ビルドのみ: Quest 専用参照保持オブジェクトを PC バンドルから除外。
            // iOS はモバイル扱い（差し替え経路へ）。PlayMode（report==null）では何もしない。
            if (!IsMobileTarget(target))
            {
                if (report != null)
                {
                    if (config == null)
                    {
                        Debug.LogWarning(
                            $"[QuestPort] 設定アセットが見つかりません（{QuestPortConfig.DefaultAssetPath}）。" +
                            "destroyOnPcPaths をスキップします。");
                    }
                    else
                    {
                        var pcLog = new StringBuilder();
                        pcLog.AppendLine(
                            $"[QuestPort] 非モバイル実ビルド (target={target}): destroyOnPcPaths 適用 scene={scene.name}");
                        int pcDestroyed = RemoveObjectsByPath(
                            scene, config.destroyOnPcPaths, destroy: true, pcLog);
                        pcLog.AppendLine($"[QuestPort] destroyOnPcPaths 完了: 破棄={pcDestroyed}");
                        Debug.Log(pcLog.ToString());
                    }
                }
                else
                {
                    Debug.Log($"[QuestPort] 非モバイル（PlayMode, target={target}）のためスキップ: {scene.name}");
                }
                return;
            }

            if (config == null)
            {
                Debug.LogWarning(
                    $"[QuestPort] 設定アセットが見つかりません（{QuestPortConfig.DefaultAssetPath}）。" +
                    $"モバイル差し替えをスキップします (target={target})。");
                return;
            }

            // report == null はエディタ PlayMode 突入。Destroy すると再生を壊すので無効化に留める。
            bool destroy = config.destroyInsteadOfDisable && report != null;

            var log = new StringBuilder();
            log.AppendLine(
                $"[QuestPort] モバイル差し替え開始 (target={target}): scene={scene.name}, " +
                $"mode={(destroy ? "DESTROY" : "DISABLE")} " +
                $"(report={(report != null ? "build" : "null/PlayMode")})");
            int removed = RemoveObjectsByPath(scene, config.disableObjectPaths, destroy, log);
            int enabled = EnableObjectsByPath(scene, config.enableOnAndroidPaths, log);
            int swapped = SwapMaterials(scene, config.materialSwaps, log);
            int meshSwapped = SwapMeshes(scene, config.meshSwaps, log);
            SwapSerializedComponentReferences(
                scene, config.materialSwaps, config.fontSwaps,
                out int serializedMaterialSwapped, out int fontSwapped, log);
            int policyMaterials = ApplyQuestMaterialPolicy(
                scene, log, out int monoShDisabled, out int vegetationWindDisabled);
            int batchingCleared = DisableStaticBatching(
                scene, config.disableStaticBatchingPaths, log, out int instancingEnabled);
            int directionalMapsRemoved = 0;
            // LightmapSettings はグローバル設定で、Bakery の _L1 参照も破棄するため
            // PlayMode で実行するとエディタのセッション全体を汚染する。実ビルド時のみ行う。
            // モバイルは MonoSH(L1) を一切使わない固定方針なので、実ビルドでは常に適用する。
            if (report != null)
                directionalMapsRemoved = ForceNonDirectionalLightmaps(scene, log);
            else
                log.AppendLine("  Non-Directional Lightmap: PlayMode のため _L1 除去のみスキップ");

            // ライトの破棄・影の無効化は PlayMode で走らせるとエディタの見た目を壊す
            // （太陽の影が消え、日向は明るすぎ・日陰は暗すぎになる）。実ビルド時のみ行う。
            int localLightsRemoved = 0;
            if (report != null)
                localLightsRemoved = OptimizeLights(
                    scene, destroy, config.removeRealtimeLocalLights,
                    config.disableRealtimeShadows, log);
            else
                log.AppendLine("  ライト最適化: PlayMode のためスキップ");
            int ppsRemoved = 0;
            if (config.removePostProcessVolumes)
                ppsRemoved = RemovePostProcessVolumes(scene, destroy, log);
            else
                log.AppendLine("  PPS Volume 除去: OFF（スキップ）");

            string removeLabel = destroy ? "破棄" : "無効化";
            log.AppendLine(
                $"[QuestPort] 完了: {removeLabel}={removed}, マテ差し替え={swapped}, " +
                $"メッシュ差し替え={meshSwapped}, " +
                $"シリアライズマテ参照={serializedMaterialSwapped}, フォント={fontSwapped}, " +
                $"Quest方針マテリアル={policyMaterials}（MonoSH無効={monoShDisabled}, " +
                $"植生風無効={vegetationWindDisabled}）, " +
                $"静的バッチング解除={batchingCleared}（マテリアルInstancing有効化={instancingEnabled}）, " +
                $"Directional Map除去={directionalMapsRemoved}, Local Light除去={localLightsRemoved}, " +
                $"PPS{removeLabel}={ppsRemoved}");
            Debug.Log(log.ToString());
        }

        static QuestPortConfig LoadConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<QuestPortConfig>(QuestPortConfig.DefaultAssetPath);
            if (config != null) return config;

            // プロジェクト内の最初の QuestPortConfig をフォールバック検索
            foreach (var guid in AssetDatabase.FindAssets("t:QuestPortConfig"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                config = AssetDatabase.LoadAssetAtPath<QuestPortConfig>(path);
                if (config != null)
                {
                    Debug.Log($"[QuestPort] 既定パス以外の設定を使用: {path}");
                    return config;
                }
            }
            return null;
        }

        static int RemoveObjectsByPath(Scene scene, string[] paths, bool destroy, StringBuilder log)
        {
            if (paths == null || paths.Length == 0)
            {
                log.AppendLine(destroy
                    ? "  オブジェクト破棄: 対象なし"
                    : "  オブジェクト無効化: 対象なし");
                return 0;
            }

            int count = 0;
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var go = FindByHierarchyPath(scene, path.Trim());
                if (go == null)
                {
                    log.AppendLine($"  [MISS] 対象が見つからない: {path}");
                    continue;
                }

                if (destroy)
                {
                    Object.DestroyImmediate(go);
                    count++;
                    log.AppendLine($"  [DESTROY] {path}");
                }
                else
                {
                    if (!go.activeSelf)
                    {
                        log.AppendLine($"  [SKIP] 既に無効: {path}");
                        continue;
                    }
                    go.SetActive(false);
                    count++;
                    log.AppendLine($"  [OFF] {path}");
                }
            }
            return count;
        }

        /// <summary>
        /// モバイル（Android / iOS）のビルド/PlayMode でのみ、指定パスの GameObject を有効化する。
        /// シーン上で非アクティブにしてある Quest 専用オブジェクトの自動 ON 用。
        /// </summary>
        static int EnableObjectsByPath(Scene scene, string[] paths, StringBuilder log)
        {
            if (paths == null || paths.Length == 0) return 0;

            int count = 0;
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var go = FindByHierarchyPath(scene, path.Trim());
                if (go == null)
                {
                    log.AppendLine($"  [MISS] 有効化対象が見つからない: {path}");
                    continue;
                }
                if (go.activeSelf) continue;
                go.SetActive(true);
                count++;
                log.AppendLine($"  [ON] {path}");
            }
            return count;
        }

        /// <summary>
        /// 指定階層配下の Renderer から BatchingStatic を外し、共有マテリアルの GPU Instancing を有効化する。
        /// インメモリ変更のみ（シーン保存しない）。PlayMode でも安全。
        /// </summary>
        static int DisableStaticBatching(
            Scene scene, string[] paths, StringBuilder log, out int materialsEnabled)
        {
            materialsEnabled = 0;
            if (paths == null || paths.Length == 0)
            {
                log.AppendLine("  静的バッチング解除: 対象なし");
                return 0;
            }

            int cleared = 0;
            var enabledMats = new HashSet<Material>();

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var root = FindByHierarchyPath(scene, path.Trim());
                if (root == null)
                {
                    log.AppendLine($"  [MISS] 静的バッチング解除対象が見つからない: {path}");
                    continue;
                }

                int pathCleared = 0;
                int pathMats = 0;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;

                    var go = r.gameObject;
                    var flags = GameObjectUtility.GetStaticEditorFlags(go);
                    if ((flags & StaticEditorFlags.BatchingStatic) != 0)
                    {
                        GameObjectUtility.SetStaticEditorFlags(
                            go, flags & ~StaticEditorFlags.BatchingStatic);
                        cleared++;
                        pathCleared++;
                    }

                    var mats = r.sharedMaterials;
                    if (mats == null) continue;
                    foreach (var mat in mats)
                    {
                        if (mat == null || !enabledMats.Add(mat)) continue;
                        mat.enableInstancing = true;
                        materialsEnabled++;
                        pathMats++;
                    }
                }

                log.AppendLine(
                    $"  [STATIC BATCH OFF] {path}: BatchingStatic解除={pathCleared}, " +
                    $"Instancing有効化={pathMats}");
            }

            log.AppendLine(
                $"  静的バッチング解除: GameObject {cleared} / マテリアルInstancing有効化 {materialsEnabled}");
            return cleared;
        }

        static int SwapMaterials(Scene scene, QuestPortConfig.MaterialSwap[] swaps, StringBuilder log)
        {
            if (swaps == null || swaps.Length == 0)
            {
                log.AppendLine("  マテリアル差し替え: 対象なし");
                return 0;
            }

            var map = new Dictionary<Material, Material>();
            foreach (var s in swaps)
            {
                if (s == null || s.sourceMat == null || s.questMat == null) continue;
                if (map.ContainsKey(s.sourceMat))
                {
                    log.AppendLine($"  [WARN] 重複する差し替え元を上書き: {s.sourceMat.name}");
                }
                map[s.sourceMat] = s.questMat;
                log.AppendLine($"  [MAP] {s.sourceMat.name} → {s.questMat.name}");
            }

            if (map.Count == 0)
            {
                log.AppendLine("  マテリアル差し替え: 有効なマッピングなし");
                return 0;
            }

            int hitCount = 0;
            int slotCount = 0;

            var renderers = CollectInScene<Renderer>(scene);
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;

                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (!map.TryGetValue(mats[i], out var quest)) continue;
                    mats[i] = quest;
                    changed = true;
                    slotCount++;
                }
                if (!changed) continue;

                r.sharedMaterials = mats;
                hitCount++;
            }

            int terrainCount = 0;
            var terrains = CollectInScene<Terrain>(scene);
            foreach (var terrain in terrains)
            {
                if (terrain == null) continue;
                var mat = terrain.materialTemplate;
                if (mat == null) continue;
                if (!map.TryGetValue(mat, out var quest)) continue;
                terrain.materialTemplate = quest;
                terrainCount++;
                hitCount++;
                slotCount++;
                log.AppendLine($"  [TERRAIN] {GetHierarchyPath(terrain.transform)}: {mat.name} → {quest.name}");
            }

            log.AppendLine(
                $"  マテリアル差し替え: Renderer/Terrain {hitCount}（うち Terrain {terrainCount}） / スロット {slotCount}");
            return hitCount;
        }

        /// <summary>
        /// MeshFilter.sharedMesh を Quest 版へ差し替える。
        /// インメモリ変更のみ（シーン保存しない）。PlayMode でも安全。
        /// </summary>
        static int SwapMeshes(Scene scene, QuestPortConfig.MeshSwap[] swaps, StringBuilder log)
        {
            if (swaps == null || swaps.Length == 0)
            {
                log.AppendLine("  メッシュ差し替え: 対象なし");
                return 0;
            }

            var map = new Dictionary<Mesh, Mesh>();
            foreach (var s in swaps)
            {
                if (s == null || s.sourceMesh == null || s.questMesh == null) continue;
                if (map.ContainsKey(s.sourceMesh))
                {
                    log.AppendLine($"  [WARN] 重複するメッシュ差し替え元を上書き: {s.sourceMesh.name}");
                }
                map[s.sourceMesh] = s.questMesh;
                log.AppendLine($"  [MAP] {s.sourceMesh.name} → {s.questMesh.name}");
            }

            if (map.Count == 0)
            {
                log.AppendLine("  メッシュ差し替え: 有効なマッピングなし");
                return 0;
            }

            int hitCount = 0;
            var filters = CollectInScene<MeshFilter>(scene);
            foreach (var mf in filters)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (!map.TryGetValue(mf.sharedMesh, out var quest)) continue;
                mf.sharedMesh = quest;
                hitCount++;
            }

            log.AppendLine($"  メッシュ差し替え: MeshFilter {hitCount}");
            return hitCount;
        }

        /// <summary>
        /// MonoBehaviour（UdonBehaviour、UI.Text、独自コンポーネント等）が保持する
        /// Material / Font 参照を Quest 版へ差し替える。
        /// Renderer 差し替えだけでは届かない Udon の publicVariablesUnityEngineObjects も対象。
        /// インメモリ変更のみ（シーン保存しない）。PlayMode でも安全。
        /// </summary>
        static void SwapSerializedComponentReferences(
            Scene scene,
            QuestPortConfig.MaterialSwap[] materialSwaps,
            QuestPortConfig.FontSwap[] fontSwaps,
            out int materialCount,
            out int fontCount,
            StringBuilder log)
        {
            var materialMap = new Dictionary<Material, Material>();
            if (materialSwaps != null)
            {
                foreach (var s in materialSwaps)
                {
                    if (s == null || s.sourceMat == null || s.questMat == null) continue;
                    materialMap[s.sourceMat] = s.questMat;
                }
            }

            var fontMap = new Dictionary<Font, Font>();
            if (fontSwaps != null)
            {
                foreach (var s in fontSwaps)
                {
                    if (s == null || s.sourceFont == null || s.questFont == null) continue;
                    fontMap[s.sourceFont] = s.questFont;
                }
            }

            materialCount = 0;
            fontCount = 0;
            int componentCount = 0;
            if (materialMap.Count == 0 && fontMap.Count == 0)
            {
                log.AppendLine("  シリアライズ参照差し替え: 有効なマッピングなし");
                return;
            }

            var components = CollectInScene<MonoBehaviour>(scene);
            foreach (var component in components)
            {
                if (component == null) continue;

                try
                {
                    var so = new SerializedObject(component);
                    var prop = so.GetIterator();
                    bool changed = false;
                    while (prop.Next(true))
                    {
                        if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var obj = prop.objectReferenceValue;
                        if (obj == null) continue;

                        if (obj is Material mat && materialMap.TryGetValue(mat, out var questMat))
                        {
                            prop.objectReferenceValue = questMat;
                            materialCount++;
                            changed = true;
                        }
                        else if (obj is Font font && fontMap.TryGetValue(font, out var questFont))
                        {
                            prop.objectReferenceValue = questFont;
                            fontCount++;
                            changed = true;
                        }
                    }

                    if (!changed) continue;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    componentCount++;
                }
                catch (Exception e)
                {
                    log.AppendLine(
                        $"  [WARN] 参照走査をスキップ: {GetHierarchyPath(component.transform)} / " +
                        $"{component.GetType().Name}: {e.Message}");
                }
            }

            log.AppendLine(
                $"  シリアライズ参照差し替え: Component {componentCount} / " +
                $"Material {materialCount} / Font {fontCount}");
        }

        /// <summary>
        /// Quest の固定方針をビルド用マテリアル複製へ適用する。
        /// アセット上の PC マテリアルは変更しない。
        /// </summary>
        static int ApplyQuestMaterialPolicy(
            Scene scene, StringBuilder log, out int monoShDisabled, out int vegetationWindDisabled)
        {
            var replacements = new Dictionary<Material, Material>();
            var monoSources = new HashSet<Material>();
            var windSources = new HashSet<Material>();

            foreach (var renderer in CollectInScene<Renderer>(scene))
            {
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material replacement = GetQuestPolicyMaterial(
                        materials[i], replacements, monoSources, windSources);
                    if (replacement == materials[i]) continue;
                    materials[i] = replacement;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }

            foreach (var terrain in CollectInScene<Terrain>(scene))
            {
                if (terrain == null || terrain.materialTemplate == null) continue;
                Material replacement = GetQuestPolicyMaterial(
                    terrain.materialTemplate, replacements, monoSources, windSources);
                if (replacement != terrain.materialTemplate)
                    terrain.materialTemplate = replacement;
            }

            // Udon や補助コンポーネントが保持し、実行時に Renderer へ渡す Material も対象にする。
            foreach (var component in CollectInScene<Component>(scene))
            {
                if (component == null || component is Renderer || component is Terrain) continue;
                try
                {
                    var so = new SerializedObject(component);
                    var prop = so.GetIterator();
                    bool changed = false;
                    while (prop.Next(true))
                    {
                        if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var material = prop.objectReferenceValue as Material;
                        if (material == null) continue;
                        Material replacement = GetQuestPolicyMaterial(
                            material, replacements, monoSources, windSources);
                        if (replacement == material) continue;
                        prop.objectReferenceValue = replacement;
                        changed = true;
                    }
                    if (changed) so.ApplyModifiedPropertiesWithoutUndo();
                }
                catch (Exception e)
                {
                    log.AppendLine(
                        $"  [WARN] Questマテリアル方針の参照走査をスキップ: " +
                        $"{GetHierarchyPath(component.transform)} / {component.GetType().Name}: {e.Message}");
                }
            }

            monoShDisabled = monoSources.Count;
            vegetationWindDisabled = windSources.Count;
            log.AppendLine(
                $"  Questマテリアル方針: 複製 {replacements.Count} / " +
                $"MonoSH無効 {monoShDisabled} / 植生風無効 {vegetationWindDisabled}");
            return replacements.Count;
        }

        static Material GetQuestPolicyMaterial(
            Material source,
            Dictionary<Material, Material> replacements,
            HashSet<Material> monoSources,
            HashSet<Material> windSources)
        {
            if (source == null) return null;
            if (replacements.TryGetValue(source, out Material cached)) return cached;

            bool disableMonoSh = UsesMonoSh(source);
            bool disableWind = UsesVegetationWind(source);
            if (!disableMonoSh && !disableWind) return source;

            var material = new Material(source)
            {
                name = source.name + " [Quest Policy]"
            };

            if (disableMonoSh)
            {
                SetFloatIfPresent(material, "_UseMonoSH", 0f);
                SetFloatIfPresent(material, "_UseMonoSHSpec", 0f);
                if (material.HasProperty("_BakeryMode") && material.GetFloat("_BakeryMode") == 3f)
                    material.SetFloat("_BakeryMode", 0f);
                material.DisableKeyword("_MONOSH_ON");
                material.DisableKeyword("_MONOSHSPEC_ON");
                material.DisableKeyword("_BAKERY_MONOSH");
                monoSources.Add(source);
            }

            if (disableWind)
            {
                SetFloatIfPresent(material, "_WindOff", 1f);
                SetFloatIfPresent(material, "_WindMultiplier", 0f);
                SetFloatIfPresent(material, "_MicroWindMultiplier", 0f);
                SetFloatIfPresent(material, "_LocalWindPower", 0f);
                SetFloatIfPresent(material, "_LocalMicroPower", 0f);
                SetFloatIfPresent(material, "_WindToggle", 0f);
                SetFloatIfPresent(material, "_VertexWindEnabled", 0f);
                material.EnableKeyword("_WIND_OFF");
                material.DisableKeyword("_WIND_ON");
                material.DisableKeyword("_VERTEXWIND_ON");
                windSources.Add(source);
            }

            replacements[source] = material;
            return material;
        }

        static bool UsesMonoSh(Material material)
        {
            return IsEnabled(material, "_UseMonoSH", "_MONOSH_ON")
                || IsEnabled(material, "_UseMonoSHSpec", "_MONOSHSPEC_ON")
                || (material.HasProperty("_BakeryMode") && material.GetFloat("_BakeryMode") == 3f)
                || material.IsKeywordEnabled("_BAKERY_MONOSH");
        }

        static bool UsesVegetationWind(Material material)
        {
            if (material.HasProperty("_WindOff") && material.GetFloat("_WindOff") < 0.5f)
                return true;
            if (material.HasProperty("_WindToggle") && material.GetFloat("_WindToggle") > 0.5f)
                return true;
            if (material.HasProperty("_VertexWindEnabled") && material.GetFloat("_VertexWindEnabled") > 0.5f)
                return true;

            string shaderName = material.shader != null ? material.shader.name : string.Empty;
            bool knownWindShader = shaderName == "BK/Grass"
                || shaderName == "BK/Impostor"
                || shaderName.StartsWith("BK/Vegetation", StringComparison.Ordinal)
                || shaderName == "you5248/SoyogiCleanroom";
            return knownWindShader
                && (!material.HasProperty("_WindMultiplier")
                    || Math.Abs(material.GetFloat("_WindMultiplier")) > 0.0001f);
        }

        static bool IsEnabled(Material material, string property, string keyword)
        {
            return (material.HasProperty(property) && material.GetFloat(property) > 0.5f)
                || material.IsKeywordEnabled(keyword);
        }

        static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        /// <summary>
        /// Quest では Directional Lightmap を使わない。LightmapSettings と Bakery の
        /// ftLightmapsStorage が保持する _L1.tga の双方を外し、バンドル混入を防ぐ。
        /// </summary>
        static int ForceNonDirectionalLightmaps(Scene scene, StringBuilder log)
        {
            int removed = 0;
            var source = LightmapSettings.lightmaps;
            if (source != null && source.Length > 0)
            {
                var nonDirectional = new LightmapData[source.Length];
                for (int i = 0; i < source.Length; i++)
                {
                    var src = source[i];
                    var dst = new LightmapData();
                    if (src != null)
                    {
                        dst.lightmapColor = src.lightmapColor;
                        dst.shadowMask = src.shadowMask;
                        if (src.lightmapDir != null) removed++;
                    }
                    nonDirectional[i] = dst;
                }
                LightmapSettings.lightmaps = nonDirectional;
            }
            LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;

            int bakeryReferences = 0;
            foreach (var component in CollectInScene<MonoBehaviour>(scene))
            {
                if (component == null) continue;
                try
                {
                    bool isBakeryStorage = component.GetType().Name == "ftLightmapsStorage";
                    var so = new SerializedObject(component);
                    var prop = so.GetIterator();
                    bool changed = false;
                    while (prop.Next(true))
                    {
                        if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                        if (!(prop.objectReferenceValue is Texture2D texture)) continue;
                        string assetPath = AssetDatabase.GetAssetPath(texture).Replace('\\', '/');
                        if (!isBakeryStorage
                            && !assetPath.StartsWith(
                                "Assets/BakeryLightmaps/", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!Path.GetFileNameWithoutExtension(assetPath)
                                .EndsWith("_L1", StringComparison.OrdinalIgnoreCase))
                            continue;

                        prop.objectReferenceValue = null;
                        bakeryReferences++;
                        changed = true;
                    }
                    if (changed) so.ApplyModifiedPropertiesWithoutUndo();
                }
                catch (Exception e)
                {
                    log.AppendLine(
                        $"  [WARN] Bakery Directional 参照走査をスキップ: " +
                        $"{GetHierarchyPath(component.transform)} / {component.GetType().Name}: {e.Message}");
                }
            }

            removed += bakeryReferences;
            log.AppendLine(
                $"  Non-Directional Lightmap: LightmapData {removed - bakeryReferences} / " +
                $"Bakery参照 {bakeryReferences} を除去");
            return removed;
        }

        static int OptimizeLights(
            Scene scene, bool destroy, bool removeRealtimeLocalLights,
            bool disableRealtimeShadows, StringBuilder log)
        {
            int removed = 0;
            int shadowsDisabled = 0;
            int keptForUdon = 0;
            // Udon が参照するライトはギミックの一部（蛍光灯の点滅演出など）なので除去しない。
            var udonReferenced = CollectUdonReferencedLights(scene);
            foreach (var light in CollectInScene<Light>(scene))
            {
                if (light == null) continue;

                bool isRealtimeLocal = light.type != LightType.Directional
                    && light.lightmapBakeType != LightmapBakeType.Baked;
                if (removeRealtimeLocalLights && isRealtimeLocal && udonReferenced.Contains(light))
                {
                    keptForUdon++;
                    log.AppendLine($"  [KEEP] Udon参照ライトのため保持 {GetHierarchyPath(light.transform)}");
                }
                else if (removeRealtimeLocalLights && isRealtimeLocal)
                {
                    string path = GetHierarchyPath(light.transform);
                    if (destroy)
                    {
                        Object.DestroyImmediate(light);
                        log.AppendLine($"  [DESTROY COMPONENT] Realtime Local Light {path}");
                    }
                    else
                    {
                        light.enabled = false;
                        log.AppendLine($"  [LIGHT OFF] Realtime Local Light {path}");
                    }
                    removed++;
                    continue;
                }

                if (!disableRealtimeShadows || light.shadows == LightShadows.None) continue;
                light.shadows = LightShadows.None;
                shadowsDisabled++;
            }

            log.AppendLine(
                $"  Quest Light: Realtime Local除去 {removed} / Shadow無効化 {shadowsDisabled}" +
                $" / Udon参照のため保持 {keptForUdon}");
            return removed;
        }

        /// <summary>UdonBehaviour がシリアライズ保持している Light を集める。</summary>
        static HashSet<Light> CollectUdonReferencedLights(Scene scene)
        {
            var result = new HashSet<Light>();
            foreach (var udon in CollectInScene<VRC.Udon.UdonBehaviour>(scene))
            {
                if (udon == null) continue;
                var so = new SerializedObject(udon);
                var prop = so.GetIterator();
                while (prop.Next(true))
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var light = prop.objectReferenceValue as Light;
                    if (light != null) result.Add(light);
                }
            }
            return result;
        }

        static int RemovePostProcessVolumes(Scene scene, bool destroy, StringBuilder log)
        {
            var volumes = CollectInScene<PostProcessVolume>(scene);
            int count = 0;
            foreach (var v in volumes)
            {
                if (v == null) continue;
                var go = v.gameObject;
                string path = GetHierarchyPath(go.transform);

                if (destroy)
                {
                    // Keep the GameObject and its non-PPS components. In particular,
                    // the underwater volume can share this object with the collider
                    // used by SeasonFXBackend for head-position water detection.
                    Object.DestroyImmediate(v);
                    count++;
                    log.AppendLine($"  [DESTROY COMPONENT] PPS {path}");
                }
                else
                {
                    if (!v.enabled)
                    {
                        log.AppendLine($"  [SKIP] PPS 既に無効: {path}");
                        continue;
                    }
                    v.enabled = false;
                    count++;
                    log.AppendLine($"  [PPS COMPONENT OFF] {path}");
                }
            }
            if (count == 0)
                log.AppendLine("  PPS Volume 除去: 対象なし（または全て無効済み）");
            return count;
        }

        static List<T> CollectInScene<T>(Scene scene) where T : Component
        {
            var list = new List<T>();
            if (!scene.IsValid()) return list;
            foreach (var root in scene.GetRootGameObjects())
                list.AddRange(root.GetComponentsInChildren<T>(true));
            return list;
        }

        /// <summary>ルート名からの階層パスで GameObject を探す（非アクティブ含む）。</summary>
        public static GameObject FindByHierarchyPath(Scene scene, string path)
        {
            if (!scene.IsValid() || string.IsNullOrEmpty(path)) return null;
            var parts = path.Split('/');
            if (parts.Length == 0) return null;

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != parts[0]) continue;
                if (parts.Length == 1) return root;

                var found = FindChildByParts(root.transform, parts, 1);
                if (found != null) return found.gameObject;
            }

            // フォールバック: 先行ビルドプロセッサが階層を組み替えている場合に備え、
            // 非アクティブを含む全 Transform の実パスと突き合わせる。
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (GetHierarchyPath(t) == path) return t.gameObject;
                }
            }
            return null;
        }

        static Transform FindChildByParts(Transform parent, string[] parts, int index)
        {
            if (index >= parts.Length) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name != parts[index]) continue;
                var deeper = FindChildByParts(child, parts, index + 1);
                if (deeper != null) return deeper;
            }
            return null;
        }

        public static string GetHierarchyPath(Transform t)
        {
            if (t == null) return string.Empty;
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
