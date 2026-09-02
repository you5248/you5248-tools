// you5248 / MeshArrayPBR  ―― v2（要・Unity / VRChat 実機での確認）
//
// 高タイリングのメッシュを「テクスチャ縮小なし・タイリング保持」で 1 ドローにまとめる専用シェーダー。
// 各マテリアルのテクスチャを Texture2DArray の 1 レイヤーとして持ち、頂点に焼いたレイヤー番号(UV3.x)で
// サンプルする。UV はタイリングのまま（配列の各レイヤーが REPEAT ラップするので維持される）。
//
// 照明: PBR + ライトマップ（ディレクショナル/バイキュービック対応）+ VRC Light Volumes + LTCGI、
//       ForwardBase（メイン光）+ ForwardAdd（追加リアルタイムライト）。
// LTCGI の統合は lilToon の lil_common_functions_thirdparty.hlsl のパターンを踏襲。
Shader "you5248/MeshArrayPBR"
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
        [Toggle(_BICUBIC_ON)] _BicubicLightmap("Bicubic Lightmap (smooth bake)", Float) = 1
        [Toggle(_LV_ON)]    _UseLightVolumes("VRC Light Volumes", Float) = 1
        // LV×リアルタイム影（DHK/TerrainLVと同じ分解。0=OFF=従来挙動）。
        // 通常(ベイク)ボリュームだけに影マスクを掛け、加算LV/PLV(蛍・提灯)には掛けない
        _LVShadowMulStrength("LV Shadow Mul Strength", Range(0,1)) = 0
        _LVShadowFloor("LV Shadow Floor", Range(0,1)) = 0.25
        [Toggle(_LTCGI_ON)] _UseLTCGI("LTCGI", Float) = 0

        [Header(MonoSH BakeryMonoSHdeyakuhitsuyou)]
        [Toggle(_MONOSH_ON)] _UseMonoSH("MonoSH (方向つきライトマップ)", Float) = 0
        [Toggle(_MONOSHSPEC_ON)] _UseMonoSHSpec("MonoSH ベイクスペキュラ", Float) = 0
        _MonoSHSpecMul("ベイクスペキュラ強さ", Range(0,2)) = 1
        [ToggleUI] _MonoSHNonlinear("MonoSH ノンリニア補正 (推奨ON)", Float) = 1

        [Header(Debug)]
        [Toggle(_DEBUG_NORMAL_ON)] _DebugNormal("DEBUG: Enable", Float) = 0
        _DebugMode("DEBUG Mode (1=N 2=NrmArray 3=Tan 4=Metallic 5=Smoothness)", Range(1,5)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        // ===================== 共通部 =====================
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
        float  _LVShadowMulStrength;
        float  _LVShadowFloor;
        float  _Saturation;
        float  _Cutoff;
        float  _DebugMode;
        float  _MonoSHSpecMul;
        float  _MonoSHNonlinear;

        struct appdata
        {
            float4 vertex  : POSITION;
            float3 normal  : NORMAL;
            float4 tangent : TANGENT;
            float2 uv      : TEXCOORD0; // タイリングUV
            float2 uv1     : TEXCOORD1; // ライトマップUV(=mesh.uv2)
            float4 uv3     : TEXCOORD3; // .x = 配列レイヤー番号
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

        // --- サーフェス（両パス共通でサンプル） ---
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
                s.worldN = N;   // タンジェント無効時は頂点法線（NaN→黒 を回避）
            }

            s.V  = normalize(_WorldSpaceCameraPos - i.worldPos);
            s.f0 = lerp(0.04, s.albedo, s.metallic);
            s.diffuseAlbedo = s.albedo * (1 - s.metallic);
            return s;
        }

        // --- GGX 鏡面 BRDF（D * Vis * F, Vis は 1/(4 NoL NoV) を含む。NoL は呼び出し側で掛ける） ---
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

        // --- リフレクションプローブ（環境反射） ---
        float3 SampleReflectionProbe(float3 worldN, float3 V, float roughness, float3 f0, float smoothness)
        {
            float3 r   = reflect(-V, worldN);
            float  mip = roughness * (1.7 - 0.7 * roughness) * UNITY_SPECCUBE_LOD_STEPS;
            half4  enc = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, r, mip);
            float3 refl = DecodeHDR(enc, unity_SpecCube0_HDR);
            float  NoV = saturate(dot(worldN, V));
            float3 grazing = saturate(smoothness.xxx + f0);
            float3 F = lerp(f0, grazing, pow(1 - NoV, 5));   // grazing フレネル
            return refl * F;
        }

        // --- バイキュービック B-spline（4 タップ。ライトマップを滑らかに） ---
        float4 CubicW(float v)
        {
            float4 n = float4(1,2,3,4) - v;
            float4 s = n * n * n;
            float x = s.x;
            float y = s.y - 4 * s.x;
            float z = s.z - 4 * s.y + 6 * s.x;
            float w = 6 - x - y - z;
            return float4(x, y, z, w) * (1.0 / 6.0);
        }
        float4 SampleLightmapBicubic(float2 uv)
        {
            // テクセルサイズはGetDimensionsで確実に取る（unity_Lightmap_TexelSizeは0のことがある）
            float lw, lh;
            unity_Lightmap.GetDimensions(lw, lh);
            float2 texSize = float2(lw, lh);
            float2 invSize = 1.0 / texSize;
            uv = uv * texSize - 0.5;
            float2 fxy = frac(uv);
            uv -= fxy;
            float4 xc = CubicW(fxy.x);
            float4 yc = CubicW(fxy.y);
            float4 c  = uv.xxyy + float2(-0.5, 1.5).xyxy;
            float4 s  = float4(xc.x + xc.y, xc.z + xc.w, yc.x + yc.y, yc.z + yc.w);
            float4 off = c + float4(xc.y, xc.w, yc.y, yc.w) / s;
            off *= invSize.xxyy;
            float4 s0 = UNITY_SAMPLE_TEX2D(unity_Lightmap, off.xz);
            float4 s1 = UNITY_SAMPLE_TEX2D(unity_Lightmap, off.yz);
            float4 s2 = UNITY_SAMPLE_TEX2D(unity_Lightmap, off.xw);
            float4 s3 = UNITY_SAMPLE_TEX2D(unity_Lightmap, off.yw);
            float sx = s.x / (s.x + s.y);
            float sy = s.z / (s.z + s.w);
            return lerp(lerp(s3, s2, sx), lerp(s1, s0, sx), sy);
        }

        // --- SH L1 のノンリニア評価（Geomerics）。暗部の潰れとリンギングを抑える ---
        // 出典: Assets/Bakery/shader/Bakery.cginc の shEvaluateDiffuseL1Geomerics()
        float MA_ShL1Geomerics(float L0, float3 L1, float3 n)
        {
            float R0 = max(L0, 1e-5);
            float3 R1 = 0.5 * L1;
            float lenR1 = length(R1);
            float q = dot(normalize(R1 + 1e-6), n) * 0.5 + 0.5;
            float p = 1.0 + 2.0 * lenR1 / R0;
            float a = (1.0 - lenR1 / R0) / (1.0 + lenR1 / R0);
            return R0 * (a + (1.0 - a) * (p + 1.0) * pow(q, p));
        }

        // --- ベイクライトマップの拡散＋（MonoSH時のみ）鏡面 ---
        // MonoSH: unity_Lightmap に明るさ L0、unity_LightmapInd に支配方向（0..1 エンコード）が入る。
        // そこから L1 を復元して法線方向に評価＝拡散、支配方向を光源とみなして GGX＝鏡面。
        // 出典: Assets/Bakery/shader/Bakery.cginc の BakeryMonoSH()
        // 注意: MonoSH ベイクでは Unity 側が CombinedDirectional になり DIRLIGHTMAP_COMBINED も立つため、
        //       _MONOSH_ON を優先して既定の DecodeDirectionalLightmap を通さないこと（式が別物）。
        float3 SampleBakedGISpec(float2 lmuv, float3 worldN, float3 V, float roughness, float3 f0, out float3 bakedSpec)
        {
            bakedSpec = 0;

            #if defined(_BICUBIC_ON)
                float4 lmRaw = SampleLightmapBicubic(lmuv);
            #else
                float4 lmRaw = UNITY_SAMPLE_TEX2D(unity_Lightmap, lmuv);
            #endif
            float3 L0 = DecodeLightmap(lmRaw);

            #if defined(_MONOSH_ON)
                float3 dominantDir = UNITY_SAMPLE_TEX2D_SAMPLER(unity_LightmapInd, unity_Lightmap, lmuv).xyz;
                float3 nL1 = dominantDir * 2 - 1;
                float3 L1x = nL1.x * L0 * 2;
                float3 L1y = nL1.y * L0 * 2;
                float3 L1z = nL1.z * L0 * 2;

                float3 sh = L0 + worldN.x * L1x + worldN.y * L1y + worldN.z * L1z;
                if (_MonoSHNonlinear > 0.5)
            {
                // 輝度だけノンリニア評価し、その比率を RGB に掛け戻す（Bakery と同じ手法）
                    float lumaSH = MA_ShL1Geomerics(dot(L0, 1), float3(dot(L1x, 1), dot(L1y, 1), dot(L1z, 1)), worldN);
                    float regularLumaSH = dot(sh, 1);
                    sh *= lerp(1, lumaSH / max(regularLumaSH, 1e-5), saturate(regularLumaSH * 16));
}

                #if defined(_MONOSHSPEC_ON)
                    // 支配方向を「焼かれた光の向き」とみなして通常の GGX を通す。
                    // focus は方向性の強さ（1=くっきり平行光, 0=環境光）。環境的な場所で
                    // 不自然なハイライトが乗らないよう強度に掛ける。
                    float focus = saturate(length(nL1));
                    float3 Ldir = normalize(nL1 + 1e-6);
                    float3 shSpec = L0 + Ldir.x * L1x + Ldir.y * L1y + Ldir.z * L1z;
                    float  NoLs = saturate(dot(worldN, Ldir));
                    bakedSpec = SpecGGX(worldN, V, Ldir, roughness, f0)
                              * max(shSpec, 0.0) * NoLs * focus * _MonoSHSpecMul;
                #endif

                return max(sh, 0.0);
            #elif defined(DIRLIGHTMAP_COMBINED)
                float4 lmInd = UNITY_SAMPLE_TEX2D_SAMPLER(unity_LightmapInd, unity_Lightmap, lmuv);
                return DecodeDirectionalLightmap(L0, lmInd, worldN);
            #else
                return L0;
            #endif
        }

        // 従来シグネチャ（拡散のみ）。既存呼び出しの互換用
        float3 SampleBakedGI(float2 lmuv, float3 worldN)
        {
            float3 unusedSpec;
            return SampleBakedGISpec(lmuv, worldN, float3(0,0,1), 1, 0, unusedSpec);
        }
        ENDCG

        // ===================== ForwardBase =====================
        Pass
        {
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment fragBase
            #pragma target   4.0
            #pragma multi_compile_fwdbase
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _INVERTSMOOTH_ON
            #pragma shader_feature_local _EMISSION_ARRAY_ON
            #pragma shader_feature_local _BICUBIC_ON
            #pragma shader_feature_local _LV_ON
            #pragma shader_feature_local _LTCGI_ON
            #pragma shader_feature_local _MONOSH_ON
            #pragma shader_feature_local _MONOSHSPEC_ON
            #pragma shader_feature_local _DEBUG_NORMAL_ON

            #if defined(_LV_ON)
                #include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
            #endif

            #if defined(_LTCGI_ON)
                float3 MA_NPL(float3 p, float3 a, float3 b)
                {
                    float3 v = b - a; float3 w = p - a;
                    float c1 = dot(w, v); float c2 = dot(v, v);
                    if (c1 <= 0.0) return a;
                    if (c2 <= c1) return b;
                    return a + (c1 / c2) * v;
                }
                float3 MA_NPQ(float3 p, float3 v[4])
                {
                    return MA_NPL(p, MA_NPL(p, v[0], v[1]), MA_NPL(p, v[2], v[3]));
                }
                #define Sample(smp,uv) SampleLevel(smp,uv,0)
                #include "Packages/at.pimaker.ltcgi/Shaders/LTCGI_structs.cginc"
                struct ma_ltcgi { float3 diff; float3 spec; float3 N; float3 V; };
                float ma_atten(inout ma_ltcgi s, in ltcgi_output o)
                {
                    float3 C = (o.input.Lw[1] + o.input.Lw[0] + o.input.Lw[3] + o.input.Lw[0]) * 0.25;
                    float3 P = MA_NPQ(float3(0,0,0), o.input.Lw);
                    float3 N = normalize(cross(o.input.Lw[1]-o.input.Lw[0], o.input.Lw[3]-o.input.Lw[0]));
                    float3 L = normalize(lerp(P, C, 0.3));
                    float dist = lerp(length(P), length(C), 0.3) + 1;
                    float rim = saturate(1 - abs(dot(s.N,s.V)) * 2 + dot(s.N,L));
                    float at = abs(dot(N, normalize(P))) / pow(dist, 3);
                    at *= lerp(rim, 1, saturate(dot(s.V,L) * 0.5 + 0.5));
                    return saturate(at);
                }
                void ma_cb_diffuse(inout ma_ltcgi s, in ltcgi_output o)  { s.diff += ma_atten(s,o) * o.color * 2; }
                void ma_cb_specular(inout ma_ltcgi s, in ltcgi_output o) { s.spec += ma_atten(s,o) * o.color; }
                #define LTCGI_V2_CUSTOM_INPUT ma_ltcgi
                #define LTCGI_V2_DIFFUSE_CALLBACK ma_cb_diffuse
                #define LTCGI_V2_SPECULAR_CALLBACK ma_cb_specular
                #include "Packages/at.pimaker.ltcgi/Shaders/LTCGI.cginc"
                #undef Sample
            #endif

            float4 fragBase(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                UNITY_APPLY_DITHER_CROSSFADE(i.pos.xy);   // LOD クロスフェード（消える時のディザ）
                Surf s = SampleSurface(i);

                #if defined(_DEBUG_NORMAL_ON)
                    // 1=ワールド法線 2=ノーマル配列 3=タンジェント 4=metallic 5=smoothness
                    if (_DebugMode < 1.5) return float4(s.worldN * 0.5 + 0.5, 1);
                    if (_DebugMode < 2.5) return float4(UNITY_SAMPLE_TEX2DARRAY(_BumpMapArray, float3(i.uv, i.layer)).rgb, 1);
                    if (_DebugMode < 3.5) return float4(normalize(i.worldTan.xyz) * 0.5 + 0.5, 1);
                    float4 mgDbg = UNITY_SAMPLE_TEX2DARRAY(_MetallicGlossMapArray, float3(i.uv, i.layer));
                    if (_DebugMode < 4.5) return float4(mgDbg.rrr, 1);   // metallic（白=金属）
                    return float4(mgDbg.aaa, 1);                          // smoothness（白=ツルツル）
                #endif

                // --- 直接光（メイン平行光源・影付き） ---
                float3 direct = 0;
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float  NoL = saturate(dot(s.worldN, L));
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float3 lc = _LightColor0.rgb * atten;
                direct += s.diffuseAlbedo * lc * NoL;
                direct += SpecGGX(s.worldN, s.V, L, s.roughness, s.f0) * lc * NoL;

                // --- 間接光（ライトマップ → Light Volumes → ライトプローブ）＋反射＋LTCGI ---
                float3 indirect = 0;
                #if defined(LIGHTMAP_ON)
                    float3 bakedSpec;
                    indirect += s.diffuseAlbedo * SampleBakedGISpec(i.lmuv, s.worldN, s.V, s.roughness, s.f0, bakedSpec);
                    indirect += bakedSpec;   // MonoSH ベイクスペキュラ（OFF時は 0）
                    #if defined(_LV_ON)
                        // ライトマップ(静的GI)に「加算ライトボリューム」(PointLightVolume等の動的光)を足す
                        float3 aL0, aL1r, aL1g, aL1b;
                        LightVolumeAdditiveSH(i.worldPos, aL0, aL1r, aL1g, aL1b);
                        indirect += s.diffuseAlbedo * LightVolumeEvaluate(s.worldN, aL0, aL1r, aL1g, aL1b);
                        indirect += LightVolumeSpecular(s.albedo, s.smoothness, s.metallic, s.worldN, s.V, aL0, aL1r, aL1g, aL1b);
                    #endif
                #elif defined(_LV_ON)
                    // 通常/加算/PLV を分解し、**通常(ベイク)ボリュームだけ**にリアルタイム影
                    // マスクを掛ける（DHK/TerrainLV と同じ。蛍・提灯の光が太陽の影で消えるのを
                    // 防ぐ）。LightVolumeSH の合成順（regular → additive → PLV）を再現。
                    // PLV は旧実装（5引数 LightVolumeSH）と同じく normal shading 無し(0,0)で呼ぶ。
                    // 注: _LVShadowMulStrength=0 でも拡散は旧実装と数学的に同値だが、
                    // スペキュラは系統別評価になるぶん僅かに変わる（GGX近似が非線形のため）
                    float3 L0 = 0, L1r = 0, L1g = 0, L1b = 0;
                    LV_LightVolumeRegularSH(i.worldPos, L0, L1r, L1g, L1b);
                    float3 A0 = 0, A1r = 0, A1g = 0, A1b = 0;
                    LV_LightVolumeAdditiveSH(i.worldPos, A0, A1r, A1g, A1b);
                    LV_PointLightVolumeSH(i.worldPos, 0, 0, A0, A1r, A1g, A1b);

                    // [branch] は平坦化されうるので、三項で strength=0 → mask=1 を保証
                    //（平坦化で「LVを丸ごと引き算」になる事故は Rna's World の実測済み）
                    float lvMask = _LVShadowMulStrength > 0.001
                        ? lerp(1.0, lerp(_LVShadowFloor, 1.0, atten), _LVShadowMulStrength)
                        : 1.0;

                    indirect += s.diffuseAlbedo * LightVolumeEvaluate(s.worldN, L0, L1r, L1g, L1b) * lvMask;
                    indirect += LightVolumeSpecular(s.albedo, s.smoothness, s.metallic, s.worldN, s.V, L0, L1r, L1g, L1b) * lvMask;
                    indirect += s.diffuseAlbedo * LightVolumeEvaluate(s.worldN, A0, A1r, A1g, A1b);
                    indirect += LightVolumeSpecular(s.albedo, s.smoothness, s.metallic, s.worldN, s.V, A0, A1r, A1g, A1b);
                #else
                    indirect += s.diffuseAlbedo * ShadeSH9(float4(s.worldN, 1));
                #endif

                #if defined(_LTCGI_ON)
                    ma_ltcgi lt = (ma_ltcgi)0;
                    lt.N = s.worldN; lt.V = s.V;
                    LTCGI_Contribution(lt, i.worldPos, s.worldN, s.V, s.roughness, i.lmuv);
                    indirect += s.diffuseAlbedo * lt.diff + lt.spec;
                #endif

                indirect += SampleReflectionProbe(s.worldN, s.V, s.roughness, s.f0, s.smoothness);

                // 間接光だけ _IndirectMul で抑えると影が締まりコントラストが上がる
                float3 col = direct + indirect * _IndirectMul;

                // 彩度調整（渋い見た目用。1=変更なし）
                col = lerp(dot(col, float3(0.299, 0.587, 0.114)), col, _Saturation);

                col *= _LightingMul;
                col += s.emission;

                float4 o = float4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, o);
                return o;
            }
            ENDCG
        }

        // ===================== ForwardAdd（追加リアルタイムライト） =====================
        Pass
        {
            Tags { "LightMode"="ForwardAdd" }
            Blend One One
            ZWrite Off

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment fragAdd
            #pragma target   4.0
            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _INVERTSMOOTH_ON
            #pragma shader_feature_local _EMISSION_ARRAY_ON

            float4 fragAdd(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                UNITY_APPLY_DITHER_CROSSFADE(i.pos.xy);   // LOD クロスフェード
                Surf s = SampleSurface(i);

                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float3 L  = normalize(UnityWorldSpaceLightDir(i.worldPos));
                float  NoL = saturate(dot(s.worldN, L));
                float3 lc = _LightColor0.rgb * atten;

                float3 col = s.diffuseAlbedo * lc * NoL;
                col += SpecGGX(s.worldN, s.V, L, s.roughness, s.f0) * lc * NoL;
                col *= _LightingMul;

                float4 o = float4(col, 1);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, o, fixed4(0,0,0,0)); // 追加光は黒へフェード
                return o;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
