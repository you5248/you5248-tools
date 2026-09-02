// Lightweight Quest replacement for Mochie/Glass (Two Pass).
// No GrabPass, refraction, blur, SSR, rain, AreaLit, LTCGI, ForwardAdd, or
// shadow pass. Cull Off keeps both faces visible in one draw pass.
Shader "you5248/Quest/Glass NoGrab"
{
    Properties
    {
        _BaseColor ("Base Color", 2D) = "black" {}
        _BaseColorTint ("Base Color Tint", Color) = (1,1,1,1)
        _GrabpassTint ("Legacy Glass Tint", Color) = (1,1,1,1)
        _SpecularityTint ("Specularity Tint", Color) = (1,1,1,1)
        _Roughness ("Roughness", Range(0,1)) = 0
        _Metallic ("Metallic", Range(0,1)) = 0
        _ReflectionStrength ("Reflection Strength", Range(0,2)) = 1
        _SpecularStrength ("Specular Strength", Range(0,2)) = 1

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

            half4 frag(QuestGlassV2F i, fixed facing : VFACE) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 color = QuestGlassShade(i, facing);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, color, half4(0,0,0,0));
                return color;
            }
            ENDCG
        }
    }
    FallBack Off
}
