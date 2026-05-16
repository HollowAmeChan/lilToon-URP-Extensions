#ifndef LIL_HOPOST_AOV_MASK_INCLUDED
#define LIL_HOPOST_AOV_MASK_INCLUDED

float _lilHoAovActive;
float _LayerAovMaskEnabled;
float _LayerAovSource;
float _LayerAovMode;
float4 _LayerAovParams; // x threshold/tolerance, y softness, z match value, w invert
float4 _LayerAovMatchColor;
float _LayerAovDebugOutput;

TEXTURE2D_X(_lilHoAovMaskIdTexture);
float4 _lilHoAovMaskIdTexture_TexelSize;
TEXTURE2D_X(_lilHoAovSurfaceDataTexture);
TEXTURE2D_X(_lilHoAovCustom0_3Texture);

float LilHoPostEncodeAovScalar(float value)
{
    return frac(abs(value) * 0.61803398875);
}

float LilHoPostMatchRawValue(float value, float target, float tolerance, float softness)
{
    float edge0 = max(tolerance, 0.0);
    float edge1 = edge0 + max(softness, 0.0001);
    return 1.0 - smoothstep(edge0, edge1, abs(value - target));
}

float LilHoPostMatchEncodedValue(float value, float target, float tolerance, float softness)
{
    float encodedTarget = LilHoPostEncodeAovScalar(target);
    float delta = abs(value - encodedTarget);
    delta = min(delta, 1.0 - delta);
    float edge0 = max(tolerance, 0.0);
    float edge1 = edge0 + max(softness, 0.0001);
    return 1.0 - smoothstep(edge0, edge1, delta);
}

float LilHoPostSelectAovScalar(float4 maskId, float4 surfaceData, float4 custom0, int source)
{
    if (source == 1)
    {
        return maskId.g;
    }

    if (source == 2)
    {
        return maskId.b;
    }

    if (source == 3)
    {
        return maskId.a;
    }

    if (source == 4)
    {
        return surfaceData.r;
    }

    if (source == 5)
    {
        return surfaceData.g;
    }

    if (source == 6)
    {
        return surfaceData.b;
    }

    if (source == 7)
    {
        return surfaceData.a;
    }

    if (source == 8)
    {
        return custom0.r;
    }

    if (source == 9)
    {
        return custom0.g;
    }

    if (source == 10)
    {
        return custom0.b;
    }

    if (source == 11)
    {
        return custom0.a;
    }

    return maskId.r;
}

float4 LilHoPostSelectAovColor(float4 maskId, float4 surfaceData, float4 custom0, int source)
{
    if (source >= 4 && source <= 7)
    {
        return surfaceData;
    }

    if (source >= 8 && source <= 11)
    {
        return custom0;
    }

    return maskId;
}

float LilHoPostResolveAovMaskInternal(float2 uv, bool forceEnabled)
{
    if (!forceEnabled && _LayerAovMaskEnabled <= 0.5)
    {
        return 1.0;
    }

    if (_lilHoAovActive <= 0.5)
    {
        return 0.0;
    }

    int source = (int)clamp(round(_LayerAovSource), 0.0, 11.0);
    int mode = (int)clamp(round(_LayerAovMode), 0.0, 3.0);
    float threshold = max(_LayerAovParams.x, 0.0);
    float softness = max(_LayerAovParams.y, 0.0001);
    float matchValue = _LayerAovParams.z;
    float invert = saturate(_LayerAovParams.w);

    float4 maskId = SAMPLE_TEXTURE2D_X(_lilHoAovMaskIdTexture, sampler_LinearClamp, uv);
    float4 surfaceData = SAMPLE_TEXTURE2D_X(_lilHoAovSurfaceDataTexture, sampler_LinearClamp, uv);
    float4 custom0 = SAMPLE_TEXTURE2D_X(_lilHoAovCustom0_3Texture, sampler_LinearClamp, uv);
    float coverage = saturate(maskId.r);
    float scalar = LilHoPostSelectAovScalar(maskId, surfaceData, custom0, source);
    float selected = saturate(scalar);

    if (mode == 1)
    {
        selected = smoothstep(threshold, threshold + softness, scalar);
    }
    else if (mode == 2)
    {
        bool encodedSource = source == 1 || source == 2 || source == 3 || source == 6;
        selected = encodedSource
            ? LilHoPostMatchEncodedValue(scalar, matchValue, threshold, softness)
            : LilHoPostMatchRawValue(scalar, matchValue, threshold, softness);
    }
    else if (mode == 3)
    {
        float4 colorSample = LilHoPostSelectAovColor(maskId, surfaceData, custom0, source);
        float colorDistance = distance(colorSample.rgb, _LayerAovMatchColor.rgb);
        selected = 1.0 - smoothstep(threshold, threshold + softness, colorDistance);
    }

    selected *= coverage;
    return lerp(selected, saturate(coverage - selected), invert);
}

float LilHoPostResolveAovLayerMask(float2 uv)
{
    return LilHoPostResolveAovMaskInternal(uv, false);
}

float LilHoPostResolveRequiredAovMask(float2 uv)
{
    return LilHoPostResolveAovMaskInternal(uv, true);
}

bool LilHoPostShouldOutputAovDebug()
{
    return _LayerAovDebugOutput > 0.5;
}

half4 LilHoPostAovDebugColor(float2 uv, bool forceEnabled, half alpha)
{
    half mask = (half)LilHoPostResolveAovMaskInternal(uv, forceEnabled);
    return half4(mask, mask, mask, alpha);
}

float LilHoPostAovCoverage(float2 uv)
{
    if (_lilHoAovActive <= 0.5)
    {
        return 0.0;
    }

    return saturate(SAMPLE_TEXTURE2D_X(_lilHoAovMaskIdTexture, sampler_LinearClamp, uv).r);
}

float2 LilHoPostAovTexelSize()
{
    return _lilHoAovMaskIdTexture_TexelSize.xy;
}

float2 LilHoPostAovTextureSize()
{
    return _lilHoAovMaskIdTexture_TexelSize.zw;
}

#endif
