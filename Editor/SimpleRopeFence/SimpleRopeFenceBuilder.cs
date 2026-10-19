using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace you5248.RopeFence
{
// 杭とロープの柵（Simple Rope Fence）の組み立て。
// 柵の物（根）の直接の子:
//   設定 (EditorOnly)  … SimpleRopeFence（EditorOnly タグ。ビルドで消える）
//   杭 01, 杭 02, …    … STAKE_THRU のメッシュを持つ物（利用者が Scene ビューで動かす。並びの順にロープを通す）
//   ロープ             … 作ったメッシュ（Assets/you5248/SimpleRopeFence/Generated/ の asset）と SHIMENAWA_N8 の材質
//   房 (始点)・房 (終点) … SHIMENAWA_END（焼いた部品をそのまま参照）
// 作り直すたびに: 杭の足元を地面に合わせ（下向きの光線）、杭の縦の軸の回りの向きだけを「穴がロープの通る向き」に回す。
// 杭の傾き（上の向き）は利用者が回した分を残す。ロープは穴に通して、両端は止め結び（Data/SimpleRopeFence_knot.json の end_thru_overhand）。
public static class SimpleRopeFenceBuilder
{
    // 生成したロープのメッシュの置き場（利用者の Assets 側。Packages 以下は読み取り専用になり得るので使わない）
    public const string GenDir = "Assets/you5248/SimpleRopeFence/Generated/";
    // 同梱の部品は GUID で探す（パッケージの置き場所・埋め込みかどうかに左右されない）。GUID は各 .meta と同じ
    const string DefStake = "5d116612af4bc97458940a8914429b65";      // Runtime/SimpleRopeFence/Parts/STAKE_THRU/STAKE_THRU.asset
    const string DefStakeMat = "13ddc6e7ebeb0f84abf6f9eda5c916e5";   // Runtime/SimpleRopeFence/Parts/STAKE_THRU/STAKE_THRU_mat.mat
    const string DefRopeMat = "980fb67935dc9424784befaab35876d9";    // Runtime/SimpleRopeFence/Parts/SHIMENAWA_N8/SHIMENAWA_N8_mat.mat
    const string DefEnd = "2d397e1726b53b248937d1aac0341d01";        // Runtime/SimpleRopeFence/Parts/SHIMENAWA_END/SHIMENAWA_END.asset
    const string DefEndMat = "88c78660e64b7ce458bcb4139eaa6b50";     // Runtime/SimpleRopeFence/Parts/SHIMENAWA_END/SHIMENAWA_END_mat.mat
    const string KnotsGuid = "ba139e7fe21747a29457a133585a8765";        // Editor/SimpleRopeFence/Data/SimpleRopeFence_knot.json（止め結びの点列。読むだけ）
    const string KnotsPath = "Packages/com.you5248.tools/Editor/SimpleRopeFence/Data/SimpleRopeFence_knot.json";   // GUID で見つからないときの予備

    static T Load<T>(string guid) where T : Object
    {
        var p = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(p) ? null : AssetDatabase.LoadAssetAtPath<T>(p);
    }

    // STAKE_THRU の寸法（STAKE_THRU_mesh.json と knots.json の決まり）
    public const float StakeTop = 0.60f;        // 原点（地表の高さの軸の中心）から頭まで
    public const float StakeBottom = -0.40f;    // 原点から下端まで
    public const float HoleY = 0.50f;           // 焼き込みの穴の中心（頭から 100mm 下、±X 向き、φ37）
    public const float HoleR = 0.0185f;
    public const float Mouth = 0.054f;          // 穴の中で縄をまっすぐにする半分の長さ（knots.json の hole_straight_half_m）
    const float Apothem = 0.048f;               // 8 角の杭の面までの距離
    const float KnotD = 0.032f;                 // knots.json の点列を作った縄の径
    const float KnotY = 0.497f;                 // knots.json の穴の中の線の高さ
    // しめ縄の 8 角の管（同梱の SHIMENAWA_N8 のマップを作った 8 角の管と同じ寸法の比）
    const int Sides = 8; const float RvOverD = 0.466272f, ApOverD = 0.430779f, Theta0 = 0.392699f, PitchOverD = 2.5f, TileOverD = 2.5f;

    public const string SettingsName = "設定 (EditorOnly)", RopeName = "ロープ", EndStartName = "房 (始点)", EndEndName = "房 (終点)";

    // ------------------------------------------------------------ 部品
    public static Mesh StakeMesh(SimpleRopeFence f) => f.stakeMesh ? f.stakeMesh : Load<Mesh>(DefStake);
    static Material StakeMat(SimpleRopeFence f) => f.stakeMaterial ? f.stakeMaterial : Load<Material>(DefStakeMat);
    static Material RopeMat(SimpleRopeFence f) => f.ropeMaterial ? f.ropeMaterial : Load<Material>(DefRopeMat);
    static Mesh EndMesh(SimpleRopeFence f) => f.endMesh ? f.endMesh : Load<Mesh>(DefEnd);
    static Material EndMat(SimpleRopeFence f) => f.endMaterial ? f.endMaterial : Load<Material>(DefEndMat);

    public static Transform Root(SimpleRopeFence f) => f != null ? f.transform.parent : null;

    /// 杭 = 根の直接の子で、杭のメッシュを持ち、表示されている物（並びの順）
    public static List<Transform> Stakes(SimpleRopeFence f)
    {
        var res = new List<Transform>(); var root = Root(f); if (root == null) return res;
        var mesh = StakeMesh(f);
        foreach (Transform c in root)
        {
            if (!c.gameObject.activeSelf) continue;
            var mf = c.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh == mesh && mesh != null) res.Add(c);
        }
        return res;
    }

    /// その物が属する柵（根・杭・ロープ・房・設定のどれを選んでも）
    public static SimpleRopeFence FindFence(GameObject go)
    {
        if (go == null) return null;
        var f = go.GetComponent<SimpleRopeFence>(); if (f != null) return f;
        foreach (Transform c in go.transform) { f = c.GetComponent<SimpleRopeFence>(); if (f != null) return f; }
        var p = go.transform.parent;
        if (p != null) foreach (Transform c in p) { f = c.GetComponent<SimpleRopeFence>(); if (f != null) return f; }
        return null;
    }

    // ------------------------------------------------------------ ばらつき（種と杭の番号で決まる 0〜1）
    public static float Rand(int seed, int i, int ch)
    {
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u ^ (uint)(i + 1) * 0x85EBCA77u ^ (uint)(ch + 7) * 0xC2B2AE3Du;
            h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
    public static float StakeHeightOf(SimpleRopeFence f, int i) => Mathf.Clamp(f.stakeHeight + (Rand(f.seed, i, 2) * 2f - 1f) * 0.06f * f.randomness, 0.40f, 0.60f);
    public static float SagOf(SimpleRopeFence f, int span) => Mathf.Max(0f, f.sagPercent * (1f + (Rand(f.seed, span, 3) * 2f - 1f) * 0.5f * f.randomness));
    /// 杭 i のランダムな傾き（上の向き）
    public static Vector3 TiltUp(SimpleRopeFence f, int i)
    {
        float ang = f.tiltMax * Rand(f.seed, i, 0), dir = 360f * Rand(f.seed, i, 1);
        var axis = Quaternion.AngleAxis(dir, Vector3.up) * Vector3.right;
        return Quaternion.AngleAxis(ang, axis) * Vector3.up;
    }

    // ------------------------------------------------------------ 新規作成・並べ直し
    [MenuItem("you5248/Simple Rope Fence/新規作成（シーンビューの中心に）", false, 10)]
    public static void MenuCreate() { var f = Create(null); if (f != null) Selection.activeGameObject = Root(f).gameObject; }

    /// at が null ならシーンビューの中心（地面）に、カメラの横の向きへ 6m の柵を作る
    public static SimpleRopeFence Create(Vector3? at, Vector3? along = null, string name = null)
    {
        var sv = SceneView.lastActiveSceneView;
        Vector3 c = at ?? (sv != null ? sv.pivot : Vector3.zero);
        if (!at.HasValue && Physics.Raycast(c + Vector3.up * 50f, Vector3.down, out var hit, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) c = hit.point;
        Vector3 right = along ?? (sv != null ? sv.camera.transform.right : Vector3.right); right.y = 0f;
        right = right.sqrMagnitude < 1e-6f ? Vector3.right : right.normalized;

        var root = new GameObject(name ?? "Simple Rope Fence (杭とロープの柵)");
        Undo.RegisterCreatedObjectUndo(root, "杭とロープの柵を作る");
        root.transform.position = c;
        var s = new GameObject(SettingsName); s.tag = "EditorOnly";
        s.transform.SetParent(root.transform, false);
        var f = s.AddComponent<SimpleRopeFence>();
        f.buildId = System.Guid.NewGuid().ToString("N").Substring(0, 8);
        float half = 0.5f * 1.5f * (f.stakeCount - 1);
        Rearrange(f, c - right * half, c + right * half, true);
        return f;
    }

    /// 始点の杭と終点の杭の間に f.stakeCount 本を等間隔に並べ直す（本数を変えたとき・ボタンのときだけ）。
    /// 杭の物は前から使い回し、足りなければ足し、余れば消す（どれも Undo できる）。傾きは振り直す。
    public static void Rearrange(SimpleRopeFence f, Vector3? a = null, Vector3? b = null, bool created = false)
    {
        var root = Root(f); if (root == null) return;
        int n = Mathf.Clamp(f.stakeCount, 2, 30);
        var st = Stakes(f);
        Vector3 pa, pb;
        if (a.HasValue && b.HasValue) { pa = a.Value; pb = b.Value; }
        else if (st.Count >= 2) { pa = st[0].position; pb = st[st.Count - 1].position; }
        else if (st.Count == 1) { pa = st[0].position; pb = pa + root.right * 1.5f * (n - 1); }
        else { pa = root.position - root.right * 0.75f * (n - 1); pb = root.position + root.right * 0.75f * (n - 1); }

        var use = new List<Transform>();
        if (st.Count >= 2)
        {
            var mids = st.GetRange(1, st.Count - 2);
            use.Add(st[0]);
            for (int i = 0; i < n - 2; i++) use.Add(i < mids.Count ? mids[i] : null);
            use.Add(st[st.Count - 1]);
            for (int i = n - 2; i < mids.Count; i++) Undo.DestroyObjectImmediate(mids[i].gameObject);
        }
        else { for (int i = 0; i < n; i++) use.Add(i < st.Count ? st[i] : null); }

        var mesh = StakeMesh(f); var mat = StakeMat(f);
        for (int i = 0; i < n; i++)
        {
            var t = use[i];
            if (t == null)
            {
                var g = new GameObject("杭");
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
                GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);   // 今の手すりの焼いた物と同じ
                g.transform.SetParent(root, false);
                if (!created) Undo.RegisterCreatedObjectUndo(g, "杭を足す");
                t = g.transform; use[i] = t;
            }
            else Undo.RecordObject(t, "杭を並べ直す");
            Vector3 p = Vector3.Lerp(pa, pb, n == 1 ? 0f : i / (float)(n - 1));
            t.SetPositionAndRotation(p, Quaternion.FromToRotation(Vector3.up, TiltUp(f, i)));
            if (t.name != StakeName(i)) { if (!created) Undo.RecordObject(t.gameObject, "杭の名前"); t.name = StakeName(i); }
        }
        // 並び: 設定、杭 01…、ロープ、房
        var s = f.transform; if (s.GetSiblingIndex() != 0) { if (!created) Undo.SetSiblingIndex(s, 0, "並び"); else s.SetSiblingIndex(0); }
        for (int i = 0; i < n; i++) if (use[i].GetSiblingIndex() != i + 1) { if (!created) Undo.SetSiblingIndex(use[i], i + 1, "並び"); else use[i].SetSiblingIndex(i + 1); }
        Build(f);
    }

    public static string StakeName(int i) => "杭 " + (i + 1).ToString("00");

    /// 傾きの幅・種を変えたとき: 全部の杭の上の向きを振り直す（位置はそのまま）
    public static void ApplyTilt(SimpleRopeFence f)
    {
        var st = Stakes(f);
        for (int i = 0; i < st.Count; i++) { Undo.RecordObject(st[i], "杭の傾きを振り直す"); st[i].rotation = Quaternion.FromToRotation(Vector3.up, TiltUp(f, i)); }
        Build(f);
    }

    // ------------------------------------------------------------ 全体の長さ（始点の杭〜終点の杭の水平距離）
    public const float MinLength = 0.3f, MaxLength = 50f;

    /// 今の全体の長さ: 始点の杭と終点の杭の足元（原点）の水平距離。杭が 2 本ないときは 0
    public static float TotalLength(SimpleRopeFence f)
    {
        var st = Stakes(f); if (st.Count < 2) return 0f;
        var d = st[st.Count - 1].position - st[0].position; d.y = 0f;
        return d.magnitude;
    }

    /// 全体の長さを L にする（値を変えたとき・「この長さに合わせる」のときだけ）。
    /// 始点の杭は動かさない。終点の杭を、始点→終点の今の向き（水平）のまま L の所へ動かし、間の杭をその間に等間隔に置き直す。
    /// 動かした杭は地面に合わせる（高さは作り直しの接地がそのまま仕上げる）。杭の傾き（上の向き）はそのまま。Undo できる。
    public static void SetTotalLength(SimpleRopeFence f, float L)
    {
        var root = Root(f); var st = Stakes(f);
        if (root == null || st.Count < 2) return;
        L = Mathf.Clamp(L, MinLength, MaxLength);
        int n = st.Count;
        Vector3 a = st[0].position, b = st[n - 1].position;
        Vector3 dir = b - a; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-8f) { dir = st[0].right; dir.y = 0f; }   // 始点と終点が重なっているときは始点の穴の向き
        if (dir.sqrMagnitude < 1e-8f) { dir = root.right; dir.y = 0f; }
        if (dir.sqrMagnitude < 1e-8f) dir = Vector3.right;
        dir.Normalize();
        // 地面の高さ（杭の原点の高さ ＋ 地上に出ていない分）
        float gA = a.y + (StakeTop - StakeHeightOf(f, 0)), gB = b.y + (StakeTop - StakeHeightOf(f, n - 1));
        Vector3 e = a + dir * L;
        float gE = GroundNear(e.x, e.z, gB, root);
        Undo.RecordObject(st[0], "全体の長さ");   // 始点は動かさないが、作り直しで向きが変わっても一緒に戻るように
        for (int i = 1; i < n; i++)
        {
            float t = i / (float)(n - 1);
            Vector3 p = a + dir * (L * t);
            float g = i == n - 1 ? gE : GroundNear(p.x, p.z, Mathf.Lerp(gA, gE, t), root);
            Undo.RecordObject(st[i], "全体の長さ");
            st[i].position = new Vector3(p.x, g - (StakeTop - StakeHeightOf(f, i)), p.z);
        }
        Build(f);
    }

    /// (x, z) の地面の高さ。見当 guess の近くから探し、見つからなければ上下へ 5m ずつ探す（坂で終点を遠くへ動かしたとき）。無ければ guess
    static float GroundNear(float x, float z, float guess, Transform root)
    {
        foreach (float o in new[] { 0f, 5f, -5f, 10f, -10f, 15f, -15f, 20f, -20f, 25f })
            if (GroundAt(x, z, guess + o, root, out float y)) return y;
        return guess;
    }

    // ------------------------------------------------------------ 地面
    /// (x, z) の真下の地面: 高さ refY より 1m 上までの面のうち一番高い面（雪の地形から岩が出ている所は岩の上。
    /// それより上の物＝枝・タープなどは見ない）。見つからなければ false
    public const float GroundAbove = 1.0f;
    public static bool GroundAt(float x, float z, float refY, Transform ignore, out float y)
    {
        y = refY;
        var hits = Physics.RaycastAll(new Vector3(x, refY + GroundAbove, z), Vector3.down, GroundAbove + 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        bool ok = false; float best = float.MinValue;
        foreach (var h in hits)
        {
            if (ignore != null && h.transform.IsChildOf(ignore)) continue;
            if (h.point.y > best) { best = h.point.y; y = h.point.y; ok = true; }
        }
        return ok;
    }

    // ------------------------------------------------------------ 作り直し
    class Frame { public Vector3 O, X, U, Z; public float h, yh, groundLow, groundHigh; public bool grounded; }

    class Last { public List<Vector3> pts; public List<Frame> frames; public float D; public int knotA, knotB; }
    static readonly Dictionary<int, Last> s_last = new Dictionary<int, Last>();

    /// ロープ・房を作り直し、杭を地面に合わせて穴の向きに回す。戻り値は報告（1 行目が要約）
    public static string Build(SimpleRopeFence f)
    {
        var root = Root(f);
        if (root == null) { f.lastReport = "× 柵の物（親）がありません。「設定 (EditorOnly)」は柵の物の子に置いてください。"; return f.lastReport; }
        if (string.IsNullOrEmpty(f.buildId) || OtherFenceHasId(f)) { f.buildId = System.Guid.NewGuid().ToString("N").Substring(0, 8); EditorUtility.SetDirty(f); }
        var st = Stakes(f);
        var log = new StringBuilder();
        if (st.Count < 2) { f.lastReport = "× 杭が 2 本以上必要です（今 " + st.Count + " 本）。本数を変えると並べ直します。"; return f.lastReport; }
        if (f.stakeCount != st.Count) { f.stakeCount = st.Count; EditorUtility.SetDirty(f); }   // 手で複製・削除した杭も数える
        Physics.SyncTransforms();
        float D = f.ropeDiameter, rv = RvOverD * D, s = D / KnotD;
        float drop = Mathf.Clamp(HoleR + 0.0003f - 0.5f * D, 0f, HoleR);   // 縄は穴の底に乗る（φ32 で 2.8mm 下がる）
        float yh = HoleY - drop;
        bool dirty = false;

        // 1) 杭の足元: 軸と、まわり 8 点（半径 50mm）の地面のうち一番低い所に原点（地表の高さ）を合わせ、高さの分だけ差し込む
        var fr = new List<Frame>();
        for (int i = 0; i < st.Count; i++)
        {
            var t = st[i]; float h = StakeHeightOf(f, i);
            var F = new Frame { h = h, yh = yh, U = t.up };
            float refY = t.position.y + (StakeTop - h);
            float lo = float.MaxValue, hi = float.MinValue; int got = 0;
            for (int k = -1; k < 8; k++)
            {
                float ax = k < 0 ? 0f : Mathf.Cos(k * Mathf.PI / 4f) * 0.05f, az = k < 0 ? 0f : Mathf.Sin(k * Mathf.PI / 4f) * 0.05f;
                if (GroundAt(t.position.x + ax, t.position.z + az, refY, root, out float gy)) { lo = Mathf.Min(lo, gy); hi = Mathf.Max(hi, gy); got++; }
            }
            F.grounded = got > 0;
            F.groundLow = F.grounded ? lo : t.position.y; F.groundHigh = F.grounded ? hi : t.position.y;
            F.O = new Vector3(t.position.x, F.groundLow - (StakeTop - h), t.position.z);
            fr.Add(F);
        }
        // 2) 穴の向き: 端は隣の杭へ、途中は前後の向きの二等分（杭の上の向きに垂直に）
        int n = fr.Count;
        System.Func<int, Vector3> C = i => fr[i].O + fr[i].U * yh;
        for (int i = 0; i < n; i++)
        {
            Vector3 x;
            if (i == 0) x = C(1) - C(0);
            else if (i == n - 1) x = C(n - 2) - C(n - 1);
            else x = (C(i + 1) - C(i)).normalized + (C(i) - C(i - 1)).normalized;
            var U = fr[i].U; x -= U * Vector3.Dot(x, U);
            if (x.sqrMagnitude < 1e-10f) x = Vector3.Cross(U, Vector3.forward);
            fr[i].X = x.normalized; fr[i].Z = Vector3.Cross(fr[i].X, U);
            var rot = Quaternion.LookRotation(fr[i].Z, U);
            var t = st[i];
            if ((t.position - fr[i].O).sqrMagnitude > 1e-12f || Quaternion.Angle(t.rotation, rot) > 0.01f) { t.SetPositionAndRotation(fr[i].O, rot); dirty = true; }
        }
        // 杭の通る向き（始点→終点へ歩く向き）
        System.Func<int, Vector3> Thru = i => i == n - 1 ? -fr[i].X : fr[i].X;

        // 3) ロープの線: 始点の止め結び（逆向き）→ 間のたわみ → 途中の杭の穴をまっすぐ → … → 終点の止め結び
        var knot = LoadKnot();
        if (knot == null) { f.lastReport = "× 結び目の点列 " + KnotsPath + " を読めません"; return f.lastReport; }
        var pts = new List<Vector3>();
        var kStart = KnotWorld(knot, fr[0], -1f, s, yh); kStart.Reverse();   // 始点は結び目を道の右に置くため z を反転
        pts.AddRange(Decimate(kStart, 0.5f));
        int knotA = pts.Count - 1, clamped = 0;
        var kEnd = KnotWorld(knot, fr[n - 1], 1f, s, yh);
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 A = pts[pts.Count - 1];
            Vector3 B = i + 1 == n - 1 ? kEnd[0] : C(i + 1) - Thru(i + 1) * Mouth;
            // たわみは「杭の穴の中心どうしの距離」に対する %（利用者に見える長さ）。曲線は穴の口から口までに掛ける
            float sagAmount = SagOf(f, i) / 100f * Vector3.Distance(C(i), C(i + 1));
            float ab = Vector3.Distance(A, B);
            var span = Span(A, Thru(i), B, Thru(i + 1), ab > 1e-4f ? sagAmount / ab : 0f, D, root, rv, ref clamped);
            span = Decimate(span, 2.5f);
            for (int k = 1; k < span.Count; k++) pts.Add(span[k]);
            if (i + 1 < n - 1) pts.Add(C(i + 1) + Thru(i + 1) * Mouth);   // 穴の中はまっすぐ
        }
        int knotB = pts.Count - 1;
        var kEndD = Decimate(kEnd, 0.5f);
        for (int k = 1; k < kEndD.Count; k++) pts.Add(kEndD[k]);
        // 近すぎる点を落とす
        var clean = new List<Vector3> { pts[0] };
        for (int k = 1; k < pts.Count; k++) if ((pts[k] - clean[clean.Count - 1]).sqrMagnitude > 0.0004f * 0.0004f || k == pts.Count - 1) clean.Add(pts[k]);
        pts = clean;

        // 4) メッシュ
        var ropeT = Child(root, RopeName, true);
        if (ropeT.localPosition != Vector3.zero || ropeT.localRotation != Quaternion.identity || ropeT.localScale != Vector3.one) { ropeT.localPosition = Vector3.zero; ropeT.localRotation = Quaternion.identity; ropeT.localScale = Vector3.one; dirty = true; }
        var V = new List<Vector3>(); var N = new List<Vector3>(); var UV = new List<Vector2>(); var T = new List<int>();
        float minDot = Tube(pts, ropeT.worldToLocalMatrix, D, V, N, UV, T);
        var mesh = SaveMesh(GenDir + "SimpleRopeFence_" + f.buildId + "_Rope.asset", V, N, UV, T);
        dirty |= SetRenderer(ropeT.gameObject, mesh, RopeMat(f));

        // 5) 房（縄の端の中心が原点、−Y が縄の端の向き。φ32 で作った物なので太さに合わせて縮める）
        var em = EndMesh(f); var emat = EndMat(f);
        dirty |= PlaceEnd(Child(root, EndStartName, true), pts[0], (pts[0] - pts[1]).normalized, s, em, emat);
        dirty |= PlaceEnd(Child(root, EndEndName, true), pts[pts.Count - 1], (pts[pts.Count - 1] - pts[pts.Count - 2]).normalized, s, em, emat);

        // 6) 検査（数値）
        s_last[f.GetInstanceID()] = new Last { pts = pts, frames = fr, D = D, knotA = knotA, knotB = knotB };
        var au = AuditCore(pts, fr, D, root, null);
        float len = 0f; for (int k = 1; k < pts.Count; k++) len += Vector3.Distance(pts[k], pts[k - 1]);
        int ungrounded = fr.Count(q => !q.grounded);
        float maxSlope = fr.Max(q => q.groundHigh - q.groundLow);
        float minEmbed = fr.Min(q => q.groundLow - (q.O.y + StakeBottom));
        bool ok = ungrounded == 0 && au.badStake == 0 && au.badGround == 0 && minDot > 0.5f;
        log.AppendLine((ok ? "○ " : "△ ") + "杭 " + n + " 本・ロープの実長（たわみ込み）" + len.ToString("F2") + "m・三角形 ロープ " + (T.Count / 3) + "（杭 " + n + "×90・房 2×132）");
        log.AppendLine("ロープと杭の木のすき間 最小 " + (au.minStakeGap * 1000f).ToString("F1") + "mm（穴の中を除く）" + (au.badStake > 0 ? "、食い込み " + au.badStake + " 点" : ""));
        log.AppendLine("ロープと地面 最小 " + (au.minGround * 1000f).ToString("F1") + "mm" + (au.badGround > 0 ? "、めり込み " + au.badGround + " 点" : "") + (clamped > 0 ? "（地面に乗せた点 " + clamped + "）" : ""));
        log.AppendLine("杭の差し込み 最小 " + (minEmbed * 1000f).ToString("F0") + "mm・足元の地面の高低差 最大 " + (maxSlope * 1000f).ToString("F0") + "mm" + (ungrounded > 0 ? "、地面が見つからない杭 " + ungrounded + " 本" : ""));
        if (minDot <= 0.5f) log.AppendLine("× ロープの断面の向きが急に変わる所があります（dot " + minDot.ToString("F2") + "）");

        int hash = StateHash(f, st);
        if (f.builtHash != hash) { f.builtHash = hash; EditorUtility.SetDirty(f); }
        f.lastReport = log.ToString().TrimEnd();
        if (dirty && !EditorApplication.isPlaying) EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        return f.lastReport;
    }

    static bool OtherFenceHasId(SimpleRopeFence f) =>
        Object.FindObjectsByType<SimpleRopeFence>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(o => o != f && o.buildId == f.buildId);

    /// 1 区間: A（穴の口、向き tA）から B（次の穴の口、向き tB）へ。たわみはカテナリー（中央で sagRatio×長さ下がる）。
    /// 口の近く（太さの 1.5 倍）は穴の向きから滑らかに曲げて垂らす（まっすぐ突き出さない）。地面より下になる点は地面に乗せる。
    static List<Vector3> Span(Vector3 A, Vector3 tA, Vector3 B, Vector3 tB, float sagRatio, float D, Transform root, float rv, ref int clamped)
    {
        float L = Vector3.Distance(A, B); var res = new List<Vector3>();
        if (L < 1e-4f) { res.Add(A); res.Add(B); return res; }
        float k = SolveCatenary(sagRatio);
        System.Func<float, Vector3> Cat = t =>
        {
            float sh = k < 1e-4f ? 4f * t * (1f - t) : (Cosh(k) - Cosh(k * (2f * t - 1f))) / (Cosh(k) - 1f);
            return Vector3.Lerp(A, B, t) + Vector3.down * (sagRatio * L * sh);
        };
        float tb = Mathf.Min(Mathf.Clamp(1.5f * D, 0.02f, 0.25f * L) / L, 0.3f);
        System.Func<float, Vector3> dCat = t => (Cat(Mathf.Min(1f, t + 1e-3f)) - Cat(Mathf.Max(0f, t - 1e-3f))).normalized;
        Vector3 P1 = Cat(tb), P2 = Cat(1f - tb);
        float d1 = Vector3.Distance(A, P1), d2 = Vector3.Distance(P2, B);
        const int nb = 10;
        for (int j = 0; j <= nb; j++) res.Add(Hermite(A, tA.normalized * d1, P1, dCat(tb) * d1, j / (float)nb));
        int nm = Mathf.Max(8, Mathf.CeilToInt(L * (1f - 2f * tb) / 0.02f));
        for (int j = 1; j < nm; j++) res.Add(Cat(Mathf.Lerp(tb, 1f - tb, j / (float)nm)));
        for (int j = 0; j <= nb; j++) res.Add(Hermite(P2, dCat(1f - tb) * d2, B, tB.normalized * d2, j / (float)nb));
        // 地面に乗せる（端の点は穴の口なので触らない）
        // 地面は、その点の上の弦（穴の口どうしを結ぶ線）の高さから探す（深く垂れた点も拾う）
        float lift = rv + 0.002f;
        var abH = B - A; abH.y = 0f; float abH2 = Mathf.Max(abH.sqrMagnitude, 1e-8f);
        for (int j = 1; j < res.Count - 1; j++)
        {
            var p = res[j]; var q = p - A; q.y = 0f;
            float tt = Mathf.Clamp01(Vector3.Dot(q, abH) / abH2);
            float chordY = Mathf.Lerp(A.y, B.y, tt);
            if (GroundAt(p.x, p.z, Mathf.Max(chordY, p.y), root, out float gy) && gy + lift > p.y) { res[j] = new Vector3(p.x, gy + lift, p.z); clamped++; }
        }
        return res;
    }

    static float Cosh(float x) => (Mathf.Exp(x) + Mathf.Exp(-x)) * 0.5f;
    /// sag/L = (cosh k − 1) / (2k) を k について解く（k = L / 2a）
    static float SolveCatenary(float sagRatio)
    {
        if (sagRatio < 1e-4f) return 0f;
        float lo = 1e-4f, hi = 12f;
        for (int it = 0; it < 60; it++) { float m = 0.5f * (lo + hi); if ((Cosh(m) - 1f) / (2f * m) < sagRatio) lo = m; else hi = m; }
        return 0.5f * (lo + hi);
    }
    static Vector3 Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float u)
    {
        float u2 = u * u, u3 = u2 * u;
        return (2f * u3 - 3f * u2 + 1f) * p0 + (u3 - 2f * u2 + u) * m0 + (-2f * u3 + 3f * u2) * p1 + (u3 - u2) * m1;
    }

    /// 方向の変わりが deg 度を超える点だけ残す（両端は残す）
    static List<Vector3> Decimate(List<Vector3> p, float deg)
    {
        if (p.Count <= 2) return new List<Vector3>(p);
        var r = new List<Vector3> { p[0] }; var last = p[0];
        for (int j = 1; j < p.Count - 1; j++)
        {
            var a = p[j] - last; var b = p[j + 1] - p[j];
            if (a.sqrMagnitude < 1e-12f || b.sqrMagnitude < 1e-12f) continue;
            if (Vector3.Angle(a, b) > deg) { r.Add(p[j]); last = p[j]; }
        }
        r.Add(p[p.Count - 1]);
        return r;
    }

    // ------------------------------------------------------------ 止め結び（knots.json の end_thru_overhand）
    static List<Vector3> s_knot;
    static List<Vector3> LoadKnot()
    {
        if (s_knot != null) return s_knot;
        var ta = Load<TextAsset>(KnotsGuid); if (ta == null) ta = AssetDatabase.LoadAssetAtPath<TextAsset>(KnotsPath); if (ta == null) return null;
        string txt = ta.text; int c = txt.IndexOf("\"end_thru_overhand\""); if (c < 0) return null;
        int pt = txt.IndexOf("\"points\"", c); if (pt < 0) return null;
        int pa = txt.IndexOf('[', pt), pb = txt.IndexOf("]]", pa);
        var nums = Regex.Matches(txt.Substring(pa, pb - pa), @"-?\d+(\.\d+)?([eE][-+]?\d+)?").Cast<Match>().Select(m => float.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        var res = new List<Vector3>();
        for (int i = 0; i + 2 < nums.Count; i += 3) res.Add(new Vector3(nums[i], nums[i + 1], nums[i + 2]));
        // 区間の側（+X）の穴の口より外は使わない（区間がそこから始まる）
        int cut = res.FindIndex(q => q.x <= Mouth + 1e-5f);
        if (cut > 0) res.RemoveRange(0, cut);
        res[0] = new Vector3(Mouth, KnotY, 0f);
        s_knot = res;
        return res;
    }

    /// 結び目の点列を杭の向き（+X＝区間の側、+Y＝杭の上、+Z）へ。太さが φ32 と違えば、穴の外の部分を穴の口を中心に縮める
    static List<Vector3> KnotWorld(List<Vector3> knot, Frame F, float zSign, float s, float yh)
    {
        var m0 = new Vector3(-Mouth, KnotY, 0f); var res = new List<Vector3>(knot.Count);
        foreach (var p0 in knot)
        {
            var q = p0;
            if (q.x < -Mouth) q = m0 + (q - m0) * s;
            q.y += yh - KnotY;
            res.Add(F.O + F.X * q.x + F.U * q.y + F.Z * (q.z * zSign));
        }
        return res;
    }

    // ------------------------------------------------------------ 8 角の管（しめ縄の Prism8 と同じ決まり: 頂点の法線は放射、u = k/N − s/P、v = s/Ltile）
    static float Tube(List<Vector3> w, Matrix4x4 toLocal, float D, List<Vector3> V, List<Vector3> Nn, List<Vector2> UV, List<int> Tr)
    {
        var p = w.Select(q => toLocal.MultiplyPoint3x4(q)).ToList(); int n = p.Count;
        float r = RvOverD * D, Pt = PitchOverD * D, Lt = TileOverD * D;
        var S = new float[n]; for (int i = 1; i < n; i++) S[i] = S[i - 1] + Vector3.Distance(p[i], p[i - 1]);
        var T = new Vector3[n]; var N = new Vector3[n]; var scale = new float[n]; var kdir = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 a = p[Mathf.Max(i - 1, 0)], b = p[Mathf.Min(i + 1, n - 1)];
            Vector3 d0 = p[i] - a, d1 = b - p[i];
            var t = (d0.sqrMagnitude > 1e-12f ? d0.normalized : Vector3.zero) + (d1.sqrMagnitude > 1e-12f ? d1.normalized : Vector3.zero);
            T[i] = t.sqrMagnitude > 1e-12f ? t.normalized : (b - a).normalized;
            float half = (d0.sqrMagnitude > 1e-12f && d1.sqrMagnitude > 1e-12f) ? Vector3.Angle(d0, d1) * 0.5f * Mathf.Deg2Rad : 0f;
            scale[i] = 1f / Mathf.Max(0.5f, Mathf.Cos(half));
            if (scale[i] > 1.0001f) { var kd = d1.normalized - d0.normalized; kd -= T[i] * Vector3.Dot(kd, T[i]); kdir[i] = kd.sqrMagnitude > 1e-10f ? kd.normalized : Vector3.zero; }
        }
        N[0] = Vector3.up - T[0] * T[0].y; N[0] = N[0].sqrMagnitude < 1e-6f ? Vector3.Cross(T[0], Vector3.right).normalized : N[0].normalized;
        float minDot = 1f;
        for (int i = 1; i < n; i++)
        {
            var q = N[i - 1] - T[i] * Vector3.Dot(T[i], N[i - 1]);
            N[i] = q.sqrMagnitude < 1e-12f ? Vector3.Cross(T[i], Vector3.up).normalized : q.normalized;
            minDot = Mathf.Min(minDot, Vector3.Dot(N[i], N[i - 1]));
        }
        int W = Sides + 1;
        for (int i = 0; i < n; i++)
        {
            var bn = Vector3.Cross(N[i], T[i]);
            for (int k = 0; k <= Sides; k++)
            {
                float th = Theta0 + 2f * Mathf.PI * k / Sides;
                var e = N[i] * Mathf.Cos(th) + bn * Mathf.Sin(th);
                var off = e * r;
                if (scale[i] > 1.0001f && kdir[i] != Vector3.zero) off += kdir[i] * (Vector3.Dot(off, kdir[i]) * (scale[i] - 1f));   // 曲がりで管が細らない
                V.Add(p[i] + off); Nn.Add(e); UV.Add(new Vector2(k / (float)Sides - S[i] / Pt, S[i] / Lt));
            }
        }
        for (int i = 0; i < n - 1; i++)
            for (int k = 0; k < Sides; k++)
            {
                int a = i * W + k, b = a + 1, c = (i + 1) * W + k + 1, d = (i + 1) * W + k;
                var want = Nn[a] + Nn[b] + Nn[c] + Nn[d];
                Tri(V, Tr, a, b, c, want); Tri(V, Tr, a, c, d, want);
            }
        return minDot;
    }
    static void Tri(List<Vector3> V, List<int> Tr, int a, int b, int c, Vector3 want)
    {
        var g = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
        if (Vector3.Dot(g, want) >= 0) { Tr.Add(a); Tr.Add(b); Tr.Add(c); } else { Tr.Add(a); Tr.Add(c); Tr.Add(b); }
    }

    static Mesh SaveMesh(string path, List<Vector3> V, List<Vector3> N, List<Vector2> UV, List<int> T)
    {
        EnsureFolder(GenDir.TrimEnd('/'));
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool create = m == null;
        if (create) m = new Mesh();
        m.Clear();
        m.name = System.IO.Path.GetFileNameWithoutExtension(path);
        m.indexFormat = V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.SetTriangles(T, 0);
        m.RecalculateTangents(); m.RecalculateBounds();
        if (create) AssetDatabase.CreateAsset(m, path);
        else EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssetIfDirty(m);
        return m;
    }

    /// "Assets/a/b/c" を上から順に作る（あれば何もしない）
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int k = path.LastIndexOf('/');
        string parent = path.Substring(0, k);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(k + 1));
    }

    static Transform Child(Transform root, string name, bool create)
    {
        var t = root.Find(name);
        if (t == null && create)
        {
            var g = new GameObject(name); g.transform.SetParent(root, false);
            GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
            t = g.transform;
        }
        return t;
    }
    static bool SetRenderer(GameObject g, Mesh mesh, Material mat)
    {
        bool ch = false;
        var mf = g.GetComponent<MeshFilter>(); if (mf == null) { mf = g.AddComponent<MeshFilter>(); ch = true; }
        var mr = g.GetComponent<MeshRenderer>(); if (mr == null) { mr = g.AddComponent<MeshRenderer>(); ch = true; }
        if (mf.sharedMesh != mesh) { mf.sharedMesh = mesh; ch = true; }
        if (mr.sharedMaterial != mat) { mr.sharedMaterial = mat; ch = true; }
        return ch;
    }
    static bool PlaceEnd(Transform t, Vector3 pos, Vector3 dir, float s, Mesh mesh, Material mat)
    {
        bool ch = SetRenderer(t.gameObject, mesh, mat);
        var rot = Quaternion.FromToRotation(Vector3.down, dir);
        if ((t.position - pos).sqrMagnitude > 1e-12f || Quaternion.Angle(t.rotation, rot) > 0.01f || Mathf.Abs(t.localScale.x - s) > 1e-5f)
        { t.SetPositionAndRotation(pos, rot); t.localScale = Vector3.one * s; ch = true; }
        return ch;
    }

    // ------------------------------------------------------------ 検査
    public struct Audit { public float minStakeGap, minGround; public int badStake, badGround, points; }

    /// ロープの点ごとに、杭の 8 角の面までのすき間（自分の穴の中・穴の口の線の上は除く）と、地面までの高さを測る。
    /// extraStake を渡すと、その位置に鉛直の杭が 1 本あるとして測る（負の確認用）。
    static Audit AuditCore(List<Vector3> pts, List<Frame> fr, float D, Transform root, Vector3? extraStake)
    {
        var frames = new List<Frame>(fr);
        if (extraStake.HasValue) frames.Add(new Frame { O = extraStake.Value, X = Vector3.right, U = Vector3.up, Z = Vector3.forward, yh = fr[0].yh });
        float rv = RvOverD * D, ra = ApOverD * D;
        var a = new Audit { minStakeGap = float.MaxValue, minGround = float.MaxValue, points = pts.Count };
        foreach (var p in pts)
        {
            foreach (var F in frames)
            {
                var d = p - F.O; float y = Vector3.Dot(d, F.U);
                if (y < StakeBottom - rv || y > StakeTop + rv) continue;
                float x = Vector3.Dot(d, F.X), z = Vector3.Dot(d, F.Z);
                // 穴の線の上（穴の中と、口から縄が出入りする所）は除く
                float off = new Vector2(y - F.yh, z).magnitude;
                if (Mathf.Abs(x) <= Mouth + rv + 0.004f && off < HoleR) continue;
                float sup = 0f;
                for (int k = 0; k < 8; k++) { float ang = k * Mathf.PI / 4f; sup = Mathf.Max(sup, x * Mathf.Cos(ang) + z * Mathf.Sin(ang)); }
                float vert = y > StakeTop ? y - StakeTop : 0f;
                float gap = Mathf.Max(sup - Apothem, vert) - ra;
                if (sup - Apothem > 2f * rv + 0.1f) continue;   // 遠い
                a.minStakeGap = Mathf.Min(a.minStakeGap, gap);
                if (gap < -0.002f) a.badStake++;
            }
            if (GroundAt(p.x, p.z, p.y, root, out float gy))
            {
                float gg = p.y - rv - gy;
                a.minGround = Mathf.Min(a.minGround, gg);
                if (gg < -0.002f) a.badGround++;
            }
        }
        if (a.minStakeGap == float.MaxValue) a.minStakeGap = 9.999f;
        if (a.minGround == float.MaxValue) a.minGround = 9.999f;
        return a;
    }

    /// Eval からの検査用: 最後に作ったロープの検査。extraStake で負の確認（そこに杭を立てたら食い込みが出るか）
    public static string AuditLast(SimpleRopeFence f, Vector3? extraStake = null)
    {
        if (!s_last.TryGetValue(f.GetInstanceID(), out var L)) return "まだ作っていない";
        var a = AuditCore(L.pts, L.frames, L.D, Root(f), extraStake);
        return "points=" + a.points + " minStakeGapMm=" + (a.minStakeGap * 1000f).ToString("F2") + " badStake=" + a.badStake + " minGroundMm=" + (a.minGround * 1000f).ToString("F2") + " badGround=" + a.badGround;
    }
    /// Eval からの検査用: 最後に作ったロープの点列（ワールド）
    public static List<Vector3> LastPoints(SimpleRopeFence f) => s_last.TryGetValue(f.GetInstanceID(), out var L) ? L.pts : null;

    // ------------------------------------------------------------ 変わったかどうか
    public static int StateHash(SimpleRopeFence f, List<Transform> st)
    {
        unchecked
        {
            int h = 17;
            void A(float v) { h = h * 31 + Mathf.RoundToInt(v * 10000f); }
            A(f.sagPercent); A(f.randomness); h = h * 31 + f.seed; A(f.tiltMax); A(f.ropeDiameter); A(f.stakeHeight); h = h * 31 + st.Count;
            h = h * 31 + (f.stakeMesh ? f.stakeMesh.GetInstanceID() : 0) + (f.ropeMaterial ? f.ropeMaterial.GetInstanceID() : 0) * 7 + (f.endMesh ? f.endMesh.GetInstanceID() : 0) * 13;
            foreach (var t in st)
            {
                var p = t.position; var q = t.rotation;
                A(p.x); A(p.y); A(p.z); A(q.x); A(q.y); A(q.z); A(q.w);
            }
            return h;
        }
    }

    [MenuItem("you5248/Simple Rope Fence/選んだ柵を今すぐ作り直す", false, 20)]
    static void MenuRebuild()
    {
        var f = FindFence(Selection.activeGameObject);
        if (f == null) { EditorUtility.DisplayDialog("杭とロープの柵", "柵（またはその杭）を選んでください。", "OK"); return; }
        Debug.Log("[SimpleRopeFence] " + Build(f));
    }

    [MenuItem("you5248/Simple Rope Fence/ビルドに残らないか確かめる", false, 40)]
    static void MenuCheck()
    {
        var msg = SimpleRopeFenceBuildGuard.Describe();
        Debug.Log("[SimpleRopeFence] " + msg);
        EditorUtility.DisplayDialog("杭とロープの柵", msg, "OK");
    }
}

// エディタで杭が動いたら（設定が変わったら）0.3 秒待って作り直す。マウスで何かをつかんでいる間は待つ。再生中・コンパイル中は何もしない。
[InitializeOnLoad]
static class SimpleRopeFenceAutoRebuild
{
    const double Debounce = 0.3, Poll = 0.1;
    static double s_nextPoll, s_nextScan;
    static SimpleRopeFence[] s_list;
    static readonly Dictionary<int, (int hash, double t)> s_pending = new Dictionary<int, (int, double)>();

    static SimpleRopeFenceAutoRebuild()
    {
        EditorApplication.update += Tick;
        EditorApplication.hierarchyChanged += () => s_nextScan = 0;
    }

    static void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        double now = EditorApplication.timeSinceStartup;
        if (now < s_nextPoll) return;
        s_nextPoll = now + Poll;
        if (s_list == null || now >= s_nextScan) { s_list = Object.FindObjectsByType<SimpleRopeFence>(FindObjectsInactive.Exclude, FindObjectsSortMode.None); s_nextScan = now + 2.0; }
        foreach (var f in s_list)
        {
            if (f == null || !f.isActiveAndEnabled || EditorUtility.IsPersistent(f) || SimpleRopeFenceBuilder.Root(f) == null) continue;
            if (UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(f.gameObject) != null) continue;
            int id = f.GetInstanceID();
            int h = SimpleRopeFenceBuilder.StateHash(f, SimpleRopeFenceBuilder.Stakes(f));
            if (h == f.builtHash) { s_pending.Remove(id); continue; }
            if (!s_pending.TryGetValue(id, out var p) || p.hash != h) { s_pending[id] = (h, now); continue; }
            if (now - p.t < Debounce || GUIUtility.hotControl != 0) continue;
            s_pending.Remove(id);
            try { SimpleRopeFenceBuilder.Build(f); }
            catch (System.Exception e) { Debug.LogException(e); f.builtHash = h; }   // 同じ失敗を繰り返さない
        }
    }
}
}
