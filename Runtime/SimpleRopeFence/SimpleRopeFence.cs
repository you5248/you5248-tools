using UnityEngine;

namespace you5248.RopeFence
{
// 杭とロープの柵（Simple Rope Fence）。杭を直接動かして位置を調整する、設定を絞った柵。
// 杭はこの物の直接の子（STAKE_THRU のメッシュを持つ物）。Scene ビューで杭を選び、Unity 標準の移動・回転で動かすと、
// エディタが 0.3 秒後にロープを作り直す（Editor/SimpleRopeFence/SimpleRopeFenceBuilder.cs）。ロープは Assets/you5248/SimpleRopeFence/Generated/ のメッシュ asset。
// この部品は設定を持つだけで、実行時は何もしない。EditorOnly タグの物に置くのでビルドでは Unity が外す（外に残ればビルドを止める: SimpleRopeFenceBuildGuard.cs）。
[DisallowMultipleComponent]
[AddComponentMenu("you5248/杭とロープの柵（設定）")]
public class SimpleRopeFence : MonoBehaviour, VRC.SDKBase.IEditorOnly
{
    [Tooltip("杭の本数。変えると、始点の杭と終点の杭の間に等間隔で並べ直す")]
    [Range(2, 30)] public int stakeCount = 5;

    [Tooltip("たわみ: 杭と杭の間の中央でロープが下がる量（間の長さに対する %）。下がりすぎて地面に届くと、ロープは地面の上に乗る")]
    [Range(0f, 40f)] public float sagPercent = 4f;

    [Tooltip("ランダム性 0〜1: たわみと杭の高さのばらつきの大きさ（0 で全部そろう）")]
    [Range(0f, 1f)] public float randomness = 0.3f;

    [Tooltip("ばらつきの種。変えると振り直し（杭の傾きも振り直す）")]
    public int seed = 1;

    [Tooltip("杭の傾きの幅（度）: 各杭をランダムな向きに、0〜この角度だけ傾ける。0 で鉛直。変えると全部の杭の傾きを振り直す（手で傾けた杭も）")]
    [Range(0f, 15f)] public float tiltMax = 3f;

    [Tooltip("ロープの太さ m。既定 32mm は同梱のしめ縄の太さ（杭の穴は φ37）")]
    [Range(0.006f, 0.036f)] public float ropeDiameter = 0.032f;

    [Tooltip("杭の高さ m（地面から頭まで）。焼いた杭の地上の長さが 0.60m なので、低くするときは深く差し込む")]
    [Range(0.40f, 0.60f)] public float stakeHeight = 0.60f;

    // ---- ふだん触らない部品（空なら既定の焼いた部品を使う） ----
    [HideInInspector] public Mesh stakeMesh;
    [HideInInspector] public Material stakeMaterial;
    [HideInInspector] public Material ropeMaterial;
    [HideInInspector] public Mesh endMesh;
    [HideInInspector] public Material endMaterial;

    // ---- エディタが使う記録 ----
    [HideInInspector] public string buildId;
    [HideInInspector] public int builtHash;
    [HideInInspector] public string lastReport;
}
}
