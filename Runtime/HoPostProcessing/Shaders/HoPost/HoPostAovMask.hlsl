#ifndef LIL_HOPOST_AOV_MASK_INCLUDED
#define LIL_HOPOST_AOV_MASK_INCLUDED

#ifndef LIL_HOPOST_AOV_RULE_GROUP_ENABLED
#define LIL_HOPOST_AOV_RULE_GROUP_ENABLED 1
#endif

#define LIL_HOPOST_AOV_RULE_MAX 4

float _lilHoAovActive;
float _LayerAovMaskEnabled;
float _LayerAovSource;
float _LayerAovMode;
float4 _LayerAovParams; // x threshold/tolerance, y reserved, z match value, w final invert
float4 _LayerAovMatchColor;
float _LayerAovDebugOutput;
#if LIL_HOPOST_AOV_RULE_GROUP_ENABLED
float _LayerAovRuleCount;
float4 _LayerAovRuleData0[LIL_HOPOST_AOV_RULE_MAX]; // x enabled, y source, z operator, w combine
float4 _LayerAovRuleData1[LIL_HOPOST_AOV_RULE_MAX]; // x value, y min, z max, w tolerance
float4 _LayerAovRuleData2[LIL_HOPOST_AOV_RULE_MAX]; // x reserved, y invert
float4 _LayerAovRuleColor[LIL_HOPOST_AOV_RULE_MAX];
#endif

TEXTURE2D_X(_lilHoAovMaskIdTexture);
float4 _lilHoAovMaskIdTexture_TexelSize;
TEXTURE2D_X(_lilHoAovSurfaceDataTexture);
TEXTURE2D_X(_lilHoAovCustom0_3Texture);
TEXTURE2D_X(_lilHoAovObjectCustom0_3Texture);
TEXTURE2D_X(_lilHoAovObjectCustom4_7Texture);

float LilHoPostEncodeAovScalar(float value)
{
    return frac(abs(value) * 0.61803398875);
}

#if LIL_HOPOST_AOV_RULE_GROUP_ENABLED
float LilHoPostHasPackedBit(float value, float bitValue)
{
    return step(0.5, fmod(floor(value / bitValue), 2.0));
}

float LilHoPostFlagsAny(float normalizedFlags, float targetFlags)
{
    float current = floor(saturate(normalizedFlags) * 255.0 + 0.5);
    float target = floor(abs(targetFlags) + 0.5);
    float selected = 0.0;

    for (int bit = 0; bit < 8; bit++)
    {
        float bitValue = exp2((float)bit);
        selected = max(selected, LilHoPostHasPackedBit(current, bitValue) * LilHoPostHasPackedBit(target, bitValue));
    }

    return selected * step(0.5, target);
}

float LilHoPostFlagsAll(float normalizedFlags, float targetFlags)
{
    float current = floor(saturate(normalizedFlags) * 255.0 + 0.5);
    float target = floor(abs(targetFlags) + 0.5);
    float selected = step(0.5, target);

    for (int bit = 0; bit < 8; bit++)
    {
        float bitValue = exp2((float)bit);
        float targetBit = LilHoPostHasPackedBit(target, bitValue);
        float currentBit = LilHoPostHasPackedBit(current, bitValue);
        selected *= lerp(1.0, currentBit, targetBit);
    }

    return selected;
}
#endif

bool LilHoPostIsByteAovSource(int source)
{
    return source == 1 || source == 2 || source == 3;
}

bool LilHoPostIsEncodedAovSource(int source)
{
    return source == 6;
}

float LilHoPostDecodeByteValue(float value)
{
    return floor(saturate(value) * 255.0 + 0.5);
}

float LilHoPostClampRuleByteValue(float value)
{
    return clamp(round(value), 0.0, 255.0);
}

float LilHoPostMatchByteValue(float value, float target, float tolerance)
{
    float valueByte = LilHoPostDecodeByteValue(value);
    float targetByte = LilHoPostClampRuleByteValue(target);
    return abs(valueByte - targetByte) <= max(tolerance, 0.0) ? 1.0 : 0.0;
}

float LilHoPostResolveMatchTolerance(int source, float tolerance)
{
    return LilHoPostIsEncodedAovSource(source) ? max(tolerance, 0.001) : max(tolerance, 0.0);
}

float LilHoPostNormalizeRuleValue(float value, int source)
{
    if (LilHoPostIsByteAovSource(source))
    {
        return saturate(round(value) / 255.0);
    }

    if (LilHoPostIsEncodedAovSource(source))
    {
        return LilHoPostEncodeAovScalar(value);
    }

    return value;
}

float LilHoPostMatchRawValue(float value, float target, float tolerance)
{
    return abs(value - target) <= max(tolerance, 0.0) ? 1.0 : 0.0;
}

float LilHoPostSelectAovScalar(float4 maskId, float4 surfaceData, float4 custom0, float4 objectCustom0, float4 objectCustom1, int source)
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

    if (source == 12)
    {
        return objectCustom0.r;
    }

    if (source == 13)
    {
        return objectCustom0.g;
    }

    if (source == 14)
    {
        return objectCustom0.b;
    }

    if (source == 15)
    {
        return objectCustom0.a;
    }

    if (source == 16)
    {
        return objectCustom1.r;
    }

    if (source == 17)
    {
        return objectCustom1.g;
    }

    if (source == 18)
    {
        return objectCustom1.b;
    }

    if (source == 19)
    {
        return objectCustom1.a;
    }

    return maskId.r;
}

float4 LilHoPostSelectAovColor(float4 maskId, float4 surfaceData, float4 custom0, float4 objectCustom0, float4 objectCustom1, int source)
{
    if (source >= 4 && source <= 7)
    {
        return surfaceData;
    }

    if (source >= 8 && source <= 11)
    {
        return custom0;
    }

    if (source >= 12 && source <= 15)
    {
        return objectCustom0;
    }

    if (source >= 16 && source <= 19)
    {
        return objectCustom1;
    }

    return maskId;
}

float LilHoPostSelectAovScalar(float4 maskId, float4 surfaceData, float4 custom0, int source)
{
    return LilHoPostSelectAovScalar(maskId, surfaceData, custom0, float4(0.0, 0.0, 0.0, 0.0), float4(0.0, 0.0, 0.0, 0.0), source);
}

float4 LilHoPostSelectAovColor(float4 maskId, float4 surfaceData, float4 custom0, int source)
{
    return LilHoPostSelectAovColor(maskId, surfaceData, custom0, float4(0.0, 0.0, 0.0, 0.0), float4(0.0, 0.0, 0.0, 0.0), source);
}

#if LIL_HOPOST_AOV_RULE_GROUP_ENABLED
float LilHoPostEvaluateAovRule(
    float4 maskId,
    float4 surfaceData,
    float4 custom0,
    float4 objectCustom0,
    float4 objectCustom1,
    int source,
    int ruleOperator,
    float4 ruleValues,
    float4 matchColor)
{
    float scalar = LilHoPostSelectAovScalar(maskId, surfaceData, custom0, objectCustom0, objectCustom1, source);
    float value = LilHoPostNormalizeRuleValue(ruleValues.x, source);
    float minValue = LilHoPostNormalizeRuleValue(ruleValues.y, source);
    float maxValue = LilHoPostNormalizeRuleValue(ruleValues.z, source);
    float tolerance = max(ruleValues.w, 0.0);
    float matchTolerance = LilHoPostResolveMatchTolerance(source, tolerance);
    if (LilHoPostIsByteAovSource(source))
    {
        float scalarByte = LilHoPostDecodeByteValue(scalar);
        float valueByte = LilHoPostClampRuleByteValue(ruleValues.x);
        float minByte = LilHoPostClampRuleByteValue(ruleValues.y);
        float maxByte = LilHoPostClampRuleByteValue(ruleValues.z);

        if (ruleOperator == 1)
        {
            return scalarByte >= valueByte ? 1.0 : 0.0;
        }

        if (ruleOperator == 2)
        {
            return scalarByte > valueByte ? 1.0 : 0.0;
        }

        if (ruleOperator == 3)
        {
            return scalarByte >= valueByte ? 1.0 : 0.0;
        }

        if (ruleOperator == 4)
        {
            return scalarByte < valueByte ? 1.0 : 0.0;
        }

        if (ruleOperator == 5)
        {
            return scalarByte <= valueByte ? 1.0 : 0.0;
        }

        if (ruleOperator == 6)
        {
            return LilHoPostMatchByteValue(scalar, ruleValues.x, tolerance);
        }

        if (ruleOperator == 7)
        {
            return 1.0 - LilHoPostMatchByteValue(scalar, ruleValues.x, tolerance);
        }

        if (ruleOperator == 8)
        {
            float lowerByte = min(minByte, maxByte);
            float upperByte = max(minByte, maxByte);
            return (scalarByte >= lowerByte && scalarByte <= upperByte) ? 1.0 : 0.0;
        }
    }

    if (ruleOperator == 1)
    {
        return scalar >= value ? 1.0 : 0.0;
    }

    if (ruleOperator == 2)
    {
        return scalar > value ? 1.0 : 0.0;
    }

    if (ruleOperator == 3)
    {
        return scalar >= value ? 1.0 : 0.0;
    }

    if (ruleOperator == 4)
    {
        return scalar < value ? 1.0 : 0.0;
    }

    if (ruleOperator == 5)
    {
        return scalar <= value ? 1.0 : 0.0;
    }

    if (ruleOperator == 6)
    {
        return abs(scalar - value) <= matchTolerance ? 1.0 : 0.0;
    }

    if (ruleOperator == 7)
    {
        return abs(scalar - value) > matchTolerance ? 1.0 : 0.0;
    }

    if (ruleOperator == 8)
    {
        float lower = min(minValue, maxValue);
        float upper = max(minValue, maxValue);
        return (scalar >= lower && scalar <= upper) ? 1.0 : 0.0;
    }

    if (ruleOperator == 9)
    {
        float4 colorSample = LilHoPostSelectAovColor(maskId, surfaceData, custom0, objectCustom0, objectCustom1, source);
        float colorDistance = distance(colorSample.rgb, matchColor.rgb);
        return colorDistance <= tolerance ? 1.0 : 0.0;
    }

    if (ruleOperator == 10)
    {
        return LilHoPostFlagsAny(scalar, ruleValues.x);
    }

    if (ruleOperator == 11)
    {
        return LilHoPostFlagsAll(scalar, ruleValues.x);
    }

    return saturate(scalar);
}

float LilHoPostCombineAovRule(float mask, float ruleMask, int combine)
{
    if (combine == 1)
    {
        return max(mask, ruleMask);
    }

    if (combine == 2)
    {
        return mask * ruleMask;
    }

    if (combine == 3)
    {
        return saturate(mask - ruleMask);
    }

    if (combine == 4)
    {
        return saturate(mask + ruleMask);
    }

    if (combine == 5)
    {
        return mask * ruleMask;
    }

    return ruleMask;
}
#endif

float LilHoPostResolveLegacyAovSelection(float4 maskId, float4 surfaceData, float4 custom0, float4 objectCustom0, float4 objectCustom1)
{
    int source = (int)clamp(round(_LayerAovSource), 0.0, 19.0);
    int mode = (int)clamp(round(_LayerAovMode), 0.0, 3.0);
    float threshold = max(_LayerAovParams.x, 0.0);
    float matchValue = _LayerAovParams.z;
    float scalar = LilHoPostSelectAovScalar(maskId, surfaceData, custom0, objectCustom0, objectCustom1, source);
    float selected = saturate(scalar);

    if (mode == 1)
    {
        selected = scalar >= threshold ? 1.0 : 0.0;
    }
    else if (mode == 2)
    {
        selected = LilHoPostIsByteAovSource(source)
            ? LilHoPostMatchByteValue(scalar, matchValue, threshold)
            : LilHoPostMatchRawValue(scalar, LilHoPostNormalizeRuleValue(matchValue, source), LilHoPostResolveMatchTolerance(source, threshold));
    }
    else if (mode == 3)
    {
        float4 colorSample = LilHoPostSelectAovColor(maskId, surfaceData, custom0, objectCustom0, objectCustom1, source);
        float colorDistance = distance(colorSample.rgb, _LayerAovMatchColor.rgb);
        selected = colorDistance <= threshold ? 1.0 : 0.0;
    }

    return selected;
}

float LilHoPostResolveAovRuleGroup(float4 maskId, float4 surfaceData, float4 custom0, float4 objectCustom0, float4 objectCustom1)
{
    float coverage = saturate(maskId.r);
#if LIL_HOPOST_AOV_RULE_GROUP_ENABLED
    int ruleCount = (int)clamp(round(_LayerAovRuleCount), 0.0, (float)LIL_HOPOST_AOV_RULE_MAX);
    if (ruleCount <= 0)
    {
        return saturate(LilHoPostResolveLegacyAovSelection(maskId, surfaceData, custom0, objectCustom0, objectCustom1)) * coverage;
    }

    float mask = 0.0;
    for (int ruleIndex = 0; ruleIndex < LIL_HOPOST_AOV_RULE_MAX; ruleIndex++)
    {
        float4 ruleData0 = _LayerAovRuleData0[ruleIndex];
        float ruleActive = step((float)ruleIndex + 0.5, (float)ruleCount) * step(0.5, ruleData0.x);

        int source = (int)clamp(round(ruleData0.y), 0.0, 19.0);
        int ruleOperator = (int)clamp(round(ruleData0.z), 0.0, 11.0);
        int combine = (int)clamp(round(ruleData0.w), 0.0, 5.0);
        float ruleMask = LilHoPostEvaluateAovRule(
            maskId,
            surfaceData,
            custom0,
            objectCustom0,
            objectCustom1,
            source,
            ruleOperator,
            _LayerAovRuleData1[ruleIndex],
            _LayerAovRuleColor[ruleIndex]);

        ruleMask = saturate(ruleMask) * coverage;
        if (_LayerAovRuleData2[ruleIndex].y > 0.5)
        {
            ruleMask = saturate(coverage - ruleMask);
        }

        mask = lerp(mask, LilHoPostCombineAovRule(mask, ruleMask, combine), ruleActive);
    }

    return saturate(mask) * coverage;
#else
    return saturate(LilHoPostResolveLegacyAovSelection(maskId, surfaceData, custom0, objectCustom0, objectCustom1)) * coverage;
#endif
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

    float4 maskId = SAMPLE_TEXTURE2D_X(_lilHoAovMaskIdTexture, sampler_PointClamp, uv);
    float coverage = saturate(maskId.r);
    if (forceEnabled && _LayerAovMaskEnabled <= 0.5)
    {
        return coverage;
    }

    float4 surfaceData = SAMPLE_TEXTURE2D_X(_lilHoAovSurfaceDataTexture, sampler_PointClamp, uv);
    float4 custom0 = SAMPLE_TEXTURE2D_X(_lilHoAovCustom0_3Texture, sampler_PointClamp, uv);
    float4 objectCustom0 = SAMPLE_TEXTURE2D_X(_lilHoAovObjectCustom0_3Texture, sampler_PointClamp, uv);
    float4 objectCustom1 = SAMPLE_TEXTURE2D_X(_lilHoAovObjectCustom4_7Texture, sampler_PointClamp, uv);
    float selected = LilHoPostResolveAovRuleGroup(maskId, surfaceData, custom0, objectCustom0, objectCustom1);
    float invert = saturate(_LayerAovParams.w);
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

    return saturate(SAMPLE_TEXTURE2D_X(_lilHoAovMaskIdTexture, sampler_PointClamp, uv).r);
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
