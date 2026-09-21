#ifndef HO_CHARACTER_SHADOW_SAMPLING_INCLUDED
#define HO_CHARACTER_SHADOW_SAMPLING_INCLUDED
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoRendererIdentity.hlsl"
#define HO_CS_MAX_SLICES 16
// PCSS 采样上限。C# 侧 HoCharacterShadowShaderContract 必须与这两个值一致
// （HoCharacterShadowValidation.Validate() 会解析本文件比对，防漂移）。
#define HO_CS_MAX_PCSS_BLOCKER_SAMPLES 16
#define HO_CS_MAX_PCSS_FILTER_SAMPLES 32
TEXTURE2D_FLOAT(_HoCSAtlas);
float _HoCSActive;
int _HoCSCount;
float _HoCSFilterRadius;
float4 _HoCSAtlasSize;
float4 _HoCSGroupSlices[64];
float4 _HoCSPartMasks[HO_CS_MAX_SLICES * 64];
float4x4 _HoCSWorldToShadow[HO_CS_MAX_SLICES];
float4x4 _HoCSWorldToBounds[HO_CS_MAX_SLICES];
float4 _HoCSTileRects[HO_CS_MAX_SLICES];
float4 _HoCSParameters[HO_CS_MAX_SLICES];
// (enabled, softness, blocker 搜索半径 texel, 半影半径上限 texel)
float4 _HoCSPcssParams;
// (blocker 深度偏移, blocker 采样数, filter 采样数, 0)
float4 _HoCSPcssParams2;

float HoCSCompare(float2 uv, float receiverDepth)
{
    float depth = SAMPLE_TEXTURE2D_LOD(_HoCSAtlas, sampler_PointClamp, uv, 0).r;
    #if UNITY_REVERSED_Z
        return receiverDepth >= depth ? 1.0 : 0.0;
    #else
        return receiverDepth <= depth ? 1.0 : 0.0;
    #endif
}

float HoCSSampleRawDepth(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_HoCSAtlas, sampler_PointClamp, uv, 0).r;
}

// 与 HoCSCompare 同一套深度约定（reversed-Z 下近 = 大）。far 值表示"这里没有遮挡物"。
bool HoCSIsBlocker(float rawDepth, float receiverDepth, float bias)
{
    #if UNITY_REVERSED_Z
        if (rawDepth <= 0.00001) return false;
        return rawDepth > receiverDepth + bias;
    #else
        if (rawDepth >= 0.99999) return false;
        return rawDepth < receiverDepth - bias;
    #endif
}

// 固定半径的 3×3 PCF：PCSS 关闭 / 半影估不出来时走这条（降级即回退，不是另一套 shader）。
float HoCSSampleManualPcf(float2 uv, float2 lo, float2 hi, float receiverDepth)
{
    float visibility = 0.0;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
        visibility += HoCSCompare(clamp(uv + float2(x, y) * _HoCSAtlasSize.xy * _HoCSFilterRadius, lo, hi), receiverDepth) / 9.0;
    return visibility;
}

// 按 atlas uv 出旋转角：同一像素每帧稳定，不同像素去相关，避免规律性条带。
float HoCSPcssRotation(float2 uv)
{
    float2 pixel = floor(uv / max(_HoCSAtlasSize.xy, 1e-6));
    return frac(sin(dot(pixel, float2(12.9898, 78.233))) * 43758.5453) * 6.2831853;
}

// 黄金角螺旋盘：把 N 个采样均匀铺在单位圆里，再整体旋转一个角度。
float2 HoCSPcssOffset(int index, int sampleCount, float rotation)
{
    float sampleIndex = (float)index + 0.5;
    float radius = sqrt(sampleIndex / max((float)sampleCount, 1.0));
    float angle = sampleIndex * 2.39996323 + rotation;
    return float2(cos(angle), sin(angle)) * radius;
}

// PCSS：先在一个盘内找遮挡物（blocker），用平均遮挡深度估半影宽度，再按该宽度做可变半径滤波。
float HoCSSampleAtlasPcss(float2 uv, float2 lo, float2 hi, float receiverDepth)
{
    if (_HoCSPcssParams.x < 0.5 || _HoCSPcssParams.y <= 0.0)
    {
        return HoCSSampleManualPcf(uv, lo, hi, receiverDepth);
    }

    int blockerSampleCount = min((int)round(_HoCSPcssParams2.y), HO_CS_MAX_PCSS_BLOCKER_SAMPLES);
    int filterSampleCount = min((int)round(_HoCSPcssParams2.z), HO_CS_MAX_PCSS_FILTER_SAMPLES);
    if (blockerSampleCount <= 0 || filterSampleCount <= 0)
    {
        return HoCSSampleManualPcf(uv, lo, hi, receiverDepth);
    }

    float2 texel = _HoCSAtlasSize.xy;
    float rotation = HoCSPcssRotation(uv);
    float blockerRadius = max(_HoCSPcssParams.z, 0.0);
    float blockerDepthSum = 0.0;
    int blockerCount = 0;
    [loop] for (int blockerIndex = 0; blockerIndex < HO_CS_MAX_PCSS_BLOCKER_SAMPLES; blockerIndex++)
    {
        if (blockerIndex >= blockerSampleCount)
        {
            break;
        }

        float2 sampleUv = clamp(uv + HoCSPcssOffset(blockerIndex, blockerSampleCount, rotation) * texel * blockerRadius, lo, hi);
        float rawDepth = HoCSSampleRawDepth(sampleUv);
        if (HoCSIsBlocker(rawDepth, receiverDepth, _HoCSPcssParams2.x))
        {
            blockerDepthSum += rawDepth;
            blockerCount++;
        }
    }

    if (blockerCount <= 0)
    {
        // 没有遮挡物 = 完全受光；但直接返回 1 会在"遮挡物刚好擦过采样盘"时留下光斑，
        // 所以走一次 PCF（它自己也只会得到 1）。
        return HoCSSampleManualPcf(uv, lo, hi, receiverDepth);
    }

    float averageBlockerDepth = blockerDepthSum / (float)blockerCount;
    // 半影宽度按**物理形式**估：penumbra = (接收距离 - 遮挡距离) / 遮挡距离。
    // atlas 里的 z 是阴影空间的归一化线性深度，所以"到光源的距离"用 (1 - z)（reversed-Z）表达，
    // 归一化的深度范围在分子分母里自然约掉。注意这里**除的是遮挡距离**（PCSS 的原始形式）；
    // ShadowCast 那边除的是接收深度，接收深度接近 0 时半影会被放大到糊掉整片阴影。
    #if UNITY_REVERSED_Z
        float receiverDistance = 1.0 - receiverDepth;
        float blockerDistance = 1.0 - averageBlockerDepth;
    #else
        float receiverDistance = receiverDepth;
        float blockerDistance = averageBlockerDepth;
    #endif
    float penumbra = saturate((receiverDistance - blockerDistance) / max(blockerDistance, 0.0001));
    float filterRadius = min(max(_HoCSPcssParams.w, 0.0), _HoCSPcssParams.y * _HoCSPcssParams.w * penumbra);
    if (filterRadius <= 0.001)
    {
        return HoCSCompare(HoCSSampleRawDepth(uv), receiverDepth);
    }

    float visibility = 0.0;
    [loop] for (int filterIndex = 0; filterIndex < HO_CS_MAX_PCSS_FILTER_SAMPLES; filterIndex++)
    {
        if (filterIndex >= filterSampleCount)
        {
            break;
        }

        // filter 阶段换个相位，免得 blocker 盘与滤波盘的采样点重合。
        float2 sampleUv = clamp(uv + HoCSPcssOffset(filterIndex, filterSampleCount, rotation + 1.731) * texel * filterRadius, lo, hi);
        visibility += HoCSCompare(sampleUv, receiverDepth);
    }

    return visibility / (float)filterSampleCount;
}

float HoCSResolveMainCast(float3 positionWS, float sceneCast)
{
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
    float3 local = mul(_HoCSWorldToBounds[slice], float4(positionWS, 1)).xyz;
    float edge = 0.5 - max(abs(local.x), max(abs(local.y), abs(local.z)));
    if (edge <= 0) return sceneCast;
    float3 shadow = mul(_HoCSWorldToShadow[slice], float4(positionWS, 1)).xyz;
    if (any(shadow < 0) || any(shadow > 1)) return sceneCast;
    float4 tile = _HoCSTileRects[slice];
    float2 uv = tile.xy + shadow.xy * tile.zw;
    float2 lo = tile.xy + _HoCSAtlasSize.xy * 0.5;
    float2 hi = tile.xy + tile.zw - _HoCSAtlasSize.xy * 0.5;
    float visibility = HoCSSampleAtlasPcss(uv, lo, hi, shadow.z);
    // Same level as MainLightRealtimeShadow: shadow strength once, no extra light/AO terms.
    float localCast = lerp(1.0, visibility, parameters.y);
    float blend = parameters.x > 0 ? smoothstep(0, parameters.x, edge) : 1;
    return lerp(sceneCast, localCast, blend);
}
#endif
