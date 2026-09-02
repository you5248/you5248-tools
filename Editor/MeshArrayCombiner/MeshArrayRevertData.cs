using System.Collections.Generic;
using UnityEngine;

namespace you5248.MeshArrayCombiner
{

    // MeshArrayCombiner の変換を元に戻すための復元データ。生成サブフォルダに保存される。
    public class MeshArrayRevertData : ScriptableObject
    {
        public enum Mode { CreateNew, ReplaceInPlace }

        [System.Serializable]
        public class Entry
        {
            public GameObject target;              // 元オブジェクト
            public Mesh       originalMesh;        // ReplaceInPlace 用：元メッシュ
            public Material[] originalMaterials;   // ReplaceInPlace 用：元マテリアル
            public bool       wasActive;           // CreateNew 用：元の有効状態
        }

        public Mode             mode;
        public string           generatedFolder;  // 生成サブフォルダ（Revert 時に丸ごと削除）
        public GameObject       createdRoot;       // CreateNew で作ったルート
        public List<Entry>      originals = new List<Entry>();
    }
}
