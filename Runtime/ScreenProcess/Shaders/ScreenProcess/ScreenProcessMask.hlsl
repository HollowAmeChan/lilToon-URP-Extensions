#ifndef LIL_SCREEN_PROCESS_MASK_INCLUDED
#define LIL_SCREEN_PROCESS_MASK_INCLUDED

float _HoMetadataBufferActive;
float _LayerMaskEnabled;
float _LayerMaskInvert;
float _LayerMaskDebugOutput;

TEXTURE2D_X(_HoMetadataBufferMaskIdTexture);
float4 _HoMetadataBufferMaskIdTexture_TexelSize;

// 遮罩来源以后由 AC（Ho-AttributeComposite）提供，这里只负责采样与每层开关。
float LilScreenProcessResolveMaskInternal(float2 uv, bool forceEnabled)
{
    if (!forceEnabled && _LayerMaskEnabled <= 0.5)
    {
        return 1.0;
    }

    if (_HoMetadataBufferActive <= 0.5)
    {
        return 0.0;
    }

    float coverage = saturate(SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaskIdTexture, sampler_PointClamp, uv).r);

    // 单族遮罩 = MetadataBuffer maskId 覆盖率（AC 以后喂同一路）；反转沿用旧语义：
    // 在覆盖范围内把选中值减掉（selected == coverage 时结果就是 0），覆盖外本来就是 0。
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
    if (_HoMetadataBufferActive <= 0.5)
    {
        return 0.0;
    }

    return saturate(SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaskIdTexture, sampler_PointClamp, uv).r);
}

float2 LilScreenProcessMaskTexelSize()
{
    return _HoMetadataBufferMaskIdTexture_TexelSize.xy;
}

float2 LilScreenProcessMaskTextureSize()
{
    return _HoMetadataBufferMaskIdTexture_TexelSize.zw;
}

#endif
