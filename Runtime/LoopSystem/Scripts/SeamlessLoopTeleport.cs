using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class SeamlessLoopTeleport : UdonSharpBehaviour
{
    [Header("ワープ先の出口地点（GameObject）")]
    [SerializeField] private Transform targetPoint;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player.isLocal) 
        {
            // 1. トリガーとプレイヤーの相対位置・回転を計算
            Quaternion triggerRotation = transform.rotation;
            Vector3 relativePosition = Quaternion.Inverse(triggerRotation) * (player.GetPosition() - transform.position);
            Quaternion relativeRotation = Quaternion.Inverse(triggerRotation) * player.GetRotation();

            // 2. ターゲット地点の座標と回転を取得
            Vector3 targetPos = targetPoint.position;
            Quaternion targetRot = targetPoint.rotation;

            // 3. ターゲット地点に、保持していた相対的な位置と回転を上乗せしてテレポート
            Vector3 finalPosition = targetRot * relativePosition + targetPos;
            Quaternion finalRotation = targetRot * relativeRotation;

            player.TeleportTo(finalPosition, finalRotation);
        }
    }
}
