Shader "you5248/SoyogiCleanroom"
{
    Properties
    {
        // ============================================================
        // 風
        // ============================================================
        [Toggle(_WIND_OFF)] _WindOff ("Wind Off", Float) = 0
        _LocalWindPower ("Local Wind Power", Range(0, 5)) = 3
        _LocalWindSpeed ("Local Wind Speed", Float) = 1
        _LocalWindBurstsPower ("Wind Bursts Power", Range(0, 10)) = 0.5
        _LocalWindBurstsSpeed ("Wind Bursts Speed", Float) = 5
        _LocalWindBurstsScale ("Wind Bursts Scale", Float) = 10
        _LocalMicroPower ("Micro Wind Power", Range(0, 1)) = 0.1
        _LocalMicroSpeed ("Micro Wind Speed", Float) = 1
        _LocalMicroFrequency ("Micro Wind Frequency", Float) = 3
        _WindMultiplier ("Wind Multiplier", Float) = 1
        _MicroWindMultiplier ("Micro Wind Multiplier", Float) = 1
        _MicroWindYDamp ("Micro Wind Y Damp", Range(0, 1)) = 0
        _WindTrunkPosition ("Wind Trunk Position", Float) = 0
        _WindTrunkContrast ("Wind Trunk Contrast", Float) = 10

        [Toggle(_WINDMASK_HEIGHT)] _WindMaskHeightMode ("Use Height Wind Mask", Float) = 0
        _WindMaskMin ("Wind Mask Minimum Height", Float) = 0
        _WindMaskMax ("Wind Mask Maximum Height", Float) = 0.3

        [Toggle(_WINDFADE_ON)] _WindDistanceFade ("Wind Distance Fade", Float) = 0
        _WindFadeStart ("Wind Fade Start", Float) = 8
        _WindFadeEnd ("Wind Fade End", Float) = 16

        // ============================================================
        // メイン
        // ============================================================
        [Enum(Off, 0, Front, 1, Back, 2)] _CullMode ("Cull Mode", Float) = 2
        _Cutoff ("Alpha Cutoff", Float) = 0.5
        _MainColor ("Main Color", Color) = (1, 1, 1, 0)
        _Diffuse ("Diffuse", 2D) = "white" {}
        _VertexOcclusionPower ("Vertex Occlusion Power", Range(0, 1)) = 0

        [Toggle(_NORMALMAP_ON)] _NormalMapEnabled ("Normal Map Enabled", Float) = 1
        _Normal ("Normal Map", 2D) = "bump" {}
        _NormalPower ("Normal Power", Range(0, 1)) = 0.45

        // ============================================================
        // グラデーション
        // ============================================================
        [Toggle(_GRADIENT_ON)] _GradientEnabled ("Gradient Enabled", Float) = 0
        _GradientColor ("Gradient Color", Color) = (1, 1, 1, 0)
        _GradientFalloff ("Gradient Falloff", Range(0, 2)) = 2
        _GradientPosition ("Gradient Position", Range(0, 1)) = 0.5
        [Toggle(_INVERTGRADIENT_ON)] _InvertGradient ("Invert Gradient", Float) = 0

        // ============================================================
        // 色ムラ
        // ============================================================
        [Toggle(_COLORVARIATION_ON)] _ColorVariationEnabled ("Color Variation Enabled", Float) = 0
        _ColorVariation ("Color Variation", Color) = (1, 0, 0, 0)
        _ColorVariationPower ("Color Variation Power", Range(0, 1)) = 1
        _ColorVariationNoise ("Color Variation Noise", 2D) = "white" {}
        _NoiseScale ("Noise Scale", Float) = 0.5

        // ============================================================
        // 逆光透過
        // ============================================================
        _Translucency ("Translucency", Range(0, 50)) = 1
        _TransNormalDistortion ("Translucency Normal Distortion", Range(0, 1)) = 0.1
        _TransScattering ("Translucency Scattering", Range(1, 50)) = 2
        _TransDirect ("Translucency Direct", Range(0, 1)) = 1
        _TransAmbient ("Translucency Ambient", Range(0, 1)) = 0.2
        _TransShadow ("Translucency Shadow", Range(0, 1)) = 0.9

        // ============================================================
        // 側面隠し
        // ============================================================
        [Toggle(_HIDESIDES_ON)] _HideSides ("Hide Sides", Float) = 0
        _HidePower ("Hide Power", Float) = 2.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "Geometry"
        }

        Cull [_CullMode]

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _Diffuse;

        // _Diffuse_ST はサーフェス側で自動宣言されるためここでは宣言しない（ShadowCasterパス内で個別宣言）
        half _Cutoff;

        float _LocalWindPower;
        float _LocalWindSpeed;
        float _LocalWindBurstsPower;
        float _LocalWindBurstsSpeed;
        float _LocalWindBurstsScale;
        float _LocalMicroPower;
        float _LocalMicroSpeed;
        float _LocalMicroFrequency;
        float _WindMultiplier;
        float _MicroWindMultiplier;
        float _MicroWindYDamp;
        float _WindTrunkPosition;
        float _WindTrunkContrast;
        float _WindMaskMin;
        float _WindMaskMax;
        float _WindFadeStart;
        float _WindFadeEnd;

        // ============================================================
        // 滑らかな流走ノイズ
        // ============================================================
        float SmoothWindField(float2 position)
        {
            float firstWave = sin(dot(position, float2(1.0, 1.61803399)));
            float secondWave = sin(dot(position, float2(-1.41421356, 0.70710678)) + 1.173);
            return saturate(0.5 + firstWave * 0.25 + secondWave * 0.25);
        }

        // ============================================================
        // 共通の風頂点変形
        // ============================================================
        void DeformVegetationVertex(inout float4 objectVertex, float3 objectNormal, float4 vertexColor)
        {
        // Quest/Android は揺れを使わない。モバイル向けコンパイルでは頂点風の
        // 計算自体を除去し、マテリアル設定に依存させない。
        #if !defined(_WIND_OFF) && !defined(SHADER_API_MOBILE)
            float3 originalObjectPosition = objectVertex.xyz;
            float3 originalWorldPosition =
                mul(unity_ObjectToWorld, float4(originalObjectPosition, 1.0)).xyz;

            float leafWeight;
            float trunkWeight;

        #if defined(_WINDMASK_HEIGHT)
            float heightSpan = max(_WindMaskMax - _WindMaskMin, 1e-4);
            float heightRamp =
                saturate((originalObjectPosition.y - _WindMaskMin) / heightSpan);

            leafWeight = heightRamp;
            trunkWeight = heightRamp;
        #else
            leafWeight = vertexColor.r;

            float shapedTrunk;
            [branch]
            if (_WindTrunkPosition == 0.0)
            {
                shapedTrunk = 1.0;
            }
            else
            {
                shapedTrunk =
                    pow(abs(1.0 - vertexColor.g), _WindTrunkPosition);
            }

            trunkWeight = saturate(
                shapedTrunk * (0.5 + 0.5 * _WindTrunkContrast)
            );
        #endif

            float timeSeconds = _Time.y;

            float2 gustCoordinates =
                (originalWorldPosition.xz + timeSeconds * _LocalWindBurstsSpeed)
                * (_LocalWindBurstsScale / 100.0);

            float gustStrength = SmoothWindField(gustCoordinates);

            float trunkAmplitude =
                _LocalWindPower
                * gustStrength
                * _LocalWindBurstsPower
                * trunkWeight;

            float trunkPhase = timeSeconds * _LocalWindSpeed;
            float3 trunkMotion = float3(
                sin(trunkPhase) * trunkAmplitude,
                0.0,
                cos(trunkPhase) * trunkAmplitude * 0.5
            ) * _WindMultiplier;

            float microDrift = SmoothWindField(
                originalWorldPosition.xz + timeSeconds * _LocalMicroSpeed
            );

            float3 microWave = sin(
                _LocalMicroFrequency
                * (originalWorldPosition + microDrift)
            );

            float3 normalizedObjectNormal = normalize(objectNormal);
            float3 normalDirectedMotion = microWave * normalizedObjectNormal;
            float3 horizontalMotion = float3(microWave.x, 0.0, microWave.z);

            float3 microDirection = lerp(
                normalDirectedMotion,
                horizontalMotion,
                _MicroWindYDamp
            );

            float3 microMotion =
                microDirection
                * _LocalMicroPower
                * leafWeight
                * _MicroWindMultiplier;

            float distanceFade = 1.0;

        #if defined(_WINDFADE_ON)
            float fadeSpan = max(_WindFadeEnd - _WindFadeStart, 1e-3);
            float cameraDistance =
                distance(originalWorldPosition, _WorldSpaceCameraPos);

            distanceFade = saturate(
                (_WindFadeEnd - cameraDistance) / fadeSpan
            );
        #endif

            objectVertex.xyz += (trunkMotion + microMotion) * distanceFade;
        #endif

            objectVertex.w = 1.0;
        }
        ENDCG

        // ============================================================
        // サーフェスシェーダー本体
        // ============================================================
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Soyogi keepalpha fullforwardshadows exclude_path:deferred dithercrossfade vertex:SoyogiVertex

        #pragma multi_compile_instancing
        #pragma instancing_options assumeuniformscaling
        #pragma multi_compile _ LOD_FADE_CROSSFADE

        #pragma shader_feature_local _ _WIND_OFF
        #pragma shader_feature_local _ _WINDMASK_HEIGHT
        #pragma shader_feature_local _ _WINDFADE_ON

        // 注意: _Diffuse_ST はサーフェス生成コードが自動宣言するため、ここで宣言してはならない
        #pragma shader_feature_local _ _NORMALMAP_ON
        #pragma shader_feature_local _ _GRADIENT_ON
        #pragma shader_feature_local _ _INVERTGRADIENT_ON
        #pragma shader_feature_local _ _COLORVARIATION_ON
        #pragma shader_feature_local _ _COLORVAR_NOISETEX_ON
        #pragma shader_feature_local _ _HIDESIDES_ON
        #pragma shader_feature_local _ _TINT_ON

        sampler2D _Normal;
        // 注意: _Normal_ST は Input の uv_Normal によりサーフェス生成コードが自動宣言する
        half _NormalPower;

        fixed4 _MainColor;
        half _VertexOcclusionPower;

        fixed4 _GradientColor;
        half _GradientFalloff;
        half _GradientPosition;

        fixed4 _ColorVariation;
        half _ColorVariationPower;
        sampler2D _ColorVariationNoise;
        float _NoiseScale;

        half _Translucency;
        half _TransNormalDistortion;
        half _TransScattering;
        half _TransDirect;
        half _TransAmbient;
        half _TransShadow;

        half _HidePower;

        struct Input
        {
            float2 uv_Diffuse;

        #if defined(_NORMALMAP_ON)
            float2 uv_Normal;
        #endif

            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA

            fixed4 vertexColor : COLOR;

        #if defined(_HIDESIDES_ON)
            float4 soyogiScreenPosition;
        #endif
        };

        struct SurfaceOutputSoyogi
        {
            fixed3 Albedo;
            fixed3 Normal;
            fixed3 Emission;
            half Occlusion;
            fixed Alpha;
            half3 Translucency;
        };

        // ============================================================
        // サーフェス用頂点処理
        // ============================================================
        void SoyogiVertex(inout appdata_full vertexData, out Input outputData)
        {
            UNITY_INITIALIZE_OUTPUT(Input, outputData);

            DeformVegetationVertex(
                vertexData.vertex,
                vertexData.normal,
                vertexData.color
            );

            outputData.vertexColor = vertexData.color;

        #if defined(_HIDESIDES_ON)
            float4 clipPosition = UnityObjectToClipPos(vertexData.vertex);
            outputData.soyogiScreenPosition = ComputeScreenPos(clipPosition);
        #endif
        }

        // ============================================================
        // 8×8 Bayerディザ
        // ============================================================
        float Bayer8x8(float2 pixelPosition)
        {
            float2 cell = floor(fmod(abs(floor(pixelPosition)), 8.0));

            float2 bit0 = fmod(cell, 2.0);
            float2 bit1 = fmod(floor(cell * 0.5), 2.0);
            float2 bit2 = fmod(floor(cell * 0.25), 2.0);

            float rank0 =
                bit0.x * 2.0 + bit0.y * 3.0
                - bit0.x * bit0.y * 4.0;

            float rank1 =
                bit1.x * 2.0 + bit1.y * 3.0
                - bit1.x * bit1.y * 4.0;

            float rank2 =
                bit2.x * 2.0 + bit2.y * 3.0
                - bit2.x * bit2.y * 4.0;

            return (rank0 * 16.0 + rank1 * 4.0 + rank2 + 0.5) / 64.0;
        }

        // ============================================================
        // サーフェス出力
        // ============================================================
        void surf(Input IN, inout SurfaceOutputSoyogi outputSurface)
        {
            fixed4 diffuseSample = tex2D(_Diffuse, IN.uv_Diffuse);
            half sourceAlpha = diffuseSample.a;

        #if !defined(_HIDESIDES_ON)
            clip(sourceAlpha - _Cutoff);
        #endif

            outputSurface.Albedo = diffuseSample.rgb;
            outputSurface.Normal = fixed3(0.0, 0.0, 1.0);
            outputSurface.Emission = 0.0;
            outputSurface.Occlusion = 1.0;
            outputSurface.Alpha = sourceAlpha;
            outputSurface.Translucency = IN.vertexColor.r;

        #if defined(_NORMALMAP_ON)
            outputSurface.Normal = UnpackScaleNormal(
                tex2D(_Normal, IN.uv_Normal),
                _NormalPower
            );
        #endif

        #if defined(_TINT_ON)
            fixed4 selectedTint;

        #if defined(_GRADIENT_ON)
            float3 unperturbedWorldNormal = normalize(
                WorldNormalVector(IN, float3(0.0, 0.0, 1.0))
            );

            float normalHeight = unperturbedWorldNormal.y;

        #if defined(_INVERTGRADIENT_ON)
            normalHeight = 1.0 - normalHeight;
        #endif

            float gradientBias = -2.0 + _GradientPosition * 3.0;
            float gradientAmount = saturate(
                (normalHeight + gradientBias)
                / max(_GradientFalloff, 1e-4)
            );

            selectedTint = lerp(
                _MainColor,
                _GradientColor,
                gradientAmount
            );
        #else
            selectedTint = _MainColor;
        #endif

            fixed4 variedTint = selectedTint;

        #if defined(_COLORVARIATION_ON)
            fixed4 dodgeTint = saturate(
                lerp(
                    _ColorVariation,
                    _ColorVariation
                        / max(1.0 - selectedTint, fixed4(1e-5, 1e-5, 1e-5, 1e-5)),
                    _ColorVariationPower
                )
            );

            fixed4 variationNoise = 1.0;

        #if defined(_COLORVAR_NOISETEX_ON)
            variationNoise = abs(
                tex2D(
                    _ColorVariationNoise,
                    IN.worldPos.xz * (_NoiseScale / 100.0)
                )
            );

            variationNoise =
                variationNoise * variationNoise * variationNoise;
        #endif

            variedTint = lerp(
                selectedTint,
                dodgeTint,
                _ColorVariationPower * variationNoise
            );
        #endif

            outputSurface.Albedo = lerp(
                diffuseSample.rgb,
                variedTint.rgb * diffuseSample.rgb,
                _MainColor.a * IN.vertexColor.r
            );
        #endif

            [branch]
            if (_VertexOcclusionPower == 0.0)
            {
                outputSurface.Occlusion = 1.0;
            }
            else
            {
                outputSurface.Occlusion = pow(
                    abs(IN.vertexColor.a),
                    _VertexOcclusionPower
                );
            }

        #if defined(_HIDESIDES_ON)
            float3 geometricNormal = normalize(
                cross(ddy(IN.worldPos), ddx(IN.worldPos))
            );

            float3 directionToCamera = normalize(
                _WorldSpaceCameraPos - IN.worldPos
            );

            float facingAmount = abs(
                dot(directionToCamera, geometricNormal)
            );

            float edgeVisibility = saturate(
                (1.0 - (1.0 - facingAmount) * 2.0)
                * sourceAlpha
                * _HidePower
            );

            float ditherTarget = lerp(
                sourceAlpha,
                edgeVisibility,
                IN.vertexColor.r
            );

            float2 normalizedScreenPosition =
                IN.soyogiScreenPosition.xy
                / max(IN.soyogiScreenPosition.w, 1e-6);

            normalizedScreenPosition =
                UnityStereoTransformScreenSpaceTex(
                    normalizedScreenPosition
                );

            float2 pixelPosition =
                normalizedScreenPosition * _ScreenParams.xy;

            float ditherThreshold = Bayer8x8(pixelPosition);

            clip(step(ditherThreshold, ditherTarget) - _Cutoff);
        #endif
        }

        // ============================================================
        // カスタムGI
        // ============================================================
        inline void LightingSoyogi_GI(
            SurfaceOutputSoyogi surface,
            UnityGIInput data,
            inout UnityGI gi)
        {
            gi = UnityGlobalIllumination(
                data,
                surface.Occlusion,
                surface.Normal
            );
        }

        // ============================================================
        // Lambert + 逆光透過
        // ============================================================
        half4 LightingSoyogi(
            SurfaceOutputSoyogi surface,
            half3 viewDir,
            UnityGI gi)
        {
            half normalLight = max(
                0.0h,
                dot(surface.Normal, gi.light.dir)
            );

            half3 transmissionLight;

        #if defined(DIRECTIONAL)
            transmissionLight = lerp(
                _LightColor0.rgb,
                gi.light.color,
                _TransShadow
            );
        #else
            transmissionLight = gi.light.color;
        #endif

            half3 distortedLightDirection =
                gi.light.dir
                + surface.Normal * _TransNormalDistortion;

            half scatteringBase = saturate(
                dot(viewDir, -distortedLightDirection)
            );

            half scattering;

            [branch]
            if (_TransScattering == 1.0h)
            {
                scattering = scatteringBase;
            }
            else if (_TransScattering == 2.0h)
            {
                scattering = scatteringBase * scatteringBase;
            }
            else
            {
                scattering = pow(
                    scatteringBase,
                    _TransScattering
                );
            }

            half3 transmitted =
                transmissionLight
                * (
                    scattering * _TransDirect
                    + gi.indirect.diffuse * _TransAmbient
                )
                * surface.Translucency;

            half3 litColor =
                surface.Albedo
                * (
                    gi.light.color * normalLight
                    + gi.indirect.diffuse * surface.Occlusion
                );

            litColor +=
                surface.Albedo
                * transmitted
                * _Translucency;

            return half4(litColor, 1.0h);
        }
        ENDCG

        // ============================================================
        // 軽量ShadowCaster
        // ============================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex SoyogiShadowVertex
            #pragma fragment SoyogiShadowFragment
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling

            #pragma shader_feature_local _ _WIND_OFF
            #pragma shader_feature_local _ _WINDMASK_HEIGHT
            #pragma shader_feature_local _ _WINDFADE_ON

            float4 _Diffuse_ST; // TRANSFORM_TEX用（サーフェスパスでは自動宣言されるがこのパスは手書きのため）

            struct SoyogiShadowAttributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct SoyogiShadowVaryings
            {
                V2F_SHADOW_CASTER;
                float2 diffuseUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // 注意: TRANSFER_SHADOW_CASTER_NORMALOFFSET マクロは頂点入力の変数名が
            // 「v」であることを暗黙に要求するため、この関数の引数名だけ v とする
            SoyogiShadowVaryings SoyogiShadowVertex(
                SoyogiShadowAttributes v)
            {
                SoyogiShadowVaryings outputData;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(
                    SoyogiShadowVaryings,
                    outputData
                );
                UNITY_TRANSFER_INSTANCE_ID(
                    v,
                    outputData
                );

                DeformVegetationVertex(
                    v.vertex,
                    v.normal,
                    v.color
                );

                outputData.diffuseUV = TRANSFORM_TEX(
                    v.texcoord,
                    _Diffuse
                );

                TRANSFER_SHADOW_CASTER_NORMALOFFSET(outputData)
                return outputData;
            }

            fixed4 SoyogiShadowFragment(
                SoyogiShadowVaryings inputData) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(inputData);

                fixed alpha = tex2D(
                    _Diffuse,
                    inputData.diffuseUV
                ).a;

                clip(alpha - _Cutoff);
                SHADOW_CASTER_FRAGMENT(inputData)
            }
            ENDCG
        }
    }

    Fallback "Diffuse"
    CustomEditor "SoyogiGUI"
}


