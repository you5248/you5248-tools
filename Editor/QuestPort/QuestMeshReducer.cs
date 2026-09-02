using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityMeshSimplifier;

namespace you5248.QuestPort
{
    /// <summary>
    /// Quest 用ローポリメッシュ生成。UnityMeshSimplifier（LODGenerator 同梱）で UV・法線を保持しつつ簡略化する。
    /// </summary>
    public static class QuestMeshReducer
    {
        public const string OutputFolder = "Assets/you5248/QuestPort/Meshes";
        const string BookNameFilter = "AtlasMesh 28_book";
        const float BookTargetRatio = 0.2f;

        [MenuItem("you5248/Quest Port/Generate Reduced Book Meshes")]
        static void MenuGenerateBookMeshes()
        {
            var sources = FindMeshesByNameContains(BookNameFilter);
            if (sources.Length == 0)
            {
                Debug.LogWarning($"[QuestPort] '{BookNameFilter}' を含むメッシュが見つかりません。");
                return;
            }

            int count = GenerateReducedMeshes(sources, BookTargetRatio, registerToConfig: true);
            Debug.Log($"[QuestPort] Generate Reduced Book Meshes 完了: {count} / {sources.Length} 件");
        }

        /// <summary>
        /// 各ソースメッシュを targetRatio（例 0.2＝20%）まで簡略化し、
        /// Assets/you5248/QuestPort/Meshes/&lt;元名&gt;_Quest.asset を生成する。
        /// UV・法線は保持する。registerToConfig 時は QuestPortConfig.meshSwaps へ登録する。
        /// </summary>
        /// <returns>生成に成功した件数</returns>
        public static int GenerateReducedMeshes(Mesh[] sources, float targetRatio, bool registerToConfig)
        {
            if (sources == null || sources.Length == 0)
            {
                Debug.LogWarning("[QuestPort] GenerateReducedMeshes: ソースが空です。");
                return 0;
            }

            float quality = Mathf.Clamp01(targetRatio);
            EnsureFolder(OutputFolder);

            var log = new StringBuilder();
            log.AppendLine(
                $"[QuestPort] メッシュ簡略化開始: sources={sources.Length}, targetRatio={quality:F2}");

            var generated = new List<(Mesh source, Mesh quest)>();
            int success = 0;
            int totalSrcTris = 0;
            int totalQuestTris = 0;

            try
            {
                for (int i = 0; i < sources.Length; i++)
                {
                    var source = sources[i];
                    if (source == null)
                    {
                        log.AppendLine("  [SKIP] null ソース");
                        continue;
                    }

                    EditorUtility.DisplayProgressBar(
                        "QuestPort Mesh Reducer",
                        $"Simplifying {source.name} ({i + 1}/{sources.Length})",
                        (float)i / Mathf.Max(1, sources.Length));

                    int srcTris = source.triangles.Length / 3;
                    Mesh reduced;
                    try
                    {
                        reduced = SimplifyMesh(source, quality);
                    }
                    catch (System.Exception e)
                    {
                        log.AppendLine($"  [FAIL] {source.name}: {e.Message}");
                        continue;
                    }

                    if (reduced == null)
                    {
                        log.AppendLine($"  [FAIL] {source.name}: 簡略化結果が null");
                        continue;
                    }

                    int questTris = reduced.triangles.Length / 3;
                    string assetName = SanitizeFileName(source.name) + "_Quest.asset";
                    string assetPath = $"{OutputFolder}/{assetName}";

                    // 既存があれば差し替え（参照切れを防ぐため Delete→Create）
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
                    if (existing != null)
                        AssetDatabase.DeleteAsset(assetPath);

                    reduced.name = Path.GetFileNameWithoutExtension(assetName);
                    AssetDatabase.CreateAsset(reduced, assetPath);

                    var saved = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
                    if (saved == null)
                    {
                        log.AppendLine($"  [FAIL] {source.name}: アセット保存失敗 ({assetPath})");
                        continue;
                    }

                    generated.Add((source, saved));
                    success++;
                    totalSrcTris += srcTris;
                    totalQuestTris += questTris;
                    log.AppendLine(
                        $"  [OK] {source.name}: {srcTris} → {questTris} tris ({assetPath})");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (registerToConfig && generated.Count > 0)
                RegisterMeshSwaps(generated, log);

            log.AppendLine(
                $"[QuestPort] メッシュ簡略化完了: {success} 件, " +
                $"三角 {totalSrcTris} → {totalQuestTris}");
            Debug.Log(log.ToString());
            return success;
        }

        static Mesh SimplifyMesh(Mesh source, float quality)
        {
            if (!source.isReadable)
            {
                Debug.LogWarning(
                    $"[QuestPort] Mesh '{source.name}' is not readable. " +
                    "Enable Read/Write in the mesh import settings.");
            }

            // LODGeneratorCore.SimplifyOnce と同流儀。UV seam / border を保持してアトラス UV を壊さない。
            var simplifier = new MeshSimplifier();
            var opts = SimplificationOptions.Default;
            opts.PreserveBorderEdges = true;
            opts.PreserveUVSeamEdges = true;
            opts.PreserveUVFoldoverEdges = true;
            opts.PreserveSurfaceCurvature = false;
            opts.EnableSmartLink = true;
            simplifier.SimplificationOptions = opts;
            simplifier.Initialize(source);
            simplifier.SimplifyMesh(quality);
            return simplifier.ToMesh();
        }

        static void RegisterMeshSwaps(List<(Mesh source, Mesh quest)> pairs, StringBuilder log)
        {
            var config = AssetDatabase.LoadAssetAtPath<QuestPortConfig>(QuestPortConfig.DefaultAssetPath);
            if (config == null)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:QuestPortConfig"))
                {
                    config = AssetDatabase.LoadAssetAtPath<QuestPortConfig>(
                        AssetDatabase.GUIDToAssetPath(guid));
                    if (config != null) break;
                }
            }

            if (config == null)
            {
                log.AppendLine(
                    $"  [WARN] QuestPortConfig が見つかりません（{QuestPortConfig.DefaultAssetPath}）。" +
                    "meshSwaps 登録をスキップします。");
                return;
            }

            var list = new List<QuestPortConfig.MeshSwap>();
            if (config.meshSwaps != null)
            {
                foreach (var s in config.meshSwaps)
                {
                    if (s == null || s.sourceMesh == null) continue;
                    list.Add(s);
                }
            }

            int added = 0;
            int updated = 0;
            foreach (var (source, quest) in pairs)
            {
                int idx = list.FindIndex(s => s.sourceMesh == source);
                if (idx >= 0)
                {
                    list[idx].questMesh = quest;
                    updated++;
                }
                else
                {
                    list.Add(new QuestPortConfig.MeshSwap
                    {
                        sourceMesh = source,
                        questMesh = quest
                    });
                    added++;
                }
            }

            config.meshSwaps = list.ToArray();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            log.AppendLine(
                $"  meshSwaps 登録: 追加={added}, 更新={updated}, 合計={config.meshSwaps.Length} " +
                $"({AssetDatabase.GetAssetPath(config)})");
        }

        static Mesh[] FindMeshesByNameContains(string nameFilter)
        {
            var result = new List<Mesh>();
            var seen = new HashSet<Mesh>();
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // メインアセットとサブアセットの双方を拾う
                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (!(obj is Mesh mesh)) continue;
                    if (string.IsNullOrEmpty(mesh.name)) continue;
                    if (mesh.name.IndexOf(nameFilter, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    if (!seen.Add(mesh)) continue;
                    result.Add(mesh);
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result.ToArray();
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Mesh";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
