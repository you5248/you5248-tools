using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace you5248.DebugAreaTeleport
{
    /// <summary>
    /// シーン上に1つ置くマネージャー。
    /// DebugAreaTeleport が SetRespawnPoint を呼ぶと、その位置を「次のリスポーン地」として記憶し、
    /// プレイヤーがリスポーンしたとき (落下・メニューからリスポーン等) に即座にそこへ TeleportTo する。
    /// 検証用なのでローカルのみ・同期なし。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class DebugRespawnManager : UdonSharpBehaviour
    {
        [Tooltip("ON: playspace ごと運ぶ (VRで頭/体デシンク防止)")]
        [SerializeField] private bool alignRoomWithSpawnPoint = true;

        [Header("ループ防止")]
        [Tooltip("この秒数内に複数回 Respawn が発火したらオーバーライド解除 (ループ脱出)")]
        [SerializeField] private float loopWindowSeconds = 2.0f;

        [Tooltip("ウィンドウ内で何回 Respawn したら異常とみなすか")]
        [SerializeField] private int loopThreshold = 3;

        private Vector3 _pos;
        private Quaternion _rot;
        private bool _hasOverride = false;

        // ループ検出用
        private float _windowStart = 0f;
        private int _respawnCount = 0;

        /// <summary>DebugAreaTeleport から呼ばれる。</summary>
        public void SetRespawnPoint(Vector3 pos, Quaternion rot)
        {
            _pos = pos;
            _rot = rot;
            _hasOverride = true;
            // 新しい目的地が設定されたらループカウンタもリセット
            _windowStart = Time.realtimeSinceStartup;
            _respawnCount = 0;
        }

        /// <summary>オーバーライドを解除 (vanillaリスポーンに戻す)。</summary>
        public void ClearRespawnPoint()
        {
            _hasOverride = false;
        }

        public override void OnPlayerRespawn(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) return;
            if (!_hasOverride) return;

            // ループ検出: 短時間に複数回 Respawn が発火 = テレポート先がリスポーン高度以下等
            float now = Time.realtimeSinceStartup;
            if (now - _windowStart > loopWindowSeconds)
            {
                // ウィンドウリセット
                _windowStart = now;
                _respawnCount = 0;
            }
            _respawnCount++;

            if (_respawnCount > loopThreshold)
            {
                // ループ確定 → オーバーライド解除して vanilla リスポーンに任せる
                _hasOverride = false;
                Debug.LogWarning($"[DebugRespawnManager] Teleport loop detected " +
                    $"({_respawnCount} respawns in {loopWindowSeconds}s). " +
                    $"Override cleared. Check that destination Y ({_pos.y:F2}) is above the scene's Respawn Height Y.");
                return;
            }

            if (alignRoomWithSpawnPoint)
            {
                player.TeleportTo(_pos, _rot,
                    VRC_SceneDescriptor.SpawnOrientation.AlignRoomWithSpawnPoint,
                    /*lerpOnRemote*/ false);
            }
            else
            {
                player.TeleportTo(_pos, _rot);
            }
        }
    }
}
