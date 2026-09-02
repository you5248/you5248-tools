// you5248 / MonoSH 共有インクルード
// -----------------------------------------------------------------------------
// Bakery の Directional Mode = MonoSH でベイクしたライトマップを読み、
// 拡散に方向性を与え、さらに「焼かれた光の艶」（ベイクスペキュラ）を作る。
//
// 使い方（Standard 系サーフェスシェーダー）:
//   1. Properties に下の 4 つを足す
//        [Toggle(_MONOSH_ON)]          _UseMonoSH          ("MonoSH", Float) = 0
//        [Toggle(_MONOSHSPEC_ON)]      _UseMonoSHSpec      ("  ベイクスペキュラ", Float) = 0
//        _MonoSHSpecMul                                    ("  強さ", Range(0,2)) = 1
//        [ToggleUI] _MonoSHNonlinear ("  ノンリニア補正", Float) = 1
//   2. #pragma shader_feature_local _MONOSH_ON / _MONOSHSPEC_ON
//      （ノンリニア補正はキーワードにしない。バリアントを倍にしないため
//        _MONOSH_ON の内側の uniform 分岐で処理する）
//   3. #pragma surface surf Standard ... を  surf StandardMonoSH ... に変える
//   4. このファイルを include する（UnityPBSLighting.cginc より後）
//
// ライトマップのサンプリングを差し替えたい場合（バイキュービック等）は、
// include の前に Y5_SAMPLE_LIGHTMAP(uv) を定義しておく。
//
// 数式は Assets/Bakery/shader/Bakery.cginc の BakeryMonoSH() に準拠。
// -----------------------------------------------------------------------------
#ifndef YOU5248_MONOSH_INCLUDED
#define YOU5248_MONOSH_INCLUDED

#include "UnityPBSLighting.cginc"

#ifndef Y5_SAMPLE_LIGHTMAP
    #define Y5_SAMPLE_LIGHTMAP(uv) UNITY_SAMPLE_TEX2D(unity_Lightmap, uv)
#endif

half _MonoSHNonlinear;

#if defined(_MONOSH_ON)
half _MonoSHSpecMul;

// SH L1 のノンリニア評価（Geomerics）。暗部の潰れとリンギングを抑える。
float y5_ShL1Geomerics(float L0, float3 L1, float3 n)
{
    float R0 = max(L0, 1e-5);
    float3 R1 = 0.5 * L1;
    float lenR1 = length(R1);
    float q = dot(normalize(R1 + 1e-6), n) * 0.5 + 0.5;
    float p = 1.0 + 2.0 * lenR1 / R0;
    float a = (1.0 - lenR1 / R0) / (1.0 + lenR1 / R0);
    return R0 * (a + (1.0 - a) * (p + 1.0) * pow(q, p));
}

// L0（デコード済みライトマップ色）と支配方向から拡散と鏡面を作る。
// viewDir は面から視点へ向く向き（UnityGIInput.worldViewDir と同じ向き）。
// 鏡面は GGX の D 項までを載せる。フレネルと反射色は後段の Unity BRDF が掛ける。
void y5_MonoSH(half3 L0, float2 lmUV, half3 normalWorld, half3 viewDir, half smoothness,
               out half3 diffuseOut, out half3 specularOut)
{
    half3 dominantDir = UNITY_SAMPLE_TEX2D_SAMPLER(unity_LightmapInd, unity_Lightmap, lmUV).xyz;
    half3 nL1 = dominantDir * 2 - 1;
    half3 L1x = nL1.x * L0 * 2;
    half3 L1y = nL1.y * L0 * 2;
    half3 L1z = nL1.z * L0 * 2;

    half3 sh = L0 + normalWorld.x * L1x + normalWorld.y * L1y + normalWorld.z * L1z;
    if (_MonoSHNonlinear > 0.5)
            {
                // 輝度だけノンリニア評価し、その比率を RGB に掛け戻す（Bakery と同じ手法）
        float lumaSH = y5_ShL1Geomerics(dot(L0, 1), float3(dot(L1x, 1), dot(L1y, 1), dot(L1z, 1)), normalWorld);
        float regularLumaSH = dot(sh, 1);
        sh *= lerp(1, lumaSH / max(regularLumaSH, 1e-5), saturate(regularLumaSH * 16));
}
    diffuseOut = max(sh, 0.0);

    specularOut = 0;
    #if defined(_MONOSHSPEC_ON)
        half3 halfDir = Unity_SafeNormalize(normalize(nL1) + viewDir);
        half nh = saturate(dot(normalWorld, halfDir));
        half roughness = PerceptualRoughnessToRoughness(SmoothnessToPerceptualRoughness(smoothness));
        half spec = GGXTerm(nh, roughness);
        half3 shSpec = L0 + nL1.x * L1x + nL1.y * L1y + nL1.z * L1z;
        specularOut = max(spec * shSpec, 0.0) * _MonoSHSpecMul;
    #endif
}
#endif // _MONOSH_ON

// -----------------------------------------------------------------------------
//  Unity の UnityGI_Base 相当。MonoSH のときだけライトマップの解釈を差し替える。
//  MonoSH ベイクでは Unity 側が CombinedDirectional になり DIRLIGHTMAP_COMBINED も
//  立つが、方向マップの意味が Unity 標準とは別物なので _MONOSH_ON を先に判定する。
// -----------------------------------------------------------------------------
inline UnityGI y5_MonoSH_UnityGI_Base(UnityGIInput data, half occlusion, half3 normalWorld, half smoothness)
{
    UnityGI o_gi;
    ResetUnityGI(o_gi);

    #if defined(HANDLE_SHADOWS_BLENDING_IN_GI)
        half bakedAtten = UnitySampleBakedOcclusion(data.lightmapUV.xy, data.worldPos);
        float zDist = dot(_WorldSpaceCameraPos - data.worldPos, UNITY_MATRIX_V[2].xyz);
        float fadeDist = UnityComputeShadowFadeDistance(data.worldPos, zDist);
        data.atten = UnityMixRealtimeAndBakedShadows(data.atten, bakedAtten, UnityComputeShadowFade(fadeDist));
    #endif

    o_gi.light = data.light;
    o_gi.light.color *= data.atten;

    #if UNITY_SHOULD_SAMPLE_SH
        o_gi.indirect.diffuse = ShadeSHPerPixel(normalWorld, data.ambient, data.worldPos);
    #endif

    #if defined(LIGHTMAP_ON)
        half4 bakedColorTex = Y5_SAMPLE_LIGHTMAP(data.lightmapUV.xy);
        half3 bakedColor = DecodeLightmap(bakedColorTex);

        #if defined(_MONOSH_ON)
            half3 monoDiff, monoSpec;
            y5_MonoSH(bakedColor, data.lightmapUV.xy, normalWorld, data.worldViewDir, smoothness, monoDiff, monoSpec);
            o_gi.indirect.diffuse += monoDiff;
            // ベイクスペキュラは環境反射と同じ枠に載せる（後段でフレネルが掛かる）。
            // 加算パスでの二重計上を避けるため base パスのみ。
            #if !defined(UNITY_PASS_FORWARDADD)
                o_gi.indirect.specular += monoSpec;
            #endif

            #if defined(LIGHTMAP_SHADOW_MIXING) && !defined(SHADOWS_SHADOWMASK) && defined(SHADOWS_SCREEN)
                ResetUnityLight(o_gi.light);
                o_gi.indirect.diffuse = SubtractMainLightWithRealtimeAttenuationFromLightmap(o_gi.indirect.diffuse, data.atten, bakedColorTex, normalWorld);
            #endif
        #elif defined(DIRLIGHTMAP_COMBINED)
            fixed4 bakedDirTex = UNITY_SAMPLE_TEX2D_SAMPLER(unity_LightmapInd, unity_Lightmap, data.lightmapUV.xy);
            o_gi.indirect.diffuse += DecodeDirectionalLightmap(bakedColor, bakedDirTex, normalWorld);

            #if defined(LIGHTMAP_SHADOW_MIXING) && !defined(SHADOWS_SHADOWMASK) && defined(SHADOWS_SCREEN)
                ResetUnityLight(o_gi.light);
                o_gi.indirect.diffuse = SubtractMainLightWithRealtimeAttenuationFromLightmap(o_gi.indirect.diffuse, data.atten, bakedColorTex, normalWorld);
            #endif
        #else
            o_gi.indirect.diffuse += bakedColor;

            #if defined(LIGHTMAP_SHADOW_MIXING) && !defined(SHADOWS_SHADOWMASK) && defined(SHADOWS_SCREEN)
                ResetUnityLight(o_gi.light);
                o_gi.indirect.diffuse = SubtractMainLightWithRealtimeAttenuationFromLightmap(o_gi.indirect.diffuse, data.atten, bakedColorTex, normalWorld);
            #endif
        #endif
    #endif

    #ifdef DYNAMICLIGHTMAP_ON
        fixed4 realtimeColorTex = UNITY_SAMPLE_TEX2D(unity_DynamicLightmap, data.lightmapUV.zw);
        half3 realtimeColor = DecodeRealtimeLightmap(realtimeColorTex);

        #ifdef DIRLIGHTMAP_COMBINED
            half4 realtimeDirTex = UNITY_SAMPLE_TEX2D_SAMPLER(unity_DynamicDirectionality, unity_DynamicLightmap, data.lightmapUV.zw);
            o_gi.indirect.diffuse += DecodeDirectionalLightmap(realtimeColor, realtimeDirTex, normalWorld);
        #else
            o_gi.indirect.diffuse += realtimeColor;
        #endif
    #endif

    o_gi.indirect.diffuse *= occlusion;
    return o_gi;
}

// -----------------------------------------------------------------------------
//  ライティングモデル "StandardMonoSH"
//  BRDF は Unity の LightingStandard をそのまま使い、GI だけ差し替える。
// -----------------------------------------------------------------------------
inline half4 LightingStandardMonoSH(SurfaceOutputStandard s, half3 viewDir, UnityGI gi)
{
    return LightingStandard(s, viewDir, gi);
}

inline void LightingStandardMonoSH_GI(SurfaceOutputStandard s, UnityGIInput data, inout UnityGI gi)
{
    Unity_GlossyEnvironmentData g = UnityGlossyEnvironmentSetup(
        s.Smoothness, data.worldViewDir, s.Normal,
        lerp(unity_ColorSpaceDielectricSpec.rgb, s.Albedo, s.Metallic));

    gi = y5_MonoSH_UnityGI_Base(data, s.Occlusion, s.Normal, s.Smoothness);
    // 環境反射は base パスのみ。MonoSH のベイクスペキュラを消さないよう加算する。
    #if !defined(UNITY_PASS_FORWARDADD)
        gi.indirect.specular += UnityGI_IndirectSpecular(data, s.Occlusion, g);
    #endif
}

#endif // YOU5248_MONOSH_INCLUDED
