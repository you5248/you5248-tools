// Lightweight Quest replacement for the seasonal leaf-cover glass variants.
Shader "you5248/Quest/Glass LeafCover NoGrab"
{
    Properties
    {
        [Header(Glass)]
        _BaseColor ("Base Color", 2D) = "black" {}
        _BaseColorTint ("Base Color Tint", Color) = (1,1,1,1)
        _GrabpassTint ("Legacy Glass Tint", Color) = (1,1,1,1)
        _SpecularityTint ("Specularity Tint", Color) = (1,1,1,1)
        _Roughness ("Roughness", Range(0,1)) = 0
        _Metallic ("Metallic", Range(0,1)) = 0
        _ReflectionStrength ("Reflection Strength", Range(0,2)) = 1
        _SpecularStrength ("Specular Strength", Range(0,2)) = 1

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

        [Header(Quest)]
        _QuestTint ("Fallback Tint", Color) = (0.72,0.82,0.9,1)
        _QuestOpacity ("Opacity", Range(0,1)) = 0.16
        _QuestFresnelStrength ("Fresnel Opacity", Range(0,1)) = 0.55

        [HideInInspector] _Culling ("PC Culling (unused)", Float) = 2
        [HideInInspector] _ZWrite ("PC ZWrite (unused)", Float) = 0
        [HideInInspector] _BlendMode ("PC Blend Mode (unused)", Float) = 0
        [HideInInspector] _Refraction ("PC Refraction (unused)", Float) = 0
        [HideInInspector] _Blur ("PC Blur (unused)", Float) = 0
        [HideInInspector] _SSRToggle ("PC SSR (unused)", Float) = 0
        [HideInInspector] _RainMode ("PC Rain (unused)", Float) = 0
        [HideInInspector] _AreaLitToggle ("PC AreaLit (unused)", Float) = 0
        [HideInInspector] _LTCGI ("PC LTCGI (unused)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "ForceNoShadowCaster"="True"
            "IgnoreProjector"="True"
        }
        LOD 100
        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "ForwardBase"
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex QuestGlassVert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_instancing

            #include "QuestGlassCommon.cginc"
            #include "LeafCoverQuestCommon.cginc"

            half4 frag(QuestGlassV2F i, fixed facing : VFACE) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 glass = QuestGlassShade(i, facing);

                half3 geometricNormal = normalize(i.worldNormal);
                half3 coverNormal = half3(
                    geometricNormal.x,
                    abs(geometricNormal.y),
                    geometricNormal.z);
                half4 leaf = LeafCoverQuestEvaluate(i.worldPos, coverNormal);

                half underside = facing >= 0 ? 1.0h : 0.7h;
                half3 leafLighting = QuestGlassDiffuseLighting(i, coverNormal);
                half3 leafPremultiplied = leaf.rgb * leafLighting * underside;

                half4 color;
                color.rgb = leafPremultiplied + glass.rgb * (1.0h - leaf.a);
                color.a = leaf.a + glass.a * (1.0h - leaf.a);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, color, half4(0,0,0,0));
                return color;
            }
            ENDCG
        }
    }
    FallBack Off
}
