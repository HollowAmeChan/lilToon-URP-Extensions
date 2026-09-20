#ifndef LIL_SCREEN_PROCESS_MASK_INCLUDED
#define LIL_SCREEN_PROCESS_MASK_INCLUDED

// 屏幕处理图层的遮罩：**来源是 AC 的总覆盖率**（OB 四层身份覆盖率之和）。
// MB 的 `maskId.r` 删掉后这一路只剩一个来源，采样口径与 SSS / PLR 完全一致。
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/AttributeComposite/Shaders/HoACQuery.hlsl"

/// <summary>本帧遮罩来源在不在（C# 每层设：图层要遮罩 **且** OB 的覆盖率图有效）。</summary>
float _lilHoSPMaskValid;
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
    if (!forceEnabled && _LayerMaskEnabled <= 0.5)
    {
        return 1.0;
    }

    if (_lilHoSPMaskValid <= 0.5)
    {
        return 0.0;
    }

    float coverage = LilScreenProcessMaskSource(uv);

    // 单族遮罩 = 角色覆盖率；反转沿用旧语义：在覆盖范围内把选中值减掉
    // （selected == coverage 时结果就是 0），覆盖外本来就是 0。
    float selected = coverage;
    float invert = saturate(_LayerMaskInvert);
    return lerp(selected, saturate(coverage - selected), invert);
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
    if (_lilHoSPMaskValid <= 0.5)
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
