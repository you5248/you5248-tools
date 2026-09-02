using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon.Common;

namespace you5248.PickupKit
{
    // VR で右スティックを上に holdSeconds 秒倒し続けると、空いているペンを右手の位置へ呼び寄せる。
    //
    // 仕様（ユーザー要望）:
    //   - 呼ぶのは "誰にも持たれていない" ペンだけ（VRC_Pickup.IsHeld で除外）
    //   - "原点付近にある" ペンを最優先（散らかったペンより未使用ペンを呼ぶ）
    //   - 同期のため、呼ぶ瞬間にオーナーを取得し、VRCObjectSync があれば不連続フラグで瞬間移動
    //
    // 入力: 右スティック上 = InputLookVertical（VRの右スティック縦・上方向が正）。
    //       デスクトップでは右スティック＝視点操作のため無効化している。
    //       ※ 機種/設定で上下が逆なら inputThreshold を負方向に読むよう調整、もしくは
    //         handleUpIsPositive を false に。
    //
    // QvPen 専用型には依存しない（VRC_Pickup 参照のみ）。QvPen でも自作ピックアップペンでも動く。
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class QvPenSummoner : UdonSharpBehaviour
    {
        [Header("呼び寄せ対象のペン（VRC Pickup を持つオブジェクト）")]
        public VRC_Pickup[] pens;

        [Header("右スティック上をこの秒数押し続けると発動")]
        public float holdSeconds = 0.6f;

        [Header("スティック入力のしきい値（0-1）")]
        public float inputThreshold = 0.7f;

        [Header("上方向が正の値か（機種で逆なら OFF）")]
        public bool handleUpIsPositive = true;

        [Header("右手からペンを出すオフセット（手のローカル空間, m）")]
        public Vector3 handOffset = new Vector3(0f, 0f, 0.03f);

        [Header("原点付近とみなす距離（m）")]
        public float homeThreshold = 0.1f;

        private Transform[] _tf;
        private VRCObjectSync[] _sync;
        private Vector3[] _homePos;

        private VRCPlayerApi _local;
        private bool _isVR;
        private float _lookVertical;
        private float _holdTimer;
        private bool _consumed; // 一度発動したら、スティックを戻すまで再発動しない
        private int _lastChosen = -1; // 直前に呼んだペン。次回はこれを最優先で呼び戻す（置き場を根こそぎにしない）

        private void Start()
        {
            _local = Networking.LocalPlayer;
            if (Utilities.IsValid(_local)) _isVR = _local.IsUserInVR();

            int n = pens != null ? pens.Length : 0;
            _tf = new Transform[n];
            _sync = new VRCObjectSync[n];
            _homePos = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                VRC_Pickup p = pens[i];
                if (!Utilities.IsValid(p)) continue;
                _tf[i] = p.transform;
                _sync[i] = (VRCObjectSync)p.GetComponent(typeof(VRCObjectSync));
                _homePos[i] = p.transform.position;
            }
        }

        public override void InputLookVertical(float value, UdonInputEventArgs args)
        {
            _lookVertical = handleUpIsPositive ? value : -value;
        }

        private void Update()
        {
            if (!_isVR) return;

            if (_lookVertical >= inputThreshold)
            {
                if (_consumed) return;
                _holdTimer += Time.deltaTime;
                if (_holdTimer >= holdSeconds)
                {
                    _SummonPen();
                    _consumed = true;
                    _holdTimer = 0f;
                }
            }
            else
            {
                _holdTimer = 0f;
                _consumed = false;
            }
        }

        private void _SummonPen()
        {
            if (!Utilities.IsValid(_local)) return;

            // 右手のトラッキング位置
            VRCPlayerApi.TrackingData hand = _local.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
            Vector3 handPos = hand.position;
            Quaternion handRot = hand.rotation;
            Vector3 targetPos = handPos + handRot * handOffset;

            int n = pens != null ? pens.Length : 0;
            int chosen = -1;

            // 最優先: 直前に呼んだペンが空いていれば、それを再び呼び戻す。
            //   これが無いと「呼ぶ→離す→呼ぶ」のたびに、離して原点から外れたペンではなく
            //   まだ原点にある別のペンが選ばれ、置き場のペンが1本ずつ根こそぎ持ち出される。
            //   同じペンを呼び戻すことで未使用ペンを置き場に温存する。
            if (_lastChosen >= 0 && _lastChosen < n)
            {
                VRC_Pickup last = pens[_lastChosen];
                if (Utilities.IsValid(last) && _tf[_lastChosen] != null && !last.IsHeld)
                    chosen = _lastChosen;
            }

            // 次点: 原点付近の空きペン → それも無ければ任意の空きペン
            if (chosen < 0)
            {
                int chosenFree = -1; // 原点付近でなくても空いているペンの控え
                for (int i = 0; i < n; i++)
                {
                    VRC_Pickup p = pens[i];
                    if (!Utilities.IsValid(p) || _tf[i] == null) continue;
                    if (p.IsHeld) continue; // 誰かが持っているペンは呼ばない

                    if (chosenFree < 0) chosenFree = i;

                    float dist = Vector3.Distance(_tf[i].position, _homePos[i]);
                    if (dist <= homeThreshold)
                    {
                        chosen = i;
                        break;
                    }
                }
                if (chosen < 0) chosen = chosenFree;
            }

            if (chosen < 0) return; // 空きペンなし（全部誰かが使用中）

            _lastChosen = chosen;

            VRC_Pickup pen = pens[chosen];

            // オーナーを取得してから移動 → VRCObjectSync が全員へ同期
            Networking.SetOwner(_local, pen.gameObject);
            _tf[chosen].SetPositionAndRotation(targetPos, handRot);
            if (_sync[chosen] != null) _sync[chosen].FlagDiscontinuity();
        }
    }
}
