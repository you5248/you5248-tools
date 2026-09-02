// Lightweight Quest replacement for the seasonal snow-cover glass variants.
Shader "you5248/Quest/Glass SnowCover NoGrab"
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

        [Header(Snow Cover)]
        [HDR] _SnowColor ("Snow Color", Color) = (1,1,1,0)
        [HDR] _SnowFresnelColor ("Snow Fresnel Color", Color) = (0.684,0.908,1,0)
        _SnowFresnelScale ("Snow Fresnel Scale", Range(0,5)) = 1
        _SnowPosition ("Snow Position", Float) = 0
        [Toggle] _SnowSharpMask ("Snow Sharp Mask", Float) = 1
        _SnowAmount ("Snow Amount", Range(0,1)) = 1

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

            float4 _SnowColor;
            float4 _SnowFresnelColor;
            half _SnowFresnelScale;
            half _SnowPosition;
            half _SnowSharpMask;
            half _SnowAmount;

            half4 frag(QuestGlassV2F i, fixed facing : VFACE) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 glass = QuestGlassShade(i, facing);

                half3 geometricNormal = normalize(i.worldNormal);
                half snowMask = saturate(abs(geometricNormal.y) + _SnowPosition);
                if (_SnowSharpMask >= 0.5h)
                    snowMask = saturate((snowMask - 0.5h) * 10.0h + 0.5h);
                snowMask *= _SnowAmount;

                half3 coverNormal = half3(
                    geometricNormal.x,
                    abs(geometricNormal.y),
                    geometricNormal.z);
                half3 viewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
                half snowFresnel = saturate(
                    _SnowFresnelScale
                    * pow(1.0h - saturate(dot(coverNormal, viewDir)), 3.0h));
                half3 snowAlbedo = lerp(
                    _SnowColor.rgb, _SnowFresnelColor.rgb, snowFresnel);
                half underside = facing >= 0 ? 1.0h : 0.85h;
                half3 snowPremultiplied = snowAlbedo
                    * QuestGlassDiffuseLighting(i, coverNormal)
                    * underside
                    * snowMask;

                half4 color;
                color.rgb = snowPremultiplied + glass.rgb * (1.0h - snowMask);
                color.a = snowMask + glass.a * (1.0h - snowMask);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, color, half4(0,0,0,0));
                return color;
            }
            ENDCG
        }
    }
    FallBack Off
}
