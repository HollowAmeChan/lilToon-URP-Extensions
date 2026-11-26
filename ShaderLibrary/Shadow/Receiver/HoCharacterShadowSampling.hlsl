#ifndef HO_CHARACTER_SHADOW_SAMPLING_INCLUDED
#define HO_CHARACTER_SHADOW_SAMPLING_INCLUDED
#include "HoRendererIdentity.hlsl"
#include "HoRTShadowSampling.hlsl"
#define HO_CS_MAX_SLICES 16
// Mirrored by HoCharacterShadowShaderContract and checked by Validate().
#define HO_CS_MAX_PCSS_BLOCKER_SAMPLES 32
#define HO_CS_MAX_PCSS_FILTER_SAMPLES 64
TEXTURE2D_FLOAT(_HoCSAtlas);
float _HoCSActive;
int _HoCSCount;
float4 _HoCSAtlasSize;
float4 _HoCSGroupSlices[64];
float4 _HoCSPartMasks[HO_CS_MAX_SLICES * 64];
float4x4 _HoCSWorldToShadow[HO_CS_MAX_SLICES];
float4x4 _HoCSWorldToBounds[HO_CS_MAX_SLICES];
float4 _HoCSTileRects[HO_CS_MAX_SLICES];
float4 _HoCSParameters[HO_CS_MAX_SLICES];
// x = orthographic far - near, in world units. No distance to a fictitious light plane.
float4 _HoCSDepthParameters[HO_CS_MAX_SLICES];
// (enabled, tan(angular diameter / 2), blocker radius metres, max penumbra metres)
float4 _HoCSPcssParams;
// (blocker depth bias, blocker samples, filter samples, additional soft radius metres)
float4 _HoCSPcssParams2;
float4 _HoCSSampleOffsets[HO_CS_MAX_PCSS_FILTER_SAMPLES];
// x = filter budget, y = blocker budget minus its explicit first centre tap.
// Zero keeps the self-contained arithmetic fallback available to existing diagnostic shaders.
float4 _HoCSSampleOffsetCounts;

float HoCSSampleRawDepth(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_HoCSAtlas, sampler_PointClamp, uv, 0).r;
}

float4 HoCSCompareDepth(float4 rawDepth, float4 receiverDepth)
{
    #if UNITY_REVERSED_Z
        return step(rawDepth, receiverDepth);
    #else
        return step(receiverDepth, rawDepth);
    #endif
}

// Derivatives are transformed from world-space deltas BEFORE adding the shadow UV offset.
// Differencing normalized coordinates near 0.5 loses precision in grazing projections.
float2 HoCSReceiverPlaneGradient(float3 dx, float3 dy)
{
    float determinant = dx.x * dy.y - dx.y * dy.x;
    float scale = max(length(dx.xy) * length(dy.xy), 1e-20);
    if (abs(determinant) <= scale * 1e-3) return 0;
    return float2(dy.y * dx.z - dx.y * dy.z, dx.x * dy.z - dy.x * dx.z) / determinant;
}

float HoCSReceiverDepth(float2 tapUv, float2 receiverUv, float receiverDepth, float3 gradient)
{
    return receiverDepth + dot(tapUv - receiverUv, gradient.xy);
}

// Gather both depths and their actual texel-centre receiver references. The weights are
// reused by filtering AND blocker search; never interpolate depths before comparison.
void HoCSReadDepthQuad(float2 tapUv, float2 lo, float2 hi,
    float2 receiverUv, float receiverDepth, float3 gradient,
    out float4 depths, out float4 references, out float4 weights)
{
    tapUv = clamp(tapUv, lo, hi);
    float2 p = tapUv * _HoCSAtlasSize.zw - 0.5;
    float2 f = frac(p);
    float2 b = (floor(p) + 0.5) * _HoCSAtlasSize.xy;
    weights = float4((1 - f.x) * f.y, f.x * f.y, f.x * (1 - f.y), (1 - f.x) * (1 - f.y));
    UNITY_BRANCH if (all(b >= lo) && all(b + _HoCSAtlasSize.xy <= hi))
    {
        #if defined(PLATFORM_SUPPORT_GATHER) && SHADER_TARGET >= 45
        // Gather order: upper-left, upper-right, lower-right, lower-left.
        // Gather at the footprint's centre, not at a fractional boundary: texture-unit
        // coordinate rounding must not select a different quad than floor(p) above.
        depths = GATHER_TEXTURE2D(_HoCSAtlas, sampler_PointClamp, b + 0.5 * _HoCSAtlasSize.xy);
        #else
        // Most receiving pixels are away from a tile edge. The SM3.5 path also
        // shares one receiver-plane reference and avoids four redundant clamps/dots.
        depths = float4(HoCSSampleRawDepth(b + float2(0, _HoCSAtlasSize.y)),
            HoCSSampleRawDepth(b + _HoCSAtlasSize.xy),
            HoCSSampleRawDepth(b + float2(_HoCSAtlasSize.x, 0)), HoCSSampleRawDepth(b));
        #endif
        float reference = HoCSReceiverDepth(b, receiverUv, receiverDepth, gradient);
        float2 dz = gradient.xy * _HoCSAtlasSize.xy;
        references = reference + float4(dz.y, dz.x + dz.y, dz.x, 0);
    }
    else
    {
        // Also isolates adjacent atlas tiles; the sampler only clamps to the whole atlas.
        float2 a = clamp(b, lo, hi);
        float2 c = clamp(b + float2(_HoCSAtlasSize.x, 0), lo, hi);
        float2 d = clamp(b + float2(0, _HoCSAtlasSize.y), lo, hi);
        float2 e = clamp(b + _HoCSAtlasSize.xy, lo, hi);
        depths = float4(HoCSSampleRawDepth(d), HoCSSampleRawDepth(e), HoCSSampleRawDepth(c), HoCSSampleRawDepth(a));
        references = float4(HoCSReceiverDepth(d, receiverUv, receiverDepth, gradient),
            HoCSReceiverDepth(e, receiverUv, receiverDepth, gradient),
            HoCSReceiverDepth(c, receiverUv, receiverDepth, gradient),
            HoCSReceiverDepth(a, receiverUv, receiverDepth, gradient));
    }
}

// gradient.xy = depth slope in atlas UV, .z = a world-scaled self-plane residual tolerance.
// Return (guarded receiver-plane visibility, unbiased visibility) for the SAME depth taps.
float2 HoCSCompareBilinear(float2 tapUv, float2 lo, float2 hi,
    float2 receiverUv, float receiverDepth, float3 gradient)
{
    float4 depths = 0, references = 0, weights = 0;
    HoCSReadDepthQuad(tapUv, lo, hi, receiverUv, receiverDepth, gradient, depths, references, weights);
    float4 visibility = HoCSCompareDepth(depths, references);
    float4 rawVisibility = HoCSCompareDepth(depths, receiverDepth.xxxx);
    UNITY_BRANCH if (all(visibility == rawVisibility))
    {
        // When plane correction cannot change any comparison, its trust blend
        // and second dot product have no effect, including along a mixed quad.
        float value = dot(rawVisibility, weights);
        return value.xx;
    }
    // A tangent triangle's plane can extrapolate through an unrelated occluder. Only use
    // its acne correction when the stored surface actually agrees with that local plane.
    float4 trust = 1.0 - smoothstep(gradient.z, gradient.z * 2.0, abs(depths - references));
    visibility = lerp(rawVisibility, visibility, trust);
    return float2(dot(visibility, weights), dot(rawVisibility, weights));
}

float HoCSTentIntegral(float position, float support)
{
    float x = clamp(position / support, -1.0, 1.0);
    return 0.5 + x * (1.0 - 0.5 * abs(x));
}

// Integrate a tent over texel CELLS, then pack adjacent cell weights into bilinear taps.
// Radius 0 retains base AA; all positive artistic radii continuously increase its variance.
// Up to 2.5 texels needs at most 8 cells per axis / 16 quads, independent of quality tier.
float2 HoCSSampleDenseTent(float2 uv, float2 lo, float2 hi, float receiverDepth,
    float3 gradient, float radiusTexels)
{
    float support = sqrt(2.25 + 1.5 * radiusTexels * radiusTexels);
    float2 position = uv * _HoCSAtlasSize.zw;
    float2 first = floor(position - support);
    float4 weightsX = 0, weightsY = 0, offsetsX = 0, offsetsY = 0;
    [unroll] for (int pair = 0; pair < 4; pair++)
    {
        float2 a = first + pair * 2 - position;
        float2 w0 = float2(HoCSTentIntegral(a.x + 1, support) - HoCSTentIntegral(a.x, support),
                          HoCSTentIntegral(a.y + 1, support) - HoCSTentIntegral(a.y, support));
        float2 w1 = float2(HoCSTentIntegral(a.x + 2, support) - HoCSTentIntegral(a.x + 1, support),
                          HoCSTentIntegral(a.y + 2, support) - HoCSTentIntegral(a.y + 1, support));
        weightsX[pair] = w0.x + w1.x;
        weightsY[pair] = w0.y + w1.y;
        float2 centre = first + pair * 2 + 0.5 + w1 / max(w0 + w1, 1e-8);
        offsetsX[pair] = centre.x * _HoCSAtlasSize.x;
        offsetsY[pair] = centre.y * _HoCSAtlasSize.y;
    }
    float2 visibility = 0;
    // Keep the branch-heavy depth comparison rolled: duplicating it for all
    // 16 taps makes FXC optimization costly. Tap order and weights are unchanged.
    [loop] for (int y = 0; y < 4; y++)
    {
        UNITY_BRANCH if (weightsY[y] > 0)
        {
            [loop] for (int x = 0; x < 4; x++)
            {
                UNITY_BRANCH if (weightsX[x] > 0)
                    visibility += weightsX[x] * weightsY[y] * HoCSCompareBilinear(
                        float2(offsetsX[x], offsetsY[y]), lo, hi, uv, receiverDepth, gradient);
            }
        }
    }
    return visibility;
}

// Fixed, symmetric samples: no world-cell hash, temporal dependency or per-pixel noise.
float2 HoCSCalculateSampleOffset(int index, int sampleCount)
{
    // Odd search budgets must still have zero centroid: a centre sample plus antipodal pairs.
    if ((sampleCount & 1) != 0)
    {
        if (index == 0) return 0;
        index--;
        sampleCount--;
    }
    float pair = floor((float)index * 0.5);
    // Some Player variants fold the inactive centre-sample branch with index
    // -1 before control-flow pruning. Valid paired indices already have a
    // nonnegative numerator; keep the folded expression defined as well.
    float radius = sqrt(max(pair + 0.5, 0.0) / max(ceil((float)sampleCount * 0.5), 1.0));
    float angle = pair * 2.39996323;
    float direction = (index & 1) == 0 ? 1.0 : -1.0;
    return float2(cos(angle), sin(angle)) * (radius * direction);
}

float2 HoCSSampleOffset(int index, int sampleCount)
{
    UNITY_BRANCH if (sampleCount == (int)_HoCSSampleOffsetCounts.x && sampleCount > 0)
        return _HoCSSampleOffsets[index].xy;
    UNITY_BRANCH if (sampleCount == (int)_HoCSSampleOffsetCounts.y && sampleCount > 0)
        return _HoCSSampleOffsets[index].zw;
    return HoCSCalculateSampleOffset(index, sampleCount);
}

float HoCSRadiusTexels(float worldRadius, float texelWorld)
{
    // Quality controls sample count, never silently changes the requested world-space radius.
    return max(worldRadius, 0.0) / max(texelWorld, 1e-6);
}

float HoCSResolveFilter(float2 visibility)
{
    // Corrections belong to an edge, not to a fully lit/shadowed kernel. Fade their allowed
    // influence over the outer eighth of the unbiased transition instead of a binary gate.
    // Keep the middle range wide enough for a subtexel-shifted, unoccluded grazing plane.
    return clamp(visibility.x, saturate(visibility.y * 8.0 - 7.0), saturate(visibility.y * 8.0));
}
float HoCSSampleDisk(float2 uv, float2 lo, float2 hi, float receiverDepth,
    float radiusTexels, int sampleCount, float3 gradient)
{
    float2 visibility = 1;
    UNITY_BRANCH if (radiusTexels <= 2.0 || sampleCount <= 0)
    {
        visibility = HoCSSampleDenseTent(uv, lo, hi, receiverDepth, gradient, min(radiusTexels, 2.0));
    }
    else
    {
        visibility = 0;
        // Preserve the base-AA variance as artistic softness grows instead of replacing its
        // kernel with a narrower disk. The dense/sparse transition uses matching second moments.
        float diskRadius = sqrt(radiusTexels * radiusTexels + 1.5);
        int firstPair = sampleCount & 1;
        if (firstPair != 0)
            visibility = HoCSCompareBilinear(uv, lo, hi, uv, receiverDepth, gradient);
        // Adjacent pattern entries are exact antipodes. Fetch/scale their shared
        // offset once and keep the original accumulation order, with half the loop work.
        [loop] for (int i = firstPair; i < HO_CS_MAX_PCSS_FILTER_SAMPLES; i += 2)
        {
            if (i >= sampleCount) break;
            float2 offset = HoCSSampleOffset(i, sampleCount) * _HoCSAtlasSize.xy * diskRadius;
            visibility += HoCSCompareBilinear(uv + offset, lo, hi, uv, receiverDepth, gradient);
            visibility += HoCSCompareBilinear(uv - offset, lo, hi, uv, receiverDepth, gradient);
        }
        visibility /= (float)sampleCount;
        // Avoid a shape discontinuity when PCSS transitions out of the small-kernel path.
        UNITY_BRANCH if (radiusTexels < 2.5)
            visibility = lerp(HoCSSampleDenseTent(uv, lo, hi, receiverDepth, gradient, radiusTexels), visibility, smoothstep(2.0, 2.5, radiusTexels));
    }
    return HoCSResolveFilter(visibility);
}

float HoCSDirectionalPenumbra(float gapDepth, float depthRange)
{
    return min(max(_HoCSPcssParams.w, 0.0), gapDepth * depthRange * max(_HoCSPcssParams.y, 0.0));
}

float2 HoCSBlockerStatistics(float2 tapUv, float2 lo, float2 hi,
    float2 receiverUv, float receiverDepth, float3 gradient)
{
    float4 depths = 0, references = 0, weights = 0;
    HoCSReadDepthQuad(tapUv, lo, hi, receiverUv, receiverDepth, gradient, depths, references, weights);
    float4 trust = 1.0 - smoothstep(gradient.z, gradient.z * 2.0, abs(depths - references));
    references = lerp(receiverDepth.xxxx, references, trust);
    #if UNITY_REVERSED_Z
        float4 gaps = depths - references;
        float4 valid = step(0.00001, depths);
    #else
        float4 gaps = references - depths;
        float4 valid = 1.0 - step(0.99999, depths);
    #endif
    float4 blockerWeights = weights * valid * (1.0 - step(gaps, _HoCSPcssParams2.x));
    return float2(dot(max(gaps, 0), blockerWeights), dot(blockerWeights, 1.0));
}

float2 HoCSSearchBlockers(float2 uv, float2 lo, float2 hi, float receiverDepth,
    float3 gradient, float blockerRadius, int sampleCount)
{
    if (sampleCount <= 0) return 0;
    float2 statistics = HoCSBlockerStatistics(uv, lo, hi, uv, receiverDepth, gradient);
    int remaining = sampleCount - 1;
    int firstPair = remaining & 1;
    if (firstPair != 0)
        statistics += HoCSBlockerStatistics(uv, lo, hi, uv, receiverDepth, gradient);
    [loop] for (int i = firstPair; i < HO_CS_MAX_PCSS_BLOCKER_SAMPLES - 1; i += 2)
    {
        if (i >= remaining) break;
        float2 offset = HoCSSampleOffset(i, remaining) * blockerRadius * _HoCSAtlasSize.xy;
        statistics += HoCSBlockerStatistics(uv + offset, lo, hi, uv, receiverDepth, gradient);
        statistics += HoCSBlockerStatistics(uv - offset, lo, hi, uv, receiverDepth, gradient);
    }
    return statistics;
}

float HoCSSampleAtlasPcss(float2 uv, float2 lo, float2 hi, float receiverDepth,
    float texelWorld, float depthRange, float3 gradient, float footprintTexels)
{
    int filterSamples = clamp((int)round(_HoCSPcssParams2.z), 1, HO_CS_MAX_PCSS_FILTER_SAMPLES);
    float minRadius = max(HoCSRadiusTexels(_HoCSPcssParams2.w, texelWorld), footprintTexels);
    float radius = minRadius;
    UNITY_BRANCH if (_HoCSPcssParams.x >= 0.5 && _HoCSPcssParams.y > 0)
    {
        int blockerSamples = clamp((int)round(_HoCSPcssParams2.y), 1, HO_CS_MAX_PCSS_BLOCKER_SAMPLES);
        float blockerRadius = HoCSRadiusTexels(_HoCSPcssParams.z, texelWorld);
        float2 blockers = HoCSSearchBlockers(uv, lo, hi, receiverDepth, gradient, blockerRadius, blockerSamples);
        if (blockers.y > 0)
        {
            float penumbraWorld = HoCSDirectionalPenumbra(blockers.x / blockers.y, depthRange);
            // A subtexel sliver entering the search must not jump straight to a full penumbra.
            // One complete tap of support restores the unmodified distance-based estimate.
            radius = max(radius, HoCSRadiusTexels(penumbraWorld, texelWorld) * smoothstep(0, 1, blockers.y));
        }
    }
    return HoCSSampleDisk(uv, lo, hi, receiverDepth, radius, filterSamples, gradient);
}

float HoCSResolveMainCast(float3 positionWS, float sceneCast)
{
    float rtCast;
    if (HoRTShadowTryMainCast(positionWS, HoRendererIdentity(), rtCast)) return rtCast;
    if (_HoCSActive < 0.5) return sceneCast;
    uint identity = HoRendererIdentity();
    uint group = HoRendererIdentityGroup(identity);
    if (group == 0u) return sceneCast;
    int slice = (int)_HoCSGroupSlices[group / 4u][group % 4u] - 1;
    if (slice < 0 || slice >= min(_HoCSCount, HO_CS_MAX_SLICES)) return sceneCast;
    uint slot = HoRendererIdentitySlot(identity);
    if (_HoCSPartMasks[slice * 64 + slot / 4u][slot % 4u] < 0.5) return sceneCast;
    float4 parameters = _HoCSParameters[slice];
    if (parameters.z < 0.5) return sceneCast;
    float3 shadow = mul(_HoCSWorldToShadow[slice], float4(positionWS, 1)).xyz;
    float4 tile = _HoCSTileRects[slice];
    float2 uv = tile.xy + shadow.xy * tile.zw;
    float3 shadowDx = mul((float3x3)_HoCSWorldToShadow[slice], ddx(positionWS));
    float3 shadowDy = mul((float3x3)_HoCSWorldToShadow[slice], ddy(positionWS));
    float planeTolerance = max(4.0 * parameters.w / max(_HoCSDepthParameters[slice].x, 1e-6), 1e-7);
    float3 gradient = float3(HoCSReceiverPlaneGradient(
        float3(shadowDx.xy * tile.zw, shadowDx.z), float3(shadowDy.xy * tile.zw, shadowDy.z)), planeTolerance);
    float2 dx = shadowDx.xy * tile.zw * _HoCSAtlasSize.zw;
    float2 dy = shadowDy.xy * tile.zw * _HoCSAtlasSize.zw;
    float footprintTexels = 0.5 * max(length(dx), length(dy));
    float3 local = mul(_HoCSWorldToBounds[slice], float4(positionWS, 1)).xyz;
    float edge = 0.5 - max(abs(local.x), max(abs(local.y), abs(local.z)));
    if (edge <= 0 || any(shadow < 0) || any(shadow > 1)) return sceneCast;
    float2 lo = tile.xy + _HoCSAtlasSize.xy * 0.5;
    float2 hi = tile.xy + tile.zw - _HoCSAtlasSize.xy * 0.5;
    float visibility = HoCSSampleAtlasPcss(uv, lo, hi, shadow.z, parameters.w,
        _HoCSDepthParameters[slice].x, gradient, footprintTexels);
    float localCast = lerp(1.0, visibility, parameters.y);
    float blend = parameters.x > 0 ? smoothstep(0, parameters.x, edge) : 1;
    return lerp(sceneCast, localCast, blend);
}
#endif
