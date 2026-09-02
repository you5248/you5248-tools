using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace you5248.QuestPort
{
    /// <summary>
    /// メインシーン依存テクスチャへプラットフォーム Override を一括設定する。
    /// SaveAndReimport は使わず WriteImportSettingsIfDirty + Refresh で現プラットフォームの大量再インポートを避ける。
    /// Unity のテクスチャプラットフォーム名: Android = "Android"、iOS = "iPhone"（仕様上の名称）。
    /// </summary>
    public static class QuestTextureOverrideTool
    {
        const string ScenePath = "Assets/Forest test/washitu.unity";
        /// <summary>Android 向け。既存 API の既定値。</summary>
        const string PlatformName = "Android";
        /// <summary>iOS 向け。Unity の TextureImporter プラットフォーム文字列は "iPhone"。</summary>
        const string IosPlatformName = "iPhone";
        const string IosAutomationRequestRelativePath = "Library/IosTextureOverrides.request";
        const string IosAutomationResultRelativePath = "Library/IosTextureOverrides.result";
        const int DefaultMaxSize = 1024;
        const int NormalMaxSize = 512;
        const int CompressionQuality = 50;

        [InitializeOnLoadMethod]
        static void ApplyRequestedIosOverridesAfterReload()
        {
            if (!File.Exists(ProjectPath(IosAutomationRequestRelativePath))) return;
            EditorApplication.delayCall += RunRequestedIosOverrides;
        }

        static void RunRequestedIosOverrides()
        {
            string requestPath = ProjectPath(IosAutomationRequestRelativePath);
            if (!File.Exists(requestPath)) return;

            string resultPath = ProjectPath(IosAutomationResultRelativePath);
            try
            {
                // 大量の Importer 設定反映中に AssetDatabase.Refresh がドメインリロードを起こしても
                // 同じ処理を再実行しないよう、開始前にリクエストを消費する。
                File.Delete(requestPath);
                File.WriteAllText(resultPath, "STARTING");
                int applied = ApplyIosOverridesMirroringAndroid(dryRun: false);
                File.WriteAllText(resultPath, $"SUCCESS applied={applied}");
                Debug.Log($"[QuestPort] iOS Texture Override automation SUCCESS: applied={applied}");
            }
            catch (System.Exception exception)
            {
                File.WriteAllText(resultPath, "FAILED\n" + exception);
                Debug.LogError("[QuestPort] iOS Texture Override automation FAILED: " + exception);
            }
        }

        [MenuItem("you5248/Quest Port/Set Android Texture Overrides")]
        static void MenuApply()
        {
            ApplyAndroidOverrides(dryRun: false);
        }

        [MenuItem("you5248/Quest Port/Set Android Texture Overrides (512 / Normal 256)")]
        static void MenuApplyCompact()
        {
            ApplyAndroidOverrides(dryRun: false, maxSize: 512, normalMaxSize: 256);
        }

        [MenuItem("you5248/Quest Port/Set iOS Texture Overrides (mirror Android)")]
        static void MenuApplyIosMirror()
        {
            ApplyIosOverridesMirroringAndroid(dryRun: false);
        }

        [MenuItem("you5248/Quest Port/Shrink Non-Color Maps (256)")]
        static void MenuShrinkMaps()
        {
            ApplyAndroidMapOverrides(dryRun: false, maxSize: 256);
        }

        [MenuItem("you5248/Quest Port/Use ASTC 8x8 for Color Textures")]
        static void MenuAstc8()
        {
            ApplyAndroidColorFormat(dryRun: false, TextureImporterFormat.ASTC_8x8);
        }

        /// <summary>
        /// Android 側で実機検証済みの Override を iPhone へ複製する。
        /// 解像度・圧縮品質は個別設定をそのまま引き継ぐ。Android 固有形式は Automatic へ戻し、
        /// ASTC だけを共有する。ライトマップは事故防止のため常に format=Automatic とする。
        /// </summary>
        public static int ApplyIosOverridesMirroringAndroid(bool dryRun)
        {
            if (AssetDatabase.LoadMainAssetAtPath(ScenePath) == null)
            {
                Debug.LogError($"[QuestPort] シーンが見つかりません: {ScenePath}");
                return 0;
            }

            string[] deps = AssetDatabase.GetDependencies(ScenePath, true);
            var seen = new HashSet<string>();
            int applied = 0;
            int inherited = 0;
            int cleared = 0;

            foreach (string path in deps)
            {
                if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                var android = importer.GetPlatformTextureSettings(PlatformName);
                var ios = importer.GetPlatformTextureSettings(IosPlatformName);

                if (android == null || !android.overridden)
                {
                    if (ios == null || !ios.overridden) continue;
                    if (!dryRun)
                    {
                        importer.ClearPlatformTextureSettings(IosPlatformName);
                        AssetDatabase.WriteImportSettingsIfDirty(path);
                    }
                    applied++;
                    cleared++;
                    continue;
                }

                var desired = CloneForIos(android, IsLightmapAsset(path, importer));
                if (IsSamePlatformOverride(ios, desired)) continue;

                if (!dryRun)
                {
                    importer.SetPlatformTextureSettings(desired);
                    AssetDatabase.WriteImportSettingsIfDirty(path);
                }
                applied++;
                inherited++;
            }

            if (!dryRun && applied > 0) AssetDatabase.Refresh();
            Debug.Log(
                $"[QuestPort] iOS Texture Overrides (mirror Android) 完了 " +
                $"({(dryRun ? "DryRun" : "Apply")}): applied={applied}, " +
                $"inherited={inherited}, cleared={cleared}, platform={IosPlatformName}");
            return applied;
        }

        static TextureImporterPlatformSettings CloneForIos(
            TextureImporterPlatformSettings android, bool isLightmap)
        {
            var ios = new TextureImporterPlatformSettings
            {
                name = IosPlatformName,
                overridden = true,
                maxTextureSize = android.maxTextureSize,
                resizeAlgorithm = android.resizeAlgorithm,
                format = IosCompatibleFormat(android.format, isLightmap),
                textureCompression = android.textureCompression,
                compressionQuality = android.compressionQuality,
                crunchedCompression = android.crunchedCompression,
                allowsAlphaSplitting = android.allowsAlphaSplitting
            };
            return ios;
        }

        static TextureImporterFormat IosCompatibleFormat(
            TextureImporterFormat androidFormat, bool isLightmap)
        {
            if (isLightmap) return TextureImporterFormat.Automatic;

            int value = (int)androidFormat;
            bool isAstcLdr = value >= 48 && value <= 53;
            bool isAstcHdr = value >= 66 && value <= 71;
            return isAstcLdr || isAstcHdr
                ? androidFormat
                : TextureImporterFormat.Automatic;
        }

        static bool IsSamePlatformOverride(
            TextureImporterPlatformSettings current,
            TextureImporterPlatformSettings desired)
        {
            return current != null
                && current.overridden == desired.overridden
                && current.maxTextureSize == desired.maxTextureSize
                && current.resizeAlgorithm == desired.resizeAlgorithm
                && current.format == desired.format
                && current.textureCompression == desired.textureCompression
                && current.compressionQuality == desired.compressionQuality
                && current.crunchedCompression == desired.crunchedCompression
                && current.allowsAlphaSplitting == desired.allowsAlphaSplitting;
        }

        /// <summary>
        /// カラー系テクスチャの Android フォーマットを明示指定する。
        /// 解像度を保ったままビット数を下げるため、256 へ落とすより精細さが残る。
        /// 法線マップは劣化が目立つので対象外。
        /// </summary>
        public static int ApplyAndroidColorFormat(bool dryRun, TextureImporterFormat format)
        {
            return ApplyAndroidColorFormat(dryRun, format, PlatformName);
        }

        /// <summary>
        /// カラー系テクスチャのフォーマットを指定プラットフォームへ明示指定する。
        /// platformName: "Android" または "iPhone"（Unity 仕様。iOS は iPhone）。
        /// </summary>
        public static int ApplyAndroidColorFormat(
            bool dryRun, TextureImporterFormat format, string platformName)
        {
            string[] deps = AssetDatabase.GetDependencies(ScenePath, true);
            var seen = new HashSet<string>();
            int applied = 0;

            foreach (string path in deps)
            {
                if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                if (importer.textureType == TextureImporterType.NormalMap) continue;
                if (IsLightmapAsset(path, importer)) continue;

                var platform = importer.GetPlatformTextureSettings(platformName);
                if (platform.overridden && platform.format == format) continue;

                if (!dryRun)
                {
                    platform.name = platformName;
                    platform.overridden = true;
                    if (platform.maxTextureSize <= 0) platform.maxTextureSize = importer.maxTextureSize;
                    platform.format = format;
                    platform.compressionQuality = CompressionQuality;
                    platform.textureCompression = TextureImporterCompression.Compressed;
                    importer.SetPlatformTextureSettings(platform);
                    AssetDatabase.WriteImportSettingsIfDirty(path);
                }
                applied++;
            }

            if (!dryRun) AssetDatabase.Refresh();
            Debug.Log(
                $"[QuestPort] カラー系を {format} に設定 ({(dryRun ? "DryRun" : "Apply")}): " +
                $"{applied} 件 platform={platformName}");
            return applied;
        }

        // メタリック/AO/ラフネス/エミッション等の低周波マップ。色ではないため
        // 解像度を落としても見た目への影響が小さい。
        static readonly System.Text.RegularExpressions.Regex NonColorMapPattern =
            new System.Text.RegularExpressions.Regex(
                "(metal|mask|_ao|occl|rough|smooth|emis|spec|height|_mt|_ms)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// ライトマップ（HDR輝度・MonoSHのL1方向性データ）は圧縮形式を固定してはならない。
        /// LDRのASTCへ落とすと輝度レンジと方向性が壊れ、明るさと影が破綻する。
        /// </summary>
        public static bool IsLightmapAsset(string path, TextureImporter importer)
        {
            if (importer != null && importer.textureType == TextureImporterType.Lightmap) return true;
            var p = path.Replace('\\', '/');
            if (p.StartsWith("Assets/BakeryLightmaps/", System.StringComparison.OrdinalIgnoreCase)) return true;
            return p.EndsWith(".hdr", System.StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".exr", System.StringComparison.OrdinalIgnoreCase);
        }

        [MenuItem("you5248/Quest Port/Shrink Lightmaps (size only, 1024)")]
        static void MenuShrinkLightmaps()
        {
            ApplyAndroidLightmapSizeOverride(dryRun: false, maxSize: 1024);
        }

        /// <summary>
        /// ライトマップの Android 上限解像度だけを下げる。
        /// format は Automatic のままにして HDR/L1 のエンコードを壊さないこと。
        /// ここで圧縮形式を固定すると明るさと方向性が破綻する（過去に事故あり）。
        /// </summary>
        public static int ApplyAndroidLightmapSizeOverride(bool dryRun, int maxSize)
        {
            return ApplyAndroidLightmapSizeOverride(dryRun, maxSize, PlatformName);
        }

        /// <summary>
        /// ライトマップの上限解像度だけを指定プラットフォームへ下げる。
        /// format は Automatic 維持（形式固定は明るさ破綻の事故歴あり）。
        /// platformName: "Android" または "iPhone"（Unity 仕様。iOS は iPhone）。
        /// </summary>
        public static int ApplyAndroidLightmapSizeOverride(
            bool dryRun, int maxSize, string platformName)
        {
            string[] deps = AssetDatabase.GetDependencies(ScenePath, true);
            var seen = new HashSet<string>();
            int applied = 0;

            foreach (string path in deps)
            {
                if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                if (!IsLightmapAsset(path, importer)) continue;

                int targetMax = Mathf.Min(maxSize, importer.maxTextureSize);
                var platform = importer.GetPlatformTextureSettings(platformName);
                if (platform.overridden && platform.maxTextureSize == targetMax
                    && platform.format == TextureImporterFormat.Automatic) continue;

                if (!dryRun)
                {
                    platform.name = platformName;
                    platform.overridden = true;
                    platform.maxTextureSize = targetMax;
                    platform.format = TextureImporterFormat.Automatic; // 固定しない
                    platform.compressionQuality = CompressionQuality;
                    platform.textureCompression = TextureImporterCompression.Compressed;
                    importer.SetPlatformTextureSettings(platform);
                    AssetDatabase.WriteImportSettingsIfDirty(path);
                }
                applied++;
            }

            if (!dryRun) AssetDatabase.Refresh();
            Debug.Log(
                $"[QuestPort] ライトマップ上限 {maxSize} 化 ({(dryRun ? "DryRun" : "Apply")}): " +
                $"{applied} 件（formatは不変） platform={platformName}");
            return applied;
        }

        /// <summary>
        /// 名前から非カラーマップと判断できるテクスチャだけを、より小さい上限へ落とす。
        /// Albedo（色）には触れないため、見た目の劣化を抑えつつ容量を削れる。
        /// </summary>
        public static int ApplyAndroidMapOverrides(bool dryRun, int maxSize)
        {
            return ApplyAndroidMapOverrides(dryRun, maxSize, PlatformName);
        }

        /// <summary>
        /// 非カラーマップを指定プラットフォームの上限へ落とす。
        /// platformName: "Android" または "iPhone"（Unity 仕様。iOS は iPhone）。
        /// </summary>
        public static int ApplyAndroidMapOverrides(bool dryRun, int maxSize, string platformName)
        {
            string[] deps = AssetDatabase.GetDependencies(ScenePath, true);
            var seen = new HashSet<string>();
            int applied = 0;

            foreach (string path in deps)
            {
                if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                if (importer.textureType == TextureImporterType.NormalMap) continue;
                if (IsLightmapAsset(path, importer)) continue;
                if (!NonColorMapPattern.IsMatch(System.IO.Path.GetFileNameWithoutExtension(path))) continue;

                int targetMax = Mathf.Min(maxSize, importer.maxTextureSize);
                var platform = importer.GetPlatformTextureSettings(platformName);
                if (IsSameOverride(platform, targetMax)) continue;

                if (!dryRun)
                {
                    platform.name = platformName;
                    platform.overridden = true;
                    platform.maxTextureSize = targetMax;
                    platform.format = TextureImporterFormat.Automatic;
                    platform.compressionQuality = CompressionQuality;
                    platform.textureCompression = TextureImporterCompression.Compressed;
                    importer.SetPlatformTextureSettings(platform);
                    AssetDatabase.WriteImportSettingsIfDirty(path);
                }
                applied++;
            }

            if (!dryRun) AssetDatabase.Refresh();
            Debug.Log(
                $"[QuestPort] 非カラーマップ {maxSize} 化 ({(dryRun ? "DryRun" : "Apply")}): " +
                $"{applied} 件 platform={platformName}");
            return applied;
        }

        /// <summary>
        /// シーン依存テクスチャに Android Override を適用する。
        /// </summary>
        /// <param name="dryRun">true のとき設定変更せずログのみ</param>
        /// <returns>適用（または DryRun で適用予定）した件数</returns>
        public static int ApplyAndroidOverrides(bool dryRun)
        {
            return ApplyAndroidOverrides(dryRun, DefaultMaxSize, NormalMaxSize);
        }

        /// <summary>
        /// 上限解像度を指定して Android Override を適用する。
        /// 既定より小さい値を渡すと、既に Override 済みのテクスチャもさらに縮小される。
        /// </summary>
        public static int ApplyAndroidOverrides(bool dryRun, int maxSize, int normalMaxSize)
        {
            return ApplyAndroidOverrides(dryRun, maxSize, normalMaxSize, PlatformName);
        }

        /// <summary>
        /// 上限解像度を指定して指定プラットフォームへ Override を適用する。
        /// platformName: "Android" または "iPhone"（Unity 仕様。iOS は iPhone）。
        /// </summary>
        public static int ApplyAndroidOverrides(
            bool dryRun, int maxSize, int normalMaxSize, string platformName)
        {
            if (AssetDatabase.LoadMainAssetAtPath(ScenePath) == null)
            {
                Debug.LogError($"[QuestPort] シーンが見つかりません: {ScenePath}");
                return 0;
            }

            string[] deps = AssetDatabase.GetDependencies(ScenePath, true);
            var seen = new HashSet<string>();
            int applied = 0;
            int skipped = 0;
            int textureCount = 0;

            string mode = dryRun ? "DryRun" : "Apply";
            Debug.Log(
                $"[QuestPort] Texture Overrides 開始 ({mode}): scene={ScenePath} platform={platformName}");

            foreach (string path in deps)
            {
                if (string.IsNullOrEmpty(path) || !seen.Add(path))
                    continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;

                textureCount++;

                int cap = importer.textureType == TextureImporterType.NormalMap
                    ? normalMaxSize
                    : maxSize;
                int currentMax = importer.maxTextureSize;
                int targetMax = Mathf.Min(cap, currentMax);

                var platform = importer.GetPlatformTextureSettings(platformName);
                if (IsSameOverride(platform, targetMax))
                {
                    skipped++;
                    continue;
                }

                if (dryRun)
                {
                    Debug.Log(
                        $"[QuestPort] [DryRun] {path}: max={targetMax} " +
                        $"(type={importer.textureType}, currentMax={currentMax}, platform={platformName})");
                    applied++;
                    continue;
                }

                platform.name = platformName;
                platform.overridden = true;
                platform.maxTextureSize = targetMax;
                platform.format = TextureImporterFormat.Automatic;
                platform.compressionQuality = CompressionQuality;
                platform.textureCompression = TextureImporterCompression.Compressed;

                importer.SetPlatformTextureSettings(platform);
                AssetDatabase.WriteImportSettingsIfDirty(path);
                applied++;
            }

            if (!dryRun && applied > 0)
                AssetDatabase.Refresh();

            Debug.Log(
                $"[QuestPort] Texture Overrides 完了 ({mode}): " +
                $"textures={textureCount}, applied={applied}, skipped={skipped}, platform={platformName}");

            return applied;
        }

        static bool IsSameOverride(TextureImporterPlatformSettings settings, int targetMax)
        {
            if (settings == null || !settings.overridden)
                return false;

            return settings.maxTextureSize == targetMax
                && settings.format == TextureImporterFormat.Automatic
                && settings.compressionQuality == CompressionQuality
                && settings.textureCompression == TextureImporterCompression.Compressed;
        }

        static string ProjectPath(string relativePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
