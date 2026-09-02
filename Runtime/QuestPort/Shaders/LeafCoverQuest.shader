// Quest counterpart of you5248/LeafCover Standard.
Shader "you5248/Quest/LeafCover Standard"
{
    Properties
    {
        [Header(Base)]
        _MainTexture ("Main Texture", 2D) = "white" {}
        _MainColor ("Main Color", Color) = (1,1,1,1)
        [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalScale ("Normal Scale", Float) = 1
        _Glossiness ("Smoothness", Range(0,1)) = 0.2
        [Gamma] _Metallic ("Metallic", Range(0,1)) = 0

        [Header(Leaf)]
        _LeafTex ("Leaf Texture", 2D) = "white" {}
        _LeafColor ("Color A", Color) = (1,1,1,1)
        _LeafColorB ("Color B", Color) = (1,0.85,0.9,1)
        _TintVariation ("Tint Variation", Range(0,1)) = 0.25
        _LeafOpacity ("Leaf Opacity", Range(0,1)) = 1
        _LeafSmoothness ("Leaf Smoothness", Range(0,1)) = 0.3

        [Header(Scatter)]
        _Density ("Cell Density", Float) = 3.3
        _Coverage ("Coverage", Range(0,1)) = 0.5
        _SizeMin ("Minimum Size", Float) = 0.05
        _SizeMax ("Maximum Size", Float) = 0.07
        _Jitter ("Position Jitter", Range(0,1)) = 0.9
        [Toggle] _RandomFlip ("Random Horizontal Flip", Float) = 1
        _Seed ("Random Seed", Float) = 0
        _Layers ("PC Layer Count (density approximation)", Range(1,3)) = 2

        [Header(Cover Mask)]
        _CoverPosition ("Cover Position", Range(-2,2)) = 0.15
        [Toggle] _SharpMask ("Sharp Mask", Float) = 1
        _SlopeFix ("Slope Stretch Correction", Range(0,1)) = 1

        [Header(Render)]
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("Cull Mode", Float) = 2

    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 150
        Cull [_CullMode]

        CGPROGRAM
        #pragma surface surf Standard noforwardadd noshadow nolppv exclude_path:deferred exclude_path:prepass
        #pragma target 3.5
        #pragma multi_compile_instancing
        #pragma skip_variants DIRLIGHTMAP_COMBINED DIRLIGHTMAP_SEPARATE

        #include "LeafCoverQuestCommon.cginc"

        sampler2D _MainTexture;
        sampler2D _NormalMap;
        float4 _MainColor;
        float _NormalScale;
        float _Glossiness;
        float _Metallic;

        struct Input
        {
            float2 uv_MainTexture;
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float4 baseColor = tex2D(_MainTexture, IN.uv_MainTexture) * _MainColor;
            o.Normal = UnpackScaleNormal(
                tex2D(_NormalMap, IN.uv_MainTexture), _NormalScale);

            float3 geometricNormal = WorldNormalVector(IN, float3(0, 0, 1));
            float4 leaf = LeafCoverQuestEvaluate(IN.worldPos, geometricNormal);
            float leafAlpha = leaf.a;

            o.Albedo = leaf.rgb + baseColor.rgb * (1.0 - leafAlpha);
            o.Metallic = _Metallic * (1.0 - leafAlpha);
            o.Smoothness = lerp(_Glossiness, _LeafSmoothness, leafAlpha);
            o.Occlusion = 1;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack Off
}
