using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace you5248.LODGenerator
{
    /// <summary>
    /// LOD Generator EditorWindow.
    /// Menu: Tools > you5248 > LOD Generator
    /// </summary>
    public class LODGeneratorWindow : EditorWindow
    {
        // ── State ──
        private LODGeneratorPreset _currentPreset;
        private List<LODGeneratorPreset> _savedPresets;
        private int _selectedPresetIndex = 0;
        private bool _includeChildren = true;
        private Vector2 _scrollPos;

        // ── Cached selection info ──
        private int _cachedObjCount;
        private int _cachedMeshCount;
        private List<LODGeneratorCore.MeshTarget> _cachedTargets;
        private double _lastSelectionCheck;

        // ── Foldouts ──
        private bool _foldTarget = true;
        private bool _foldPreset = true;
        private bool _foldLOD = true;
        private bool _foldCrossfade = true;
        private bool _foldSave = true;
        private bool _foldReplace = true;

        // ── Replace tab state ──
        private float _replaceQuality = 0.5f; // 0-1, lower = fewer triangles

        // ── Before/After preview state ──
        private LODMeshPreview _meshPreview;
        private Mesh _previewSource;   // representative original mesh
        private Mesh _previewResult;   // simplified mesh (in-memory, owned — must DestroyImmediate)
        private Material[] _previewMats;
        private int _previewSrcTris, _previewSrcVerts;
        private int _previewDstTris, _previewDstVerts;
        private float _previewComputedQuality = -1f;
        private bool _previewComputed;
        private GUIStyle _previewLabelStyle;
        private float _previewHeight = 220f;   // ユーザーがドラッグで変更可能
        private bool _resizingPreview;

        // ── Tab & Utility state ──
        private enum Tab { Generate, Replace, Utility }
        private Tab _currentTab = Tab.Generate;
        private List<DuplicateLODGroup> _scanResults;
        private bool[] _groupFoldouts;
        private List<OrphanedLODAsset> _orphanResults;
        private bool _orphanListExpanded;

        // ── Styles (lazy init) ──
        private GUIStyle _headerStyle;
        private GUIStyle _generateButtonStyle;

        private const string PresetFolderKey = "LODGenerator_PresetFolder";
        private const string LanguageKey = "LODGenerator_Language";
        private const string PreviewHeightKey = "LODGenerator_PreviewHeight";

        [MenuItem("you5248/LOD Generator")]
        public static void ShowWindow()
        {
            var window = GetWindow<LODGeneratorWindow>();
            window.titleContent = new GUIContent("LOD Generator");
            window.minSize = new Vector2(340, 500);
        }

        private void OnEnable()
        {
            // Load language
            int langIdx = EditorPrefs.GetInt(LanguageKey, 1); // default Japanese
            LODGeneratorLocalization.Current = (LODGenLanguage)langIdx;

            // Load/create working preset
            _currentPreset = LODGeneratorPreset.CreateDefault();
            LoadSavedPresets();

            _previewHeight = EditorPrefs.GetFloat(PreviewHeightKey, 220f);

            Selection.selectionChanged += Repaint;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;

            _meshPreview?.Cleanup();
            _meshPreview = null;
            if (_previewResult != null)
            {
                Object.DestroyImmediate(_previewResult);
                _previewResult = null;
            }
        }

        private GUIStyle _targetBadgeStyle;
        private GUIStyle _tabStyle;

        private void InitStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.foldout)
                {
                    fontStyle = FontStyle.Bold
                };
            }
            if (_generateButtonStyle == null)
            {
                _generateButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 15,
                    fontStyle = FontStyle.Bold,
                    fixedHeight = 48
                };
            }
            if (_targetBadgeStyle == null)
            {
                _targetBadgeStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleLeft
                };
            }
            if (_tabStyle == null)
            {
                _tabStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    fixedHeight = 26
                };
            }
        }

        private void OnGUI()
        {
            InitStyles();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawLanguageBar();
            EditorGUILayout.Space(4);
            _currentTab = (Tab)GUILayout.Toolbar((int)_currentTab,
                new[] { L("TabGenerate"), L("TabReplace"), L("TabUtility") }, _tabStyle);
            EditorGUILayout.Space(6);

            switch (_currentTab)
            {
                case Tab.Generate:
                    RefreshSelection();
                    DrawTargetSection();
                    EditorGUILayout.Space(4);
                    DrawPresetSection();
                    EditorGUILayout.Space(4);
                    DrawLODSection();
                    EditorGUILayout.Space(4);
                    DrawCrossfadeSection();
                    EditorGUILayout.Space(4);
                    DrawSaveSection();
                    break;
                case Tab.Replace:
                    RefreshSelection();
                    DrawTargetSection();
                    EditorGUILayout.Space(4);
                    DrawReplaceSection();
                    EditorGUILayout.Space(4);
                    DrawPreviewSection();
                    EditorGUILayout.Space(4);
                    DrawSaveSection();
                    break;
                case Tab.Utility:
                    DrawUtilityTab();
                    break;
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);
            switch (_currentTab)
            {
                case Tab.Generate:
                    DrawGenerateButton();
                    break;
                case Tab.Replace:
                    DrawReplaceButton();
                    break;
                case Tab.Utility:
                    DrawConsolidateButton();
                    break;
            }
            EditorGUILayout.Space(4);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Language
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void DrawLanguageBar()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField(L("Language"), GUILayout.Width(50));
            var newLang = (LODGenLanguage)EditorGUILayout.EnumPopup(
                LODGeneratorLocalization.Current, GUILayout.Width(100));

            if (newLang != LODGeneratorLocalization.Current)
            {
                LODGeneratorLocalization.Current = newLang;
                EditorPrefs.SetInt(LanguageKey, (int)newLang);
            }

            EditorGUILayout.EndHorizontal();
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Target
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void DrawTargetSection()
        {
            _foldTarget = EditorGUILayout.Foldout(_foldTarget, L("SectionTarget"), true, _headerStyle);
            if (!_foldTarget) return;

            EditorGUI.indentLevel++;

            if (_cachedMeshCount > 0)
            {
                // 件数バッジ — 緑のテキストで目立たせる
                var prevColor = GUI.contentColor;
                GUI.contentColor = new Color(0.55f, 0.85f, 0.55f);
                EditorGUILayout.LabelField(
                    "● " + L("SelectedObjects", _cachedObjCount, _cachedMeshCount),
                    _targetBadgeStyle);
                GUI.contentColor = prevColor;

                if (GUILayout.Button(L("SelectSameMesh")))
                {
                    SelectAllWithSameMesh();
                }
            }
            else
            {
                EditorGUILayout.HelpBox(L("NoMeshSelected"), MessageType.Info);
            }

            _includeChildren = EditorGUILayout.Toggle(L("IncludeChildren"), _includeChildren);

            EditorGUI.indentLevel--;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Preset
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void DrawPresetSection()
        {
            _foldPreset = EditorGUILayout.Foldout(_foldPreset, L("SectionPreset"), true, _headerStyle);
            if (!_foldPreset) return;

            EditorGUI.indentLevel++;

            // Build preset name list
            var names = new List<string> { L("PresetDefault") };
            if (_savedPresets != null)
                foreach (var p in _savedPresets)
                    if (p != null) names.Add(p.name);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(L("PresetLabel"), GUILayout.Width(56));

            int newIdx = EditorGUILayout.Popup(_selectedPresetIndex, names.ToArray());
            if (newIdx != _selectedPresetIndex)
            {
                _selectedPresetIndex = newIdx;
                if (newIdx == 0)
                {
                    _currentPreset = LODGeneratorPreset.CreateDefault();
                }
                else if (_savedPresets != null && newIdx - 1 < _savedPresets.Count)
                {
                    _currentPreset.CopyFrom(_savedPresets[newIdx - 1]);
                }
            }

            if (GUILayout.Button(L("PresetSave"), GUILayout.Width(48)))
                SaveCurrentPreset();

            if (GUILayout.Button(L("PresetSaveAs"), GUILayout.Width(48)))
                SaveAsNewPreset();

            if (GUILayout.Button(L("PresetDelete"), GUILayout.Width(48)))
                DeletePreset();

            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  LOD Settings
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void DrawLODSection()
        {
            _foldLOD = EditorGUILayout.Foldout(_foldLOD, L("SectionLOD"), true, _headerStyle);
            if (!_foldLOD) return;

            EditorGUI.indentLevel++;

            int newCount = EditorGUILayout.IntSlider(L("LODCount"), _currentPreset.lodCount, 1, 4);
            if (newCount != _currentPreset.lodCount)
            {
                _currentPreset.lodCount = newCount;
                _currentPreset.SyncLevels();
            }

            EditorGUILayout.Space(4);

            // LOD0 (original)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(L("LODOriginal"), EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            _currentPreset.lod0ScreenPercent = EditorGUILayout.Slider(
                L("ScreenPercent"), _currentPreset.lod0ScreenPercent, 0.01f, 1f);
            EditorGUI.indentLevel--;

            // 代表メッシュの「薄さ」(min/max extent比)。板ポリ自動サジェストに使う。
            bool hasRep = _cachedTargets != null && _cachedTargets.Count > 0 && _cachedTargets[0].sharedMesh != null;
            float repThinness = hasRep ? LODBillboardBaker.Thinness(_cachedTargets[0].sharedMesh.bounds) : 1f;
            bool repIsThin = hasRep && repThinness <= _currentPreset.billboardThinRatio;

            // LOD1 ~ LODn
            for (int i = 0; i < _currentPreset.lodCount; i++)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(L("LODLevel", i + 1), EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel++;

                _currentPreset.levels[i].mode = (LODLevelMode)EditorGUILayout.EnumPopup(
                    L("LODMode"), _currentPreset.levels[i].mode);

                if (_currentPreset.levels[i].mode == LODLevelMode.Cube)
                {
                    EditorGUILayout.HelpBox(L("CubeLevelInfo"), MessageType.None);
                }
                if (_currentPreset.levels[i].mode == LODLevelMode.Billboard)
                {
                    EditorGUILayout.HelpBox(L("BillboardLevelInfo"), MessageType.None);
                }
                else
                {
                    _currentPreset.levels[i].quality = ReductionSlider(
                        L("Reduction"), _currentPreset.levels[i].quality);

                    // 薄いメッシュは簡略化が効きにくい → 板ポリ化を提案
                    if (repIsThin)
                    {
                        EditorGUILayout.HelpBox(
                            L("BillboardSuggest", Mathf.RoundToInt(repThinness * 100f)), MessageType.Info);
                        if (GUILayout.Button(L("BillboardSwitchButton")))
                            _currentPreset.levels[i].mode = LODLevelMode.Billboard;
                    }
                }

                _currentPreset.levels[i].screenPercent = EditorGUILayout.Slider(
                    L("ScreenPercent"), _currentPreset.levels[i].screenPercent, 0f, 1f);

                EditorGUI.indentLevel--;
            }

            // 板ポリ設定（いずれかのレベルが Billboard のときのみ表示）
            bool anyBillboard = false;
            for (int i = 0; i < _currentPreset.lodCount; i++)
                if (_currentPreset.levels[i].mode == LODLevelMode.Billboard) { anyBillboard = true; break; }

            if (anyBillboard)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(L("BillboardSettings"), EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                _currentPreset.billboardResolution = EditorGUILayout.IntPopup(
                    L("BillboardResolution"), _currentPreset.billboardResolution,
                    new[] { "256", "512", "1024", "2048" }, new[] { 256, 512, 1024, 2048 });
                _currentPreset.billboardCutoff = EditorGUILayout.Slider(
                    L("BillboardCutoff"), _currentPreset.billboardCutoff, 0f, 1f);
                _currentPreset.billboardPadding = EditorGUILayout.Slider(
                    L("BillboardPadding"), _currentPreset.billboardPadding, 0f, 0.5f);
                _currentPreset.billboardTwoSided = EditorGUILayout.Toggle(
                    L("BillboardTwoSided"), _currentPreset.billboardTwoSided);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            _currentPreset.enableCulled = EditorGUILayout.Toggle(L("EnableCulled"), _currentPreset.enableCulled);
            _currentPreset.skipDuplicateLODs = EditorGUILayout.Toggle(
                L("SkipDuplicateLODs"), _currentPreset.skipDuplicateLODs);

            EditorGUILayout.Space(4);
            DrawPreserveOptions();

            EditorGUI.indentLevel--;
        }

        /// <summary>境界辺・UV縫い目などの保持トグル群（LOD生成/置換で共用）。</summary>
        private void DrawPreserveOptions()
        {
            _currentPreset.preserveBorderEdges = EditorGUILayout.Toggle(
                L("PreserveBorderEdges"), _currentPreset.preserveBorderEdges);
            _currentPreset.preserveUVSeamEdges = EditorGUILayout.Toggle(
                L("PreserveUVSeamEdges"), _currentPreset.preserveUVSeamEdges);
            _currentPreset.preserveUVFoldoverEdges = EditorGUILayout.Toggle(
                L("PreserveUVFoldoverEdges"), _currentPreset.preserveUVFoldoverEdges);
            _currentPreset.preserveSurfaceCurvature = EditorGUILayout.Toggle(
                L("PreserveSurfaceCurvature"), _currentPreset.preserveSurfaceCurvature);
        }

        /// <summary>削減率（0〜99%）スライダー。内部のquality(0〜1)と相互変換する。</summary>
        private static float ReductionSlider(string label, float quality)
        {
            float reduction = EditorGUILayout.Slider(label, (1f - quality) * 100f, 0f, 99f);
            return 1f - reduction / 100f;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Low-Poly Replace
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void DrawReplaceSection()
        {
            _foldReplace = EditorGUILayout.Foldout(_foldReplace, L("SectionReplace"), true, _headerStyle);
            if (!_foldReplace) return;

            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(L("ReplaceDesc"), MessageType.Info);
            EditorGUILayout.Space(4);

            _replaceQuality = ReductionSlider(L("Reduction"), _replaceQuality);

            // ── 構造簡略化（独立した機能ブロック）──
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(L("StructuralSimplify"), EditorStyles.boldLabel);
            _currentPreset.replaceWithCube = EditorGUILayout.Toggle(
                L("ReplaceCube"), _currentPreset.replaceWithCube);
            if (_currentPreset.replaceWithCube)
                EditorGUILayout.HelpBox(L("ReplaceCubeInfo"), MessageType.None);

            using (new EditorGUI.DisabledScope(_currentPreset.replaceWithCube))
            {
            _currentPreset.replaceStructuralSimplify = EditorGUILayout.Toggle(
                L("StructuralEnable"), _currentPreset.replaceStructuralSimplify);
            }
            if (_currentPreset.replaceStructuralSimplify)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(L("StructuralDesc"), MessageType.None);
                _currentPreset.structuralCoplanarAngle = EditorGUILayout.Slider(
                    L("StructuralCoplanarAngle"), _currentPreset.structuralCoplanarAngle, 0f, 30f);
                _currentPreset.structuralBevelLength = Mathf.Max(0f, EditorGUILayout.FloatField(
                    L("StructuralBevelLength"), _currentPreset.structuralBevelLength));
                _currentPreset.structuralFlatShade = EditorGUILayout.Toggle(
                    L("StructuralFlatShade"), _currentPreset.structuralFlatShade);
                EditorGUI.indentLevel--;
            }

            // ── メッシュ簡略化の保持オプション（別レイヤー）──
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(L("PreserveHeader"), EditorStyles.boldLabel);
            DrawPreserveOptions();

            EditorGUI.indentLevel--;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Before / After preview
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        // 元メッシュの三角形/頂点数キャッシュ（毎フレームの triangles 確保を避ける）
        private Mesh _srcCountMesh;
        private int _srcCountTris, _srcCountVerts;

        private void EnsureSrcCounts(Mesh src)
        {
            if (src == _srcCountMesh) return;
            _srcCountMesh = src;
            _srcCountTris = src != null ? src.triangles.Length / 3 : 0;
            _srcCountVerts = src != null ? src.vertexCount : 0;
        }

        private void DrawPreviewSection()
        {
            EditorGUILayout.LabelField(L("PreviewHeader"), _headerStyle);

            if (_cachedTargets == null || _cachedTargets.Count == 0)
            {
                EditorGUILayout.HelpBox(L("PreviewSelectFirst"), MessageType.Info);
                return;
            }

            var rep = _cachedTargets[0];
            var src = rep.sharedMesh;

            if (_cachedMeshCount > 1)
                EditorGUILayout.HelpBox(L("PreviewMultiNote", _cachedMeshCount), MessageType.None);

            if (src != null && !src.isReadable)
                EditorGUILayout.HelpBox(L("PreviewNotReadable"), MessageType.Warning);

            // 変更前の数値（キャッシュ）と目標値（削減率から算出）
            EnsureSrcCounts(src);
            int targetTris = Mathf.RoundToInt(_srcCountTris * _replaceQuality);

            EditorGUILayout.LabelField(L("PreviewSourceInfo", _srcCountTris, _srcCountVerts));
            EditorGUILayout.LabelField(L("PreviewTargetInfo", targetTris, 1f - _replaceQuality));

            EditorGUILayout.Space(2);
            if (GUILayout.Button(L("PreviewComputeButton")))
                ComputePreview(rep);

            // 計算後にスライダーや選択が変わっていたら「古い」と警告
            bool stale = _previewComputed &&
                (_previewSource != src || !Mathf.Approximately(_previewComputedQuality, _replaceQuality));
            if (stale)
                EditorGUILayout.HelpBox(L("PreviewStale"), MessageType.Warning);

            if (_previewComputed && _previewResult != null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(L("PreviewResultInfo", _previewDstTris, _previewDstVerts));

                float actual = _previewSrcTris > 0
                    ? 1f - (float)_previewDstTris / _previewSrcTris : 0f;
                EditorGUILayout.LabelField(L("PreviewActualReduction", actual));

                // 目標まで届かなかった（＝これ以上減らせない）ことを通知
                int tgt = Mathf.RoundToInt(_previewSrcTris * _previewComputedQuality);
                if (_previewComputedQuality < 1f && _previewDstTris > tgt * 1.05f + 1)
                    EditorGUILayout.HelpBox(L("PreviewLimitNote"), MessageType.Info);

                DrawPreviewViewports();
            }
        }

        private void DrawPreviewViewports()
        {
            if (_meshPreview == null) _meshPreview = new LODMeshPreview();
            if (_previewLabelStyle == null)
                _previewLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                { alignment = TextAnchor.MiddleCenter };

            Rect labelRow = EditorGUILayout.GetControlRect(false, 14f);
            Rect total = EditorGUILayout.GetControlRect(false, _previewHeight);

            const float gap = 6f;
            float vpW = (total.width - gap) * 0.5f;
            Rect left = new Rect(total.x, total.y, vpW, total.height);
            Rect right = new Rect(total.x + vpW + gap, total.y, vpW, total.height);

            GUI.Label(new Rect(left.x, labelRow.y, vpW, labelRow.height), L("PreviewBefore"), _previewLabelStyle);
            GUI.Label(new Rect(right.x, labelRow.y, vpW, labelRow.height), L("PreviewAfter"), _previewLabelStyle);

            if (_meshPreview.HandleDrag(total))
                Repaint();

            _meshPreview.DrawViewport(left, _previewSource, _previewMats);
            _meshPreview.DrawViewport(right, _previewResult, _previewMats);

            DrawPreviewResizeHandle();

            EditorGUILayout.LabelField(L("PreviewDragHint"), EditorStyles.centeredGreyMiniLabel);
        }

        /// <summary>ビューポート下のドラッグハンドル。上下ドラッグで高さを変更し EditorPrefs に保存。</summary>
        private void DrawPreviewResizeHandle()
        {
            Rect handle = EditorGUILayout.GetControlRect(false, 8f);
            EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeVertical);

            // つまみの見た目（中央に細いライン）
            var lineColor = _resizingPreview
                ? new Color(0.45f, 0.85f, 0.55f, 0.9f)
                : new Color(1f, 1f, 1f, 0.22f);
            EditorGUI.DrawRect(new Rect(handle.x + handle.width * 0.5f - 18f, handle.y + 3f, 36f, 2f), lineColor);

            var e = Event.current;
            if (e.type == EventType.MouseDown && handle.Contains(e.mousePosition))
            {
                _resizingPreview = true;
                e.Use();
            }
            else if (_resizingPreview && e.type == EventType.MouseDrag)
            {
                _previewHeight = Mathf.Clamp(_previewHeight + e.delta.y, 140f, 900f);
                e.Use();
                Repaint();
            }
            else if (_resizingPreview && e.type == EventType.MouseUp)
            {
                _resizingPreview = false;
                EditorPrefs.SetFloat(PreviewHeightKey, _previewHeight);
                e.Use();
            }
        }

        private void ComputePreview(LODGeneratorCore.MeshTarget rep)
        {
            var src = rep.sharedMesh;
            if (src == null) return;

            if (_previewResult != null)
            {
                Object.DestroyImmediate(_previewResult);
                _previewResult = null;
            }

            _previewResult = LODGeneratorCore.SimplifyPreview(src, _replaceQuality, _currentPreset);
            _previewSource = src;
            _previewComputedQuality = _replaceQuality;
            _previewMats = rep.skinnedMesh != null
                ? rep.skinnedMesh.sharedMaterials
                : (rep.meshRenderer != null ? rep.meshRenderer.sharedMaterials : null);

            _previewSrcTris = src.triangles.Length / 3;
            _previewSrcVerts = src.vertexCount;
            _previewDstTris = _previewResult != null ? _previewResult.triangles.Length / 3 : 0;
            _previewDstVerts = _previewResult != null ? _previewResult.vertexCount : 0;
            _previewComputed = true;
            Repaint();
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Cross-fade
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void DrawCrossfadeSection()
        {
            _foldCrossfade = EditorGUILayout.Foldout(_foldCrossfade, L("SectionCrossfade"), true, _headerStyle);
            if (!_foldCrossfade) return;

            EditorGUI.indentLevel++;

            _currentPreset.enableCrossFade = EditorGUILayout.Toggle(
                L("CrossfadeEnable"), _currentPreset.enableCrossFade);

            if (_currentPreset.enableCrossFade)
            {
                _currentPreset.animateCrossFading = EditorGUILayout.Toggle(
                    L("CrossfadeAnimate"), _currentPreset.animateCrossFading);

                // 遷移幅は SpeedTree モード（アニメーションOFF）でのみ Unity に反映される
                using (new EditorGUI.DisabledScope(_currentPreset.animateCrossFading))
                {
                    _currentPreset.crossFadeWidth = EditorGUILayout.Slider(
                        L("CrossfadeWidth"), _currentPreset.crossFadeWidth, 0f, 1f);
                }
                if (_currentPreset.animateCrossFading)
                    EditorGUILayout.HelpBox(L("CrossfadeWidthNote"), MessageType.None);
            }

            EditorGUI.indentLevel--;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Save Location
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void DrawSaveSection()
        {
            _foldSave = EditorGUILayout.Foldout(_foldSave, L("SectionSave"), true, _headerStyle);
            if (!_foldSave) return;

            EditorGUI.indentLevel++;

            bool sameFolder = EditorGUILayout.Toggle(L("SaveSameFolder"), _currentPreset.saveSameFolder);
            _currentPreset.saveSameFolder = sameFolder;

            if (!sameFolder)
            {
                EditorGUILayout.BeginHorizontal();
                _currentPreset.customSavePath = EditorGUILayout.TextField(
                    L("SaveFolderLabel"), _currentPreset.customSavePath);

                if (GUILayout.Button(L("SaveBrowse"), GUILayout.Width(30)))
                    BrowseForSaveFolder();
                EditorGUILayout.EndHorizontal();

                // Assets配下チェック（不正なら警告。生成時にもガードする）
                if (!IsCustomPathValid())
                    EditorGUILayout.HelpBox(L("SaveInvalidPath"), MessageType.Warning);
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>フォルダ選択ダイアログ。Assets外を選んだ場合は警告して破棄する。</summary>
        private void BrowseForSaveFolder()
        {
            string selected = EditorUtility.OpenFolderPanel(
                L("SaveFolderLabel"), _currentPreset.customSavePath, "");
            if (string.IsNullOrEmpty(selected)) return;

            string dataPath = Application.dataPath.Replace('\\', '/');
            selected = selected.Replace('\\', '/');

            if (selected == dataPath)
                _currentPreset.customSavePath = "Assets";
            else if (selected.StartsWith(dataPath + "/"))
                _currentPreset.customSavePath = "Assets" + selected.Substring(dataPath.Length);
            else
                EditorUtility.DisplayDialog(L("WindowTitle"), L("SaveOutsideAssets"), "OK");
        }

        /// <summary>保存先が有効か（同フォルダモード、または Assets 配下の指定フォルダ）。</summary>
        private bool IsCustomPathValid()
        {
            if (_currentPreset.saveSameFolder) return true;
            string p = _currentPreset.customSavePath;
            if (string.IsNullOrEmpty(p)) return false;
            p = p.Replace('\\', '/');
            return p == "Assets" || p.StartsWith("Assets/");
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Generate Button
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void DrawGenerateButton()
        {
            string label = _cachedMeshCount > 0
                ? L("GenerateButtonReady", _cachedMeshCount)
                : L("GenerateButton");
            DrawActionButton(label, _cachedMeshCount > 0, ExecuteGeneration);
        }

        /// <summary>下部の大型アクションボタン（緑アクセント）を描画。Generate/Replaceで共用。</summary>
        private void DrawActionButton(string label, bool enabled, System.Action onClick)
        {
            EditorGUI.BeginDisabledGroup(!enabled);

            // 緑系アクセント (準備完了なら明るく、未選択なら暗く)
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = enabled
                ? new Color(0.45f, 0.85f, 0.55f)
                : new Color(0.5f, 0.5f, 0.5f);

            if (GUILayout.Button(label, _generateButtonStyle))
                onClick();

            GUI.backgroundColor = prevBg;
            EditorGUI.EndDisabledGroup();
        }

        private void ExecuteGeneration()
        {
            if (!IsCustomPathValid())
            {
                EditorUtility.DisplayDialog(L("WindowTitle"), L("SaveInvalidPath"), "OK");
                return;
            }

            var targets = LODGeneratorCore.CollectTargets(
                Selection.gameObjects, _includeChildren);

            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    L("WindowTitle"), L("GenerateNoMesh"), "OK");
                return;
            }

            int processed = LODGeneratorCore.GenerateAll(
                targets, _currentPreset, out var errors, out var skipped);

            // Build result message
            string msg = L("GenerateComplete", processed);
            if (skipped.Count > 0)
                msg += "\n" + L("GenerateSkipped", string.Join(", ", skipped));
            if (errors.Count > 0)
                msg += "\n\nErrors:\n" + string.Join("\n", errors);

            EditorUtility.DisplayDialog(L("WindowTitle"), msg, "OK");
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Replace Button
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void DrawReplaceButton()
        {
            string label = _cachedMeshCount > 0
                ? L("ReplaceButtonReady", _cachedMeshCount)
                : L("ReplaceButton");
            DrawActionButton(label, _cachedMeshCount > 0, ExecuteReplace);
        }

        private void ExecuteReplace()
        {
            if (!IsCustomPathValid())
            {
                EditorUtility.DisplayDialog(L("WindowTitle"), L("SaveInvalidPath"), "OK");
                return;
            }

            var targets = LODGeneratorCore.CollectTargets(
                Selection.gameObjects, _includeChildren);

            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    L("WindowTitle"), L("GenerateNoMesh"), "OK");
                return;
            }

            bool confirm = EditorUtility.DisplayDialog(
                L("WindowTitle"),
                L("ReplaceConfirm", targets.Count),
                "OK", "Cancel");
            if (!confirm) return;

            int processed = LODGeneratorCore.ReplaceAll(
                targets, _currentPreset, _replaceQuality, out var errors);

            string msg = L("ReplaceComplete", processed);
            if (errors.Count > 0)
                msg += "\n\nErrors:\n" + string.Join("\n", errors);

            EditorUtility.DisplayDialog(L("WindowTitle"), msg, "OK");
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Preset IO
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private string GetPresetFolder()
        {
            string folder = EditorPrefs.GetString(PresetFolderKey, "");
            if (string.IsNullOrEmpty(folder))
            {
                // Find this script's folder
                var guids = AssetDatabase.FindAssets("LODGeneratorWindow t:Script");
                if (guids.Length > 0)
                {
                    string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    folder = Path.GetDirectoryName(scriptPath) + "/Presets";
                }
                else
                {
                    folder = "Assets/you5248/LODGenerator/Editor/Presets";
                }
                EditorPrefs.SetString(PresetFolderKey, folder);
            }
            return folder;
        }

        private void LoadSavedPresets()
        {
            _savedPresets = new List<LODGeneratorPreset>();
            string folder = GetPresetFolder();
            if (!AssetDatabase.IsValidFolder(folder)) return;

            var guids = AssetDatabase.FindAssets("t:LODGeneratorPreset", new[] { folder });
            foreach (var guid in guids)
            {
                var preset = AssetDatabase.LoadAssetAtPath<LODGeneratorPreset>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (preset != null) _savedPresets.Add(preset);
            }
        }

        /// <summary>選択中の保存済みプリセットを上書き保存する。デフォルト選択時は新規保存にフォールバック。</summary>
        private void SaveCurrentPreset()
        {
            if (_selectedPresetIndex <= 0 || _savedPresets == null ||
                _selectedPresetIndex - 1 >= _savedPresets.Count)
            {
                // 上書き対象がない（デフォルト選択）→ 新規保存
                SaveAsNewPreset();
                return;
            }

            var target = _savedPresets[_selectedPresetIndex - 1];
            target.CopyFrom(_currentPreset);
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LOD Generator] {L("PresetUpdated", target.name)}");
        }

        private void SaveAsNewPreset()
        {
            string name = _selectedPresetIndex > 0 && _savedPresets != null &&
                           _selectedPresetIndex - 1 < _savedPresets.Count
                ? _savedPresets[_selectedPresetIndex - 1].name
                : "MyPreset";

            name = EditorInputDialog.Show(L("PresetSaveTitle"), L("PresetSaveName"), name);
            if (string.IsNullOrEmpty(name)) return;

            string folder = GetPresetFolder();
            if (!AssetDatabase.IsValidFolder(folder))
            {
                string parent = Path.GetDirectoryName(folder);
                string folderName = Path.GetFileName(folder);
                AssetDatabase.CreateFolder(parent, folderName);
            }

            string path = folder + "/" + name + ".asset";
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            var newPreset = ScriptableObject.CreateInstance<LODGeneratorPreset>();
            newPreset.CopyFrom(_currentPreset);
            newPreset.name = name;
            AssetDatabase.CreateAsset(newPreset, path);
            AssetDatabase.SaveAssets();

            LoadSavedPresets();

            // Select the newly saved preset
            for (int i = 0; i < _savedPresets.Count; i++)
            {
                if (_savedPresets[i].name == name)
                {
                    _selectedPresetIndex = i + 1;
                    break;
                }
            }
        }

        private void DeletePreset()
        {
            if (_selectedPresetIndex <= 0 || _savedPresets == null) return;
            int idx = _selectedPresetIndex - 1;
            if (idx >= _savedPresets.Count) return;

            var preset = _savedPresets[idx];
            bool confirm = EditorUtility.DisplayDialog(
                L("WindowTitle"),
                L("PresetDeleteConfirm", preset.name),
                "OK", "Cancel");

            if (!confirm) return;

            string path = AssetDatabase.GetAssetPath(preset);
            if (!string.IsNullOrEmpty(path))
                AssetDatabase.DeleteAsset(path);

            _selectedPresetIndex = 0;
            _currentPreset = LODGeneratorPreset.CreateDefault();
            LoadSavedPresets();
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Selection refresh
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void RefreshSelection()
        {
            // Throttle to avoid excessive recalculation
            if (EditorApplication.timeSinceStartup - _lastSelectionCheck < 0.3) return;
            _lastSelectionCheck = EditorApplication.timeSinceStartup;

            var gos = Selection.gameObjects;
            _cachedTargets = LODGeneratorCore.CollectTargets(gos, _includeChildren);
            _cachedObjCount = gos != null ? gos.Length : 0;
            _cachedMeshCount = _cachedTargets != null ? _cachedTargets.Count : 0;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Select Same Mesh
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void SelectAllWithSameMesh()
        {
            // 現在の選択からメッシュを収集
        var targets = _cachedTargets;
            if (targets == null || targets.Count == 0) return;

            var sourceMeshes = new HashSet<Mesh>();
            foreach (var t in targets)
            {
                if (t.sharedMesh != null)
                    sourceMeshes.Add(t.sharedMesh);
            }

            if (sourceMeshes.Count == 0) return;

            // シーン全体から同じメッシュを持つオブジェクトを検索
            var result = new List<GameObject>();

            foreach (var mf in Object.FindObjectsOfType<MeshFilter>(true))
            {
                if (mf.sharedMesh != null && sourceMeshes.Contains(mf.sharedMesh))
                    result.Add(mf.gameObject);
            }
            foreach (var smr in Object.FindObjectsOfType<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh != null && sourceMeshes.Contains(smr.sharedMesh))
                {
                    if (!result.Contains(smr.gameObject))
                        result.Add(smr.gameObject);
                }
            }

            // LOD子オブジェクトを除外
        result.RemoveAll(go => LODGeneratorCore.IsLODChild(go));

            int added = result.Count - _cachedObjCount;
            Selection.objects = result.ToArray();

            Debug.Log($"[LOD Generator] {L("SelectSameMeshResult", result.Count, sourceMeshes.Count, added)}");
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Utility Tab
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void DrawUtilityTab()
        {
            EditorGUILayout.HelpBox(L("UtilityDesc"), MessageType.Info);
            EditorGUILayout.Space(8);

            if (GUILayout.Button(L("UtilityScanButton")))
            {
                _scanResults = LODMeshConsolidator.Scan();
                _groupFoldouts = new bool[_scanResults.Count];
            }

            EditorGUILayout.Space(4);

            if (_scanResults == null)
            {
                EditorGUILayout.HelpBox(L("UtilityNoResults"), MessageType.None);
            }
            else if (_scanResults.Count == 0)
            {
                EditorGUILayout.HelpBox(L("UtilityNoDuplicates"), MessageType.Info);
            }
            else
            {
                int totalDuplicates = 0;
                foreach (var g in _scanResults) totalDuplicates += g.duplicates.Count;

                EditorGUILayout.LabelField(
                    L("UtilityScanSummary", _scanResults.Count, totalDuplicates),
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(4);

                if (_groupFoldouts == null || _groupFoldouts.Length != _scanResults.Count)
                    _groupFoldouts = new bool[_scanResults.Count];

                for (int i = 0; i < _scanResults.Count; i++)
                {
                    var group = _scanResults[i];
                    string header = L("UtilityGroupHeader",
                        group.meshName, group.vertexCount, group.TotalAssetCount);

                    _groupFoldouts[i] = EditorGUILayout.Foldout(_groupFoldouts[i], header, true);

                    if (_groupFoldouts[i])
                    {
                        EditorGUI.indentLevel++;

                        EditorGUILayout.LabelField(
                            L("UtilityKeep", group.keepAssetPath, group.keepRendererCount));

                        foreach (var dup in group.duplicates)
                        {
                            EditorGUILayout.LabelField(
                                L("UtilityRemove", dup.assetPath, dup.renderers.Count));
                        }

                        EditorGUI.indentLevel--;
                    }
                }
            }

            EditorGUILayout.Space(12);
            DrawOrphanSection();
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Orphan / Unused LOD Assets
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private void DrawOrphanSection()
        {
            EditorGUILayout.LabelField(L("UtilitySectionOrphan"), _headerStyle);
            EditorGUILayout.HelpBox(L("UtilityOrphanDesc"), MessageType.Info);
            EditorGUILayout.Space(4);

            if (GUILayout.Button(L("UtilityOrphanScanButton")))
            {
                _orphanResults = LODMeshConsolidator.ScanOrphaned();
            }

            EditorGUILayout.Space(4);

            if (_orphanResults == null)
            {
                EditorGUILayout.HelpBox(L("UtilityOrphanNoResults"), MessageType.None);
            }
            else if (_orphanResults.Count == 0)
            {
                EditorGUILayout.HelpBox(L("UtilityOrphanNoneFound"), MessageType.Info);
            }
            else
            {
                _orphanListExpanded = EditorGUILayout.Foldout(
                    _orphanListExpanded,
                    L("UtilityOrphanSummary", _orphanResults.Count),
                    true);

                if (_orphanListExpanded)
                {
                    EditorGUI.indentLevel++;
                    for (int i = 0; i < _orphanResults.Count; i++)
                        EditorGUILayout.LabelField(_orphanResults[i].assetPath);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(8);

                if (GUILayout.Button(L("UtilityOrphanDeleteButton")))
                {
                    bool confirm = EditorUtility.DisplayDialog(
                        L("WindowTitle"),
                        L("UtilityOrphanDeleteConfirm", _orphanResults.Count),
                        "OK", "Cancel");

                    if (confirm)
                    {
                        int deleted = LODMeshConsolidator.DeleteOrphaned(_orphanResults);
                        EditorUtility.DisplayDialog(
                            L("WindowTitle"),
                            L("UtilityOrphanDeleteComplete", deleted),
                            "OK");
                        _orphanResults = LODMeshConsolidator.ScanOrphaned();
                    }
                }
            }
        }

        private void DrawConsolidateButton()
        {
            bool hasResults = _scanResults != null && _scanResults.Count > 0;
            EditorGUI.BeginDisabledGroup(!hasResults);

            if (GUILayout.Button(L("UtilityConsolidateButton"), _generateButtonStyle))
            {
                int totalDuplicates = 0;
                int totalRenderers = 0;
                foreach (var g in _scanResults)
                {
                    totalDuplicates += g.duplicates.Count;
                    totalRenderers += g.DuplicateRendererCount;
                }

                bool confirm = EditorUtility.DisplayDialog(
                    L("WindowTitle"),
                    L("UtilityConsolidateConfirm", _scanResults.Count, totalRenderers, totalDuplicates),
                    "OK", "Cancel");

                if (confirm)
                {
                    var result = LODMeshConsolidator.Consolidate(_scanResults);
                    EditorUtility.DisplayDialog(
                        L("WindowTitle"),
                        L("UtilityConsolidateComplete", result.refsUpdated, result.assetsDeleted),
                        "OK");

                    _scanResults = LODMeshConsolidator.Scan();
                    _groupFoldouts = new bool[_scanResults.Count];
                    _orphanResults = null;
                }
            }

            EditorGUI.EndDisabledGroup();
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Helpers
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private static string L(string key) => LODGeneratorLocalization.Get(key);
        private static string L(string key, params object[] args) => LODGeneratorLocalization.Get(key, args);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                    //  Simple input dialog (Unity doesn't provide one)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    public class EditorInputDialog : EditorWindow
    {
        private string _message;
        private string _value;
        private bool _confirmed;
        private bool _initialized;
        private static string _result;

        public static string Show(string title, string message, string defaultValue = "")
        {
            _result = null;
            var window = CreateInstance<EditorInputDialog>();
            window.titleContent = new GUIContent(title);
            window._message = message;
            window._value = defaultValue;
            window._confirmed = false;
            window.minSize = new Vector2(300, 100);
            window.maxSize = new Vector2(400, 100);
            window.ShowModal();
            return _result;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(_message);

            GUI.SetNextControlName("InputField");
            _value = EditorGUILayout.TextField(_value);

            if (!_initialized)
            {
                EditorGUI.FocusTextInControl("InputField");
                _initialized = true;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("OK", GUILayout.Width(80)) ||
                (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return))
            {
                _result = _value;
                Close();
            }

            if (GUILayout.Button("Cancel", GUILayout.Width(80)) ||
                (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape))
            {
                _result = null;
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}

