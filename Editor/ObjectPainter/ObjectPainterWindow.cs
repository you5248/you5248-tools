// ObjectPainterWindow.cs
// Unity 2022.3 compatible.
//
// you5248 > Object Painter
//
// SceneView object-painting tool inspired by NOT_Lonely's Object Placement Tool.
// Modes:
//   - Scatter : brush-paint many prefabs at once (density + radius)
//   - Path    : click to place a fence/line, each object faces the path direction
//   - Physics : drop a bunch and let an edit-time physics sim settle them
//   - Erase   : remove painted objects under the brush
// Plus: align-to-surface + random rotation/scale, layer/surface mask filtering,
// grouping under a parent, and per-material mesh combining for draw-call cleanup.
//
// Painting raycasts against scene Colliders filtered by the surface mask, so the
// target surface needs a collider. Every stroke is one Undo step.

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace you5248.ObjectPainter
{
    public class ObjectPainterWindow : EditorWindow
    {
        enum Language { Japanese, English }
        enum ToolMode { Scatter, Path, Physics, Erase }

        [SerializeField] Language _lang = Language.Japanese;
        [SerializeField] ToolMode _mode = ToolMode.Scatter;
        [SerializeField] PaintSettings _s = new PaintSettings();
        [SerializeField] bool _painting = false;

        // Foldouts
        [SerializeField] bool _foldPalette = true;
        [SerializeField] bool _foldCommon = true;
        [SerializeField] bool _foldMode = true;
        [SerializeField] bool _foldGroup = true;
        [SerializeField] bool _foldCombine = false;

        Vector2 _scroll;

        // Session / stroke state (not serialized — reset on reload, that's fine)
        readonly List<GameObject> _session = new List<GameObject>();
        readonly List<Vector3> _taken = new List<Vector3>();
        Vector3 _lastStrokePos;
        bool _strokeActive;
        Vector3 _lastPathPoint;
        bool _hasLastPath;

        string JP(string ja, string en) => _lang == Language.Japanese ? ja : en;

        [MenuItem("you5248/Object Painter")]
        static void Open()
        {
            var w = GetWindow<ObjectPainterWindow>();
            w.titleContent = new GUIContent("Object Painter");
            w.minSize = new Vector2(340, 560);
        }

        void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;
        void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

        // ============================================================ Window UI

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawHeader();
            EditorGUILayout.Space(4);
            DrawPaintToggle();
            EditorGUILayout.Space(6);
            DrawModeBar();
            EditorGUILayout.Space(4);
            DrawPalette();
            EditorGUILayout.Space(2);
            DrawCommon();
            EditorGUILayout.Space(2);
            DrawModeOptions();
            EditorGUILayout.Space(2);
            DrawGroup();
            EditorGUILayout.Space(2);
            DrawCombine();
            EditorGUILayout.Space(8);
            DrawFooter();

            EditorGUILayout.EndScrollView();
        }

        void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(JP("オブジェクトペインター", "Object Painter"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            _lang = (Language)EditorGUILayout.EnumPopup(_lang, GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();
        }

        void DrawPaintToggle()
        {
            var bg = GUI.backgroundColor;
            GUI.backgroundColor = _painting ? new Color(0.4f, 0.85f, 0.45f) : bg;
            string label = _painting
                ? JP("● ペイント中（クリックで配置／停止はこのボタン）", "● Painting (click in Scene / press to stop)")
                : JP("○ ペイント開始", "○ Start Painting");
            if (GUILayout.Button(label, GUILayout.Height(30)))
            {
                _painting = !_painting;
                _hasLastPath = false;
                if (_painting) SceneView.lastActiveSceneView?.Focus();
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = bg;

            if (_painting)
                EditorGUILayout.HelpBox(
                    JP("Sceneビューで左ドラッグ＝配置。Altでカメラ回転。Escでペイント終了。",
                       "Left-drag in Scene to paint. Alt orbits the camera. Esc stops painting."),
                    MessageType.Info);
        }

        void DrawModeBar()
        {
            string[] names =
            {
                JP("スキャッター", "Scatter"),
                JP("パス/フェンス", "Path"),
                JP("物理ドロップ", "Physics"),
                JP("消しゴム", "Erase"),
            };
            int sel = GUILayout.Toolbar((int)_mode, names);
            if (sel != (int)_mode) { _mode = (ToolMode)sel; _hasLastPath = false; }
        }

        void DrawPalette()
        {
            _foldPalette = Foldout(_foldPalette, JP("パレット（配置するPrefab）", "Palette (prefabs to place)"));
            if (!_foldPalette) return;

            using (new EditorGUI.IndentLevelScope())
            {
                int removeAt = -1;
                for (int i = 0; i < _s.prefabs.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    _s.prefabs[i] = (GameObject)EditorGUILayout.ObjectField(_s.prefabs[i], typeof(GameObject), true);
                    if (GUILayout.Button("✕", GUILayout.Width(24))) removeAt = i;
                    EditorGUILayout.EndHorizontal();
                }
                if (removeAt >= 0) _s.prefabs.RemoveAt(removeAt);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(JP("＋ 追加", "+ Add"))) _s.prefabs.Add(null);
                if (GUILayout.Button(JP("選択中を取込", "Use Selection")))
                {
                    var sel = Selection.gameObjects;
                    if (sel != null && sel.Length > 0)
                    {
                        _s.prefabs.Clear();
                        _s.prefabs.AddRange(sel);
                    }
                }
                if (GUILayout.Button(JP("クリア", "Clear"))) _s.prefabs.Clear();
                EditorGUILayout.EndHorizontal();

                // Drag & drop area
                var rect = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
                GUI.Box(rect, JP("ここにPrefabをドラッグ&ドロップ", "Drag & drop prefabs here"), EditorStyles.helpBox);
                HandlePaletteDrop(rect);

                int valid = _s.prefabs.Count(p => p != null);
                if (valid == 0)
                    EditorGUILayout.HelpBox(JP("Prefabを1つ以上追加してください。", "Add at least one prefab."), MessageType.Warning);
            }
        }

        void HandlePaletteDrop(Rect rect)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;
            if (e.type == EventType.DragUpdated) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; e.Use(); }
            else if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences)
                    if (obj is GameObject go && !_s.prefabs.Contains(go)) _s.prefabs.Add(go);
                e.Use();
            }
        }

        void DrawCommon()
        {
            _foldCommon = Foldout(_foldCommon, JP("共通設定（向き・スケール・面）", "Common (orientation / scale / surface)"));
            if (!_foldCommon) return;

            using (new EditorGUI.IndentLevelScope())
            {
                _s.surfaceMask = LayerMaskField(JP("配置可能なレイヤー", "Surface layers"), _s.surfaceMask);

                _s.alignToSurface = EditorGUILayout.Toggle(JP("面の法線に整列", "Align to surface"), _s.alignToSurface);
                using (new EditorGUI.DisabledScope(!_s.alignToSurface))
                    _s.alignBlend = EditorGUILayout.Slider(JP("整列ブレンド", "Align blend"), _s.alignBlend, 0f, 1f);

                _s.randomYaw = EditorGUILayout.Toggle(JP("Y軸ランダム回転", "Random Y rotation"), _s.randomYaw);
                _s.randomTilt = EditorGUILayout.Toggle(JP("ランダム傾き", "Random tilt"), _s.randomTilt);
                using (new EditorGUI.DisabledScope(!_s.randomTilt))
                    _s.maxTilt = EditorGUILayout.Slider(JP("最大傾き(度)", "Max tilt (deg)"), _s.maxTilt, 0f, 45f);

                EditorGUILayout.LabelField(JP("ランダムスケール (min/max)", "Random scale (min/max)"));
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.BeginHorizontal();
                    float mn = EditorGUILayout.FloatField(_s.scaleRange.x);
                    float mx = EditorGUILayout.FloatField(_s.scaleRange.y);
                    EditorGUILayout.EndHorizontal();
                    _s.scaleRange = new Vector2(Mathf.Max(0.01f, mn), Mathf.Max(0.01f, mx));
                }

                _s.surfaceOffset = EditorGUILayout.FloatField(JP("面からのオフセット", "Surface offset"), _s.surfaceOffset);
            }
        }

        void DrawModeOptions()
        {
            _foldMode = Foldout(_foldMode, JP("モード別設定", "Mode options"));
            if (!_foldMode) return;

            using (new EditorGUI.IndentLevelScope())
            {
                switch (_mode)
                {
                    case ToolMode.Scatter:
                        _s.brushRadius = EditorGUILayout.Slider(JP("ブラシ半径", "Brush radius"), _s.brushRadius, 0.1f, 50f);
                        _s.countPerStroke = EditorGUILayout.IntSlider(JP("1ストロークの個数", "Count per stroke"), _s.countPerStroke, 1, 100);
                        _s.minSpacing = EditorGUILayout.FloatField(JP("最小間隔(0=重なり許可)", "Min spacing (0=overlap)"), Mathf.Max(0f, _s.minSpacing));
                        _s.strokeSpacing = EditorGUILayout.Slider(JP("ドラッグ間隔", "Drag spacing"), _s.strokeSpacing, 0.05f, 20f);
                        break;

                    case ToolMode.Path:
                        EditorGUILayout.HelpBox(JP("クリックで頂点を打つと、直前の点との向きに沿って配置します。Escでパスをリセット。",
                                                   "Click to drop points; objects face along the path. Esc resets the path."), MessageType.None);
                        _s.pathFillGaps = EditorGUILayout.Toggle(JP("間隔を埋める", "Fill gaps"), _s.pathFillGaps);
                        using (new EditorGUI.DisabledScope(!_s.pathFillGaps))
                            _s.pathSpacing = EditorGUILayout.Slider(JP("配置間隔", "Spacing"), _s.pathSpacing, 0.1f, 20f);
                        break;

                    case ToolMode.Physics:
                        _s.brushRadius = EditorGUILayout.Slider(JP("ブラシ半径", "Brush radius"), _s.brushRadius, 0.1f, 50f);
                        _s.countPerStroke = EditorGUILayout.IntSlider(JP("1クリックの個数", "Count per click"), _s.countPerStroke, 1, 60);
                        _s.dropHeight = EditorGUILayout.Slider(JP("落下高さ", "Drop height"), _s.dropHeight, 0.1f, 20f);
                        _s.simSteps = EditorGUILayout.IntSlider(JP("シミュレーションステップ", "Sim steps"), _s.simSteps, 10, 600);
                        _s.keepRigidbody = EditorGUILayout.Toggle(JP("Rigidbodyを残す", "Keep Rigidbody"), _s.keepRigidbody);
                        EditorGUILayout.HelpBox(JP("コライダー未設定のものには一時コライダーを付けて落下させます（デスクトップ検証）。",
                                                   "Temp colliders are added to collider-less objects for the drop (desktop-tested)."), MessageType.None);
                        break;

                    case ToolMode.Erase:
                        _s.brushRadius = EditorGUILayout.Slider(JP("ブラシ半径", "Brush radius"), _s.brushRadius, 0.1f, 50f);
                        EditorGUILayout.HelpBox(_s.groupUnderParent && _s.parentGroup != null
                            ? JP($"親グループ「{_s.parentGroup.name}」の中だけを消去します。", $"Erases only inside parent '{_s.parentGroup.name}'.")
                            : JP("親グループ未設定のため、このセッションで配置したものだけ消去します。",
                                 "No parent group set — erases only objects placed in this session."), MessageType.None);
                        break;
                }
            }
        }

        void DrawGroup()
        {
            _foldGroup = Foldout(_foldGroup, JP("グループ化", "Grouping"));
            if (!_foldGroup) return;

            using (new EditorGUI.IndentLevelScope())
            {
                _s.groupUnderParent = EditorGUILayout.Toggle(JP("親オブジェクトにまとめる", "Group under parent"), _s.groupUnderParent);
                using (new EditorGUI.DisabledScope(!_s.groupUnderParent))
                {
                    _s.parentGroup = (Transform)EditorGUILayout.ObjectField(JP("親グループ", "Parent group"), _s.parentGroup, typeof(Transform), true);
                    _s.autoParentName = EditorGUILayout.TextField(JP("自動生成名", "Auto-create name"), _s.autoParentName);
                }
            }
        }

        void DrawCombine()
        {
            _foldCombine = Foldout(_foldCombine, JP("メッシュ結合（ドローコール削減）", "Combine meshes (draw-call cleanup)"));
            if (!_foldCombine) return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.BeginHorizontal();
                _s.combineSaveFolder = EditorGUILayout.TextField(JP("保存先", "Save folder"), _s.combineSaveFolder);
                if (GUILayout.Button("...", GUILayout.Width(28)))
                {
                    string abs = EditorUtility.OpenFolderPanel(JP("保存先フォルダ", "Save folder"), Application.dataPath, "");
                    if (!string.IsNullOrEmpty(abs)) _s.combineSaveFolder = ToAssetPath(abs);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.HelpBox(JP("親グループ内で同一マテリアルを共有する単一マテリアルのメッシュを1つに結合します。複数マテリアル/サブメッシュはスキップ。",
                                           "Combines single-material meshes sharing one material inside the parent group. Multi-material/submesh meshes are skipped."), MessageType.None);

                using (new EditorGUI.DisabledScope(_s.parentGroup == null))
                    if (GUILayout.Button(JP("親グループ内のメッシュを結合", "Combine meshes in parent group")))
                    {
                        Undo.IncrementCurrentGroup();
                        int n = ObjectPainterCore.CombineGroup(_s, _s.parentGroup, out string msg);
                        ShowNotification(new GUIContent(msg));
                        Debug.Log("[ObjectPainter] " + msg);
                    }
                if (_s.parentGroup == null)
                    EditorGUILayout.HelpBox(JP("親グループを設定してください。", "Assign a parent group first."), MessageType.Warning);
            }
        }

        void DrawFooter()
        {
            EditorGUILayout.LabelField(JP("セッション配置数: ", "Placed this session: ") + _session.Count(g => g != null),
                                       EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(JP("配置面にはコライダーが必要です。各ストロークはUndo(Ctrl+Z)1回で戻せます。",
                                       "Target surfaces need colliders. Each stroke is a single Undo (Ctrl+Z)."), MessageType.None);
        }

        // ============================================================ SceneView painting

        void OnSceneGUI(SceneView sv)
        {
            if (!_painting) return;
            var e = Event.current;

            // Esc stops / resets path
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                if (_mode == ToolMode.Path && _hasLastPath) { _hasLastPath = false; }
                else _painting = false;
                e.Use();
                Repaint();
                return;
            }

            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            // トリガー（足音エリア・PPSゾーン等）は塗り面として拾わない（物理と面を一致させる）
            bool hasHit = Physics.Raycast(ray, out RaycastHit hit, 5000f, _s.surfaceMask, QueryTriggerInteraction.Ignore);

            if (hasHit) DrawBrushGizmo(hit.point, hit.normal);
            sv.Repaint();

            bool paintBtn = e.button == 0 && !e.alt;
            switch (e.type)
            {
                case EventType.MouseDown when paintBtn:
                    if (hasHit)
                    {
                        BeginStroke();
                        ApplyAt(hit);
                        _lastStrokePos = hit.point;
                    }
                    e.Use();
                    break;

                case EventType.MouseDrag when paintBtn && _strokeActive:
                    if (hasHit && _mode != ToolMode.Path)
                    {
                        if ((hit.point - _lastStrokePos).magnitude >= _s.strokeSpacing)
                        {
                            ApplyAt(hit);
                            _lastStrokePos = hit.point;
                        }
                    }
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0:
                    EndStroke();
                    e.Use();
                    break;
            }
        }

        void DrawBrushGizmo(Vector3 point, Vector3 normal)
        {
            Color c = _mode == ToolMode.Erase ? new Color(1f, 0.3f, 0.3f) : new Color(0.3f, 0.8f, 1f);
            Handles.color = c;
            if (_mode == ToolMode.Path)
            {
                Handles.DrawSolidDisc(point, normal, 0.15f);
                if (_hasLastPath)
                {
                    Handles.DrawDottedLine(_lastPathPoint, point, 4f);
                    Handles.DrawSolidDisc(_lastPathPoint, Vector3.up, 0.1f);
                }
            }
            else
            {
                Handles.DrawWireDisc(point, normal, _s.brushRadius);
                Handles.color = new Color(c.r, c.g, c.b, 0.08f);
                Handles.DrawSolidDisc(point, normal, _s.brushRadius);
            }
        }

        void BeginStroke()
        {
            _strokeActive = true;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Object Painter Stroke");
        }

        void EndStroke()
        {
            if (_strokeActive) Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            _strokeActive = false;
        }

        void ApplyAt(RaycastHit hit)
        {
            Transform parent = EnsureParent();
            switch (_mode)
            {
                case ToolMode.Scatter:
                    Track(ObjectPainterCore.Scatter(_s, hit.point, hit.normal, parent, _taken));
                    break;
                case ToolMode.Physics:
                    Track(ObjectPainterCore.PhysicsDrop(_s, hit.point, hit.normal, parent, _taken));
                    break;
                case ToolMode.Path:
                    ApplyPath(hit, parent);
                    break;
                case ToolMode.Erase:
                    Transform scope = (_s.groupUnderParent && _s.parentGroup != null) ? _s.parentGroup : null;
                    ObjectPainterCore.EraseAround(scope, scope == null ? _session : null, hit.point, _s.brushRadius);
                    break;
            }
        }

        void ApplyPath(RaycastHit hit, Transform parent)
        {
            if (!_hasLastPath)
            {
                Track(ObjectPainterCore.PlaceScatterAt(_s, hit.point, hit.normal, parent));
                _lastPathPoint = hit.point;
                _hasLastPath = true;
                return;
            }

            Vector3 from = _lastPathPoint, to = hit.point;
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 1e-3f) return;

            if (_s.pathFillGaps && _s.pathSpacing > 0.01f)
            {
                int n = Mathf.FloorToInt(dist / _s.pathSpacing);
                for (int i = 1; i <= n; i++)
                {
                    Vector3 p = Vector3.Lerp(from, to, (_s.pathSpacing * i) / dist);
                    if (ObjectPainterCore.ProjectToSurface(_s, p, hit.normal, out Vector3 sp, out Vector3 sn))
                        Track(ObjectPainterCore.PlacePathAt(_s, sp, sn, dir, parent));
                }
                _lastPathPoint = (n > 0) ? from + dir.normalized * (_s.pathSpacing * n) : from;
            }
            else
            {
                Track(ObjectPainterCore.PlacePathAt(_s, to, hit.normal, dir, parent));
                _lastPathPoint = to;
            }
        }

        Transform EnsureParent()
        {
            if (!_s.groupUnderParent) return null;
            if (_s.parentGroup != null) return _s.parentGroup;
            var p = new GameObject(string.IsNullOrEmpty(_s.autoParentName) ? "PaintedObjects" : _s.autoParentName);
            Undo.RegisterCreatedObjectUndo(p, "Object Painter Parent");
            _s.parentGroup = p.transform;
            return p.transform;
        }

        void Track(GameObject go) { if (go != null) _session.Add(go); }
        void Track(List<GameObject> list) { if (list != null) _session.AddRange(list.Where(g => g != null)); }

        // ============================================================ helpers

        static bool Foldout(bool state, string label)
            => EditorGUILayout.Foldout(state, label, true, EditorStyles.foldoutHeader);

        static string ToAssetPath(string absolute)
        {
            absolute = absolute.Replace('\\', '/');
            string dataPath = Application.dataPath.Replace('\\', '/');
            if (absolute.StartsWith(dataPath)) return "Assets" + absolute.Substring(dataPath.Length);
            return absolute; // outside the project; user will see it's invalid
        }

        // Standard LayerMask <-> MaskField conversion (skips empty layer slots).
        static LayerMask LayerMaskField(string label, LayerMask selected)
        {
            string[] layers = InternalEditorUtility.layers;
            int shown = 0;
            for (int i = 0; i < layers.Length; i++)
                if ((selected.value & (1 << LayerMask.NameToLayer(layers[i]))) != 0) shown |= (1 << i);

            shown = EditorGUILayout.MaskField(label, shown, layers);

            int mask = 0;
            for (int i = 0; i < layers.Length; i++)
                if ((shown & (1 << i)) != 0) mask |= (1 << LayerMask.NameToLayer(layers[i]));
            selected.value = mask;
            return selected;
        }
    }
}
