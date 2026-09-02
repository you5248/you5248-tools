// you5248 / FloatingPetalsCore.cginc
// FloatingPetals / FloatingPetalsMeniscus 共通の散布ロジック。
// _MENISCUS_ON 未定義時は追加補間・追加引数・追加タップを一切出さない。

#ifndef FLOATING_PETALS_CORE_INCLUDED
#define FLOATING_PETALS_CORE_INCLUDED

#include "UnityCG.cginc"

// ---------------------------------------------------------------------------
// 共通ユニフォーム
// ---------------------------------------------------------------------------
sampler2D _MainTex;     float4 _MainTex_ST;
#if defined(_MASK_ON)
sampler2D _DensityMask; float4 _DensityMask_ST;
#endif
#if defined(_WAVE_ON)
sampler2D _WaveTex;
float  _WaveScale, _WaveStrength;
float4 _WaveSpeed;
#endif
#if defined(_FOAM_ON)
UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
float _FoamDistance, _CoverageNear, _FoamPower, _FoamDensityMul, _FoamSizeScale;
#endif

float4 _Color, _ColorB;
float  _TintVariation, _Opacity;
float  _Density, _Coverage, _SizeMin, _SizeMax, _Jitter, _RandomFlip, _Seed, _Layers;
float  _SpinSpeed, _SwaySpeed, _SwayAmount, _DriftSpeed, _DriftAmount;
float4 _FlowDir;
float  _AmbientStrength, _AlphaClip;

// ---------------------------------------------------------------------------
// メニスカス用ユニフォーム / GrabPass サンプル（SPS-I 対応）
// ---------------------------------------------------------------------------
#if defined(_MENISCUS_ON)
float _MeniscusWidth, _MeniscusStrength, _MeniscusOpacity, _MeniscusSign, _MeniscusContrast;
float _MeniscusFoam;

#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
Texture2DArray _FPGrab; SamplerState sampler_FPGrab;
#define FP_SAMPLE_GRAB(uv) _FPGrab.SampleLevel(sampler_FPGrab, float3((uv).xy, (float)unity_StereoEyeIndex), 0)
#else
Texture2D _FPGrab; SamplerState sampler_FPGrab;
#define FP_SAMPLE_GRAB(uv) _FPGrab.SampleLevel(sampler_FPGrab, (uv).xy, 0)
#endif
#endif

// ---------------------------------------------------------------------------
// 頂点入出力
// ---------------------------------------------------------------------------
struct appdata
{
    float4 vertex : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct v2f
{
    float4 pos      : SV_POSITION;
    float2 uv       : TEXCOORD0;
    float3 worldPos : TEXCOORD1;
    float4 screenPos: TEXCOORD2;
    float  eyeDepth : TEXCOORD3;
    UNITY_FOG_COORDS(4)
#if defined(_MENISCUS_ON)
    float4 grabPos  : TEXCOORD5;
#endif
    UNITY_VERTEX_OUTPUT_STEREO
};

// ---------------------------------------------------------------------------
// 乱数 / 回転
// ---------------------------------------------------------------------------
// Dave Hoskins hash22 ―― sin非依存で実機安定
float2 hash22(float2 p)
{
    p += _Seed * 19.19;
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float2 rot2(float2 v, float a)
{
    float s, c; sincos(a, s, c);
    return float2(v.x * c - v.y * s, v.x * s + v.y * c);
}

// ---------------------------------------------------------------------------
// 頂点
// ---------------------------------------------------------------------------
v2f vert(appdata v)
{
    v2f o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.pos      = UnityObjectToClipPos(v.vertex);
    o.uv       = TRANSFORM_TEX(v.uv, _MainTex);
    o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
    o.screenPos= ComputeScreenPos(o.pos);
    COMPUTE_EYEDEPTH(o.eyeDepth);
    UNITY_TRANSFER_FOG(o, o.pos);
#if defined(_MENISCUS_ON)
    // ComputeGrabScreenPos のみ。UnityStereoTransformScreenSpaceTex は重ねがけしない
    o.grabPos  = ComputeGrabScreenPos(o.pos);
#endif
    return o;
}

// ---------------------------------------------------------------------------
// 1セルぶんの花びらを評価して、premultipliedで accum に合成する
// doMeniscus は呼び出し側でリテラル bool を渡し定数畳み込みさせる
// ---------------------------------------------------------------------------
void accumPetal(float2 cellId, float2 field, float cellDensity,
                float baseCoverage, float sizeScale, float layerPhase,
                float2 ddxW, float2 ddyW, inout float4 accum
#if defined(_MENISCUS_ON)
                , inout float3 men, bool doMeniscus
#endif
                )
{
    float2 r1 = hash22(cellId);
    float2 r2 = hash22(cellId + 17.17);
    float2 r3 = hash22(cellId + 41.41);

    // spawn 判定（物の周りで baseCoverage を上げてある）
    if (r1.x > baseCoverage) return;

    // セル内のランダム中心
    float2 center = cellId + 0.5 + (r1 - 0.5) * _Jitter;

    // 時間アニメ：ふよふよドリフト
    float t  = _Time.y;
    float ph = (r3.x + r3.y) * 6.2831853 + layerPhase;
    center += float2(sin(t * _DriftSpeed + ph),
                     sin(t * _DriftSpeed * 0.9 + ph * 1.3))
              * (_DriftAmount * cellDensity);

#if defined(_WAVE_ON)
    // 波ノーマルマップで座標を揺らす（Mochie水と同じmapを入れると馴染む）
    float2 wuv = center / max(cellDensity, 1e-3) * _WaveScale + _WaveSpeed.xy * t;
    float3 wn  = UnpackNormal(tex2Dlod(_WaveTex, float4(wuv, 0, 0)));
    center += wn.xy * (_WaveStrength * cellDensity);
#endif

    // 局所座標 → 回転 → テクスチャUV
    float size = lerp(_SizeMin, _SizeMax, r2.y) * sizeScale;
    float fullSizeCells = max(size * cellDensity, 1e-4);

    float angle = r2.x * 6.2831853
                + t * _SpinSpeed * (r3.x > 0.5 ? 1.0 : -1.0)
                + sin(t * _SwaySpeed + ph) * _SwayAmount;

    float2 p   = field - center;
    float2 loc = rot2(p, angle) / fullSizeCells + 0.5;

    // 左右反転
#if defined(_MENISCUS_ON)
    bool flipX = (_RandomFlip > 0.5 && r3.y > 0.5);
    if (flipX) loc.x = 1.0 - loc.x;
#else
    if (_RandomFlip > 0.5 && r3.y > 0.5) loc.x = 1.0 - loc.x;
#endif

    // 範囲外は寄与なし
    float inB = step(0.0, loc.x) * step(loc.x, 1.0)
              * step(0.0, loc.y) * step(loc.y, 1.0);
    if (inB < 0.5) return;

    // 勾配を明示してサンプル：ループ＋early-returnで画面微分(ddx/ddy)が壊れ、
    // ミップ段が暴れて高ミップの「四角ブロック」が出るのを防ぐ。
    // angle/fullSizeCellsはセル定数なので、ワールド微分を回して割るだけで正しい勾配になる。
    float2 dFx = ddxW * cellDensity;
    float2 dFy = ddyW * cellDensity;
    float2 dLocX = rot2(dFx, angle) / fullSizeCells;
    float2 dLocY = rot2(dFy, angle) / fullSizeCells;
    float4 tex = tex2Dgrad(_MainTex, loc, dLocX, dLocY);

#if defined(_MENISCUS_ON)
    // メニスカス：縁帯の輪郭方向を4タップαから推定し men に集積（accum更新前）
    if (doMeniscus)
    {
        float footprint = max(length(dLocX), length(dLocY));
        float r  = max(_MeniscusWidth, footprint * 1.5);
        float bs = r / max(footprint, 1e-5);
        float2 gX = dLocX * bs;
        float2 gY = dLocY * bs;

        float aPX = tex2Dgrad(_MainTex, loc + float2( r, 0), gX, gY).a;
        float aNX = tex2Dgrad(_MainTex, loc + float2(-r, 0), gX, gY).a;
        float aPY = tex2Dgrad(_MainTex, loc + float2( 0, r), gX, gY).a;
        float aNY = tex2Dgrad(_MainTex, loc + float2( 0,-r), gX, gY).a;

        float band = saturate(((aPX + aNX + aPY + aNY) * 0.25 - tex.a) * _MeniscusContrast);

        float2 g = float2(aPX - aNX, aPY - aNY);
        float2 dirLoc = -g / max(length(g), 1e-5);
        if (flipX) dirLoc.x = -dirLoc.x;

        // 局所UV方向 → ワールドXZ（rot2の逆＝角度符号反転）× size × 盛り上がり符号
        float2 dirW = rot2(dirLoc, -angle) * size * _MeniscusSign;

        float w = band * (1.0 - accum.a);
        men.xy += dirW * w;
        men.z  += w;
    }
#endif

    float3 tcol = lerp(_Color.rgb, _ColorB.rgb, r3.x * _TintVariation) * tex.rgb;
    float  a    = tex.a * _Opacity;
    if (a <= 0.0) return;

    // premultiplied over（順不同合成）
    float3 pc = tcol * a;
    accum.rgb += pc * (1.0 - accum.a);
    accum.a   += a  * (1.0 - accum.a);
}

// ---------------------------------------------------------------------------
// 1スキャン分の散布（3x3近傍 × layers枚）を accum に合成
// ---------------------------------------------------------------------------
void scatterPass(float2 worldXZ, float cellDensity, float coverage,
                 float sizeScale, int layers, float seedShift,
                 float2 ddxW, float2 ddyW, inout float4 accum
#if defined(_MENISCUS_ON)
                 , inout float3 men, bool doMeniscus
#endif
                 )
{
    float2 flow = _FlowDir.xy * cellDensity * _Time.y;        // 一様な流れ（X,Y=ワールドXZ）
    [loop]
    for (int L = 0; L < layers; L++)
    {
        float2 lOff  = float2(L * 0.37, L * 0.61) + seedShift;
        float2 field = worldXZ * cellDensity + lOff * 13.0 - flow;
        float2 baseCell = floor(field);
        float  lPhase = L * 2.39996 + seedShift;

        [unroll]
        for (int oy = -1; oy <= 1; oy++)
        [unroll]
        for (int ox = -1; ox <= 1; ox++)
        {
            accumPetal(baseCell + float2(ox, oy), field, cellDensity,
                       coverage, sizeScale, lPhase, ddxW, ddyW, accum
#if defined(_MENISCUS_ON)
                       , men, doMeniscus
#endif
                       );
        }
    }
}

// ---------------------------------------------------------------------------
// フラグメント
// ---------------------------------------------------------------------------
float4 frag(v2f i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

    float mask = 1.0;
#if defined(_MASK_ON)
    mask = tex2D(_DensityMask, i.uv).r;
#endif

    // --- 物の際の検出（深度の交差。0=遠い 1=際） ---
    float foam = 0;
#if defined(_FOAM_ON)
    float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos)));
    float diff   = sceneZ - i.eyeDepth;                       // 交差近くで小さくなる
    foam = pow(saturate(1.0 - diff / max(_FoamDistance, 1e-3)), _FoamPower);
#endif

#if defined(_DEBUG_FOAM)
    // 際が検出できていれば赤く出る。出ないなら深度テクスチャが無い。
    return fixed4(foam, foam * 0.15, 0, 1);
#endif

    float4 accum = 0;
#if defined(_MENISCUS_ON)
    float3 men = 0;
#endif
    int layers = (int)round(_Layers);

    // ワールドXZの画面微分は「分岐前」に取る＝2x2クアッド内で必ず有効。
    // これを花びらUVの勾配に変換して tex2Dgrad に渡す（ミップ暴走による四角化を防ぐ）。
    float2 ddxW = ddx(i.worldPos.xz);
    float2 ddyW = ddy(i.worldPos.xz);

#if defined(_MENISCUS_ON)
    // grabUV の画面微分もループ前に取得（分岐後の ddx/ddy 破綻を避ける）
    float2 grabUV0 = i.grabPos.xy / i.grabPos.w;
    float2 dUx = ddx(grabUV0);
    float2 dUy = ddy(grabUV0);
#endif

    // (1) 背景：全体に薄く散らす
#if defined(_MENISCUS_ON)
    scatterPass(i.worldPos.xz, _Density, _Coverage * mask, 1.0, layers, 0.0, ddxW, ddyW, accum, men, true);
#else
    scatterPass(i.worldPos.xz, _Density, _Coverage * mask, 1.0, layers, 0.0, ddxW, ddyW, accum);
#endif

    // (2) まとわりつき：際にだけ、細かい密集レイヤーを上乗せ
    //     毎ピクセルfoam × セルごとランダムしきい値 = 自然な散り際（切れるが集合として美しい）
#if defined(_FOAM_ON)
    if (foam > 0.003)
    {
    #if defined(_MENISCUS_ON)
        // foam層へのメニスカスは _MeniscusFoam トグル（uniform分岐、DX11では実質フリー）
        scatterPass(i.worldPos.xz, _Density * _FoamDensityMul,
                    _CoverageNear * foam * mask, _FoamSizeScale, 1, 53.7, ddxW, ddyW, accum, men, (_MeniscusFoam > 0.5));
    #else
        scatterPass(i.worldPos.xz, _Density * _FoamDensityMul,
                    _CoverageNear * foam * mask, _FoamSizeScale, 1, 53.7, ddxW, ddyW, accum);
    #endif
    }
#endif

#if defined(_MENISCUS_ON)
    // メニスカス屈折：集積方向をワールドXZ→grabUV にヤコビアン変換して Grab を歪ませる
    float2 offsetW = men.xy / max(men.z, 1e-5) * saturate(men.z) * _MeniscusStrength;

    // offsetW = sx*ddxW + sy*ddyW を解き、uvOff = sx*dUx + sy*dUy
    float det = ddxW.x * ddyW.y - ddyW.x * ddxW.y;
    float idet = 1.0 / (det >= 0.0 ? max(det, 1e-8) : min(det, -1e-8));
    float sx = ( offsetW.x * ddyW.y - ddyW.x * offsetW.y) * idet;
    float sy = (-offsetW.x * ddxW.y + ddxW.x * offsetW.y) * idet;
    float2 uvOff = sx * dUx + sy * dUy;

    float3 grabCol = FP_SAMPLE_GRAB(grabUV0 + uvOff).rgb;
    float  ringA   = saturate(men.z * _MeniscusOpacity);

    // 透明ピクセルは破棄して合成(ROP)コストを省く（リング分も残す）
    clip(max(accum.a, ringA) - _AlphaClip);
#else
    // 透明ピクセルは破棄して合成(ROP)コストを省く（軽量化）
    clip(accum.a - _AlphaClip);
#endif

    // 軽いアンビエント（板を完全フラットにしない用、既定OFF）
    if (_AmbientStrength > 0.0)
    {
        float3 amb = ShadeSH9(float4(0,1,0,1));
        accum.rgb *= lerp(1.0.xxx, amb, _AmbientStrength);
    }

#if defined(_MENISCUS_ON)
    // リングを premultiplied over の後段として合成
    accum.rgb += grabCol * ringA * (1.0 - accum.a);
    accum.a   += ringA * (1.0 - accum.a);
#endif

    // premultiplied なので fog のターゲットは 0（遠景で消える）
    UNITY_APPLY_FOG_COLOR(i.fogCoord, accum, fixed4(0,0,0,0));
    return accum;
}

#endif // FLOATING_PETALS_CORE_INCLUDED
