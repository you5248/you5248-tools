// Reduced-cost Quest leaf scatter.
// The PC implementation checks 3x3 cells for up to three layers (27 leaf
// candidates). This version checks the current cell plus the three neighbours
// towards the closest cell corner (four candidates total). Existing material
// controls and deterministic world-space placement remain compatible.
#ifndef YOU5248_LEAFCOVER_QUEST_COMMON_INCLUDED
#define YOU5248_LEAFCOVER_QUEST_COMMON_INCLUDED

sampler2D _LeafTex;
float4 _LeafColor, _LeafColorB;
float _TintVariation, _LeafOpacity, _LeafSmoothness;
float _Density, _Coverage, _SizeMin, _SizeMax, _Jitter, _RandomFlip, _Seed, _Layers;
float _CoverPosition, _SharpMask, _SlopeFix;

float2 LeafQuestHash22(float2 p)
{
    p += _Seed * 19.19;
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float2 LeafQuestRotate(float2 v, float2 cs)
{
    return float2(v.x * cs.x - v.y * cs.y,
                  v.x * cs.y + v.y * cs.x);
}

float2 LeafQuestSlopeWarp(float2 v, float2 slopeDir, float amount)
{
    float d = dot(v, slopeDir);
    return v + slopeDir * (d * (amount - 1.0));
}

float LeafQuestCoverMask(float normalY)
{
    float cover = saturate(normalY + _CoverPosition);
    if (_SharpMask > 0.5)
        cover = saturate((cover - 0.5) * 10.0 + 0.5);
    return cover;
}

void LeafQuestAccumulate(
    float2 cellId,
    float2 field,
    float2 ddxWorld,
    float2 ddyWorld,
    float2 slopeDir,
    float slopeAmount,
    float effectiveCoverage,
    float surfaceMask,
    inout float4 accum)
{
    float2 r1 = LeafQuestHash22(cellId);
    if (r1.x > effectiveCoverage)
        return;

    float2 r2 = LeafQuestHash22(cellId + 17.17);
    float2 r3 = LeafQuestHash22(cellId + 41.41);
    float2 center = cellId + 0.5 + (r1 - 0.5) * _Jitter;

    float size = lerp(_SizeMin, _SizeMax, r2.y);
    float sizeInCells = max(size * _Density, 1e-4);

    // A normalized hash vector replaces four per-pixel sin/cos evaluations.
    float2 rotation = normalize(r2 * 2.0 - 1.0 + float2(1e-4, 0));
    float2 local = LeafQuestSlopeWarp(field - center, slopeDir, slopeAmount);
    local = LeafQuestRotate(local, rotation) / sizeInCells + 0.5;

    if (_RandomFlip > 0.5 && r3.y > 0.5)
        local.x = 1.0 - local.x;

    float inside = step(0.0, local.x) * step(local.x, 1.0)
                 * step(0.0, local.y) * step(local.y, 1.0);
    if (inside < 0.5)
        return;

    float2 gradX = LeafQuestSlopeWarp(ddxWorld * _Density, slopeDir, slopeAmount);
    float2 gradY = LeafQuestSlopeWarp(ddyWorld * _Density, slopeDir, slopeAmount);
    gradX = LeafQuestRotate(gradX, rotation) / sizeInCells;
    gradY = LeafQuestRotate(gradY, rotation) / sizeInCells;

    float4 leafTex = tex2Dgrad(_LeafTex, local, gradX, gradY);
    float3 tint = lerp(_LeafColor.rgb, _LeafColorB.rgb,
                       r3.x * _TintVariation);
    float alpha = leafTex.a * _LeafOpacity * surfaceMask;
    float3 premultiplied = leafTex.rgb * tint * alpha;

    accum.rgb += premultiplied * (1.0 - accum.a);
    accum.a += alpha * (1.0 - accum.a);
}

float4 LeafCoverQuestEvaluate(float3 worldPos, float3 worldNormalGeo)
{
    float surfaceMask = LeafQuestCoverMask(worldNormalGeo.y);
    if (surfaceMask <= 0.003)
        return 0;

    float density = max(_Density, 1e-3);
    float2 field = worldPos.xz * density;
    float2 baseCell = floor(field);
    float2 cellFraction = frac(field);
    float2 neighbour = step(0.5, cellFraction) * 2.0 - 1.0;

    float2 ddxWorld = ddx(worldPos.xz);
    float2 ddyWorld = ddy(worldPos.xz);

    float2 horizontalNormal = worldNormalGeo.xz;
    float horizontalLength = length(horizontalNormal);
    float2 slopeDir = horizontalLength > 1e-4
        ? horizontalNormal / horizontalLength
        : float2(1, 0);
    float slopeAmount = lerp(
        1.0,
        1.0 / max(abs(worldNormalGeo.y), 0.25),
        _SlopeFix);

    // Approximate the visual density of the PC layer count without evaluating
    // more cells. At three layers this is 1.65x the single-layer spawn chance.
    float layerBoost = lerp(1.0, 1.65, saturate((_Layers - 1.0) * 0.5));
    float effectiveCoverage = saturate(_Coverage * layerBoost);

    float4 result = 0;
    LeafQuestAccumulate(baseCell, field, ddxWorld, ddyWorld,
        slopeDir, slopeAmount, effectiveCoverage, surfaceMask, result);
    LeafQuestAccumulate(baseCell + float2(neighbour.x, 0), field,
        ddxWorld, ddyWorld, slopeDir, slopeAmount,
        effectiveCoverage, surfaceMask, result);
    LeafQuestAccumulate(baseCell + float2(0, neighbour.y), field,
        ddxWorld, ddyWorld, slopeDir, slopeAmount,
        effectiveCoverage, surfaceMask, result);
    LeafQuestAccumulate(baseCell + neighbour, field,
        ddxWorld, ddyWorld, slopeDir, slopeAmount,
        effectiveCoverage, surfaceMask, result);
    return result;
}

#endif
