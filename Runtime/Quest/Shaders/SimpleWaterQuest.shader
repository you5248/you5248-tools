// you5248 / SimpleWater Quest ―― Mochie Water 代替の最軽量水面（Quest/Android）
//
// GrabPass・深度テクスチャ不使用。
// スクロール法線2枚ブレンド＋フレネル＋ReflectionProbe反射＋半透明1パス。
Shader "you5248/SimpleWaterQuest"
{
    Properties
    {
        [Header(Color)]
        _Color("Water Color", Color) = (0.05, 0.25, 0.35, 0.65)
        _Opacity("Base Opacity", Range(0,1)) = 0.65
        _Smoothness("Smoothness", Range(0,1)) = 0.85
        _Specular("Specular Strength", Range(0,2)) = 0.6

        [Header(Normals)]
        [Normal] _BumpMap("Normal Map A", 2D) = "bump" {}
        [Normal] _BumpMap2("Normal Map B", 2D) = "bump" {}
        _BumpScale("Normal Scale", Range(0,2)) = 0.5
        _ScrollA("Scroll A (xy)", Vector) = (0.03, 0.02, 0, 0)
        _ScrollB("Scroll B (xy)", Vector) = (-0.02, 0.04, 0, 0)

        [Header(Fresnel Reflection)]
        _FresnelPower("Fresnel Power", Range(0.5,8)) = 4
        _FresnelBias("Fresnel Bias", Range(0,1)) = 0.05
        _ReflectionStrength("Reflection Strength", Range(0,2)) = 1
        _ReflectionMip("Reflection Roughness (mip)", Range(0,1)) = 0.15
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "IgnoreProjector"="True"
        }
        LOD 100

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #pragma multi_compile_fog
            #pragma multi_compile_fwdbase
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            #include "UnityStandardUtils.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _BumpMap;
            float4    _BumpMap_ST;
            sampler2D _BumpMap2;
            float4    _BumpMap2_ST;
            float4    _Color;
            float     _Opacity;
            float     _Smoothness;
            float     _Specular;
            float     _BumpScale;
            float4    _ScrollA;
            float4    _ScrollB;
            float     _FresnelPower;
            float     _FresnelBias;
            float     _ReflectionStrength;
            float     _ReflectionMip;

            struct appdata
            {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                float4 tangent : TANGENT;
                float2 uv      : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNrm : TEXCOORD2;
                float4 worldTan : TEXCOORD3;
                UNITY_FOG_COORDS(4)
                UNITY_SHADOW_COORDS(5)
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
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNrm = UnityObjectToWorldNormal(v.normal);
                o.worldTan = float4(UnityObjectToWorldDir(v.tangent.xyz),
                                    v.tangent.w * unity_WorldTransformParams.w);
                UNITY_TRANSFER_SHADOW(o, v.uv);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float3 BlendNormalsTS(float3 n0, float3 n1)
            {
                return normalize(float3(n0.xy + n1.xy, n0.z * n1.z));
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 uvA = TRANSFORM_TEX(i.uv, _BumpMap)  + _ScrollA.xy * _Time.y;
                float2 uvB = TRANSFORM_TEX(i.uv, _BumpMap2) + _ScrollB.xy * _Time.y;

                float3 nA = UnpackScaleNormal(tex2D(_BumpMap,  uvA), _BumpScale);
                float3 nB = UnpackScaleNormal(tex2D(_BumpMap2, uvB), _BumpScale);
                float3 nTS = BlendNormalsTS(nA, nB);

                float3 N = normalize(i.worldNrm);
                float  tlen = length(i.worldTan.xyz);
                float3 worldN;
                if (tlen > 1e-4)
                {
                    float3 T = i.worldTan.xyz / tlen;
                    float3 B = cross(N, T) * i.worldTan.w;
                    worldN = normalize(T * nTS.x + B * nTS.y + N * nTS.z);
                }
                else
                {
                    worldN = N;
                }

                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float  NoV = saturate(dot(worldN, V));
                float  fresnel = saturate(_FresnelBias + (1.0 - _FresnelBias) * pow(1.0 - NoV, _FresnelPower));

                // ReflectionProbe
                float3 R = reflect(-V, worldN);
                float  mip = _ReflectionMip * UNITY_SPECCUBE_LOD_STEPS;
                half4  enc = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, R, mip);
                float3 refl = DecodeHDR(enc, unity_SpecCube0_HDR) * _ReflectionStrength;

                // 簡易直接光＋環境SH
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float  NoL = saturate(dot(worldN, L));
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float3 lightCol = _LightColor0.rgb * atten;

                float3 diffuse = _Color.rgb * (ShadeSH9(float4(worldN, 1)) + lightCol * NoL);

                // 簡易スペキュラ（フレネル加重）
                float3 H = normalize(L + V);
                float  NoH = saturate(dot(worldN, H));
                float  specPow = exp2(10.0 * _Smoothness + 1.0);
                float3 spec = lightCol * pow(NoH, specPow) * _Specular * fresnel;

                float3 col = lerp(diffuse, refl, fresnel) + spec;
                float  alpha = saturate(lerp(_Opacity, 1.0, fresnel) * _Color.a);

                float4 o = float4(col, alpha);
                UNITY_APPLY_FOG(i.fogCoord, o);
                return o;
            }
            ENDCG
        }
    }
    Fallback "Mobile/Particles/Alpha Blended"
}
