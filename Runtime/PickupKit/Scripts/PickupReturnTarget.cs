using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace you5248.PickupKit
{
    // 1つのピックアップに付く「原点記憶 ＋ N分後に戻る」コンポーネント。自己完結型。
    //
    // ＜なぜ自己完結にしたか＞
    //   以前は中央マネージャが targets[] を持って巡回していたが、エディタスクリプトから設定した
    //   「UdonSharpBehaviour 参照の配列」「コンポーネント参照」は実機の UdonBehaviour 側へ
    //   シリアライズされないことがあり、実行時に空になって "永遠に戻らない" 不具合が出やすい。
    //   そこで本コンポーネントは:
    //     - 自前で SendCustomEventDelayedSeconds の tick を回す（マネージャ不要）
    //     - pickup / objectSync は実行時 GetComponent で取得（参照シリアライズに依存しない）
    //   とした。確実にシリアライズされる float（minutes）だけ合っていれば動く。
    //
    // ＜戻り判定（位置ベース・OnDrop非依存）＞
    //   - 掴まれている間（VRC_Pickup.IsHeld）はカウントしない
    //   - 原点付近（homeThreshold 以内）にある間はカウントしない
    //   - 原点から離れて放置された瞬間から計測し、minutes 分経過したら戻す
    //
    // ＜戻し方（同期モデル自動両対応）＞
    //   - VRCObjectSync あり : 必要ならオーナーを取得して Respawn()（全員に反映）
    //   - VRCObjectSync なし : ローカルで原点へ戻す
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class PickupReturnTarget : UdonSharpBehaviour
    {
        [Header("放置から戻るまでの時間（分）")]
        public float minutes = 5f;

        [Header("判定間隔（秒）")]
        [Tooltip("この秒数ごとに自前で戻り判定する。N分戻りには2秒で十分軽い。")]
        public float checkInterval = 2f;

        [Header("原点とみなす距離（m）")]
        [Tooltip("原点からこの距離以内なら『戻っている』とみなし計測しない")]
        public float homeThreshold = 0.05f;

        [Header("デバッグログ（原因切り分け用）")]
        [Tooltip("ON にすると計測開始・経過秒・戻り実行を Console に出す")]
        public bool debugLog = false;

        private VRC_Pickup _pickup;
        private VRCObjectSync _objectSync;
        private Rigidbody _rb;
        private Vector3 _homePos;
        private Quaternion _homeRot;
        private float _awaySince;     // 原点を離れて放置され始めた時刻。-1 は未計測。
        private float _returnSeconds;

        private void Start()
        {
            _homePos = transform.position;
            _homeRot = transform.rotation;
            _awaySince = -1f;
            if (minutes < 0f) minutes = 0f;
            _returnSeconds = minutes * 60f;
            if (checkInterval < 0.2f) checkInterval = 0.2f;

            _pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
            _objectSync = (VRCObjectSync)GetComponent(typeof(VRCObjectSync));
            _rb = (Rigidbody)GetComponent(typeof(Rigidbody));

            // 自前 tick 開始
            SendCustomEventDelayedSeconds(nameof(_Tick), checkInterval);
        }

        public void _Tick()
        {
            _CheckReturn(Time.time);
            SendCustomEventDelayedSeconds(nameof(_Tick), checkInterval);
        }

        // 位置ベースの戻り判定（外部から呼ばれても良いように public のまま残す）
        public void _CheckReturn(float now)
        {
            // 掴まれている間はカウントしない
            if (Utilities.IsValid(_pickup) && _pickup.IsHeld)
            {
                _awaySince = -1f;
                return;
            }

            float dist = Vector3.Distance(transform.position, _homePos);
            if (dist <= homeThreshold)
            {
                // すでに原点付近にある
                _awaySince = -1f;
                return;
            }

            // 原点から離れて放置中
            if (_awaySince < 0f)
            {
                _awaySince = now;
                if (debugLog) Debug.Log("[ReturnTarget] " + name + " 計測開始 dist=" + dist.ToString("F2") + "m / 戻りまで" + _returnSeconds + "s");
                return;
            }

            float elapsed = now - _awaySince;
            if (debugLog) Debug.Log("[ReturnTarget] " + name + " 経過 " + elapsed.ToString("F0") + "/" + _returnSeconds + "s dist=" + dist.ToString("F2") + "m");

            if (elapsed >= _returnSeconds)
            {
                _ReturnNow();
                _awaySince = -1f;
            }
        }

        private void _ReturnNow()
        {
            if (Utilities.IsValid(_objectSync))
            {
                // 同期ピックアップ: オーナーでなければ取得してから戻す（放置者が退出/移動しても確実に戻す）。
                bool owner = Networking.IsOwner(Networking.LocalPlayer, gameObject);
                if (!owner)
                {
                    Networking.SetOwner(Networking.LocalPlayer, gameObject);
                    owner = Networking.IsOwner(Networking.LocalPlayer, gameObject);
                }
                if (owner)
                {
                    if (debugLog) Debug.Log("[ReturnTarget] " + name + " → VRCObjectSync.Respawn()");
                    _objectSync.Respawn();
                }
                else if (debugLog)
                {
                    Debug.Log("[ReturnTarget] " + name + " オーナー取得失敗のため今回はスキップ（次回再試行）");
                }
            }
            else
            {
                // 非同期: ローカルで原点へ戻す（速度も消す）
                if (Utilities.IsValid(_rb) && !_rb.isKinematic)
                {
                    _rb.velocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
                if (debugLog) Debug.Log("[ReturnTarget] " + name + " → ローカルで原点へ");
                transform.SetPositionAndRotation(_homePos, _homeRot);
            }
        }
    }
}
