using UnityEngine;
using System.Collections.Generic;

namespace you5248.MeshAtlasTool
{

    /// <summary>
    /// アトラス変換前の状態を記録するリバートデータ。
    /// 実行フォルダ内に保存され、ツールから読み込んで変換前に戻すことができる。
    /// </summary>
    public class AtlasRevertData : ScriptableObject
    {
        public enum ModeAtTime
        {
            MergeAndAtlas_CreateNew,
            AtlasOnly_CreateNew,
            AtlasOnly_ReplaceInPlace,
        }

        [System.Serializable]
        public class OriginalEntry
        {
            // シーンオブジェクトへの直接参照はアセット保存時に null になる（ドメインリロードまでの同一セッション専用）。
            // リロード後は targetGlobalId (GlobalObjectId 文字列) から解決する。
            public GameObject  target;
            public string      targetGlobalId;
            public Mesh[]      originalMeshes;     // MeshFilter.sharedMesh (ReplaceInPlace)
            public Material[]  originalMaterials;  // MeshRenderer.sharedMaterials
            public bool        wasActive;
            public string      originalTag;
        }

        public ModeAtTime          mode;
        public List<OriginalEntry> originals    = new List<OriginalEntry>();
        public GameObject          createdRoot;         // CreateNew 系モードで生成した親 GameObject（同一セッション専用）
        public string              createdRootGlobalId; // 生成直後はシーン未保存で ID が確定しないことがある
        public string              createdRootName;     // ID が解決できない場合の名前フォールバック用
    }
}
