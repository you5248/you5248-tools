#if UNITY_EDITOR && BAKERY_INCLUDED
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace you5248.Editor.BakeryConverter
{
    public class BakeryConverterWindow : EditorWindow
    {
        private enum Scope { Selection, Scene }

        // ----- 状態 -----
        [SerializeField] private bool _isJapanese = true;
        [SerializeField] private Scope _scope = Scope.Selection;
        [SerializeField] private bool _dryRun = false;
        [SerializeField] private bool _keepUnityLightEnabled = false;
        [SerializeField] private bool _removeAreaMeshOnReverse = false;
        [SerializeField] private bool _includeRealtime = false;
        [SerializeField] private bool _showSkippedInPreview = true;

        // Directional 既定値
        [SerializeField] private float _dirShadowSpread = 0.01f;
        [SerializeField] private int _dirSamples = 16;
        [SerializeField] private bool _dirBakeToIndirect = true;
        [SerializeField] private float _dirIndirectIntensity = 1f;
        [SerializeField] private bool _dirShadowmask = false;

        // Point / Spot 既定値
        [SerializeField] private float _pointShadowSpread = 0.05f;
        [SerializeField] private int _pointSamples = 8;
        [SerializeField] private bool _pointRealisticFalloff = false;
        [SerializeField] private bool _pointBakeToIndirect = true;
        [SerializeField] private float _pointIndirectIntensity = 1f;
        [SerializeField] private bool _pointShadowmask = false;

        // Area (LightMesh) 既定値
        [SerializeField] private int _meshSamples = 256;
        [SerializeField] private bool _meshSelfShadow = true;
        [SerializeField] private bool _meshBakeToIndirect = true;
        [SerializeField] private float _meshIndirectIntensity = 1f;

        // 折りたたみ
        [SerializeField] private bool _foldDir = true;
        [SerializeField] private bool _foldPoint = true;
        [SerializeField] private bool _foldMesh = false;

        // 生成メッシュの判定マーカー
        private const string GeneratedDiscMeshName = "BakeryConv_Disc";

        private enum LightStatus { Ready, AlreadyConverted, RealtimeExcluded, UnsupportedType }
        private struct LightRow { public Light light; public LightStatus status; }

        private List<LightRow> _allRows = new List<LightRow>();
        private List<Component> _reversePreview = new List<Component>();
        private Vector2 _scroll;

        // ----- メニュー -----
        [MenuItem("you5248/Bakery Converter")]
        public static void Open()
        {
            var w = GetWindow<BakeryConverterWindow>();
            w.titleContent = new GUIContent("Bakery Converter");
            w.minSize = new Vector2(380, 520);
        }

        private void OnEnable() { Refresh(); }
        private void OnSelectionChange() { if (_scope == Scope.Selection) { Refresh(); Repaint(); } }

        // ----- ローカライズ -----
        private string T(string en, string jp) => _isJapanese ? jp : en;

        // ----- GUI -----
        private void OnGUI()
        {
            DrawHeader();
            EditorGUILayout.Space();
            DrawScopeAndOptions();
            EditorGUILayout.Space();
            DrawDefaults();
            EditorGUILayout.Space();
            DrawPreview();
            EditorGUILayout.Space();
            DrawButtons();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Unity Light  ⇄  Bakery Light", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(_isJapanese ? "EN" : "JP", EditorStyles.miniButton, GUILayout.Width(40)))
                    _isJapanese = !_isJapanese;
            }
            EditorGUILayout.HelpBox(
                T("Adds Bakery components to the SAME GameObject. Already-converted lights are auto-skipped.",
                  "同じGameObjectにBakeryコンポーネントを追加します。変換済みのLightは自動的にスキップされます。"),
                MessageType.None);
        }

        private void DrawScopeAndOptions()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUI.BeginChangeCheck();
                _scope = (Scope)EditorGUILayout.EnumPopup(T("Scope", "対象"), _scope);
                if (EditorGUI.EndChangeCheck()) Refresh();

                _dryRun = EditorGUILayout.Toggle(T("Dry Run (no changes)", "DryRun (変更なし)"), _dryRun);
                _keepUnityLightEnabled = EditorGUILayout.Toggle(
                    T("Keep Unity Light enabled (preview)",
                      "Unity Lightを有効のまま残す (プレビュー用)"),
                    _keepUnityLightEnabled);
                _removeAreaMeshOnReverse = EditorGUILayout.Toggle(
                    T("Remove generated mesh on Reverse",
                      "Reverse時に生成メッシュ(Quad/Disc)を削除"),
                    _removeAreaMeshOnReverse);

                EditorGUI.BeginChangeCheck();
                _includeRealtime = EditorGUILayout.Toggle(
                    T("Include Realtime lights",
                      "Realtimeも対象に含める"),
                    _includeRealtime);
                _showSkippedInPreview = EditorGUILayout.Toggle(
                    T("Show skipped in preview (diagnostic)",
                      "スキップ理由をプレビューに表示 (診断)"),
                    _showSkippedInPreview);
                if (EditorGUI.EndChangeCheck()) Refresh();
            }
        }

        private void DrawDefaults()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField(T("Bake defaults (applied at conversion)", "ベイク既定値 (変換時に適用)"), EditorStyles.boldLabel);

                _foldDir = EditorGUILayout.Foldout(_foldDir, "Directional", true);
                if (_foldDir)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        _dirShadowSpread = EditorGUILayout.Slider("Shadow Spread", _dirShadowSpread, 0f, 0.5f);
                        _dirSamples = EditorGUILayout.IntSlider("Samples", _dirSamples, 1, 128);
                        _dirBakeToIndirect = EditorGUILayout.Toggle("Bake To Indirect", _dirBakeToIndirect);
                        _dirIndirectIntensity = EditorGUILayout.Slider("Indirect Intensity", _dirIndirectIntensity, 0f, 4f);
                        _dirShadowmask = EditorGUILayout.Toggle("Shadowmask", _dirShadowmask);
                    }
                }

                _foldPoint = EditorGUILayout.Foldout(_foldPoint, "Point / Spot", true);
                if (_foldPoint)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        _pointShadowSpread = EditorGUILayout.Slider("Shadow Spread", _pointShadowSpread, 0f, 0.5f);
                        _pointSamples = EditorGUILayout.IntSlider("Samples", _pointSamples, 1, 128);
                        _pointRealisticFalloff = EditorGUILayout.Toggle("Realistic Falloff", _pointRealisticFalloff);
                        _pointBakeToIndirect = EditorGUILayout.Toggle("Bake To Indirect", _pointBakeToIndirect);
                        _pointIndirectIntensity = EditorGUILayout.Slider("Indirect Intensity", _pointIndirectIntensity, 0f, 4f);
                        _pointShadowmask = EditorGUILayout.Toggle("Shadowmask", _pointShadowmask);
                    }
                }

                _foldMesh = EditorGUILayout.Foldout(_foldMesh, "Area (LightMesh)", true);
                if (_foldMesh)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        _meshSamples = EditorGUILayout.IntSlider("Samples", _meshSamples, 16, 1024);
                        _meshSelfShadow = EditorGUILayout.Toggle("Self Shadow", _meshSelfShadow);
                        _meshBakeToIndirect = EditorGUILayout.Toggle("Bake To Indirect", _meshBakeToIndirect);
                        _meshIndirectIntensity = EditorGUILayout.Slider("Indirect Intensity", _meshIndirectIntensity, 0f, 4f);
                    }
                }
            }
        }

        private void DrawPreview()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                int readyCount = _allRows.Count(r => r.status == LightStatus.Ready);
                EditorGUILayout.LabelField(
                    T($"Preview — Convert: {readyCount} / {_allRows.Count}, Reverse: {_reversePreview.Count}",
                      $"プレビュー — 変換対象: {readyCount} / {_allRows.Count}, 逆変換対象: {_reversePreview.Count}"),
                    EditorStyles.boldLabel);

                using (var sv = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.MinHeight(140), GUILayout.MaxHeight(260)))
                {
                    _scroll = sv.scrollPosition;

                    if (_allRows.Count > 0)
                    {
                        EditorGUILayout.LabelField("→ Bakery", EditorStyles.miniBoldLabel);
                        foreach (var row in _allRows)
                        {
                            if (row.light == null) continue;
                            if (!_showSkippedInPreview && row.status != LightStatus.Ready) continue;
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                EditorGUILayout.ObjectField(row.light, typeof(Light), true);
                                EditorGUILayout.LabelField($"{row.light.type} / {row.light.lightmapBakeType}", GUILayout.Width(150));
                                var statusLabel = StatusLabel(row.status);
                                var prev = GUI.color;
                                GUI.color = row.status == LightStatus.Ready ? Color.green : new Color(1f, 0.7f, 0.4f);
                                EditorGUILayout.LabelField(statusLabel, GUILayout.Width(110));
                                GUI.color = prev;
                            }
                        }
                    }

                    if (_reversePreview.Count > 0)
                    {
                        EditorGUILayout.LabelField("← Unity Light", EditorStyles.miniBoldLabel);
                        foreach (var c in _reversePreview)
                        {
                            if (c == null) continue;
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                EditorGUILayout.ObjectField(c, typeof(Component), true);
                                EditorGUILayout.LabelField(c.GetType().Name, GUILayout.Width(150));
                            }
                        }
                    }

                    if (_allRows.Count == 0 && _reversePreview.Count == 0)
                        EditorGUILayout.HelpBox(T("Nothing in scope.", "対象がありません。"), MessageType.Info);
                }
            }
        }

        private string StatusLabel(LightStatus s)
        {
            switch (s)
            {
                case LightStatus.Ready: return T("Ready", "Ready");
                case LightStatus.AlreadyConverted: return T("Already", "変換済み");
                case LightStatus.RealtimeExcluded: return T("Realtime", "Realtime除外");
                case LightStatus.UnsupportedType: return T("Unsupported", "非対応");
                default: return "?";
            }
        }

        private void DrawButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(T("Refresh", "更新"))) Refresh();
                int readyCount = _allRows.Count(r => r.status == LightStatus.Ready);
                using (new EditorGUI.DisabledScope(readyCount == 0))
                    if (GUILayout.Button(T("Convert → Bakery", "Bakery化"))) DoConvert();
                using (new EditorGUI.DisabledScope(_reversePreview.Count == 0))
                    if (GUILayout.Button(T("Reverse → Unity", "Unityに戻す"))) DoReverse();
            }
        }

        // ----- 列挙 -----
        private void Refresh()
        {
            _allRows = EnumerateLights()
                .Where(l => l != null)
                .Select(l => new LightRow { light = l, status = ClassifyLight(l) })
                .ToList();
            _reversePreview = EnumerateBakeryComponents().ToList();
        }

        private IEnumerable<Light> EnumerateLights()
        {
            if (_scope == Scope.Selection)
            {
                var roots = Selection.gameObjects;
                if (roots == null) yield break;
                foreach (var g in roots)
                    foreach (var l in g.GetComponentsInChildren<Light>(true))
                        yield return l;
            }
            else
            {
                foreach (var l in Object.FindObjectsOfType<Light>(true))
                    yield return l;
            }
        }

        private IEnumerable<Component> EnumerateBakeryComponents()
        {
            IEnumerable<GameObject> source;
            if (_scope == Scope.Selection)
            {
                if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
                    return System.Linq.Enumerable.Empty<Component>();
                source = Selection.gameObjects
                    .SelectMany(g => g.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject));
            }
            else
            {
                source = Object.FindObjectsOfType<GameObject>(true);
            }

            var result = new List<Component>();
            foreach (var go in source.Distinct())
            {
                var d = go.GetComponent<BakeryDirectLight>(); if (d != null) result.Add(d);
                var p = go.GetComponent<BakeryPointLight>(); if (p != null) result.Add(p);
                var m = go.GetComponent<BakeryLightMesh>(); if (m != null) result.Add(m);
            }
            return result;
        }

        // 各Lightの状態を分類 (Ready / 既変換 / Realtime除外 / 非対応)
        private LightStatus ClassifyLight(Light l)
        {
            var go = l.gameObject;
            bool alreadyConverted;
            switch (l.type)
            {
                case LightType.Directional:
                    alreadyConverted = go.GetComponent<BakeryDirectLight>() != null; break;
                case LightType.Point:
                case LightType.Spot:
                    alreadyConverted = go.GetComponent<BakeryPointLight>() != null; break;
#if UNITY_2020_1_OR_NEWER
                case LightType.Rectangle:
                case LightType.Disc:
#else
                case LightType.Area:
#endif
                    alreadyConverted = go.GetComponent<BakeryLightMesh>() != null; break;
                default:
                    return LightStatus.UnsupportedType;
            }
            if (alreadyConverted) return LightStatus.AlreadyConverted;
            if (l.lightmapBakeType == LightmapBakeType.Realtime && !_includeRealtime) return LightStatus.RealtimeExcluded;
            return LightStatus.Ready;
        }

        // ----- 変換 (同じGOにadd) -----
        private void DoConvert()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Convert Unity Light → Bakery");
            int undoGroup = Undo.GetCurrentGroup();

            int ok = 0, skip = 0, fail = 0;

            var targets = _allRows.Where(r => r.status == LightStatus.Ready).Select(r => r.light).ToList();
            foreach (var uLight in targets)
            {
                if (uLight == null) { skip++; continue; }
                try
                {
                    if (_dryRun) { ok++; continue; }

                    var go = uLight.gameObject;

                    switch (uLight.type)
                    {
                        case LightType.Directional: ConvertDirectional(go, uLight); break;
                        case LightType.Point: ConvertPoint(go, uLight); break;
                        case LightType.Spot: ConvertSpot(go, uLight); break;
#if UNITY_2020_1_OR_NEWER
                        case LightType.Rectangle: ConvertArea(go, uLight, isDisc: false); break;
                        case LightType.Disc: ConvertArea(go, uLight, isDisc: true); break;
#else
                        case LightType.Area: ConvertArea(go, uLight, isDisc: false); break;
#endif
                        default: skip++; continue;
                    }

                    if (!_keepUnityLightEnabled)
                    {
                        Undo.RecordObject(uLight, "Disable Unity Light");
                        uLight.enabled = false;
                    }

                    EditorSceneManager.MarkSceneDirty(go.scene);
                    ok++;
                }
                catch (System.Exception ex)
                {
                    fail++;
                    Debug.LogError($"[BakeryConverter] Convert failed: {uLight?.name}\n{ex}");
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[BakeryConverter] Convert: ok={ok}, skip={skip}, fail={fail}, dryRun={_dryRun}");
            Refresh();
        }

        private void ConvertDirectional(GameObject go, Light uLight)
        {
            var b = Undo.AddComponent<BakeryDirectLight>(go);
            b.color = uLight.color;
            b.intensity = uLight.intensity;
            b.shadowSpread = _dirShadowSpread;
            b.samples = _dirSamples;
            b.bakeToIndirect = _dirBakeToIndirect;
            b.indirectIntensity = _dirIndirectIntensity;
            b.shadowmask = _dirShadowmask;
            b.bitmask = 1;
        }

        private void ConvertPoint(GameObject go, Light uLight)
        {
            var b = Undo.AddComponent<BakeryPointLight>(go);
            ApplyPointCommon(b, uLight);
            b.projMode = BakeryPointLight.ftLightProjectionMode.Omni;
            b.cookie = null;
        }

        private void ConvertSpot(GameObject go, Light uLight)
        {
            var b = Undo.AddComponent<BakeryPointLight>(go);
            ApplyPointCommon(b, uLight);
            b.projMode = BakeryPointLight.ftLightProjectionMode.Cone;
            b.angle = uLight.spotAngle;
#if UNITY_2020_1_OR_NEWER
            b.innerAngle = uLight.innerSpotAngle;
#else
            b.innerAngle = uLight.spotAngle * 0.7f;
#endif
            b.cookie = null;
        }

        private void ApplyPointCommon(BakeryPointLight b, Light uLight)
        {
            b.color = uLight.color;
            b.intensity = uLight.intensity;
            b.cutoff = uLight.range;
            b.shadowSpread = _pointShadowSpread;
            b.samples = _pointSamples;
            b.realisticFalloff = _pointRealisticFalloff;
            b.bakeToIndirect = _pointBakeToIndirect;
            b.indirectIntensity = _pointIndirectIntensity;
            b.shadowmask = _pointShadowmask;
            b.bitmask = 1;
        }

        private void ConvertArea(GameObject go, Light uLight, bool isDisc)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = Undo.AddComponent<MeshFilter>(go);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = Undo.AddComponent<MeshRenderer>(go);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            if (isDisc)
            {
                Undo.RecordObject(mf, "Set Disc Mesh");
                mf.sharedMesh = BuildDiscMesh(uLight.areaSize.x > 0 ? uLight.areaSize.x : 0.5f);
                Debug.LogWarning($"[BakeryConverter] Disc area light: tessellated disc mesh generated for {uLight.name}. Verify scale.");
            }
            else
            {
                Undo.RecordObject(mf, "Set Quad Mesh");
                mf.sharedMesh = GetBuiltinQuadMesh();
                Undo.RecordObject(go.transform, "Scale for area size");
                go.transform.localScale = new Vector3(uLight.areaSize.x, uLight.areaSize.y, 1f);
            }

            var b = Undo.AddComponent<BakeryLightMesh>(go);
            b.color = uLight.color;
            b.intensity = uLight.intensity;
            b.cutoff = uLight.range > 0 ? uLight.range : 100f;
            b.samples = _meshSamples;
            b.selfShadow = _meshSelfShadow;
            b.bakeToIndirect = _meshBakeToIndirect;
            b.indirectIntensity = _meshIndirectIntensity;
            b.bitmask = 1;
        }

        private static Mesh _quadMesh;
        private static Mesh GetBuiltinQuadMesh()
        {
            if (_quadMesh != null) return _quadMesh;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quadMesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return _quadMesh;
        }

        private static Mesh BuildDiscMesh(float radius, int segments = 32)
        {
            var m = new Mesh { name = $"{GeneratedDiscMeshName}_{segments}" };
            var verts = new Vector3[segments + 1];
            var tris = new int[segments * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0);
            }
            for (int i = 0; i < segments; i++)
            {
                tris[i * 3 + 0] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = ((i + 1) % segments) + 1;
            }
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ----- 逆変換 (同じGOから取り除く + Lightを復元) -----
        private void DoReverse()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Reverse Bakery → Unity Light");
            int undoGroup = Undo.GetCurrentGroup();

            int ok = 0, fail = 0;

            foreach (var bComp in _reversePreview.ToList())
            {
                if (bComp == null) continue;
                try
                {
                    if (_dryRun) { ok++; continue; }

                    var go = bComp.gameObject;
                    var u = go.GetComponent<Light>();
                    if (u == null) u = Undo.AddComponent<Light>(go);
                    Undo.RecordObject(u, "Restore Unity Light");
                    u.enabled = true;
                    u.lightmapBakeType = LightmapBakeType.Baked;

                    if (bComp is BakeryDirectLight bd)
                    {
                        u.type = LightType.Directional;
                        u.color = bd.color;
                        u.intensity = bd.intensity;
                    }
                    else if (bComp is BakeryPointLight bp)
                    {
                        u.color = bp.color;
                        u.intensity = bp.intensity;
                        u.range = bp.cutoff;
                        if (bp.projMode == BakeryPointLight.ftLightProjectionMode.Cone)
                        {
                            u.type = LightType.Spot;
                            u.spotAngle = bp.angle;
#if UNITY_2020_1_OR_NEWER
                            u.innerSpotAngle = bp.innerAngle;
#endif
                        }
                        else
                        {
                            u.type = LightType.Point;
                        }
                    }
                    else if (bComp is BakeryLightMesh bm)
                    {
#if UNITY_2020_1_OR_NEWER
                        u.type = LightType.Rectangle;
#else
                        u.type = LightType.Area;
#endif
                        u.color = bm.color;
                        u.intensity = bm.intensity;
                        u.range = bm.cutoff;
                        u.areaSize = new Vector2(go.transform.localScale.x, go.transform.localScale.y);

                        if (_removeAreaMeshOnReverse)
                            TryRemoveGeneratedAreaMesh(go);
                    }

                    Undo.DestroyObjectImmediate(bComp);
                    EditorSceneManager.MarkSceneDirty(go.scene);
                    ok++;
                }
                catch (System.Exception ex)
                {
                    fail++;
                    Debug.LogError($"[BakeryConverter] Reverse failed: {bComp?.name}\n{ex}");
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[BakeryConverter] Reverse: ok={ok}, fail={fail}, dryRun={_dryRun}");
            Refresh();
        }

        // Quad (Unity primitive) または BakeryConv_Disc_* のメッシュが付いていれば MeshFilter/MeshRenderer ごと削除
        private static void TryRemoveGeneratedAreaMesh(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) return;
            var mesh = mf.sharedMesh;
            if (mesh == null) return;
            bool isOurs = mesh.name == "Quad" || mesh.name.StartsWith(GeneratedDiscMeshName);
            if (!isOurs) return;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) Undo.DestroyObjectImmediate(mr);
            Undo.DestroyObjectImmediate(mf);
        }
    }
}
#endif
