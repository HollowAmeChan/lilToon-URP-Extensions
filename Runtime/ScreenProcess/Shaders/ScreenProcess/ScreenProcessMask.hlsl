#ifndef LIL_SCREEN_PROCESS_MASK_INCLUDED
#define LIL_SCREEN_PROCESS_MASK_INCLUDED

// 层遮罩由 AC typed query 提供；效果自身的 subject coverage 仍是 OB 身份总覆盖率。
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/AttributeComposite/Shaders/HoACQuery.hlsl"

/// <summary>本相机当前层查询与范围输入是否有效（C# 每层设置）。</summary>
float _lilHoSPMaskValid;
float _lilHoSPCoverageValid;
float4 _LayerMaskQuery;
/// <summary>遮罩纹理的 texel / 尺寸：xy = texel、zw = 像素尺寸（C# 发布 —— 全局纹理没有 `_TexelSize`）。</summary>
float4 _lilHoSPMaskTexelSize;
float _LayerMaskEnabled;
float _LayerMaskInvert;
float _LayerMaskDebugOutput;

/// <summary>遮罩来源本身：AC 的总覆盖率（0 = 这一格不属于任何身份）。</summary>
float LilScreenProcessMaskSource(float2 uv)
{
    return saturate(HoAC_TotalCoverage(uv));
}

float LilScreenProcessResolveMaskInternal(float2 uv, bool forceEnabled)
{
    if (!forceEnabled && _LayerMaskEnabled <= 0.5 && _LayerMaskDebugOutput <= 0.5)
    {
        return 1.0;
    }

    if (_lilHoSPMaskValid <= 0.5)
    {
        return 0.0;
    }

    // Intrinsic subject coverage keeps its old meaning when the optional layer mask is off.
    if (_LayerMaskEnabled <= 0.5 && _LayerMaskDebugOutput <= 0.5)
        return _lilHoSPCoverageValid > 0.5 ? LilScreenProcessMaskSource(uv) : 0.0;
    return HoAC_QueryMask(uv, _LayerMaskQuery, _LayerMaskInvert > 0.5);
}

float LilScreenProcessResolveLayerMask(float2 uv)
{
    return LilScreenProcessResolveMaskInternal(uv, false);
}

float LilScreenProcessResolveCoverageMask(float2 uv)
{
    return LilScreenProcessResolveMaskInternal(uv, true);
}

bool LilScreenProcessShouldOutputMaskDebug()
{
    return _LayerMaskDebugOutput > 0.5;
}

half4 LilScreenProcessMaskDebugColor(float2 uv, bool forceEnabled, half alpha)
{
    half mask = (half)LilScreenProcessResolveMaskInternal(uv, forceEnabled);
    return half4(mask, mask, mask, alpha);
}

float LilScreenProcessMaskCoverage(float2 uv)
{
    if (_lilHoSPCoverageValid <= 0.5)
    {
        return 0.0;
    }

    return LilScreenProcessMaskSource(uv);
}

float2 LilScreenProcessMaskTexelSize()
{
    return _lilHoSPMaskTexelSize.xy;
}

float2 LilScreenProcessMaskTextureSize()
{
    return _lilHoSPMaskTexelSize.zw;
}

#endif
