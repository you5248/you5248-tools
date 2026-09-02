// ObjectPainterCore.cs
// Unity 2022.3 compatible (version-guarded physics for older).
//
// Placement / physics / combine / erase logic for the you5248 Object Painter.
// The EditorWindow (ObjectPainterWindow) owns the SceneView interaction and the
// per-stroke Undo grouping; this file holds the pure-ish placement primitives.
//
// Design notes:
// - Painting raycasts against scene Colliders filtered by surfaceMask. Objects
//   without colliders cannot be painted on (honest, predictable).
// - Every created object is registered with Undo so a whole stroke collapses to
//   one undo step (the window opens/closes the group).
// - Combine groups children by single shared material; multi-material / multi-
//   submesh meshes are skipped and reported, matching the reference tool.

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace you5248.ObjectPainter
{
    /// <summary>All painter settings. Serialized by the window, passed to the core.</summary>
    [System.Serializable]
    public class PaintSettings
    {
        // Palette
        public List<GameObject> prefabs = new List<GameObject>();

        // Surface filtering
        public LayerMask surfaceMask = ~0;

        // Common placement
        public bool alignToSurface = true;
        [Range(0f, 1f)] public float alignBlend = 1f;
        public bool randomYaw = true;
        public bool randomTilt = false;
        public float maxTilt = 8f;
        public Vector2 scaleRange = new Vector2(1f, 1f);
        public float surfaceOffset = 0f;

        // Grouping
        public bool groupUnderParent = true;
        public Transform parentGroup;
        public string autoParentName = "PaintedObjects";

        // Scatter
        public float brushRadius = 3f;
        public int countPerStroke = 5;
        public float minSpacing = 0.5f;   // 0 = allow overlap
        public float strokeSpacing = 1f;  // drag throttle distance

        // Path / fence
        public float pathSpacing = 1.5f;
        public bool pathFillGaps = true;

        // Physics drop
        public float dropHeight = 2f;
        public int simSteps = 120;
        public float simTimestep = 0.02f;
        public bool keepRigidbody = false;

        // Combine
        public string combineSaveFolder = "Assets/you5248/ObjectPainter/CombinedMeshes";
    }

    public static class ObjectPainterCore
    {
        // ---------------------------------------------------------------- public placement API

        /// <summary>Scatter up to countPerStroke objects within the brush around a surface point.</summary>
        public static List<GameObject> Scatter(PaintSettings s, Vector3 center, Vector3 centerNormal,
                                               Transform parent, List<Vector3> taken)
        {
            var made = new List<GameObject>();
            if (s.prefabs == null || s.prefabs.Count(p => p != null) == 0) return made;

            GetTangents(centerNormal, out Vector3 t1, out Vector3 t2);
            int target = Mathf.Max(1, s.countPerStroke);
            int placed = 0, attempts = 0, maxAttempts = target * 10;

            while (placed < target && attempts < maxAttempts)
            {
                attempts++;
                Vector2 d = Random.insideUnitCircle * s.brushRadius;
                Vector3 sample = center + t1 * d.x + t2 * d.y;
                if (!SampleSurface(s, centerNormal, sample, out Vector3 pos, out Vector3 nrm)) continue;
                if (s.minSpacing > 0f && TooClose(pos, taken, s.minSpacing)) continue;

                var go = PlaceScatterAt(s, pos, nrm, parent);
                if (go == null) continue;
                made.Add(go);
                taken?.Add(pos);
                placed++;
            }
            return made;
        }

        /// <summary>Scatter raised by dropHeight, then run an edit-time physics settle.</summary>
        public static List<GameObject> PhysicsDrop(PaintSettings s, Vector3 center, Vector3 centerNormal,
                                                   Transform parent, List<Vector3> taken)
        {
            float savedOffset = s.surfaceOffset;
            bool savedTilt = s.randomTilt;
            s.surfaceOffset = 0.02f;  // まずサンプル点のほぼ表面に置く
            s.randomTilt = true;      // spawn at varied orientations so they settle naturally
            // 重ね塗り対応: 最小間隔はこのストローク内だけで判定する。
            // セッション全体の履歴(taken)と照合すると同じ場所への2回目以降が全部弾かれて山が作れない。
            var strokeTaken = new List<Vector3>();
            var objs = Scatter(s, center, centerNormal, parent, strokeTaken);
            s.surfaceOffset = savedOffset;
            s.randomTilt = savedTilt;

            // 落下開始位置はサーフェス法線ではなく「世界座標の真上」へ持ち上げる。
            // 法線方向に持ち上げると斜面・壁で横にずれた位置から絶対下方向へ落ちるため、
            // 着地点が狙いから大きく流れる（振りかけ方式なら真下＝サンプル点に落ちる）。
            float h = Mathf.Max(0.05f, s.dropHeight);
            var samplePos = new List<Vector3>(objs.Count);
            foreach (var go in objs)
            {
                samplePos.Add(go.transform.position);   // ほぼ表面のサンプル点
                go.transform.position += Vector3.up * h;
            }

            // リアルタイムで降らせる（毎エディタフレーム少しずつ物理を進める＝降り注ぐのが見える）。
            // 保険の押し戻し（大きく流れた個体→サンプル点）は完了時に FinishSettle が行う。
            if (objs.Count > 0) BeginSettle(s, objs, samplePos, h + s.brushRadius + 1f);
            return objs;
        }

        /// <summary>Place one scatter-style object at an exact surface point.</summary>
        public static GameObject PlaceScatterAt(PaintSettings s, Vector3 pos, Vector3 normal, Transform parent)
        {
            var pf = PickPrefab(s);
            if (pf == null) return null;
            return Create(s, pf, pos + normal * s.surfaceOffset, ScatterRotation(s, normal), parent);
        }

        /// <summary>Place one path/fence object facing along dir.</summary>
        public static GameObject PlacePathAt(PaintSettings s, Vector3 pos, Vector3 normal, Vector3 dir, Transform parent)
        {
            var pf = PickPrefab(s);
            if (pf == null) return null;
            return Create(s, pf, pos + normal * s.surfaceOffset, PathRotation(s, normal, dir), parent);
        }

        /// <summary>Raycast onto the masked surface near a world point (used for path gap-fill).</summary>
        public static bool ProjectToSurface(PaintSettings s, Vector3 around, Vector3 normal,
                                            out Vector3 pos, out Vector3 nrm)
        {
            var ray = new Ray(around + normal * 5f, -normal);
            if (Physics.Raycast(ray, out RaycastHit hit, 10f, s.surfaceMask, QueryTriggerInteraction.Ignore))
            {
                pos = hit.point; nrm = hit.normal; return true;
            }
            pos = around; nrm = normal; return false;
        }

        // ---------------------------------------------------------------- physics

        /// <summary>実時間で少しずつ物理を進める「降り注ぐ」セトル。EditorApplication.update 駆動。</summary>
        class SettleState
        {
            public PaintSettings s;
            public List<GameObject> objs;
            public List<Vector3> samplePos;
            public float limit;
            public List<Rigidbody> tempRb = new List<Rigidbody>();
            public List<Collider> tempCol = new List<Collider>();
            public List<MeshCollider> madeConvex = new List<MeshCollider>();
            public List<Rigidbody> frozen = new List<Rigidbody>();
            public int stepsDone;
            public int stepsTotal;
            public double lastTime;
#if UNITY_2022_2_OR_NEWER
            public SimulationMode prevMode;
#else
            public bool prevAuto;
#endif
        }

        static SettleState _settle;

        static void BeginSettle(PaintSettings s, List<GameObject> objs, List<Vector3> samplePos, float limit)
        {
            FinishSettle(true);   // 直前の落下が進行中なら残りを即確定してから始める

            var st = new SettleState { s = s, objs = objs, samplePos = samplePos, limit = limit };

            // Physics.Simulate はシーン全体を進めるため、ペイント対象以外の動的Rigidbody
            // （ピックアップ等）が動いてしまう。シミュレーション中だけkinematic化して守る。
            var objSet = new HashSet<GameObject>(objs);
            foreach (var rb in Object.FindObjectsOfType<Rigidbody>())
            {
                if (rb.isKinematic || objSet.Contains(rb.gameObject)) continue;
                rb.isKinematic = true;
                st.frozen.Add(rb);
            }

            // 重ね塗り対応: 同じ親グループ内の既配置物のうちコライダーを持たないものに
            // 一時コライダー（静的）を付け、今回の落下を受け止められるようにする（完了時に除去）。
            var parent = objs.Count > 0 ? objs[0].transform.parent : null;
            if (parent != null)
            {
                foreach (Transform sibling in parent)
                {
                    var go2 = sibling.gameObject;
                    if (objSet.Contains(go2)) continue;
                    if (go2.GetComponentInChildren<Collider>() != null) continue;
                    var mf2 = go2.GetComponentInChildren<MeshFilter>();
                    if (mf2 == null || mf2.sharedMesh == null) continue;
                    var mc2 = go2.AddComponent<MeshCollider>();
                    mc2.sharedMesh = mf2.sharedMesh;
                    mc2.convex = true;
                    st.tempCol.Add(mc2);
                }
            }

            foreach (var go in objs)
            {
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var mf = go.GetComponentInChildren<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        var mc = go.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        mc.convex = true;
                        st.tempCol.Add(mc);
                    }
                    else
                    {
                        st.tempCol.Add(go.AddComponent<BoxCollider>());
                    }
                }
                else
                {
                    // 非convexのMeshCollider＋動的RigidbodyはPhysXが無視する（Unity5以降非対応）
                    // ＝当たり判定ゼロで落下して明後日の場所へ行く。シミュレーション中だけconvex化する。
                    foreach (var mc in go.GetComponentsInChildren<MeshCollider>())
                    {
                        if (mc.convex) continue;
                        mc.convex = true;
                        st.madeConvex.Add(mc);
                    }
                }
                var rb = go.GetComponent<Rigidbody>();
                if (rb == null)
                {
                    rb = go.AddComponent<Rigidbody>();
                    st.tempRb.Add(rb);
                }
                // 小物・薄物は落下速度×タイムステップで地面を貫通（トンネリング）して
                // はるか下へ抜けるため、シミュレーション中は連続衝突判定にする
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

#if UNITY_2022_2_OR_NEWER
            st.prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
#else
            st.prevAuto = Physics.autoSimulation;
            Physics.autoSimulation = false;
#endif
            st.stepsTotal = Mathf.Clamp(s.simSteps, 1, 2000);
            st.lastTime = EditorApplication.timeSinceStartup;
            _settle = st;
            EditorApplication.update += StepSettle;
        }

        static void StepSettle()
        {
            var st = _settle;
            if (st == null) { EditorApplication.update -= StepSettle; return; }

            float dt = Mathf.Max(0.005f, st.s.simTimestep);
            double now = EditorApplication.timeSinceStartup;
            int want = (int)((now - st.lastTime) / dt);
            if (want <= 0) return;
            st.lastTime += want * dt;
            want = Mathf.Min(want, 8);   // エディタのヒッチで一気に進みすぎない
            int steps = Mathf.Min(want, st.stepsTotal - st.stepsDone);
            for (int i = 0; i < steps; i++) Physics.Simulate(dt);
            st.stepsDone += steps;
            SceneView.RepaintAll();

            if (st.stepsDone >= st.stepsTotal) FinishSettle(false);
        }

        static void FinishSettle(bool runRemaining)
        {
            var st = _settle;
            if (st == null) return;
            _settle = null;
            EditorApplication.update -= StepSettle;

            if (runRemaining && st.stepsDone < st.stepsTotal)
            {
                float dt = Mathf.Max(0.005f, st.s.simTimestep);
                for (int i = st.stepsDone; i < st.stepsTotal; i++) Physics.Simulate(dt);
            }

#if UNITY_2022_2_OR_NEWER
            Physics.simulationMode = st.prevMode;
#else
            Physics.autoSimulation = st.prevAuto;
#endif

            // 保険: 貫通・弾き飛ばしで大きく流れた個体はサンプル点へ戻す
            for (int i = 0; i < st.objs.Count && i < st.samplePos.Count; i++)
            {
                var go = st.objs[i];
                if (go == null) continue;
                if ((go.transform.position - st.samplePos[i]).magnitude > st.limit)
                    go.transform.position = st.samplePos[i] + Vector3.up * 0.02f;
            }

            // Strip the temp components we added (objects are part of the same undo
            // group, so an undo removes the whole creation anyway).
            if (!st.s.keepRigidbody)
            {
                foreach (var rb in st.tempRb) if (rb != null) Object.DestroyImmediate(rb);
                // Rigidbodyを外した後でconvexを元に戻す（残したままだと非convex＋動的RBの
                // 無効組み合わせが復活するため、keepRigidbody時はconvexのままにする）
                foreach (var mc in st.madeConvex) if (mc != null) mc.convex = false;
            }
            foreach (var c in st.tempCol) if (c != null) Object.DestroyImmediate(c);
            foreach (var rb in st.frozen) if (rb != null) rb.isKinematic = false;
        }

        // ---------------------------------------------------------------- combine

        public static int CombineGroup(PaintSettings s, Transform parent, out string message)
        {
            message = "";
            if (parent == null) { message = "親グループが未設定です。"; return 0; }

            var filters = parent.GetComponentsInChildren<MeshFilter>(false)
                .Where(mf => mf.sharedMesh != null && mf.GetComponent<MeshRenderer>() != null)
                .ToList();
            if (filters.Count == 0) { message = "結合できるメッシュがありません。"; return 0; }

            var groups = new Dictionary<Material, List<MeshFilter>>();
            int skipped = 0;
            foreach (var mf in filters)
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh.subMeshCount > 1 || mr.sharedMaterials.Length > 1 || mr.sharedMaterial == null)
                {
                    skipped++;
                    continue;
                }
                var mat = mr.sharedMaterial;
                if (!groups.TryGetValue(mat, out var list)) { list = new List<MeshFilter>(); groups[mat] = list; }
                list.Add(mf);
            }

            EnsureFolder(s.combineSaveFolder);
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Object Painter Combine");
            int group = Undo.GetCurrentGroup();

            int combined = 0;
            foreach (var kv in groups)
            {
                var mat = kv.Key;
                var list = kv.Value;
                if (list.Count < 2) continue; // nothing gained combining a single object

                var cis = new CombineInstance[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    cis[i].mesh = list[i].sharedMesh;
                    cis[i].transform = parent.worldToLocalMatrix * list[i].transform.localToWorldMatrix;
                }

                var mesh = new Mesh { name = "Combined_" + Sanitize(mat.name) };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.CombineMeshes(cis, true, true);
                mesh.RecalculateBounds();

                string path = AssetDatabase.GenerateUniqueAssetPath(
                    s.combineSaveFolder + "/Combined_" + Sanitize(mat.name) + ".asset");
                AssetDatabase.CreateAsset(mesh, path);

                var go = new GameObject("Combined_" + mat.name);
                Undo.RegisterCreatedObjectUndo(go, "Object Painter Combine");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;

                foreach (var mf in list) Undo.DestroyObjectImmediate(mf.gameObject);
                combined++;
            }

            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(group);

            message = combined > 0
                ? $"{combined} 個のメッシュに結合しました（{skipped} 件スキップ：複数マテリアル/サブメッシュ）。保存先: {s.combineSaveFolder}"
                : $"結合対象が見つかりませんでした（単一マテリアルで2個以上の共有が必要、{skipped} 件スキップ）。";
            return combined;
        }

        // ---------------------------------------------------------------- erase

        public static int EraseAround(Transform scope, List<GameObject> session, Vector3 center, float radius)
        {
            var candidates = new List<GameObject>();
            if (scope != null)
                for (int i = scope.childCount - 1; i >= 0; i--) candidates.Add(scope.GetChild(i).gameObject);
            else if (session != null)
                candidates.AddRange(session.Where(g => g != null));

            float r2 = radius * radius;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Object Painter Erase");
            int group = Undo.GetCurrentGroup();

            int removed = 0;
            foreach (var go in candidates)
            {
                if (go == null) continue;
                var rend = go.GetComponentInChildren<Renderer>();
                Vector3 closest = rend != null ? rend.bounds.ClosestPoint(center) : go.transform.position;
                if ((closest - center).sqrMagnitude <= r2)
                {
                    session?.Remove(go);
                    Undo.DestroyObjectImmediate(go);
                    removed++;
                }
            }
            Undo.CollapseUndoOperations(group);
            return removed;
        }

        // ---------------------------------------------------------------- internals

        static GameObject Create(PaintSettings s, GameObject prefab, Vector3 worldPos, Quaternion worldRot, Transform parent)
        {
            GameObject go = PrefabUtility.IsPartOfPrefabAsset(prefab)
                ? (GameObject)PrefabUtility.InstantiatePrefab(prefab)
                : Object.Instantiate(prefab);
            Undo.RegisterCreatedObjectUndo(go, "Object Painter");

            go.transform.position = worldPos;
            go.transform.rotation = worldRot;

            float f = Mathf.Approximately(s.scaleRange.x, s.scaleRange.y)
                ? s.scaleRange.x
                : Random.Range(s.scaleRange.x, s.scaleRange.y);
            if (!Mathf.Approximately(f, 1f)) go.transform.localScale *= f;

            if (parent != null) Undo.SetTransformParent(go.transform, parent, "Object Painter Parent");
            return go;
        }

        static Quaternion SurfaceUp(PaintSettings s, Vector3 normal)
        {
            if (!s.alignToSurface) return Quaternion.identity;
            var full = Quaternion.FromToRotation(Vector3.up, normal);
            return Quaternion.Slerp(Quaternion.identity, full, Mathf.Clamp01(s.alignBlend));
        }

        static Quaternion ScatterRotation(PaintSettings s, Vector3 normal)
        {
            Quaternion up = SurfaceUp(s, normal);
            float yaw = s.randomYaw ? Random.Range(0f, 360f) : 0f;
            Quaternion tilt = s.randomTilt
                ? Quaternion.Euler(Random.Range(-s.maxTilt, s.maxTilt), 0f, Random.Range(-s.maxTilt, s.maxTilt))
                : Quaternion.identity;
            return up * Quaternion.AngleAxis(yaw, Vector3.up) * tilt;
        }

        static Quaternion PathRotation(PaintSettings s, Vector3 normal, Vector3 dir)
        {
            Vector3 up = s.alignToSurface ? normal : Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(dir, up);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
            Quaternion baseRot = Quaternion.LookRotation(fwd.normalized, up);
            Quaternion wobble = s.randomTilt
                ? Quaternion.AngleAxis(Random.Range(-s.maxTilt, s.maxTilt), up)
                : Quaternion.identity;
            return baseRot * wobble;
        }

        static bool SampleSurface(PaintSettings s, Vector3 centerNormal, Vector3 sample,
                                  out Vector3 pos, out Vector3 normal)
        {
            float h = s.brushRadius + 1f;
            var ray = new Ray(sample + centerNormal * h, -centerNormal);
            // トリガー（足音エリア・PPSゾーン等）を面として拾うと、物理は素通りするのに
            // 生成基準だけが幻の面になり高さが大きくずれる。必ず物理面のみサンプルする。
            if (Physics.Raycast(ray, out RaycastHit hit, h * 2f, s.surfaceMask, QueryTriggerInteraction.Ignore))
            {
                pos = hit.point; normal = hit.normal; return true;
            }
            pos = sample; normal = centerNormal; return false;
        }

        static bool TooClose(Vector3 p, List<Vector3> taken, float minSpacing)
        {
            if (taken == null) return false;
            float m2 = minSpacing * minSpacing;
            for (int i = 0; i < taken.Count; i++)
                if ((taken[i] - p).sqrMagnitude < m2) return true;
            return false;
        }

        static GameObject PickPrefab(PaintSettings s)
        {
            var pool = s.prefabs.Where(p => p != null).ToList();
            if (pool.Count == 0) return null;
            return pool[Random.Range(0, pool.Count)];
        }

        static void GetTangents(Vector3 normal, out Vector3 t1, out Vector3 t2)
        {
            Vector3 reference = Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up;
            t1 = Vector3.Cross(normal, reference).normalized;
            t2 = Vector3.Cross(normal, t1).normalized;
        }

        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        static string Sanitize(string n)
        {
            if (string.IsNullOrEmpty(n)) return "mat";
            var sb = new System.Text.StringBuilder(n.Length);
            foreach (char ch in n)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
            return sb.Length == 0 ? "mat" : sb.ToString();
        }
    }
}
