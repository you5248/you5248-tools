// Shared single-pass, no-GrabPass glass path for Quest.
#ifndef YOU5248_QUEST_GLASS_COMMON_INCLUDED
#define YOU5248_QUEST_GLASS_COMMON_INCLUDED

#include "UnityCG.cginc"
#include "Lighting.cginc"

sampler2D _BaseColor;
float4 _BaseColor_ST;
float4 _BaseColorTint;
float4 _GrabpassTint;
float4 _SpecularityTint;
float4 _QuestTint;
half _Roughness;
half _Metallic;
half _ReflectionStrength;
half _SpecularStrength;
half _QuestOpacity;
half _QuestFresnelStrength;

struct QuestGlassAppData
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct QuestGlassV2F
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 worldPos : TEXCOORD1;
    half3 worldNormal : TEXCOORD2;
    float2 lightmapUV : TEXCOORD3;
    UNITY_FOG_COORDS(4)
    UNITY_VERTEX_OUTPUT_STEREO
};

QuestGlassV2F QuestGlassVert(QuestGlassAppData v)
{
    QuestGlassV2F o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_OUTPUT(QuestGlassV2F, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = TRANSFORM_TEX(v.uv, _BaseColor);
    o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
    o.worldNormal = UnityObjectToWorldNormal(v.normal);
    o.lightmapUV = v.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
    UNITY_TRANSFER_FOG(o, o.pos);
    return o;
}

half3 QuestGlassFacingNormal(half3 worldNormal, half facing)
{
    half3 n = normalize(worldNormal);
    return facing >= 0 ? n : -n;
}

half3 QuestGlassDiffuseLighting(QuestGlassV2F i, half3 normalWorld)
{
    half3 indirect;
    #if defined(LIGHTMAP_ON)
        half4 encoded = UNITY_SAMPLE_TEX2D(unity_Lightmap, i.lightmapUV);
        indirect = DecodeLightmap(encoded);
    #else
        indirect = ShadeSH9(half4(normalWorld, 1));
    #endif

    half3 lightDir = normalize(UnityWorldSpaceLightDir(i.worldPos));
    half direct = saturate(dot(normalWorld, lightDir));
    return max(indirect + _LightColor0.rgb * direct, 0.02h);
}

half4 QuestGlassShade(QuestGlassV2F i, half facing)
{
    half3 normalWorld = QuestGlassFacingNormal(i.worldNormal, facing);
    half3 viewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
    half ndv = saturate(dot(normalWorld, viewDir));
    half fresnel = pow(1.0h - ndv, 5.0h);

    half4 baseSample = tex2D(_BaseColor, i.uv);
    half3 baseSurface = baseSample.rgb * _BaseColorTint.rgb;
    half3 diffuseLight = QuestGlassDiffuseLighting(i, normalWorld);

    half3 reflectionDirection = reflect(-viewDir, normalWorld);
    half reflectionLod = saturate(_Roughness) * 6.0h;
    half4 encodedReflection = UNITY_SAMPLE_TEXCUBE_LOD(
        unity_SpecCube0, reflectionDirection, reflectionLod);
    half3 reflection = DecodeHDR(encodedReflection, unity_SpecCube0_HDR);
    half reflectionWeight = lerp(0.18h, 1.0h, fresnel)
                          * _ReflectionStrength
                          * lerp(0.65h, 1.0h, _Metallic)
                          * lerp(1.0h, 0.45h, _Roughness);

    half3 lightDir = normalize(UnityWorldSpaceLightDir(i.worldPos));
    half3 halfDir = normalize(lightDir + viewDir);
    half smoothness = 1.0h - saturate(_Roughness);
    half specularPower = lerp(8.0h, 96.0h, smoothness);
    half specular = pow(saturate(dot(normalWorld, halfDir)), specularPower)
                  * _SpecularStrength;

    half alpha = saturate(
        _QuestOpacity + fresnel * _QuestFresnelStrength);
    alpha *= _BaseColorTint.a * _GrabpassTint.a;

    half3 tint = _QuestTint.rgb * _GrabpassTint.rgb;
    half3 color = baseSurface * diffuseLight;
    color += reflection * reflectionWeight * _SpecularityTint.rgb;
    color += _LightColor0.rgb * specular * _SpecularityTint.rgb;
    // Keeps silhouettes readable when a reflection probe is absent or black.
    color += tint * (0.025h + fresnel * 0.12h);

    return half4(color * alpha, alpha);
}

#endif
