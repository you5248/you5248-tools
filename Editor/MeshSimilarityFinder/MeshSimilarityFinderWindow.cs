using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace you5248.MeshSimilarityFinder
{
    public class MeshSimilarityFinderWindow : EditorWindow
    {
        // ── モード ──
        private enum ScanMode { FromReference, ScanAll }
        private ScanMode _scanMode = ScanMode.FromReference;

        // ── 参照メッシュ ──
        private Mesh _referenceMesh;

        // ── 結果 ──
        private readonly List<SimilarGroup> _groups = new List<SimilarGroup>();
        private bool _scanned;
        private string _statusMessage = "";

        // ── UI ──
        private Vector2 _scrollPos;
        private GUIStyle _sectionBox;
        private GUIStyle _exactBadge;
        private GUIStyle _structBadge;
        private GUIStyle _actionButton;
        private bool _stylesReady;

        [MenuItem("you5248/Mesh Similarity Finder")]
        public static void ShowWindow()
        {
            var w = GetWindow<MeshSimilarityFinderWindow>();
            w.titleContent = new GUIContent("Mesh Similarity Finder");
            w.minSize = new Vector2(420, 540);
        }

        private void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            TryAutoFillReference();
            Repaint();
        }

        private void TryAutoFillReference()
        {
            var sel = Selection.activeObject;
            if (sel is Mesh m) { _referenceMesh = m; return; }

            var go = Selection.activeGameObject;
            if (go == null) return;

            // Unityコンポーネントに ?. は使わない（Missing Component の偽nullを素通しして
            // sharedMesh アクセスで MissingComponentException になる）。== 演算子で判定する。
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) { _referenceMesh = mf.sharedMesh; return; }

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null) _referenceMesh = smr.sharedMesh;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  データモデル
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        internal enum SimilarityKind { Exact, Structural }

        internal class MeshUsage
        {
            public GameObject Object;
            public string ScenePath;
            public bool IsMeshFilter;
        }

        internal class MeshEntry
        {
            public Mesh Mesh;
            public string AssetPath;
            public SimilarityKind Kind;
            public List<MeshUsage> Usages = new List<MeshUsage>();
            public bool ReplaceSelected;
            public bool UsagesFoldout;
        }

        internal class SimilarGroup
        {
            public Mesh Reference;
            public List<MeshUsage> ReferenceUsages = new List<MeshUsage>();
            public List<MeshEntry> Candidates = new List<MeshEntry>();
            public bool Foldout = true;
            public bool RefUsagesFoldout = true;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  GUI
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void InitStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _sectionBox = new GUIStyle("HelpBox")
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 2, 6)
            };
            _exactBadge = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.95f, 0.35f, 0.2f) },
                fontStyle = FontStyle.Bold
            };
            _structBadge = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.9f, 0.72f, 0.1f) },
                fontStyle = FontStyle.Bold
            };
            _actionButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                fixedHeight = 36
            };
        }

        private void OnGUI()
        {
            InitStyles();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawHeader();
            EditorGUILayout.Space(6);

            var prevMode = _scanMode;
            _scanMode = (ScanMode)GUILayout.Toolbar((int)_scanMode,
                new[] { "参照メッシュから検索", "全体スキャン（重複グループ）" });
            if (_scanMode != prevMode) { _groups.Clear(); _scanned = false; _statusMessage = ""; }

            EditorGUILayout.Space(6);

            if (_scanMode == ScanMode.FromReference)
                DrawReferenceMode();
            else
                DrawScanAllMode();

            EditorGUILayout.Space(6);

            if (_scanned)
                DrawResults();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Mesh Similarity Finder", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "プロジェクト内の似たようなメッシュを検索し、統一することでファイルサイズを削減します。\n" +
                "● 完全一致 (赤): 頂点データが同一の重複メッシュ\n" +
                "● 構造一致 (黄): 頂点数・三角形数・サブメッシュ数が同じメッシュ",
                MessageType.Info);
        }

        private void DrawReferenceMode()
        {
            using (new EditorGUILayout.VerticalScope(_sectionBox))
            {
                EditorGUILayout.LabelField("参照メッシュ（統一先）", EditorStyles.boldLabel);
                _referenceMesh = (Mesh)EditorGUILayout.ObjectField("Mesh", _referenceMesh, typeof(Mesh), false);

                if (_referenceMesh != null)
                {
                    int triCount = 0;
                    for (int s = 0; s < _referenceMesh.subMeshCount; s++)
                        triCount += (int)_referenceMesh.GetIndexCount(s) / 3;
                    EditorGUILayout.LabelField(
                        $"頂点: {_referenceMesh.vertexCount:N0}　三角形: {triCount:N0}　サブメッシュ: {_referenceMesh.subMeshCount}",
                        EditorStyles.miniLabel);
                    string refPath = AssetDatabase.GetAssetPath(_referenceMesh);
                    if (!string.IsNullOrEmpty(refPath))
                        EditorGUILayout.LabelField(refPath, EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(_referenceMesh == null))
            {
                if (GUILayout.Button("このメッシュに似たものを検索", _actionButton))
                    RunScanFromReference();
            }
        }

        private void DrawScanAllMode()
        {
            EditorGUILayout.HelpBox(
                "現在のシーンにある MeshFilter / SkinnedMeshRenderer が参照しているメッシュ同士を比較し、" +
                "重複・類似グループを列挙します。",
                MessageType.None);
            EditorGUILayout.Space(4);

            if (GUILayout.Button("全体スキャン実行", _actionButton))
                RunScanAll();
        }

        private void DrawResults()
        {
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
                EditorGUILayout.Space(4);
            }

            if (_groups.Count == 0) return;

            foreach (var group in _groups.ToList())
                DrawGroup(group);
        }

        private void DrawGroup(SimilarGroup group)
        {
            using (new EditorGUILayout.VerticalScope(_sectionBox))
            {
                // ── グループヘッダー ──
                EditorGUILayout.BeginHorizontal();
                int exactCount = group.Candidates.Count(e => e.Kind == SimilarityKind.Exact);
                string foldLabel = $"  {group.Reference.name}  ({group.Candidates.Count} 件の類似";
                if (exactCount > 0) foldLabel += $"、うち完全一致 {exactCount} 件";
                foldLabel += ")";
                group.Foldout = EditorGUILayout.Foldout(group.Foldout, foldLabel, true, EditorStyles.boldLabel);

                if (GUILayout.Button("全選択", EditorStyles.miniButton, GUILayout.Width(52)))
                    foreach (var e in group.Candidates) e.ReplaceSelected = true;
                if (GUILayout.Button("解除", EditorStyles.miniButton, GUILayout.Width(40)))
                    foreach (var e in group.Candidates) e.ReplaceSelected = false;
                EditorGUILayout.EndHorizontal();

                if (!group.Foldout) return;

                EditorGUILayout.Space(2);

                // 参照メッシュ情報
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("統一先:", GUILayout.Width(48));
                    EditorGUILayout.ObjectField(group.Reference, typeof(Mesh), false);
                }
                string rPath = AssetDatabase.GetAssetPath(group.Reference);
                if (!string.IsNullOrEmpty(rPath))
                    EditorGUILayout.LabelField("  " + rPath, EditorStyles.miniLabel);
                DrawUsageObjects(group.ReferenceUsages, ref group.RefUsagesFoldout, indent: 2);

                EditorGUILayout.Space(4);

                // ── 候補リスト ──
                foreach (var entry in group.Candidates)
                    DrawEntry(entry);

                EditorGUILayout.Space(4);

                // ── 置換ボタン ──
                int selectedCount = group.Candidates.Count(e => e.ReplaceSelected);
                using (new EditorGUI.DisabledScope(selectedCount == 0))
                {
                    int usageCount = group.Candidates
                        .Where(e => e.ReplaceSelected)
                        .Sum(e => e.Usages.Count);
                    string btnLabel = selectedCount == 0
                        ? "置換する候補を選択してください"
                        : $"選択した {selectedCount} 件を「{group.Reference.name}」に置換（シーン内 {usageCount} 箇所）";

                    if (GUILayout.Button(btnLabel, GUILayout.Height(28)))
                        ExecuteReplace(group);
                }
            }

            EditorGUILayout.Space(4);
        }

        private void DrawEntry(MeshEntry entry)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.BeginHorizontal();

                entry.ReplaceSelected = EditorGUILayout.Toggle(entry.ReplaceSelected, GUILayout.Width(18));

                var badge = entry.Kind == SimilarityKind.Exact ? _exactBadge : _structBadge;
                string label = entry.Kind == SimilarityKind.Exact ? "●完全一致" : "○構造一致";
                GUILayout.Label(label, badge, GUILayout.Width(68));

                EditorGUILayout.ObjectField(entry.Mesh, typeof(Mesh), false, GUILayout.ExpandWidth(true));

                int triCount = 0;
                for (int s = 0; s < entry.Mesh.subMeshCount; s++)
                    triCount += (int)entry.Mesh.GetIndexCount(s) / 3;
                EditorGUILayout.LabelField(
                    $"v:{entry.Mesh.vertexCount}  t:{triCount}",
                    EditorStyles.miniLabel, GUILayout.Width(90));

                EditorGUILayout.EndHorizontal();

                // パス
                if (!string.IsNullOrEmpty(entry.AssetPath))
                {
                    EditorGUI.indentLevel += 2;
                    EditorGUILayout.LabelField(entry.AssetPath, EditorStyles.miniLabel);
                    EditorGUI.indentLevel -= 2;
                }

                DrawUsageObjects(entry.Usages, ref entry.UsagesFoldout, indent: 2);
            }

            var rect = EditorGUILayout.GetControlRect(false, 1);
            rect.height = 1;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            EditorGUILayout.Space(2);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  スキャンロジック
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void RunScanFromReference()
        {
            if (_referenceMesh == null) return;

            _groups.Clear();
            _scanned = false;
            _statusMessage = "";

            EditorUtility.DisplayProgressBar("Mesh Similarity Finder", "メッシュアセットを収集中…", 0f);
            try
            {
                var usageMap = BuildUsageMap();
                var allMeshes = LoadAllProjectMeshes();

                string refHash = null;
                EditorUtility.DisplayProgressBar("Mesh Similarity Finder", "参照メッシュのハッシュを計算中…", 0.1f);

                var candidates = new List<MeshEntry>();
                int total = allMeshes.Count;

                for (int i = 0; i < total; i++)
                {
                    var (mesh, path) = allMeshes[i];
                    if (mesh == _referenceMesh) continue;
                    if (!IsStructuralMatch(_referenceMesh, mesh)) continue;

                    if (refHash == null)
                    {
                        EditorUtility.DisplayProgressBar("Mesh Similarity Finder", "ハッシュ計算中…", 0.1f + 0.8f * i / total);
                        refHash = ComputeVertexHash(_referenceMesh);
                    }

                    EditorUtility.DisplayProgressBar("Mesh Similarity Finder", $"比較中… {mesh.name}", 0.1f + 0.8f * i / total);

                    string meshHash = ComputeVertexHash(mesh);
                    candidates.Add(new MeshEntry
                    {
                        Mesh = mesh,
                        AssetPath = path,
                        Kind = meshHash == refHash ? SimilarityKind.Exact : SimilarityKind.Structural,
                        Usages = usageMap.TryGetValue(mesh, out var u) ? u : new List<MeshUsage>()
                    });
                }

                // 完全一致 → 構造一致 の順でソート
                candidates.Sort((a, b) => a.Kind.CompareTo(b.Kind));

                if (candidates.Count > 0)
                    _groups.Add(new SimilarGroup
                    {
                        Reference = _referenceMesh,
                        ReferenceUsages = usageMap.TryGetValue(_referenceMesh, out var refU) ? refU : new List<MeshUsage>(),
                        Candidates = candidates
                    });

                int exactCount = candidates.Count(c => c.Kind == SimilarityKind.Exact);
                _statusMessage = candidates.Count == 0
                    ? "類似・重複メッシュは見つかりませんでした。"
                    : $"スキャン完了。{candidates.Count} 件の類似メッシュが見つかりました"
                      + (exactCount > 0 ? $"（完全一致: {exactCount} 件）" : "");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _scanned = true;
                Repaint();
            }
        }

        private void RunScanAll()
        {
            _groups.Clear();
            _scanned = false;
            _statusMessage = "";

            EditorUtility.DisplayProgressBar("Mesh Similarity Finder", "シーンのメッシュを収集中…", 0f);
            try
            {
                var usageMap = BuildUsageMap();

                // シーンで実際に使われているメッシュだけを対象にする
                var sceneMeshes = usageMap.Keys
                    .Select(m => (mesh: m, path: AssetDatabase.GetAssetPath(m)))
                    .ToList();

                int total = sceneMeshes.Count;

                // 構造キーでグループ化
                var byStructure = new Dictionary<string, List<(Mesh mesh, string path)>>();
                foreach (var (mesh, path) in sceneMeshes)
                {
                    string key = StructureKey(mesh);
                    if (!byStructure.TryGetValue(key, out var list))
                        byStructure[key] = list = new List<(Mesh, string)>();
                    list.Add((mesh, path));
                }

                int processed = 0;
                int totalStructGroups = byStructure.Count;
                int totalDuplicates = 0;

                foreach (var kvp in byStructure)
                {
                    var meshList = kvp.Value;
                    if (meshList.Count < 2) { processed++; continue; }

                    EditorUtility.DisplayProgressBar("Mesh Similarity Finder",
                        $"ハッシュ計算中… ({processed}/{totalStructGroups})",
                        0.1f + 0.85f * processed / totalStructGroups);

                    // ハッシュでサブグループ化
                    var byHash = new Dictionary<string, List<(Mesh mesh, string path)>>();
                    foreach (var (mesh, path) in meshList)
                    {
                        string h = ComputeVertexHash(mesh);
                        if (!byHash.TryGetValue(h, out var hl))
                            byHash[h] = hl = new List<(Mesh, string)>();
                        hl.Add((mesh, path));
                    }

                    // 同一ハッシュが2件以上 → 完全一致グループ
                    foreach (var hashGroup in byHash.Values)
                    {
                        if (hashGroup.Count < 2) continue;

                        // 使用数が最多のメッシュを参照（統一先）にする
                        hashGroup.Sort((a, b) =>
                            (usageMap.TryGetValue(b.mesh, out var ub) ? ub.Count : 0)
                            .CompareTo(usageMap.TryGetValue(a.mesh, out var ua) ? ua.Count : 0));

                        var reference = hashGroup[0].mesh;
                        var group = new SimilarGroup
                        {
                            Reference = reference,
                            ReferenceUsages = usageMap.TryGetValue(reference, out var refU1) ? refU1 : new List<MeshUsage>()
                        };
                        for (int i = 1; i < hashGroup.Count; i++)
                        {
                            var (mesh, path) = hashGroup[i];
                            group.Candidates.Add(new MeshEntry
                            {
                                Mesh = mesh,
                                AssetPath = path,
                                Kind = SimilarityKind.Exact,
                                Usages = usageMap.TryGetValue(mesh, out var u) ? u : new List<MeshUsage>()
                            });
                        }
                        _groups.Add(group);
                        totalDuplicates += group.Candidates.Count;
                    }

                    // 構造一致（ハッシュ違い）グループ — 異なるアセットが同じ構造
                    if (byHash.Count > 1)
                    {
                        // ハッシュごとに代表1件だけ取り出す
                        var structCandidates = byHash.Values
                            .Select(hg => hg[0])
                            .ToList();

                        if (structCandidates.Count >= 2)
                        {
                            // 使用数が最多を参照にする
                            structCandidates.Sort((a, b) =>
                                (usageMap.TryGetValue(b.mesh, out var ub) ? ub.Count : 0)
                                .CompareTo(usageMap.TryGetValue(a.mesh, out var ua) ? ua.Count : 0));

                            var reference = structCandidates[0].mesh;
                            var group = new SimilarGroup
                            {
                                Reference = reference,
                                ReferenceUsages = usageMap.TryGetValue(reference, out var refU2) ? refU2 : new List<MeshUsage>()
                            };
                            for (int i = 1; i < structCandidates.Count; i++)
                            {
                                var (mesh, path) = structCandidates[i];
                                group.Candidates.Add(new MeshEntry
                                {
                                    Mesh = mesh,
                                    AssetPath = path,
                                    Kind = SimilarityKind.Structural,
                                    Usages = usageMap.TryGetValue(mesh, out var u) ? u : new List<MeshUsage>()
                                });
                            }
                            _groups.Add(group);
                        }
                    }

                    processed++;
                }

                // 完全一致件数の多いグループを上に
                _groups.Sort((a, b) =>
                {
                    int aExact = a.Candidates.Count(e => e.Kind == SimilarityKind.Exact);
                    int bExact = b.Candidates.Count(e => e.Kind == SimilarityKind.Exact);
                    return bExact != aExact
                        ? bExact.CompareTo(aExact)
                        : b.Candidates.Count.CompareTo(a.Candidates.Count);
                });

                _statusMessage = _groups.Count == 0
                    ? $"シーン内 {total} メッシュを確認しましたが、重複・類似は見つかりませんでした。"
                    : $"スキャン完了（シーン内 {total} メッシュ）。{_groups.Count} グループ（合計 {totalDuplicates} 件の重複メッシュ）が見つかりました。";
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _scanned = true;
                Repaint();
            }
        }

        private List<(Mesh mesh, string path)> LoadAllProjectMeshes()
        {
            var result = new List<(Mesh, string)>();
            var guids = AssetDatabase.FindAssets("t:Mesh");
            var paths = guids.Select(AssetDatabase.GUIDToAssetPath).Distinct();

            foreach (var path in paths)
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is Mesh m)
                        result.Add((m, path));
                }
            }
            return result;
        }

        private Dictionary<Mesh, List<MeshUsage>> BuildUsageMap()
        {
            var map = new Dictionary<Mesh, List<MeshUsage>>();

            void Add(Mesh mesh, GameObject go, bool isMF)
            {
                if (mesh == null || go == null) return;
                // アセットパスなし = TMP・プロシージャル生成メッシュ → 統一対象外
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mesh))) return;
                // EditorOnly タグ（自身または親）が付いているオブジェクトは除外
                if (IsEditorOnly(go)) return;
                if (!map.TryGetValue(mesh, out var list))
                    map[mesh] = list = new List<MeshUsage>();
                list.Add(new MeshUsage
                {
                    Object = go,
                    ScenePath = GetScenePath(go),
                    IsMeshFilter = isMF
                });
            }

            // ロード済みシーンのルートGameObjectからたどる（非アクティブも含む）
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                        Add(mf.sharedMesh, mf.gameObject, true);
                    foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        Add(smr.sharedMesh, smr.gameObject, false);
                }
            }

            return map;
        }

        private static string StructureKey(Mesh mesh)
        {
            int triCount = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
                triCount += (int)mesh.GetIndexCount(s);
            return $"{mesh.vertexCount}_{triCount}_{mesh.subMeshCount}";
        }

        private static bool IsStructuralMatch(Mesh a, Mesh b)
        {
            if (a.vertexCount != b.vertexCount) return false;
            if (a.subMeshCount != b.subMeshCount) return false;
            for (int s = 0; s < a.subMeshCount; s++)
            {
                if (a.GetIndexCount(s) != b.GetIndexCount(s)) return false;
            }
            return true;
        }

        private static string ComputeVertexHash(Mesh mesh)
        {
            var verts = mesh.vertices;
            int hash = verts.Length * 1000003 + mesh.subMeshCount;

            // サンプリング: 大きいメッシュは全頂点だと重いので最大4096点
            int step = Mathf.Max(1, verts.Length / 4096);
            for (int i = 0; i < verts.Length; i += step)
            {
                hash = hash * 31 + QuantizeFloat(verts[i].x);
                hash = hash * 31 + QuantizeFloat(verts[i].y);
                hash = hash * 31 + QuantizeFloat(verts[i].z);
            }
            // 先頭・末尾・中間の頂点で総当たり（スキップした場合の補正）
            if (step > 1 && verts.Length > 0)
            {
                int mid = verts.Length / 2;
                hash = hash * 31 + QuantizeFloat(verts[verts.Length - 1].x);
                hash = hash * 31 + QuantizeFloat(verts[mid].y);
            }
            return hash.ToString("X8");
        }

        private static int QuantizeFloat(float f) => (int)(f * 10000f);

        private static bool IsEditorOnly(GameObject go)
        {
            var t = go.transform;
            while (t != null)
            {
                if (t.CompareTag("EditorOnly")) return true;
                t = t.parent;
            }
            return false;
        }

        private static void DrawUsageObjects(List<MeshUsage> usages, ref bool foldout, int indent = 0)
        {
            EditorGUI.indentLevel += indent;
            if (usages.Count == 0)
            {
                EditorGUILayout.LabelField("（シーン内での使用なし）", EditorStyles.miniLabel);
            }
            else
            {
                bool needFold = usages.Count > 4;
                if (needFold)
                    foldout = EditorGUILayout.Foldout(foldout, $"シーン使用箇所 ({usages.Count})", true, EditorStyles.miniLabel);
                else
                    EditorGUILayout.LabelField($"シーン使用箇所 ({usages.Count})", EditorStyles.miniLabel);

                if (!needFold || foldout)
                {
                    EditorGUI.indentLevel++;
                    foreach (var u in usages)
                        EditorGUILayout.ObjectField(u.Object, typeof(GameObject), true);
                    EditorGUI.indentLevel--;
                }
            }
            EditorGUI.indentLevel -= indent;
        }

        private static string GetScenePath(GameObject go)
        {
            var t = go.transform;
            var parts = new List<string>();
            while (t != null) { parts.Insert(0, t.name); t = t.parent; }
            return string.Join("/", parts);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  置換ロジック
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void ExecuteReplace(SimilarGroup group)
        {
            var toReplace = group.Candidates.Where(e => e.ReplaceSelected).ToList();
            if (toReplace.Count == 0) return;

            int usageTotal = toReplace.Sum(e => e.Usages.Count);
            bool confirm = EditorUtility.DisplayDialog(
                "メッシュを置換",
                $"{toReplace.Count} 件のメッシュをすべて\n「{group.Reference.name}」に置換します。\n\n" +
                $"シーン内 {usageTotal} 箇所の参照が更新されます。\n" +
                "この操作はUndoできます。",
                "置換する", "キャンセル");

            if (!confirm) return;

            Undo.SetCurrentGroupName("Replace Similar Meshes");
            int undoGroup = Undo.GetCurrentGroup();

            int replacedUsages = 0;

            foreach (var entry in toReplace)
            {
                foreach (var usage in entry.Usages)
                {
                    if (usage.IsMeshFilter)
                    {
                        var mf = usage.Object.GetComponent<MeshFilter>();
                        if (mf == null) continue;
                        Undo.RecordObject(mf, "Replace Mesh");
                        mf.sharedMesh = group.Reference;
                        EditorUtility.SetDirty(mf);
                    }
                    else
                    {
                        var smr = usage.Object.GetComponent<SkinnedMeshRenderer>();
                        if (smr == null) continue;
                        Undo.RecordObject(smr, "Replace Mesh");
                        smr.sharedMesh = group.Reference;
                        EditorUtility.SetDirty(smr);
                    }
                    replacedUsages++;
                }

                // 置換済みエントリはリストから除去
                group.Candidates.Remove(entry);
            }

            Undo.CollapseUndoOperations(undoGroup);

            if (group.Candidates.Count == 0)
                _groups.Remove(group);

            _statusMessage = $"{toReplace.Count} 件のメッシュを置換しました（シーン内 {replacedUsages} 箇所を更新）。";
            Repaint();
        }
    }
}
