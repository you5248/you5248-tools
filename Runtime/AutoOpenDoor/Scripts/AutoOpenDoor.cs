using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 近づいたら自動で開く扉（ローカル限定・開きっぱなし・音1回）。
// この GameObject に IsTrigger=true のコライダーを付けて使う。
// pivot は扉の親に置いた「ヒンジ位置の Transform」を指定する想定。
public class AutoOpenDoor : UdonSharpBehaviour
{
    [Header("回転対象（ヒンジのピボット。未指定ならこの Transform）")]
    [SerializeField] private Transform pivot;

    [Header("閉じた状態からの相対回転（degrees）")]
    [SerializeField] private Vector3 openDeltaEuler = new Vector3(0f, 90f, 0f);

    [Header("開ききるまでの秒数")]
    [SerializeField] private float duration = 1.2f;

    [Header("開く瞬間に1回鳴らす音（任意）")]
    [SerializeField] private AudioSource openSoundSource;
    [SerializeField] private AudioClip openSoundClip;

    private Quaternion _closedRot;
    private Quaternion _openRot;
    private bool _triggered;
    private bool _animating;
    private float _elapsed;

    private void Start()
    {
        if (pivot == null) pivot = transform;
        _closedRot = pivot.localRotation;
        _openRot = _closedRot * Quaternion.Euler(openDeltaEuler);
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (_triggered) return;
        if (player == null || !player.isLocal) return;

        _triggered = true;
        _animating = true;
        _elapsed = 0f;

        if (openSoundSource != null && openSoundClip != null)
        {
            openSoundSource.PlayOneShot(openSoundClip);
        }
    }

    private void Update()
    {
        if (!_animating) return;

        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / Mathf.Max(0.0001f, duration));
        // smootherstep: 6t^5 - 15t^4 + 10t^3 （SmoothStep より両端が滑らかなS字）
        float s = t * t * t * (t * (t * 6f - 15f) + 10f);
        pivot.localRotation = Quaternion.Slerp(_closedRot, _openRot, s);

        if (t >= 1f)
        {
            pivot.localRotation = _openRot;
            _animating = false;
        }
    }
}
