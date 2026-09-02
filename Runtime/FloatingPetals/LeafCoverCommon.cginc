// you5248 / LeafCoverCommon.cginc ―― 葉/花びら積もりの共通ロジック
//
// LeafCover Standard（単一テクスチャ物体用）と MeshArrayPBR LeafCover（結合メッシュ用）で共有。
// FloatingPetals のワールドXZセル散布スタンプの静止版 + SnowCover 由来の法線Y積もりマスク。
//
// 使い方: 呼び出し側シェーダーで本ファイルを include し、フラグメント段で
//   float4 leaf = LeafCover_Evaluate(worldPos, 頂点由来ワールド法線);
// を呼ぶ。戻り値は premultiplied（rgb=葉色×α, a=葉α）。
// マスク/散布の法線はノーマルマップ適用「前」の頂点法線を渡すこと
// （高周波ノーマルだと葉中心へのマスク外挿の勾配が壊れる）。

#ifndef LEAFCOVER_COMMON_INCLUDED
#define LEAFCOVER_COMMON_INCLUDED

sampler2D _LeafTex;
float4 _LeafColor, _LeafColorB;
float  _TintVariation, _LeafOpacity, _LeafSmoothness;
float  _Density, _Coverage, _SizeMin, _SizeMax, _Jitter, _RandomFlip, _Seed, _Layers;
float  _CoverPosition, _SharpMask, _SlopeFix;

// Dave Hoskins hash22 ―― FloatingPetals と同一（sin非依存で実機安定）
float2 LeafHash22(float2 p)
{
    p += _Seed * 19.19;
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float2 LeafRot2(float2 v, float a)
{
    float s, c; sincos(a, s, c);
    return float2(v.x * c - v.y * s, v.x * s + v.y * c);
}

// 斜面の伸び補正: XZ真上投影は傾きθの面で坂方向に1/cosθ倍伸びる。
// 坂の水平方向(hdir)成分だけ座標の変化を速めて(=fix倍)、面上で見て丸に戻す。
float2 LeafSlopeWarp(float2 v, float2 hdir, float fix)
{
    float d = dot(v, hdir);
    return v + hdir * (d * (fix - 1.0));
}

// SnowCover 由来の積もりマスク（法線Y → 0..1）
float LeafCoverMask(float ny)
{
    float c = saturate(ny + _CoverPosition);
    if (_SharpMask > 0.5) c = saturate((c - 0.5) * 10.0 + 0.5);
    return c;
}

// 1セルぶんの葉を評価して premultiplied で accum に合成
void LeafAccum(float2 cellId, float2 field, float cellDensity,
               float layerPhase,
               float2 ddxW, float2 ddyW, float2 slopeDir, float slopeFixAmt,
               float pixelNy, float2 nyGrad,
               inout float4 accum)
{
    float2 r1 = LeafHash22(cellId);
    float2 r2 = LeafHash22(cellId + 17.17);
    float2 r3 = LeafHash22(cellId + 41.41);

    float2 center = cellId + 0.5 + (r1 - 0.5) * _Jitter;

    // 湧き判定は葉1枚につき1回＝「葉の中心」でのマスク値で行う（ピクセルごとにやると
    // 1枚の途中で閾値をまたいで、ちぎれた葉になる）。中心の法線Yは持っていないので、
    // 法線YのワールドXZ勾配で中心位置へ外挿する（滑らかな曲面ではほぼ正確）。
    float2 toCenterXZ = (center - field) / cellDensity;
    float  centerNy   = pixelNy + dot(nyGrad, toCenterXZ);
    if (r1.x > _Coverage * LeafCoverMask(centerNy)) return;

    // 局所座標 → 回転 → テクスチャUV（回転は完全ランダム・静止。layerPhase で層ごとにずらす）
    float size = lerp(_SizeMin, _SizeMax, r2.y);
    float fullSizeCells = max(size * cellDensity, 1e-4);
    float angle = r2.x * 6.2831853 + layerPhase;

    float2 p   = LeafSlopeWarp(field - center, slopeDir, slopeFixAmt);
    float2 loc = LeafRot2(p, angle) / fullSizeCells + 0.5;

    if (_RandomFlip > 0.5 && r3.y > 0.5) loc.x = 1.0 - loc.x;

    float inB = step(0.0, loc.x) * step(loc.x, 1.0)
              * step(0.0, loc.y) * step(loc.y, 1.0);
    if (inB < 0.5) return;

    // 勾配を明示してサンプル（early-return でミップ段が暴れて四角ブロック化するのを防ぐ）
    float2 dLocX = LeafRot2(LeafSlopeWarp(ddxW * cellDensity, slopeDir, slopeFixAmt), angle) / fullSizeCells;
    float2 dLocY = LeafRot2(LeafSlopeWarp(ddyW * cellDensity, slopeDir, slopeFixAmt), angle) / fullSizeCells;
    float4 tex = tex2Dgrad(_LeafTex, loc, dLocX, dLocY);
    float3 tcol = lerp(_LeafColor.rgb, _LeafColorB.rgb, r3.x * _TintVariation) * tex.rgb;
    float  a    = tex.a * _LeafOpacity;
    if (a <= 0.0) return;

    float3 pc = tcol * a;
    accum.rgb += pc * (1.0 - accum.a);
    accum.a   += a  * (1.0 - accum.a);
}

// メイン評価: 3x3近傍 × layers枚を合成した premultiplied 色を返す
// worldNormalGeo にはノーマルマップ適用前の（頂点由来）ワールド法線を渡す
float4 LeafCover_Evaluate(float3 worldPos, float3 worldNormalGeo)
{
    float ny = worldNormalGeo.y;

    // 画面微分は分岐前に取る（FloatingPetals の四角ブロック対策と同じ）
    float2 ddxW = ddx(worldPos.xz);
    float2 ddyW = ddy(worldPos.xz);

    // 法線YのワールドXZ勾配を画面微分から復元（葉中心へのマスク外挿用）:
    // dot(g, ddxW)=ddx(ny), dot(g, ddyW)=ddy(ny) の2x2を解く
    float dnyx = ddx(ny), dnyy = ddy(ny);
    float det = ddxW.x * ddyW.y - ddxW.y * ddyW.x;
    float2 nyGrad = abs(det) > 1e-8
        ? float2(ddyW.y * dnyx - ddxW.y * dnyy, ddxW.x * dnyy - ddyW.x * dnyx) / det
        : float2(0, 0);

    // 斜面補正のパラメータ: 坂の水平方向と圧縮率 1/ny（急斜面は4倍まで）
    float2 nh  = worldNormalGeo.xz;
    float  nhl = length(nh);
    float2 slopeDir = nhl > 1e-4 ? nh / nhl : float2(1, 0);
    float  slopeFixAmt = lerp(1.0, 1.0 / max(ny, 0.25), _SlopeFix);

    // 早期スキップ: 近傍セルの葉中心(最遠~2.8セル)まで外挿しても届かない面だけ捨てる。
    // ピクセル自身のマスクで切ると、マスク外にはみ出した葉の端がちぎれるので広めに取る
    float reachNy = ny + length(nyGrad) * (2.8 / max(_Density, 1e-3));
    float4 accum = 0;
    if (LeafCoverMask(reachNy) > 0.003)
    {
        int layers = (int)round(_Layers);
        [loop]
        for (int L = 0; L < layers; L++)
        {
            float2 lOff  = float2(L * 0.37, L * 0.61);
            float2 field = worldPos.xz * _Density + lOff * 13.0;
            float2 baseCell = floor(field);
            float  lPhase = L * 2.39996;

            [unroll]
            for (int oy = -1; oy <= 1; oy++)
            [unroll]
            for (int ox = -1; ox <= 1; ox++)
            {
                LeafAccum(baseCell + float2(ox, oy), field, _Density,
                          lPhase, ddxW, ddyW, slopeDir, slopeFixAmt,
                          ny, nyGrad, accum);
            }
        }
    }
    return accum;
}

#endif // LEAFCOVER_COMMON_INCLUDED
