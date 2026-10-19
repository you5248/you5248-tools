using UnityEditor;
using UnityEngine;

namespace you5248.RopeFence
{
// 杭とロープの柵の設定のインスペクター（項目は絞ってある）。
// 柵の物・杭・ロープ・房のどれを選んでも、インスペクターの上にこの設定が出る（SimpleRopeFenceHeader）。
[CustomEditor(typeof(SimpleRopeFence))]
public class SimpleRopeFenceEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        var f = (SimpleRopeFence)target;
        var root = SimpleRopeFenceBuilder.Root(f);
        if (root == null) { EditorGUILayout.HelpBox("この「設定」は柵の物の子に置いてください（親がありません）。", MessageType.Error); return; }

        EditorGUILayout.HelpBox("杭（「杭 01」など）を Scene ビューで選び、移動・回転ツールでそのまま動かせます。手を離して 0.3 秒でロープが付いてきます。\n" +
                                "・杭の足元はいつも地面に合わせます（高さは下の「杭の高さ」）\n" +
                                "・杭の縦の軸の回りの向きは、穴がロープの通る向きになるよう自動で回します\n" +
                                "・手で傾けた分は残ります（「傾きの幅」「シード」を変えると全部振り直し）\n" +
                                "・杭を複製（Ctrl+D）すると 1 本増え、並びの順にロープを通します", MessageType.Info);

        int count = SimpleRopeFenceBuilder.Stakes(f).Count;
        EditorGUI.BeginChangeCheck();
        int n = EditorGUILayout.IntSlider(new GUIContent("杭の本数", "変えると、始点の杭と終点の杭の間に等間隔で並べ直します（途中の杭の手の調整は消えます）"), count, 2, 30);
        if (EditorGUI.EndChangeCheck() && n != count)
        {
            Undo.RecordObject(f, "杭の本数"); f.stakeCount = n;
            SimpleRopeFenceBuilder.Rearrange(f);
        }
        // 全体の長さ（始点の杭〜終点の杭の水平距離）。表示はいつも今の実測（杭を手で動かすと追従する）。値を入れて確定したときだけ杭を動かす
        float curLen = Mathf.Round(SimpleRopeFenceBuilder.TotalLength(f) * 1000f) / 1000f;
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        float newLen = EditorGUILayout.DelayedFloatField(new GUIContent("全体の長さ（m）",
            "始点の杭（杭 01）から終点の杭（最後の杭）までの水平の距離。表示は今の実測です。\n" +
            "値を入れて Enter で確定すると、始点の杭は動かさず、終点の杭を今の向きのままその距離へ動かし、間の杭を等間隔に並べ直します（" +
            SimpleRopeFenceBuilder.MinLength.ToString("0.0") + "〜" + SimpleRopeFenceBuilder.MaxLength.ToString("0") + "m）"), curLen);
        bool lenChanged = EditorGUI.EndChangeCheck() && Mathf.Abs(newLen - curLen) > 1e-5f;
        bool lenButton = GUILayout.Button(new GUIContent("この長さに合わせる", "左の長さに合わせて、終点の杭を置き直し、間の杭を等間隔に並べ直します"), GUILayout.Width(120));
        EditorGUILayout.EndHorizontal();
        if (lenChanged || lenButton)
            SimpleRopeFenceBuilder.SetTotalLength(f, Mathf.Clamp(lenChanged ? newLen : curLen, SimpleRopeFenceBuilder.MinLength, SimpleRopeFenceBuilder.MaxLength));
        if (GUILayout.Button(new GUIContent("始点と終点の間に等間隔で並べ直す", "途中の杭を、始点（杭 01）と終点（最後の杭）を結ぶまっすぐな線の上に等間隔で置き直します")))
        {
            Undo.RecordObject(f, "並べ直す"); f.stakeCount = count;
            SimpleRopeFenceBuilder.Rearrange(f);
        }

        EditorGUILayout.Space(4);
        EditorGUI.BeginChangeCheck();
        float sag = EditorGUILayout.Slider(new GUIContent("たわみ（%）", "杭と杭の間の中央でロープが下がる量。間の長さに対する %。地面に届くとロープは地面に乗ります"), f.sagPercent, 0f, 40f);
        float rnd = EditorGUILayout.Slider(new GUIContent("ランダム性", "たわみと杭の高さのばらつき（0 で全部そろう）"), f.randomness, 0f, 1f);
        float dmm = EditorGUILayout.Slider(new GUIContent("ロープの太さ（mm）", "既定 32mm は同梱のしめ縄の太さ（杭の穴は φ37）"), f.ropeDiameter * 1000f, 6f, 36f);
        float hgt = EditorGUILayout.Slider(new GUIContent("杭の高さ（m）", "地面から杭の頭まで。焼いた杭の地上の長さが 0.60m なので、低くするときは深く差し込みます"), f.stakeHeight, 0.40f, 0.60f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(f, "柵の設定");
            f.sagPercent = sag; f.randomness = rnd; f.ropeDiameter = Mathf.Round(dmm) / 1000f; f.stakeHeight = hgt;
            EditorUtility.SetDirty(f);   // 0.3 秒後に自動で作り直す
        }

        EditorGUI.BeginChangeCheck();
        float tilt = EditorGUILayout.Slider(new GUIContent("杭の傾きの幅（度）", "各杭をランダムな向きに 0〜この角度だけ傾けます。0 で鉛直。変えると全部の杭の傾きを振り直します"), f.tiltMax, 0f, 15f);
        EditorGUILayout.BeginHorizontal();
        int seed = EditorGUILayout.IntField(new GUIContent("シード", "ばらつきの種。変えると、たわみ・高さ・傾きを振り直します"), f.seed);
        if (GUILayout.Button("振り直す", GUILayout.Width(72))) seed = f.seed + 1;
        EditorGUILayout.EndHorizontal();
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(f, "柵の傾き");
            f.tiltMax = tilt; f.seed = seed; EditorUtility.SetDirty(f);
            SimpleRopeFenceBuilder.ApplyTilt(f);
        }

        EditorGUILayout.Space(4);
        if (GUILayout.Button("今すぐ作り直す")) SimpleRopeFenceBuilder.Build(f);
        if (!string.IsNullOrEmpty(f.lastReport))
            EditorGUILayout.HelpBox(f.lastReport, f.lastReport.StartsWith("○") ? MessageType.None : f.lastReport.StartsWith("×") ? MessageType.Error : MessageType.Warning);
        if (!f.CompareTag("EditorOnly"))
            EditorGUILayout.HelpBox("「設定」の物のタグが EditorOnly ではありません。このままだとアップロードを止めます（タグを EditorOnly に戻してください）。", MessageType.Error);
    }
}

// 柵の物・杭・ロープ・房を選んだとき、インスペクターの見出しの下に柵の設定を出す
[InitializeOnLoad]
static class SimpleRopeFenceHeader
{
    static UnityEditor.Editor s_cached;
    static SimpleRopeFenceHeader() { UnityEditor.Editor.finishedDefaultHeaderGUI += OnHeader; }

    static void OnHeader(UnityEditor.Editor ed)
    {
        if (ed == null || ed.targets.Length != 1) return;
        var go = ed.target as GameObject;
        if (go == null || go.GetComponent<SimpleRopeFence>() != null) return;   // 設定の物そのものはふつうに出る
        var f = SimpleRopeFenceBuilder.FindFence(go);
        if (f == null) return;
        var st = SimpleRopeFenceBuilder.Stakes(f);
        int idx = st.IndexOf(go.transform);
        var root = SimpleRopeFenceBuilder.Root(f);
        EditorGUILayout.Space(2);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            string what = idx >= 0 ? "この杭は「" + root.name + "」の " + (idx + 1) + " 本目（全 " + st.Count + " 本）" + (idx == 0 ? "・始点" : idx == st.Count - 1 ? "・終点" : "")
                        : go.transform == root ? "杭とロープの柵「" + root.name + "」" : "「" + root.name + "」の部品（作り直すたびに置き直すので、動かさないでください）";
            EditorGUILayout.LabelField(what, EditorStyles.boldLabel);
            UnityEditor.Editor.CreateCachedEditor(f, typeof(SimpleRopeFenceEditor), ref s_cached);
            s_cached.serializedObject.Update();
            s_cached.OnInspectorGUI();
        }
    }
}
}
