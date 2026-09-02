// you5248 / FloatingPetals  ―― v1（要・Unity / VRChat 実機での確認）
//
// 水面に浮かぶ花びら（桜など）を、板ポリ1枚に手続き的に散らすシェーダー。
// 水の表現は持たない（Mochie の Water の上に薄板を重ねて使う想定）。
//
// 使い方:
//   1. 横向き(水平)の Quad/Plane を水面のすぐ上に置く。
//   2. このシェーダーのマテリアルを割り当て、_MainTex に「1枚の花びら(RGBA, 背景透明)」を入れる。
//   3. 立体感が欲しければ Quad を少し高さを変えて複数枚重ね、各マテリアルの _Seed を変える。
//
// 散らし方:
//   ワールドXZをセル格子に分割し、各セルをハッシュして 位置/サイズ/回転/揺れ位相 を決め、
//   そのセルに花びらテクスチャを1枚スタンプする（3x3近傍を見て境界をつなぐ）。
//
// 注意:
//   - 花びら1枚の大きさ(_SizeMax)はセル1個(=1/_Density メートル)より小さく保つこと。
//     大きすぎると 3x3 近傍をはみ出して端が切れる。
//   - _FOAM_ON（物の周りに溜める）は _CameraDepthTexture が必要。
//     VRChat ではリアルタイム影付きの平行光源があるとだいたい有効。無い場合は _DensityMask で代用。
//
// 実装: 散布ロジックは FloatingPetalsCore.cginc に抽出（挙動等価）。

Shader "you5248/FloatingPetals"
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

        [Header(Render  byouga)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("カリング", Float) = 0 // Off
        _AmbientStrength ("環境光 (ShadeSH9)", Range(0,1)) = 0.0
        _AlphaClip      ("透明ピクセル破棄しきい値 (軽量化)", Range(0,0.2)) = 0.004
    }

    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        LOD 200

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

            #include "FloatingPetalsCore.cginc"
            ENDCG
        }
    }
    Fallback Off
}
