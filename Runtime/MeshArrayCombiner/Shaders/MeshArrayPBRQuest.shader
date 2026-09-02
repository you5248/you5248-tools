// you5248 / MeshArrayPBR Quest ―― Quest/Android 向け軽量バリアント
//
// 原本 MeshArrayPBR.shader の Texture2DArray PBR を維持しつつ、
// LTCGI / Bicubic / MonoSH / Light Volumes / Debug を除去。target 3.5。
// プロパティ名は原本と同名（マテリアル差し替え互換）。*_Quest ASTC 配列を想定。
Shader "you5248/MeshArrayPBRQuest"
{
    Properties
    {
        [Header(Texture Arrays)]
        _MainTexArray("Albedo Array (sRGB)", 2DArray) = "white" {}
        _BumpMapArray("Normal Array (linear, RGB=normal)", 2DArray) = "bump" {}
        _MetallicGlossMapArray("Metallic(R) Smoothness(A) Array (linear)", 2DArray) = "white" {}
        _EmissionMapArray("Emission Array (sRGB)", 2DArray) = "black" {}

        [Header(Tweaks)]
        _Color("Color Tint", Color) = (1,1,1,1)
        [HDR]_EmissionColor("Emission Tint", Color) = (1,1,1,1)
        _EmissionStrength("Emission Strength", Range(0,8)) = 1
        [Toggle(_EMISSION_ARRAY_ON)] _UseEmissionArray("Emission Array", Float) = 1
        _BumpScale("Normal Scale", Float) = 1
        _MetallicMul("Metallic", Range(0,1)) = 1
        [Toggle(_INVERTSMOOTH_ON)] _InvertSmoothness("Map A is Roughness", Float) = 0
        _SmoothnessMin("Smoothness Min", Range(0,1)) = 0
        _SmoothnessMax("Smoothness Max", Range(0,1)) = 1

        [Header(Alpha)]
        [Toggle(_ALPHATEST_ON)] _AlphaTest("Alpha Cutout", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5

        [Header(Lighting)]
        _LightingMul("Lighting Multiplier", Range(0,2)) = 1
        _IndirectMul("Indirect Multiplier (lower = darker shadows)", Range(0,2)) = 1
        _Saturation("Saturation", Range(0,2)) = 1

        // Serialized for PC -> Quest material copies; unused on Quest.
        [HideInInspector] _BicubicLightmap("PC Bicubic (unused)", Float) = 0
        [HideInInspector] _UseLightVolumes("PC Light Volumes (unused)", Float) = 0
        [HideInInspector] _UseLTCGI("PC LTCGI (unused)", Float) = 0
        [HideInInspector] _DebugNormal("PC Debug (unused)", Float) = 0
        [HideInInspector] _DebugMode("PC Debug Mode (unused)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "Lighting.cginc"
        #include "AutoLight.cginc"

        UNITY_DECLARE_TEX2DARRAY(_MainTexArray);
        UNITY_DECLARE_TEX2DARRAY(_BumpMapArray);
        UNITY_DECLARE_TEX2DARRAY(_MetallicGlossMapArray);
        UNITY_DECLARE_TEX2DARRAY(_EmissionMapArray);

        float4 _Color;
        float4 _EmissionColor;
        float  _EmissionStrength;
        float  _BumpScale;
        float  _MetallicMul;
        float  _SmoothnessMin;
        float  _SmoothnessMax;
        float  _LightingMul;
        float  _IndirectMul;
        float  _Saturation;
        float  _Cutoff;

        struct appdata
        {
            float4 vertex  : POSITION;
            float3 normal  : NORMAL;
            float4 tangent : TANGENT;
            float2 uv      : TEXCOORD0;
            float2 uv1     : TEXCOORD1;
            float4 uv3     : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4 pos      : SV_POSITION;
            float2 uv       : TEXCOORD0;
            float2 lmuv     : TEXCOORD1;
            float3 worldPos : TEXCOORD2;
            float3 worldNrm : TEXCOORD3;
            float4 worldTan : TEXCOORD4;
            nointerpolation float layer : TEXCOORD5;
            UNITY_FOG_COORDS(6)
            UNITY_SHADOW_COORDS(7)
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        v2f vert(appdata v)
        {
            v2f o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_OUTPUT(v2f, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

            o.pos      = UnityObjectToClipPos(v.vertex);
            o.uv       = v.uv;
            o.lmuv     = v.uv1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
            o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.worldNrm = UnityObjectToWorldNormal(v.normal);
            o.worldTan = float4(UnityObjectToWorldDir(v.tangent.xyz),
                                v.tangent.w * unity_WorldTransformParams.w);
            o.layer    = v.uv3.x;
            UNITY_TRANSFER_SHADOW(o, v.uv1.xy);
            UNITY_TRANSFER_FOG(o, o.pos);
            return o;
        }

        struct Surf
        {
            float3 albedo, diffuseAlbedo, f0, emission, worldN, V;
            float  metallic, smoothness, roughness;
        };

        Surf SampleSurface(v2f i)
        {
            float3 uvw = float3(i.uv, i.layer);
            float4 albedoTex = UNITY_SAMPLE_TEX2DARRAY(_MainTexArray, uvw);
            #if defined(_ALPHATEST_ON)
                clip(albedoTex.a - _Cutoff);
            #endif

            Surf s;
            s.albedo = albedoTex.rgb * _Color.rgb;

            float3 nrmTex = UNITY_SAMPLE_TEX2DARRAY(_BumpMapArray, uvw).rgb;
            float3 nrmTS  = float3((nrmTex.xy * 2 - 1) * _BumpScale, 1);
            nrmTS.z = sqrt(saturate(1 - dot(nrmTS.xy, nrmTS.xy)));

            float4 mg = UNITY_SAMPLE_TEX2DARRAY(_MetallicGlossMapArray, uvw);
            s.metallic = saturate(mg.r * _MetallicMul);
            float sm = mg.a;
            #if defined(_INVERTSMOOTH_ON)
                sm = 1 - sm;
            #endif
            s.smoothness = saturate(lerp(_SmoothnessMin, _SmoothnessMax, sm));
            s.roughness  = 1 - s.smoothness;

            #if defined(_EMISSION_ARRAY_ON)
            s.emission = UNITY_SAMPLE_TEX2DARRAY(_EmissionMapArray, uvw).rgb * _EmissionColor.rgb * _EmissionStrength;
            #else
            s.emission = 0;
            #endif

            float3 N = normalize(i.worldNrm);
            float  tlen = length(i.worldTan.xyz);
            if (tlen > 1e-4)
            {
                float3 T = i.worldTan.xyz / tlen;
                float3 B = cross(N, T) * i.worldTan.w;
                s.worldN = normalize(T * nrmTS.x + B * nrmTS.y + N * nrmTS.z);
            }
            else
            {
                s.worldN = N;
            }

            s.V  = normalize(_WorldSpaceCameraPos - i.worldPos);
            s.f0 = lerp(0.04, s.albedo, s.metallic);
            s.diffuseAlbedo = s.albedo * (1 - s.metallic);
            return s;
        }

        float3 SpecGGX(float3 N, float3 V, float3 L, float roughness, float3 f0)
        {
            float3 H = normalize(L + V);
            float NoH = saturate(dot(N, H));
            float NoL = saturate(dot(N, L));
            float NoV = abs(dot(N, V)) + 1e-5;
            float VoH = saturate(dot(V, H));
            float a   = max(roughness * roughness, 2e-3);
            float a2  = a * a;
            float d   = NoH * NoH * (a2 - 1) + 1;
            float D   = a2 / (UNITY_PI * d * d);
            float lambdaV = NoL * sqrt(NoV * NoV * (1 - a2) + a2);
            float lambdaL = NoV * sqrt(NoL * NoL * (1 - a2) + a2);
            float Vis = 0.5 / max(lambdaV + lambdaL, 1e-5);
            float3 F  = f0 + (1 - f0) * pow(1 - VoH, 5);
            return D * Vis * F;
        }

        float3 SampleReflectionProbe(float3 worldN, float3 V, float roughness, float3 f0, float smoothness)
        {
            float3 r   = reflect(-V, worldN);
            float  mip = roughness * (1.7 - 0.7 * roughness) * UNITY_SPECCUBE_LOD_STEPS;
            half4  enc = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, r, mip);
            float3 refl = DecodeHDR(enc, unity_SpecCube0_HDR);
            float  NoV = saturate(dot(worldN, V));
            float3 grazing = saturate(smoothness.xxx + f0);
            float3 F = lerp(f0, grazing, pow(1 - NoV, 5));
            return refl * F;
        }

        // Non-directional lightmap only（Quest は L1 / MonoSH 不使用）。
        float3 SampleBakedGI(float2 lmuv)
        {
            return DecodeLightmap(UNITY_SAMPLE_TEX2D(unity_Lightmap, lmuv));
        }
        ENDCG

        Pass
        {
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment fragBase
            #pragma target   3.5
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma skip_variants DIRLIGHTMAP_COMBINED DIRLIGHTMAP_SEPARATE
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _INVERTSMOOTH_ON
            #pragma shader_feature_local _EMISSION_ARRAY_ON

            float4 fragBase(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                Surf s = SampleSurface(i);

                float3 direct = 0;
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float  NoL = saturate(dot(s.worldN, L));
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float3 lc = _LightColor0.rgb * atten;
                direct += s.diffuseAlbedo * lc * NoL;
                direct += SpecGGX(s.worldN, s.V, L, s.roughness, s.f0) * lc * NoL;

                float3 indirect = 0;
                #if defined(LIGHTMAP_ON)
                    indirect += s.diffuseAlbedo * SampleBakedGI(i.lmuv);
                #else
                    indirect += s.diffuseAlbedo * ShadeSH9(float4(s.worldN, 1));
                #endif

                indirect += SampleReflectionProbe(s.worldN, s.V, s.roughness, s.f0, s.smoothness);

                float3 col = direct + indirect * _IndirectMul;
                col = lerp(dot(col, float3(0.299, 0.587, 0.114)), col, _Saturation);
                col *= _LightingMul;
                col += s.emission;

                float4 o = float4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, o);
                return o;
            }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode"="ForwardAdd" }
            Blend One One
            ZWrite Off

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment fragAdd
            #pragma target   3.5
            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _INVERTSMOOTH_ON
            #pragma shader_feature_local _EMISSION_ARRAY_ON

            float4 fragAdd(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                Surf s = SampleSurface(i);

                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float3 L  = normalize(UnityWorldSpaceLightDir(i.worldPos));
                float  NoL = saturate(dot(s.worldN, L));
                float3 lc = _LightColor0.rgb * atten;

                float3 col = s.diffuseAlbedo * lc * NoL;
                col += SpecGGX(s.worldN, s.V, L, s.roughness, s.f0) * lc * NoL;
                col *= _LightingMul;

                float4 o = float4(col, 1);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, o, fixed4(0,0,0,0));
                return o;
            }
            ENDCG
        }
    }
    Fallback "Mobile/Diffuse"
}
