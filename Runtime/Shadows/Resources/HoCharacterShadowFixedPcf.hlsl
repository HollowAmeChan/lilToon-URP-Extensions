#ifndef HO_CHARACTER_SHADOW_FIXED_PCF_INCLUDED
#define HO_CHARACTER_SHADOW_FIXED_PCF_INCLUDED

// Producer-side subset of the existing Character Shadow atlas ABI.
#define HO_CS_FIXED_MAX_SLICES 16
TEXTURE2D_FLOAT(_HoCSAtlas);
float _HoCSActive;
int _HoCSCount;
float4 _HoCSAtlasSize;
float4 _HoCSGroupSlices[64];
float4 _HoCSPartMasks[HO_CS_FIXED_MAX_SLICES * 64];
float4x4 _HoCSWorldToShadow[HO_CS_FIXED_MAX_SLICES];
float4x4 _HoCSWorldToBounds[HO_CS_FIXED_MAX_SLICES];
float4 _HoCSTileRects[HO_CS_FIXED_MAX_SLICES];
float4 _HoCSParameters[HO_CS_FIXED_MAX_SLICES];
float4 _HoCSDepthParameters[HO_CS_FIXED_MAX_SLICES];

struct HoCharacterShadowLocalSample
{
    float localVisibility;
    float blend;
    uint valid;
};

float2 HoCSFixedReceiverPlaneGradient(float3 dx, float3 dy)
{
    float determinant = dx.x * dy.y - dx.y * dy.x;
    float scale = max(length(dx.xy) * length(dy.xy), 1e-20);
    if (abs(determinant) <= scale * 1e-3)
        return 0.0;
    return float2(dy.y * dx.z - dx.y * dy.z, dx.x * dy.z - dy.x * dx.z) / determinant;
}

float HoCSFixedDepthCompare(float atlasDepth, float receiverDepth)
{
#if UNITY_REVERSED_Z
    return step(atlasDepth, receiverDepth);
#else
    return step(receiverDepth, atlasDepth);
#endif
}

float HoCSFixedCompareTap(float2 requestedUV, float2 lo, float2 hi, float2 receiverUV,
    float receiverDepth, float3 gradient)
{
    float2 clampedUV = clamp(requestedUV, lo, hi);
    float2 texel = floor(clampedUV * _HoCSAtlasSize.zw);
    float2 sampleUV = clamp((texel + 0.5) * _HoCSAtlasSize.xy, lo, hi);
    float atlasDepth = SAMPLE_TEXTURE2D_LOD(_HoCSAtlas, sampler_PointClamp, sampleUV, 0).r;
    float planeDepth = receiverDepth + dot(sampleUV - receiverUV, gradient.xy);
    // Preserve the old per-tap residual guard, so a distant blocker cannot
    // receive the same planar correction as a locally agreeing surface.
    float trust = 1.0 - smoothstep(gradient.z, gradient.z * 2.0, abs(atlasDepth - planeDepth));
    float raw = HoCSFixedDepthCompare(atlasDepth, receiverDepth);
    return lerp(raw, HoCSFixedDepthCompare(atlasDepth, planeDepth), trust);
}

float HoCSFixedFilter(float2 uv, float2 lo, float2 hi, float receiverDepth, float3 gradient,
    bool soft, float radiusTexels)
{
    float visibility = HoCSFixedCompareTap(uv, lo, hi, uv, receiverDepth, gradient);
    UNITY_BRANCH if (soft)
    {
        float2 stride = _HoCSAtlasSize.xy * clamp(radiusTexels, 1.0, 2.0);
        float radiusX = stride.x;
        float radiusY = stride.y;
        float cardinals =
            HoCSFixedCompareTap(uv + float2(-radiusX, 0.0), lo, hi, uv, receiverDepth, gradient) +
            HoCSFixedCompareTap(uv + float2( radiusX, 0.0), lo, hi, uv, receiverDepth, gradient) +
            HoCSFixedCompareTap(uv + float2(0.0, -radiusY), lo, hi, uv, receiverDepth, gradient) +
            HoCSFixedCompareTap(uv + float2(0.0,  radiusY), lo, hi, uv, receiverDepth, gradient);
        float corners =
            HoCSFixedCompareTap(uv + float2(-radiusX, -radiusY), lo, hi, uv, receiverDepth, gradient) +
            HoCSFixedCompareTap(uv + float2(-radiusX,  radiusY), lo, hi, uv, receiverDepth, gradient) +
            HoCSFixedCompareTap(uv + float2( radiusX, -radiusY), lo, hi, uv, receiverDepth, gradient) +
            HoCSFixedCompareTap(uv + float2( radiusX,  radiusY), lo, hi, uv, receiverDepth, gradient);
        visibility = (4.0 * visibility + 2.0 * cardinals + corners) * (1.0 / 16.0);
    }
    return visibility;
}

HoCharacterShadowLocalSample HoCSFixedEvaluate(float3 positionWS, float3 positionDx, float3 positionDy,
    uint owner, bool soft, float radiusTexels)
{
    HoCharacterShadowLocalSample result = (HoCharacterShadowLocalSample)0;
    result.localVisibility = 1.0;
    uint group = (owner >> 8u) & 255u;
    if (_HoCSActive < 0.5 || group == 0u)
        return result;

    int slice = (int)_HoCSGroupSlices[group / 4u][group % 4u] - 1;
    if (slice < 0 || slice >= min(_HoCSCount, HO_CS_FIXED_MAX_SLICES))
        return result;

    uint slot = owner & 255u;
    float4 parameters = _HoCSParameters[slice];
    if (_HoCSPartMasks[slice * 64 + slot / 4u][slot % 4u] < 0.5 || parameters.z < 0.5)
        return result;

    float3 local = mul(_HoCSWorldToBounds[slice], float4(positionWS, 1.0)).xyz;
    float edge = 0.5 - max(abs(local.x), max(abs(local.y), abs(local.z)));
    float3 shadow = mul(_HoCSWorldToShadow[slice], float4(positionWS, 1.0)).xyz;
    if (edge <= 0.0 || any(shadow < 0.0) || any(shadow > 1.0))
        return result;

    float4 tile = _HoCSTileRects[slice];
    float2 lo = tile.xy + _HoCSAtlasSize.xy * 0.5;
    float2 hi = tile.xy + tile.zw - _HoCSAtlasSize.xy * 0.5;
    if (any(_HoCSAtlasSize.xy <= 0.0) || any(lo > hi))
        return result;

    float3 dx = mul((float3x3)_HoCSWorldToShadow[slice], positionDx);
    float3 dy = mul((float3x3)_HoCSWorldToShadow[slice], positionDy);
    float planeTolerance = max(4.0 * parameters.w / max(_HoCSDepthParameters[slice].x, 1e-6), 1e-7);
    float3 gradient = float3(HoCSFixedReceiverPlaneGradient(float3(dx.xy * tile.zw, dx.z),
        float3(dy.xy * tile.zw, dy.z)), planeTolerance);
    float2 uv = tile.xy + shadow.xy * tile.zw;
    float visibility = HoCSFixedFilter(uv, lo, hi, shadow.z, gradient, soft, radiusTexels);
    result.localVisibility = lerp(1.0, visibility, saturate(parameters.y));
    result.blend = parameters.x > 0.0 ? smoothstep(0.0, parameters.x, edge) : 1.0;
    result.valid = 1u;
    return result;
}

#endif
