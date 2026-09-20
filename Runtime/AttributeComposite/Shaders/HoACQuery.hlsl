#ifndef LIL_HO_AC_QUERY_INCLUDED
#define LIL_HO_AC_QUERY_INCLUDED

// Ho-AttributeComposite（AC）查询门面（规划 §3）。**消费端只经这里读语义与属性**：
// 不自己解码 OB/SB 的 packing、也不自己攒语义图；RenderGraph 的物理读依赖仍要各自声明。
//
// 本轮（R3-obj / R4b / R4c）落地的：Identity / Group / Layer0Identity / Predicate / TotalCoverage /
// Selection / Attribute / SurfaceValid。`HoAC_Attribute` 现在有生产者了：surface 来自 SB 的
// Classification + owner（`constant < surface`，读取时合成）。

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoObjectBufferIdPass.hlsl"

// 身份池（引用 OB；AC 的 pass 把它们绑成全局，消费者从 AC 资源集取句柄并声明读依赖）。
TEXTURE2D_X(_HoObjectBufferId0Texture);
TEXTURE2D_X(_HoObjectBufferId1Texture);
TEXTURE2D_X(_HoObjectBufferCoverageTexture);

// AC Selection 池：每张 RGBA8 装 2 条 `(SemanticId, coverage)`，共 8 条 lane。
TEXTURE2D_X(_HoACSelection0Texture);
TEXTURE2D_X(_HoACSelection1Texture);
TEXTURE2D_X(_HoACSelection2Texture);
TEXTURE2D_X(_HoACSelection3Texture);

struct HoACLaneData
{
    uint semanticId;
    uint objectTagBit;
    uint sourceMode;
    uint reserved;
};

// runtime catalog（C# 侧变脏重建时上传）：lane → SemanticId / object 位 / sourceMode。
StructuredBuffer<HoACLaneData> _HoACLanes;
float _HoACLaneCount;
/// <summary>本帧 AC 有没有产出（0 = 没有；调试与兜底都靠它，不要拿 LaneCount 当"有没有跑"）。</summary>
float _HoACActive;

/// <summary>一张 RGBA8 里两条 lane 的 `(SemanticId, coverage)`。</summary>
void HoAC_UnpackSelection(float4 packed, out uint semanticIdA, out float coverageA, out uint semanticIdB, out float coverageB)
{
    HoObjectBufferUnpackSelection(packed, semanticIdA, coverageA, semanticIdB, coverageB);
}

/// <summary>身份池的一层：(partId, coverage)；id = 0 表示这一层没有东西。</summary>
void HoAC_IdentityLayer(float4 id0, float4 id1, float4 coverage, uint layer, out uint partId, out float layerCoverage)
{
    float4 packed = layer < 2u ? id0 : id1;
    float2 pair = (layer & 1u) == 0u ? packed.xy : packed.zw;
    partId = HoObjectBufferDecodeIdExact(pair);
    layerCoverage = layer == 0u ? coverage.r : (layer == 1u ? coverage.g : (layer == 2u ? coverage.b : coverage.a));
}

/// <summary>0..3 层的身份与覆盖率（consumer 自己读一遍，别在循环里反复采样）。</summary>
void HoAC_LoadIdentityPool(float2 uv, out float4 id0, out float4 id1, out float4 coverage)
{
    id0 = SAMPLE_TEXTURE2D_X(_HoObjectBufferId0Texture, sampler_PointClamp, uv);
    id1 = SAMPLE_TEXTURE2D_X(_HoObjectBufferId1Texture, sampler_PointClamp, uv);
    coverage = SAMPLE_TEXTURE2D_X(_HoObjectBufferCoverageTexture, sampler_PointClamp, uv);
}

/// <summary>层 0 获胜身份的完整 16 bit（组 8 + 槽位 8）= 本像素"这是谁"。没有东西时是 0。</summary>
uint HoAC_Layer0Identity(float2 uv)
{
    float4 id0 = SAMPLE_TEXTURE2D_X(_HoObjectBufferId0Texture, sampler_PointClamp, uv);
    return HoObjectBufferDecodeIdExact(id0.xy);
}

/// <summary>层 0 的组字节（0..255）。注意区别于 `HoAC_Group`：那个是"命中某组的覆盖率"。</summary>
uint HoAC_Layer0Group(float2 uv)
{
    return HoObjectBufferGroupId(HoAC_Layer0Identity(uv));
}

/// <summary>四层身份覆盖率之和（不含多层 alpha / OIT 的颜色贡献）。</summary>
float HoAC_TotalCoverage(float2 uv)
{
    float4 coverage = SAMPLE_TEXTURE2D_X(_HoObjectBufferCoverageTexture, sampler_PointClamp, uv);
    return saturate(coverage.r + coverage.g + coverage.b + coverage.a);
}

/// <summary>完整 `(group,slot)` 精确匹配的覆盖率。</summary>
float HoAC_Identity(float2 uv, uint id16)
{
    if (id16 == 0u)
    {
        return 0.0;
    }

    float4 id0;
    float4 id1;
    float4 coverage;
    HoAC_LoadIdentityPool(uv, id0, id1, coverage);

    float total = 0.0;
    [unroll]
    for (uint layer = 0u; layer < 4u; layer++)
    {
        uint partId;
        float layerCoverage;
        HoAC_IdentityLayer(id0, id1, coverage, layer, partId, layerCoverage);
        total += (partId == id16) ? layerCoverage : 0.0;
    }

    return saturate(total);
}

/// <summary>按组字节匹配的覆盖率（"这个像素有多少属于第 group8 组"）。</summary>
float HoAC_Group(float2 uv, uint group8)
{
    if (group8 == 0u)
    {
        return 0.0;
    }

    float4 id0;
    float4 id1;
    float4 coverage;
    HoAC_LoadIdentityPool(uv, id0, id1, coverage);

    float total = 0.0;
    [unroll]
    for (uint layer = 0u; layer < 4u; layer++)
    {
        uint partId;
        float layerCoverage;
        HoAC_IdentityLayer(id0, id1, coverage, layer, partId, layerCoverage);
        total += (HoObjectBufferGroupId(partId) == group8) ? layerCoverage : 0.0;
    }

    return saturate(total);
}

/// <summary>
/// 物体位谓词：`Σ cov_i · 该层部件带不带这一位`（规划 §0.2 的 `HoAC_Predicate`）。
/// 位号就是 <c>HoObjectBufferPartTags</c> 的位序；部件行表里存的是整份标签掩码。
/// </summary>
float HoAC_Predicate(float2 uv, uint objectTagBit)
{
    if (objectTagBit >= 32u)
    {
        return 0.0;
    }

    float4 id0;
    float4 id1;
    float4 coverage;
    HoAC_LoadIdentityPool(uv, id0, id1, coverage);

    uint bit = 1u << objectTagBit;
    float total = 0.0;
    [unroll]
    for (uint layer = 0u; layer < 4u; layer++)
    {
        uint partId;
        float layerCoverage;
        HoAC_IdentityLayer(id0, id1, coverage, layer, partId, layerCoverage);
        if (partId == 0u || layerCoverage <= 0.0)
        {
            continue;
        }

        uint tags = HoObjectBufferLoadPart(partId).tags;
        total += ((tags & bit) != 0u) ? layerCoverage : 0.0;
    }

    return saturate(total);
}

/// <summary>
/// 一条 lane 的覆盖率：按 runtime catalog 定位 lane、**校验图内 SemanticId** 后返回
/// （图内 ID 与声明不符时按"未写"处理，规划 §3）。`laneIndex` 是编译期常量时会被折叠。
/// </summary>
float HoAC_Selection(float2 uv, uint laneIndex)
{
    uint laneCount = (uint)max(0.0, _HoACLaneCount);
    if (laneIndex >= laneCount)
    {
        return 0.0;
    }

    float4 packed;
    if (laneIndex < 2u)
    {
        packed = SAMPLE_TEXTURE2D_X(_HoACSelection0Texture, sampler_PointClamp, uv);
    }
    else if (laneIndex < 4u)
    {
        packed = SAMPLE_TEXTURE2D_X(_HoACSelection1Texture, sampler_PointClamp, uv);
    }
    else if (laneIndex < 6u)
    {
        packed = SAMPLE_TEXTURE2D_X(_HoACSelection2Texture, sampler_PointClamp, uv);
    }
    else
    {
        packed = SAMPLE_TEXTURE2D_X(_HoACSelection3Texture, sampler_PointClamp, uv);
    }

    uint semanticIdA;
    float coverageA;
    uint semanticIdB;
    float coverageB;
    HoAC_UnpackSelection(packed, semanticIdA, coverageA, semanticIdB, coverageB);

    bool isEven = (laneIndex & 1u) == 0u;
    uint inImageId = isEven ? semanticIdA : semanticIdB;
    float coverage = isEven ? coverageA : coverageB;
    uint declaredId = _HoACLanes[laneIndex].semanticId;
    return inImageId == declaredId ? coverage : 0.0;
}

/// <summary>
/// 具名语义的覆盖率：名字在 C# 侧已经解析成 lane 号，这里只做 ID 校验与取值（规划 §3：像素里只有 ID 比较）。
/// 解析不到的名字要传 laneCount 之外的号（C# 会这么给），于是恒返回 0 并在诊断里报出来。
/// </summary>
float HoAC_SelectionByName(float2 uv, uint laneIndex)
{
    return HoAC_Selection(uv, laneIndex);
}

// SB 的数值面（AC 只发布**引用**：属性合成是读取时做的，不落一张合成图）。
TEXTURE2D_X(_HoSurfaceBufferClassificationTexture);
TEXTURE2D_X(_HoSurfaceBufferOwnerTexture);
float _HoSurfaceBufferActive;

/// <summary>
/// 这个像素的 surface 数值能不能用：SB 有产出 **且** SB 的前表面就是 OB 层 0 说的那个身份
/// （规划 §0.1 的 owner 对齐）。不匹配时消费者拿到的是 constant 兜底，不是错值。
/// </summary>
bool HoAC_SurfaceValid(float2 uv)
{
    if (_HoSurfaceBufferActive <= 0.5)
    {
        return false;
    }

    float4 ownerTexel = SAMPLE_TEXTURE2D_X(_HoSurfaceBufferOwnerTexture, sampler_PointClamp, uv);
    uint owner = (((uint)round(saturate(ownerTexel.r) * 255.0)) << 8) | (uint)round(saturate(ownerTexel.g) * 255.0);
    return owner != 0u && owner == HoAC_Layer0Identity(uv);
}

/// <summary>
/// 合成数值属性：覆盖链 `constant &lt; surface`（规划 §4）。本轮只有**一条**：
/// `Classification` = `(sssProfileIdByte, curvatureHint, transmittanceHint, materialClassIdByte)`。
/// <list type="bullet">
/// <item>`attributeId`：0 = Classification；其它值本轮没有生产者，返回 0（消费者不要依赖）。</item>
/// <item>byte 通道是**精确 ID**：`round(v * 255)` 还原；`0` 是合法值，"没写"要用
/// <see cref="HoAC_SurfaceValid"/> 区分 —— 这正是"0 值与未写可区分"那条验收。</item>
/// <item>constant 兜底本轮恒 0：等真有消费者要非 0 兜底，再加 AC 侧的常量表（feature 的声明节）。</item>
/// </list>
/// </summary>
float4 HoAC_Attribute(float2 uv, uint attributeId)
{
    if (attributeId == 0u && HoAC_SurfaceValid(uv))
    {
        return SAMPLE_TEXTURE2D_X(_HoSurfaceBufferClassificationTexture, sampler_PointClamp, uv);
    }

    return float4(0.0, 0.0, 0.0, 0.0);
}

#endif
