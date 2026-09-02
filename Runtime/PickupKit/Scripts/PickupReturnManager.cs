using UdonSharp;
using UnityEngine;

namespace you5248.PickupKit
{
    // 【任意・非必須】戻り処理は各 PickupReturnTarget が自前で tick する自己完結方式に変更したため、
    // このマネージャは無くても動作する。中央巡回（targets[] 参照配列）はエディタからの参照
    // シリアライズが壊れやすく "永遠に戻らない" 不具合の原因になり得たため廃止した。
    //
    // 既存シーンに残っていても無害なように、ここでは何もしない（互換のためクラスのみ残置）。
    // シーン上に本コンポーネントがあっても削除して構わない。
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class PickupReturnManager : UdonSharpBehaviour
    {
        [Header("（非推奨）旧・中央巡回の名残。各 Target が自前で動くため未使用。")]
        public PickupReturnTarget[] targets;

        // 旧バージョン互換: 万一どこかから呼ばれても安全な空実装。
        public void _Tick() { }
    }
}
