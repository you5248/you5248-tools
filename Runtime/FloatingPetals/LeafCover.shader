// you5248 / LeafCover Standard ―― 春夏秋の「葉/花びらが積もる」不透明サーフェスシェーダー
//
// SnowCover/Standard の「上向き面に積もる」法線Yマスクと、
// FloatingPetals のセル散布スタンプを合体させた派生シェーダー。
// 雪（単色lerp）の代わりに、葉/花びらテクスチャを1枚ずつ上向き面に積もらせる。
// 散布ロジック本体は LeafCoverCommon.cginc（アレイ版 MeshArrayPBR LeafCover と共有）。
//
// FloatingPetals との違い:
//   - Opaque サーフェスシェーダー（ベースPBR + 葉レイヤー）。水面板ではなく物体そのものに使う
//   - 葉は静止（ドリフト/自転/首振り/流れ/波/foam は持たない）
//   - 散布はワールドXZの真上投影。ワールド法線Yマスクで上向き面にだけ出す
//     （湧き判定は葉の中心で1回＝境界の葉は丸ごと出るか丸ごと消えるかの二択）

Shader "you5248/LeafCover Standard"
{
    Properties
    {
        [Header(Base)]
        _MainTexture ("Main Texture", 2D) = "white" {}
        _MainColor   ("Main Color", Color) = (1,1,1,1)
        [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalScale ("Normal Scale", Float) = 1
        _Glossiness  ("Smoothness", Range(0,1)) = 0.2
        [Gamma] _Metallic ("Metallic", Range(0,1)) = 0.0

        [Header(Leaf happa)]
        _LeafTex       ("葉テクスチャ (RGBA 背景透明)", 2D) = "white" {}
        _LeafColor     ("色A", Color) = (1,1,1,1)
        _LeafColorB    ("色B (ばらつきの行き先)", Color) = (1, 0.85, 0.9, 1)
        _TintVariation ("色のばらつき", Range(0,1)) = 0.25
        _LeafOpacity   ("葉の不透明度", Range(0,1)) = 1.0
        _LeafSmoothness("葉のSmoothness", Range(0,1)) = 0.3

        [Header(Scatter chirabari)]
        _Density  ("セル密度 (1mあたりのセル数)", Float) = 3.3
        _Coverage ("量 (セルの湧き率)", Range(0,1)) = 0.5
        _SizeMin  ("葉サイズ 最小 (m)", Float) = 0.05
        _SizeMax  ("葉サイズ 最大 (m)", Float) = 0.07
        _Jitter   ("位置のばらつき", Range(0,1)) = 0.9
        [Toggle] _RandomFlip ("左右反転をランダムに", Float) = 1
        _Seed     ("乱数シード", Float) = 0
        _Layers   ("重ね枚数 (1-3)", Range(1,3)) = 2

        [Header(Cover mask tsumori)]
        _CoverPosition ("積もり位置 (SnowCoverの_SnowPosition相当)", Range(-2,2)) = 0.15
        [Toggle] _SharpMask ("マスクをシャープに", Float) = 1
        _SlopeFix ("斜面の伸び補正 (1=完全補正)", Range(0,1)) = 1

        [Header(Render)]
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("Cull Mode", Float) = 2

        [Header(MonoSH  Bakery no Directional Mode wo MonoSH de bake suru koto)]
        [Toggle(_MONOSH_ON)] _UseMonoSH ("MonoSH (方向つきライトマップ)", Float) = 0
        [Toggle(_MONOSHSPEC_ON)] _UseMonoSHSpec ("  ベイクスペキュラ (焼いた光の艶)", Float) = 0
        _MonoSHSpecMul ("  ベイクスペキュラ強さ", Range(0,2)) = 1
        [ToggleUI] _MonoSHNonlinear ("  ノンリニア補正 (推奨 ON)", Float) = 1

    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+0" }
        LOD 200
        Cull [_CullMode]

        CGPROGRAM
        #pragma surface surf StandardMonoSH fullforwardshadows
        #pragma shader_feature_local _MONOSH_ON
        #pragma shader_feature_local _MONOSHSPEC_ON
        #include "Packages/com.you5248.tools/Runtime/Shaders/you5248_MonoSH.cginc"
        #pragma target 4.0

        #include "LeafCoverCommon.cginc"

        sampler2D _MainTexture;
        sampler2D _NormalMap;
        float4 _MainColor;
        float  _NormalScale;
        float  _Glossiness, _Metallic;

        struct Input
        {
            float2 uv_MainTexture;
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float4 baseCol = tex2D(_MainTexture, IN.uv_MainTexture) * _MainColor;
            o.Normal = UnpackScaleNormal(tex2D(_NormalMap, IN.uv_MainTexture), _NormalScale);

            // マスク/散布は頂点由来のワールド法線（ノーマルマップ適用前）で評価する
            float3 geoN = WorldNormalVector(IN, float3(0, 0, 1));
            float4 leaf = LeafCover_Evaluate(IN.worldPos, geoN);

            float leafA = leaf.a;
            o.Albedo     = leaf.rgb + baseCol.rgb * (1.0 - leafA); // leafはpremultiplied
            o.Metallic   = _Metallic * (1.0 - leafA);
            o.Smoothness = lerp(_Glossiness, _LeafSmoothness, leafA);
            o.Alpha      = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
