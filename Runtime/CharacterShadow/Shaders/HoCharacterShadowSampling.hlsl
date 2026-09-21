#ifndef HO_CHARACTER_SHADOW_SAMPLING_INCLUDED
#define HO_CHARACTER_SHADOW_SAMPLING_INCLUDED
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoRendererIdentity.hlsl"
#define HO_CS_MAX_SLICES 16
// PCSS 采样上限。C# 侧 HoCharacterShadowShaderContract 必须与这两个值一致
// （HoCharacterShadowValidation.Validate() 会解析本文件比对，防漂移）。
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
// (enabled, softness, blocker 搜索半径 **世界单位**, 半影半径上限 **世界单位**)
float4 _HoCSPcssParams;
// (blocker 深度偏移, blocker 采样数, filter 采样数, 最低软度 **世界单位**)
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

// 固定半径的 3×3 PCF 已被"旋转盘"取代（见 HoCSSampleDisk）：固定网格在斜边上会留下
// 规律性台阶，用户实测"PCF 边缘锯齿依旧明显"就是它。这里只留一个中心比较。
float HoCSSampleCenter(float2 uv, float2 lo, float2 hi, float receiverDepth)
{
    return HoCSCompare(clamp(uv, lo, hi), receiverDepth);
}

// 旋转角按**世界位置**取：每个屏幕像素都不一样。
// 曾经按 atlas texel 取（floor(uv / texel)），相邻像素共用同一套采样图案，
// 结果噪声表现成 texel 大小的方块 —— 用户看到的"一片黑点"就是它。
float HoCSSampleRotation(float3 positionWS)
{
    float3 cell = floor(positionWS * 1024.0);
    return frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453) * 6.2831853;
}

// 黄金角螺旋盘：把 N 个采样均匀铺在单位圆里，再整体旋转一个角度。
float2 HoCSSampleOffset(int index, int sampleCount, float rotation)
{
    float sampleIndex = (float)index + 0.5;
    float radius = sqrt(sampleIndex / max((float)sampleCount, 1.0));
    float angle = sampleIndex * 2.39996323 + rotation;
    return float2(cos(angle), sin(angle)) * radius;
}

// 世界半径 → texel 半径。**软阴影半径一律用世界单位给**：tile 分辨率越高，1 texel 覆盖的世界尺寸越小，
// 用 texel 当单位的话同一个数值在不同分辨率下软硬完全不一样（4096 的 tile 上"12 texel"只有 7mm，
// 看起来还是硬边 —— 用户就是这么踩的）。
// 再按采样预算收一下：盘面积不能远超采样数，否则采样太稀会出颗粒（用户报的"噪声黑点"）。
float HoCSRadiusTexels(float worldRadius, float texelWorld, int sampleCount)
{
    float texels = worldRadius / max(texelWorld, 1e-6);
    float budget = sqrt(max((float)sampleCount, 1.0) * 6.25);
    return max(min(texels, budget), 0.0);
}

// 旋转盘滤波：半径按 texel 计（调用方用 HoCSRadiusTexels 换算）。PCF 与 PCSS 的滤波阶段共用它。
float HoCSSampleDisk(float2 uv, float2 lo, float2 hi, float receiverDepth,
    float radiusTexels, int sampleCount, float rotation)
{
    if (radiusTexels <= 0.001 || sampleCount <= 0)
    {
        return HoCSSampleCenter(uv, lo, hi, receiverDepth);
    }

    float2 texel = _HoCSAtlasSize.xy;
    float visibility = 0.0;
    [loop] for (int index = 0; index < HO_CS_MAX_PCSS_FILTER_SAMPLES; index++)
    {
        if (index >= sampleCount)
        {
            break;
        }

        float2 sampleUv = clamp(uv + HoCSSampleOffset(index, sampleCount, rotation) * texel * radiusTexels, lo, hi);
        visibility += HoCSCompare(sampleUv, receiverDepth);
    }

    return visibility / (float)sampleCount;
}

// PCSS：先在 blocker 盘里找遮挡物，用平均遮挡深度估半影宽度，再按该宽度做可变半径滤波。
// texelWorld = 该 slice 的 1 texel 等于多少世界单位（由 C# 发布在 _HoCSParameters[slice].w）。
float HoCSSampleAtlasPcss(float2 uv, float2 lo, float2 hi, float receiverDepth, float3 positionWS, float texelWorld)
{
    int filterSampleCount = min((int)round(_HoCSPcssParams2.z), HO_CS_MAX_PCSS_FILTER_SAMPLES);
    if (filterSampleCount <= 0)
    {
        filterSampleCount = 1;
    }

    float rotation = HoCSSampleRotation(positionWS);
    float minRadius = HoCSRadiusTexels(max(_HoCSPcssParams2.w, 0.0), texelWorld, filterSampleCount);
    if (_HoCSPcssParams.x < 0.5 || _HoCSPcssParams.y <= 0.0)
    {
        return HoCSSampleDisk(uv, lo, hi, receiverDepth, minRadius, filterSampleCount, rotation);
    }

    int blockerSampleCount = min((int)round(_HoCSPcssParams2.y), HO_CS_MAX_PCSS_BLOCKER_SAMPLES);
    float blockerRadius = HoCSRadiusTexels(max(_HoCSPcssParams.z, 0.0), texelWorld, max(blockerSampleCount, 1));
    if (blockerSampleCount <= 0 || blockerRadius <= 0.0)
    {
        return HoCSSampleDisk(uv, lo, hi, receiverDepth, minRadius, filterSampleCount, rotation);
    }

    float2 texel = _HoCSAtlasSize.xy;
    float blockerDepthSum = 0.0;
    int blockerCount = 0;
    [loop] for (int blockerIndex = 0; blockerIndex < HO_CS_MAX_PCSS_BLOCKER_SAMPLES; blockerIndex++)
    {
        if (blockerIndex >= blockerSampleCount)
        {
            break;
        }

        float2 sampleUv = clamp(uv + HoCSSampleOffset(blockerIndex, blockerSampleCount, rotation) * texel * blockerRadius, lo, hi);
        float rawDepth = HoCSSampleRawDepth(sampleUv);
        if (HoCSIsBlocker(rawDepth, receiverDepth, _HoCSPcssParams2.x))
        {
            blockerDepthSum += rawDepth;
            blockerCount++;
        }
    }

    float penumbra = 1.0;
    if (blockerCount > 0)
    {
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
        penumbra = saturate((receiverDistance - blockerDistance) / max(blockerDistance, 0.0001));
    }
    else if (HoCSSampleCenter(uv, lo, hi, receiverDepth) >= 0.5)
    {
        // 盘里没有遮挡物、中心也受光 → 这片确实没有遮挡，按最低软度走一次即可（保持平滑，不引入硬边）。
        return HoCSSampleDisk(uv, lo, hi, receiverDepth, minRadius, filterSampleCount, rotation);
    }
    // 剩下一种：中心被遮挡、但盘里没采到遮挡物 —— 这是**稀疏采样漏掉**了，不是"没有遮挡"。
    // 早期版本在这里退回 PCF，于是同一个半影带里一半像素是硬边、一半是软边，看起来就是一片斑点。
    // 现在按"半影最大"处理。

    float maxRadius = HoCSRadiusTexels(max(_HoCSPcssParams.w, 0.0), texelWorld, filterSampleCount);
    float filterRadius = max(minRadius, min(maxRadius, maxRadius * _HoCSPcssParams.y * penumbra));
    return HoCSSampleDisk(uv, lo, hi, receiverDepth, filterRadius, filterSampleCount, rotation + 1.731);
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
    float visibility = HoCSSampleAtlasPcss(uv, lo, hi, shadow.z, positionWS, parameters.w);
    // Same level as MainLightRealtimeShadow: shadow strength once, no extra light/AO terms.
    float localCast = lerp(1.0, visibility, parameters.y);
    float blend = parameters.x > 0 ? smoothstep(0, parameters.x, edge) : 1;
    return lerp(sceneCast, localCast, blend);
}
#endif
