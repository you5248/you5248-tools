using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace you5248.DebugAreaTeleport
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class DebugAreaTeleport : UdonSharpBehaviour
    {
        [Header("テレポート先")]
        [Tooltip("プレイヤーをここに飛ばす (未指定ならこのCubeの位置)")]
        [SerializeField] private Transform teleportDestination;

        [Header("エリア切替")]
        [Tooltip("インタラクト時に ON にするエリア")]
        [SerializeField] private GameObject areaToEnable;

        [Tooltip("インタラクト時に OFF にするエリア (複数指定可)")]
        [SerializeField] private GameObject[] areasToDisable;

        [Header("リスポーン更新 (任意)")]
        [Tooltip("DebugRespawnManager を指定するとリスポーン地点もこのテレポート先になる")]
        [SerializeField] private DebugRespawnManager respawnManager;

        [Header("VR/Desktop 互換")]
        [Tooltip("ON: playspace ごと運ぶ (VRで頭/体デシンク防止) — 通常はON推奨")]
        [SerializeField] private bool alignRoomWithSpawnPoint = true;

        [Tooltip("Area切替後にテレポートを1フレ遅らせる (Collider初期化待ち)。床抜け対策。")]
        [SerializeField] private bool deferTeleportOneFrame = true;

        // 1フレ遅延テレポート用の保持
        private Vector3 _pendingPos;
        private Quaternion _pendingRot;
        private bool _pendingPending;

        void Start()
        {
            // インタラクト文字列を分かりやすく
            if (areaToEnable != null)
                InteractionText = "▶ " + areaToEnable.name + " へワープ";
            else
                InteractionText = "▶ Debug Warp";
        }

        public override void Interact()
        {
            VRCPlayerApi player = Networking.LocalPlayer;
            if (player == null) return;

            // 1) 行き先エリアを先に ON (collider 等が有効化される)
            if (areaToEnable != null && !areaToEnable.activeSelf)
                areaToEnable.SetActive(true);

            // 2) テレポート先を解決
            Vector3 pos;
            Quaternion rot;
            if (teleportDestination != null)
            {
                pos = teleportDestination.position;
                rot = teleportDestination.rotation;
            }
            else
            {
                pos = transform.position;
                rot = transform.rotation;
            }

            // 3) リスポーン地点を更新 (テレポート前でも後でも順不同)
            if (respawnManager != null)
                respawnManager.SetRespawnPoint(pos, rot);

            // 4) テレポート (deferするなら次フレで実行、即実行なら即TP+即disable)
            if (deferTeleportOneFrame)
            {
                _pendingPos = pos;
                _pendingRot = rot;
                _pendingPending = true;
                SendCustomEventDelayedFrames(nameof(_DoDeferredTeleport), 1);
            }
            else
            {
                DoTeleport(player, pos, rot);
                DisableOldAreas();
            }
        }

        // SendCustomEventDelayedFrames で呼ばれる
        public void _DoDeferredTeleport()
        {
            if (!_pendingPending) return;
            _pendingPending = false;

            VRCPlayerApi player = Networking.LocalPlayer;
            if (player == null) return;

            DoTeleport(player, _pendingPos, _pendingRot);
            DisableOldAreas();
        }

        private void DoTeleport(VRCPlayerApi player, Vector3 pos, Quaternion rot)
        {
            if (alignRoomWithSpawnPoint)
            {
                // playspace ごと運ぶ (VRで頭/体デシンク防止)
                player.TeleportTo(pos, rot,
                    VRC_SceneDescriptor.SpawnOrientation.AlignRoomWithSpawnPoint,
                    /*lerpOnRemote*/ false);
            }
            else
            {
                player.TeleportTo(pos, rot);
            }
        }

        private void DisableOldAreas()
        {
            if (areasToDisable == null) return;
            int n = areasToDisable.Length;
            for (int i = 0; i < n; i++)
            {
                GameObject a = areasToDisable[i];
                if (a != null && a != areaToEnable)
                    a.SetActive(false);
            }
        }
    }
}
