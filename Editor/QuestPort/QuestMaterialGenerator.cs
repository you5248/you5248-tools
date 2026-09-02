using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace you5248.QuestPort
{
    /// <summary>
    /// メインシーン依存マテリアルから Quest 版マテリアルを生成し、
    /// QuestPortConfig.materialSwaps に登録する。
    /// </summary>
    public static class QuestMaterialGenerator
    {
        const string ScenePath = "Assets/Forest test/washitu.unity";
        const string OutputFolder = "Assets/you5248/QuestPort/Materials";

        static readonly string[] MeshArrayTextureProps =
        {
            "_MainTexArray",
            "_BumpMapArray",
            "_MetallicGlossMapArray",
            "_EmissionMapArray",
        };

        [MenuItem("you5248/Quest Port/Generate Quest Materials")]
        static void MenuGenerate()
        {
            GenerateAll(dryRun: false);
        }

        /// <summary>
        /// シーン依存マテリアルの Quest 版を生成し materialSwaps に登録する。
        /// </summary>
        /// <param name="dryRun">true のときアセット・設定を変更せずログのみ</param>
        /// <returns>生成（または DryRun で生成予定）した件数</returns>
        public static int GenerateAll(bool dryRun)
        {
            if (AssetDatabase.LoadMainAssetAtPath(ScenePath) == null)
            {
                Debug.LogError($"[QuestPort] シーンが見つかりません: {ScenePath}");
                return 0;
            }

            var config = LoadConfig();
            if (config == null)
            {
                Debug.LogError(
                    $"[QuestPort] 設定アセットが見つかりません（{QuestPortConfig.DefaultAssetPath}）。");
                return 0;
            }

            string mode = dryRun ? "DryRun" : "Apply";
            Debug.Log($"[QuestPort] Generate Quest Materials 開始 ({mode}): scene={ScenePath}");

            string[] deps = AssetDatabase.GetDependencies(ScenePath, true);
            var seenMats = new HashSet<Material>();
            var registeredSources = new HashSet<Material>();
            if (config.materialSwaps != null)
            {
                foreach (var s in config.materialSwaps)
                {
                    if (s != null && s.sourceMat != null)
                        registeredSources.Add(s.sourceMat);
                }
            }

            int generated = 0;
            int noRule = 0;
            int errors = 0;
            int alreadyRegistered = 0;
            int materialCount = 0;

            var pendingSwaps = new List<QuestPortConfig.MaterialSwap>();
            if (config.materialSwaps != null)
                pendingSwaps.AddRange(config.materialSwaps);

            if (!dryRun)
                EnsureFolder(OutputFolder);

            foreach (string path in deps)
            {
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".mat"))
                    continue;

                var source = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (source == null || !seenMats.Add(source))
                    continue;

                materialCount++;

                if (source.shader == null)
                {
                    errors++;
                    Debug.Log($"[QuestPort] [SKIP] シェーダーなし: {path}");
                    continue;
                }

                string srcShaderName = source.shader.name;
                if (!TryResolveQuestShader(srcShaderName, out string questShaderName, out string ruleTag))
                {
                    noRule++;
                    continue;
                }

                var questShader = Shader.Find(questShaderName);
                if (questShader == null)
                {
                    errors++;
                    Debug.LogWarning(
                        $"[QuestPort] [SKIP] Quest シェーダー未検出 ({ruleTag}): " +
                        $"'{questShaderName}' ← {srcShaderName} ({path})");
                    continue;
                }

                string outPath = $"{OutputFolder}/{SanitizeFileName(source.name)}_Quest.mat";

                if (dryRun)
                {
                    string reg = registeredSources.Contains(source) ? "登録済" : "新規登録";
                    Debug.Log(
                        $"[QuestPort] [DryRun] {source.name}: {srcShaderName} → {questShaderName} " +
                        $"({ruleTag}), out={outPath}, swap={reg}");
                    generated++;
                    continue;
                }

                Material questMat = BuildOrUpdateQuestMaterial(source, questShader, outPath, ruleTag);
                if (questMat == null)
                {
                    errors++;
                    Debug.LogWarning($"[QuestPort] [SKIP] 生成失敗: {path}");
                    continue;
                }

                if (registeredSources.Contains(source))
                {
                    alreadyRegistered++;
                    UpdateExistingSwap(pendingSwaps, source, questMat);
                    Debug.Log(
                        $"[QuestPort] [UPDATE] {source.name}: {srcShaderName} → {questShaderName} " +
                        $"({ruleTag}) ※materialSwaps 重複登録スキップ");
                }
                else
                {
                    pendingSwaps.Add(new QuestPortConfig.MaterialSwap
                    {
                        sourceMat = source,
                        questMat = questMat,
                    });
                    registeredSources.Add(source);
                    Debug.Log(
                        $"[QuestPort] [GEN] {source.name}: {srcShaderName} → {questShaderName} " +
                        $"({ruleTag}) → {outPath}");
                }

                generated++;
            }

            if (!dryRun && generated > 0)
            {
                config.materialSwaps = pendingSwaps.ToArray();
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log(
                $"[QuestPort] Generate Quest Materials 完了 ({mode}): " +
                $"materials={materialCount}, generated={generated}, " +
                $"noRule={noRule}, errors={errors}, alreadyRegistered={alreadyRegistered}");

            return generated;
        }

        static Material BuildOrUpdateQuestMaterial(
            Material source, Shader questShader, string outPath, string ruleTag)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(outPath);
            Material questMat;
            if (existing != null)
            {
                questMat = existing;
                questMat.shader = questShader;
            }
            else
            {
                questMat = new Material(questShader);
                AssetDatabase.CreateAsset(questMat, outPath);
            }

            questMat.CopyPropertiesFromMaterial(source);
            ApplyShaderSpecificFixes(questMat, source, ruleTag);
            EditorUtility.SetDirty(questMat);
            return questMat;
        }

        static void ApplyShaderSpecificFixes(Material questMat, Material source, string ruleTag)
        {
            if (ruleTag == "MeshArrayPBR" || ruleTag == "MeshArrayPBRLeaf" || ruleTag == "MeshArrayPBRSnow")
            {
                SwapMeshArrayTexturesToQuest(questMat, source);
                return;
            }

            if (ruleTag == "Glass" || ruleTag == "GlassLeaf" || ruleTag == "GlassSnow")
            {
                if (questMat.HasProperty("_QuestOpacity"))
                    questMat.SetFloat("_QuestOpacity", 0.16f);
                if (questMat.HasProperty("_QuestFresnelStrength"))
                    questMat.SetFloat("_QuestFresnelStrength", 0.55f);
                return;
            }

            if (ruleTag == "Water")
            {
                // プロパティ名が異なる分だけ明示マップ。CopyProperties で移った同名は維持。
                if (source.HasProperty("_NormalMap0") && questMat.HasProperty("_BumpMap"))
                {
                    var n0 = source.GetTexture("_NormalMap0");
                    if (n0 != null) questMat.SetTexture("_BumpMap", n0);
                }
                if (source.HasProperty("_NormalMap1") && questMat.HasProperty("_BumpMap2"))
                {
                    var n1 = source.GetTexture("_NormalMap1");
                    if (n1 != null) questMat.SetTexture("_BumpMap2", n1);
                }
                if (source.HasProperty("_Color") && questMat.HasProperty("_Color"))
                    questMat.SetColor("_Color", source.GetColor("_Color"));
                if (source.HasProperty("_Opacity") && questMat.HasProperty("_Opacity"))
                {
                    // Mochie 側の値は不透明寄り（0.88等）になりがちなので、水らしい
                    // 半透明を保つため上限 0.65 でクランプする。
                    questMat.SetFloat("_Opacity",
                        Mathf.Min(source.GetFloat("_Opacity"), 0.65f));
                }
            }
        }

        static void SwapMeshArrayTexturesToQuest(Material questMat, Material source)
        {
            foreach (string prop in MeshArrayTextureProps)
            {
                if (!questMat.HasProperty(prop) || !source.HasProperty(prop))
                    continue;

                var tex = source.GetTexture(prop);
                if (tex == null)
                    continue;

                string texPath = AssetDatabase.GetAssetPath(tex);
                if (string.IsNullOrEmpty(texPath))
                {
                    Debug.LogWarning(
                        $"[QuestPort] 配列テクスチャにアセットパスなし ({prop}): {source.name}");
                    continue;
                }

                string dir = Path.GetDirectoryName(texPath)?.Replace("\\", "/");
                string baseName = Path.GetFileNameWithoutExtension(texPath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(baseName))
                    continue;

                string questPath = $"{dir}/{baseName}_Quest.asset";
                var questTex = AssetDatabase.LoadAssetAtPath<Texture>(questPath);
                if (questTex == null)
                {
                    Debug.LogWarning(
                        $"[QuestPort] _Quest 配列なし → 元のまま: {questPath} " +
                        $"(mat={source.name}, prop={prop})");
                    questMat.SetTexture(prop, tex);
                    continue;
                }

                questMat.SetTexture(prop, questTex);
            }
        }

        /// <summary>
        /// PC シェーダー名 → Quest シェーダー名。該当しなければ false。
        /// </summary>
        static bool TryResolveQuestShader(string srcShaderName, out string questShaderName, out string ruleTag)
        {
            questShaderName = null;
            ruleTag = null;
            if (string.IsNullOrEmpty(srcShaderName))
                return false;

            // MeshArrayPBR 系（完全一致）
            if (srcShaderName == "you5248/MeshArrayPBR")
            {
                questShaderName = "you5248/MeshArrayPBRQuest";
                ruleTag = "MeshArrayPBR";
                return true;
            }
            if (srcShaderName == "you5248/MeshArrayPBR LeafCover")
            {
                questShaderName = "you5248/MeshArrayPBRLeafQuest";
                ruleTag = "MeshArrayPBRLeaf";
                return true;
            }
            if (srcShaderName == "you5248/MeshArrayPBR SnowCover")
            {
                questShaderName = "you5248/MeshArrayPBRSnowQuest";
                ruleTag = "MeshArrayPBRSnow";
                return true;
            }

            // Water
            if (srcShaderName == "Mochie/Water")
            {
                questShaderName = "you5248/SimpleWaterQuest";
                ruleTag = "Water";
                return true;
            }

            // SeasonHouse Glass*NoGrab → README 対応表（長い名前から先に判定）
            if (srcShaderName == "Mochie/Glass (Two Pass) LeafCover NoGrab")
            {
                questShaderName = "you5248/Quest/Glass LeafCover NoGrab";
                ruleTag = "GlassLeaf";
                return true;
            }
            if (srcShaderName == "Mochie/Glass (Two Pass) SnowCover NoGrab")
            {
                questShaderName = "you5248/Quest/Glass SnowCover NoGrab";
                ruleTag = "GlassSnow";
                return true;
            }
            if (srcShaderName == "Mochie/Glass SnowCover NoGrab")
            {
                questShaderName = "you5248/Quest/Glass SnowCover NoGrab";
                ruleTag = "GlassSnow";
                return true;
            }

            // Mochie Glass 系（Two Pass・Leaf/Snow 非 NoGrab 含む）→ Glass NoGrab または派生
            if (srcShaderName == "Mochie/Glass (Two Pass) LeafCover")
            {
                questShaderName = "you5248/Quest/Glass LeafCover NoGrab";
                ruleTag = "GlassLeaf";
                return true;
            }
            if (srcShaderName == "Mochie/Glass (Two Pass) SnowCover" ||
                srcShaderName == "Mochie/Glass SnowCover")
            {
                questShaderName = "you5248/Quest/Glass SnowCover NoGrab";
                ruleTag = "GlassSnow";
                return true;
            }
            if (srcShaderName == "Mochie/Glass (Two Pass)" || srcShaderName == "Mochie/Glass")
            {
                questShaderName = "you5248/Quest/Glass NoGrab";
                ruleTag = "Glass";
                return true;
            }

            // dhk Standard 系
            if (srcShaderName.StartsWith("you5248/dhk Standard"))
            {
                questShaderName = "you5248/Quest/dhk Standard";
                ruleTag = "dhkStandard";
                return true;
            }

            // LeafCover
            if (srcShaderName == "you5248/LeafCover Standard")
            {
                questShaderName = "you5248/Quest/LeafCover Standard";
                ruleTag = "LeafCover";
                return true;
            }
            if (srcShaderName == "you5248/LeafCover StandardCutout")
            {
                questShaderName = "you5248/Quest/LeafCover StandardCutout";
                ruleTag = "LeafCoverCutout";
                return true;
            }

            return false;
        }

        static void UpdateExistingSwap(
            List<QuestPortConfig.MaterialSwap> swaps, Material source, Material questMat)
        {
            for (int i = 0; i < swaps.Count; i++)
            {
                var s = swaps[i];
                if (s == null || s.sourceMat != source) continue;
                s.questMat = questMat;
                return;
            }
        }

        static QuestPortConfig LoadConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<QuestPortConfig>(QuestPortConfig.DefaultAssetPath);
            if (config != null) return config;

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

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(leaf))
                AssetDatabase.CreateFolder(parent, leaf);
        }

        static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Material";
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (chars[i] == invalid[j])
                    {
                        chars[i] = '_';
                        break;
                    }
                }
            }
            return new string(chars);
        }
    }
}
