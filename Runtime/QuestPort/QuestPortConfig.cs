using System;
using UnityEngine;

namespace you5248.QuestPort
{
    /// <summary>
    /// モバイル（Android / iOS）ビルド時のシーン差し替え設定。
    /// ビルドプロセッサがインメモリで適用し、シーンファイル自体は書き換えない。
    /// </summary>
    [CreateAssetMenu(fileName = "QuestPortConfig", menuName = "you5248/Quest Port Config")]
    public class QuestPortConfig : ScriptableObject
    {
        public const string DefaultAssetPath = "Assets/you5248/QuestPort/QuestPortConfig.asset";

        [Serializable]
        public class MaterialSwap
        {
            [Tooltip("PC版マテリアル（差し替え元）")]
            public Material sourceMat;

            [Tooltip("Quest版マテリアル（差し替え先）")]
            public Material questMat;
        }

        [Serializable]
        public class FontSwap
        {
            [Tooltip("PC版フォント（差し替え元）")]
            public Font sourceFont;

            [Tooltip("Quest版フォント（差し替え先）")]
            public Font questFont;
        }

        [Serializable]
        public class MeshSwap
        {
            [Tooltip("PC版メッシュ（差し替え元）")]
            public Mesh sourceMesh;

            [Tooltip("Quest版メッシュ（差し替え先）")]
            public Mesh questMesh;
        }

        [Header("無効化 / 破棄する GameObject（シーン階層パス）")]
        [Tooltip("ルートからの階層パス。例: Environment/FarTrees/TreeCluster")]
        public string[] disableObjectPaths = Array.Empty<string>();

        [Tooltip(
            "モバイルビルド時のみ、この階層パス配下の Renderer から BatchingStatic を解除し、" +
            "共有マテリアルの GPU Instancing を有効化する。PC 版には影響しない。")]
        public string[] disableStaticBatchingPaths = Array.Empty<string>();

        [Tooltip(
            "ON（既定）: 実ビルド時は Object.DestroyImmediate で破棄し AssetBundle から除外する。" +
            "OFF: SetActive(false) のみ。PlayMode 突入時は常に SetActive(false)。")]
        public bool destroyInsteadOfDisable = true;

        [Tooltip(
            "非モバイルの実ビルド時のみ DestroyImmediate する階層パス。" +
            "モバイル専用参照保持オブジェクトを PC バンドルから除外する用途。" +
            "PlayMode（report=null）では何もしない。")]
        public string[] destroyOnPcPaths = Array.Empty<string>();

        [Tooltip(
            "モバイルのビルド/PlayMode 時のみ SetActive(true) する階層パス。" +
            "シーン上では非アクティブにしておくモバイル専用オブジェクト" +
            "（モバイル用設定ボード等）の自動有効化に使う。PC では触らない。")]
        public string[] enableOnAndroidPaths = Array.Empty<string>();

        [Header("マテリアル差し替え")]
        [Tooltip("Renderer.sharedMaterials および Terrain.materialTemplate を差し替える")]
        public MaterialSwap[] materialSwaps = Array.Empty<MaterialSwap>();

        [Header("メッシュ差し替え")]
        [Tooltip("MeshFilter.sharedMesh を差し替える（モバイルビルド時のみ・シーンは保存しない）")]
        public MeshSwap[] meshSwaps = Array.Empty<MeshSwap>();

        [Header("フォント差し替え")]
        [Tooltip("UI/Udon 等がシリアライズ保持する Font をモバイルビルド時だけ差し替える")]
        public FontSwap[] fontSwaps = Array.Empty<FontSwap>();

        [Header("モバイル ライティング")]
        [Tooltip("Realtime/Mixed の Point/Spot/Area Light をモバイルビルドから除去する")]
        public bool removeRealtimeLocalLights = true;

        [Tooltip("全 Light のリアルタイムシャドウを無効化する")]
        public bool disableRealtimeShadows = true;

        [Header("Post Process Volume")]
        [Tooltip("ON のとき、シーン内の PostProcessVolume コンポーネントを無効化／破棄する（同居する Collider 等は維持）")]
        public bool removePostProcessVolumes = true;
    }
}
