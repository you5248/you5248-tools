// you5248 / FloatingPetalsMeniscus  ―― GrabPass 縁屈折（メニスカス）版
//
// FloatingPetals と同じ散布に、花びら縁の盛り上がり屈折リングを加算する。
// 水シェーダーの上に薄板として重ねて使う想定。
// 散布コアは FloatingPetalsCore.cginc（_MENISCUS_ON 時のみ追加パスが有効）。

Shader "you5248/FloatingPetalsMeniscus"
{
    Properties
    {
        [Header(Petal  hanabira)]
        _MainTex        ("花びらテクスチャ (RGBA 背景透明)", 2D) = "white" {}
        _Color          ("色A", Color) = (1,1,1,1)
        _ColorB         ("色B (ばらつきの行き先)", Color) = (1, 0.85, 0.9, 1)
        _TintVariation  ("色のばらつき", Range(0,1)) = 0.25
        _Opacity        ("不透明度", Range(0,1)) = 1.0

        [Header(Scatter  chirabari)]
        _Density        ("セル密度 (1mあたりのセル数)", Float) = 1.5
        _Coverage       ("基本の量 (背景の湧き率 薄め推奨)", Range(0,1)) = 0.25
        _SizeMin        ("花びらサイズ 最小 (m)", Float) = 0.07
        _SizeMax        ("花びらサイズ 最大 (m)", Float) = 0.16
        _Jitter         ("位置のばらつき", Range(0,1)) = 0.9
        [Toggle] _RandomFlip ("左右反転をランダムに", Float) = 1
        _Seed           ("乱数シード (板ごとに変える)", Float) = 0
        _Layers         ("重ね枚数 (1-3)", Range(1,3)) = 1

        [Header(Motion  fuyofuyo)]
        _SpinSpeed      ("自転スピード (rad/s)", Float) = 0.12
        _SwaySpeed      ("首振りスピード", Float) = 1.0
        _SwayAmount     ("首振りの大きさ (rad)", Float) = 0.4
        _DriftSpeed     ("浮遊スピード", Float) = 0.5
        _DriftAmount    ("浮遊の大きさ (m)", Float) = 0.03

        [Header(Flow  nagare)]
        _FlowDir        ("流れの向き (m/s X,Y=ワールドXZ)", Vector) = (0,0,0,0)

        [Header(Wave Normal  yurasu)]
        [Toggle(_WAVE_ON)] _UseWave ("波ノーマルを使う", Float) = 0
        [Normal] _WaveTex ("波ノーマルマップ", 2D) = "bump" {}
        _WaveScale      ("波スケール (1/m)", Float) = 0.5
        _WaveSpeed      ("波スクロール XZ", Vector) = (0.02,0.013,0,0)
        _WaveStrength   ("波の強さ (m)", Float) = 0.05

        [Header(Cling around objects  matowaritsuku)]
        [Toggle(_FOAM_ON)] _UseDepthFoam ("物の周りに溜める ON", Float) = 0
        _FoamDistance   ("まとわりつく幅 (m)", Float) = 0.6
        _FoamPower      ("帯のシャープさ (大きいほど際に集中)", Range(0.5,8)) = 3.0
        _CoverageNear   ("物の近くの量 (上乗せ密集レイヤー)", Range(0,1)) = 1.0
        _FoamDensityMul ("際の細かさ (背景の何倍のセル数)", Range(1,6)) = 2.5
        _FoamSizeScale  ("際の花びらサイズ倍率", Range(0.2,1.5)) = 0.7
        [Toggle(_MASK_ON)] _UseDensityMask ("密度マスクを使う (OFFで軽量)", Float) = 0
        _DensityMask    ("密度マスク (UVで塗る 代用可)", 2D) = "white" {}
        [Toggle(_DEBUG_FOAM)] _DebugFoam ("デバッグ：際の検出を赤で表示", Float) = 0

        [Header(Meniscus  menisukasu)]
        [Toggle(_MENISCUS_ON)] _UseMeniscus ("メニスカス屈折 ON", Float) = 1
        [Toggle] _MeniscusFoam ("際の密集層にもメニスカス", Float) = 1
        _MeniscusWidth    ("縁帯幅(テクスチャUV)", Range(0.01,0.3)) = 0.08
        _MeniscusStrength ("屈折強さ(m)", Range(0,0.05)) = 0.01
        _MeniscusOpacity  ("リング不透明度", Range(0,1)) = 1
        _MeniscusSign     ("盛り上がり方向", Range(-1,1)) = 1
        _MeniscusContrast ("帯コントラスト", Range(1,10)) = 4

        [Header(Render  byouga)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("カリング", Float) = 0 // Off
        _AmbientStrength ("環境光 (ShadeSH9)", Range(0,1)) = 0.0
        _AlphaClip      ("透明ピクセル破棄しきい値 (軽量化)", Range(0,0.2)) = 0.004
    }

    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        LOD 200

        // 名前付き GrabPass（SPS-I では Texture2DArray として Core 側で宣言）
        GrabPass { "_FPGrab" }

        Pass
        {
            Cull [_Cull]
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha   // premultiplied alpha

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   4.0
            #pragma multi_compile_fog
            #pragma shader_feature_local _WAVE_ON
            #pragma shader_feature_local _FOAM_ON
            #pragma shader_feature_local _MASK_ON
            #pragma shader_feature_local _DEBUG_FOAM
            #pragma shader_feature_local _MENISCUS_ON

            #include "FloatingPetalsCore.cginc"
            ENDCG
        }
    }
    Fallback Off
}
