using UdonSharp;
using UnityEngine;

namespace you5248.PickupKit
{
    // 「掴むと物理ON / 離すと固定」ハイブリッド挙動。ピックアップと同じ GameObject に付ける。
    //   - 待機中     : Rigidbody.isKinematic = true（その場に固定・落ちない）
    //   - 掴んだ瞬間 : isKinematic = false（物理で振り回せる）
    //   - 離した瞬間 : isKinematic = true（離した位置でピタッと固定）
    //
    // 物理は本来クライアントローカル。VRCObjectSync が付いている場合はオーナーの物理結果が
    // 同期されるため、掴んだ人がオーナー → 投げ味も同期、離した位置で全員に固定される。
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class PickupHybridFreeze : UdonSharpBehaviour
    {
        [Header("掴んでいる間は重力も有効にする")]
        public bool useGravityWhileHeld = true;

        private Rigidbody _rb;

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _Freeze();
        }

        public override void OnPickup()
        {
            if (_rb == null) return;
            _rb.isKinematic = false;
            _rb.useGravity = useGravityWhileHeld;
        }

        public override void OnDrop()
        {
            _Freeze();
        }

        private void _Freeze()
        {
            if (_rb == null) return;
            // kinematic にする前に速度を殺してから固定（離した位置でピタッと止める）
            if (!_rb.isKinematic)
            {
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.isKinematic = true;
        }
    }
}
