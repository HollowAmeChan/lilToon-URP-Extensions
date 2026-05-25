#ifndef LIL_SCREEN_PROCESS_RULE_MASK_INCLUDED
#define LIL_SCREEN_PROCESS_RULE_MASK_INCLUDED

#ifndef LIL_SCREEN_PROCESS_RULE_MASK_GROUP_ENABLED
#define LIL_SCREEN_PROCESS_RULE_MASK_GROUP_ENABLED 1
#endif

#define LIL_SCREEN_PROCESS_RULE_MASK_MAX 4

float _HoMetadataBufferActive;
float _LayerRuleMaskEnabled;
float _LayerRuleSource;
float _LayerRuleMode;
float4 _LayerRuleParams; // x threshold/tolerance, y reserved, z match value, w final invert
float4 _LayerRuleMatchColor;
float _LayerRuleDebugOutput;
#if LIL_SCREEN_PROCESS_RULE_MASK_GROUP_ENABLED
float _LayerRuleMaskCount;
float4 _LayerRuleMaskData0[LIL_SCREEN_PROCESS_RULE_MASK_MAX]; // x enabled, y source, z operator, w combine
float4 _LayerRuleMaskData1[LIL_SCREEN_PROCESS_RULE_MASK_MAX]; // x value, y min, z max, w tolerance
float4 _LayerRuleMaskData2[LIL_SCREEN_PROCESS_RULE_MASK_MAX]; // x reserved, y invert
float4 _LayerRuleMaskColor[LIL_SCREEN_PROCESS_RULE_MASK_MAX];
#endif

TEXTURE2D_X(_HoMetadataBufferMaskIdTexture);
float4 _HoMetadataBufferMaskIdTexture_TexelSize;
TEXTURE2D_X(_HoMetadataBufferSurfaceDataTexture);
TEXTURE2D_X(_HoMetadataBufferMaterialCustom0_3Texture);
TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture);
TEXTURE2D_X(_HoMetadataBufferObjectCustom4_7Texture);

float LilScreenProcessEncodeRuleScalar(float value)
{
    return frac(abs(value) * 0.61803398875);
}

#if LIL_SCREEN_PROCESS_RULE_MASK_GROUP_ENABLED
float LilScreenProcessHasPackedBit(float value, float bitValue)
{
    return step(0.5, fmod(floor(value / bitValue), 2.0));
}

float LilScreenProcessFlagsAny(float normalizedFlags, float targetFlags)
{
    float current = floor(saturate(normalizedFlags) * 255.0 + 0.5);
    float target = floor(abs(targetFlags) + 0.5);
    float selected = 0.0;

    for (int bit = 0; bit < 8; bit++)
    {
        float bitValue = exp2((float)bit);
        selected = max(selected, LilScreenProcessHasPackedBit(current, bitValue) * LilScreenProcessHasPackedBit(target, bitValue));
    }

    return selected * step(0.5, target);
}

float LilScreenProcessFlagsAll(float normalizedFlags, float targetFlags)
{
    float current = floor(saturate(normalizedFlags) * 255.0 + 0.5);
    float target = floor(abs(targetFlags) + 0.5);
    float selected = step(0.5, target);

    for (int bit = 0; bit < 8; bit++)
    {
        float bitValue = exp2((float)bit);
        float targetBit = LilScreenProcessHasPackedBit(target, bitValue);
        float currentBit = LilScreenProcessHasPackedBit(current, bitValue);
        selected *= lerp(1.0, currentBit, targetBit);
    }

    return selected;
}
#endif

bool LilScreenProcessIsByteRuleSource(int source)
{
    return source == 1 || source == 2 || source == 3;
}

bool LilScreenProcessIsEncodedRuleSource(int source)
{
    return source == 6;
}

float LilScreenProcessDecodeByteValue(float value)
{
    return floor(saturate(value) * 255.0 + 0.5);
}

float LilScreenProcessClampRuleByteValue(float value)
{
    return clamp(round(value), 0.0, 255.0);
}

float LilScreenProcessMatchByteValue(float value, float target, float tolerance)
{
    float valueByte = LilScreenProcessDecodeByteValue(value);
    float targetByte = LilScreenProcessClampRuleByteValue(target);
    return abs(valueByte - targetByte) <= max(tolerance, 0.0) ? 1.0 : 0.0;
}

float LilScreenProcessResolveMatchTolerance(int source, float tolerance)
{
    return LilScreenProcessIsEncodedRuleSource(source) ? max(tolerance, 0.001) : max(tolerance, 0.0);
}

float LilScreenProcessNormalizeRuleValue(float value, int source)
{
    if (LilScreenProcessIsByteRuleSource(source))
    {
        return saturate(round(value) / 255.0);
    }

    if (LilScreenProcessIsEncodedRuleSource(source))
    {
        return LilScreenProcessEncodeRuleScalar(value);
    }

    return value;
}

float LilScreenProcessMatchRawValue(float value, float target, float tolerance)
{
    return abs(value - target) <= max(tolerance, 0.0) ? 1.0 : 0.0;
}

float LilScreenProcessSelectRuleScalar(float4 maskId, float4 surfaceData, float4 custom0, float4 objectCustom0, float4 objectCustom1, int source)
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

float4 LilScreenProcessSelectRuleColor(float4 maskId, float4 surfaceData, float4 custom0, float4 objectCustom0, float4 objectCustom1, int source)
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

float LilScreenProcessSelectRuleScalar(float4 maskId, float4 surfaceData, float4 custom0, int source)
{
    return LilScreenProcessSelectRuleScalar(maskId, surfaceData, custom0, float4(0.0, 0.0, 0.0, 0.0), float4(0.0, 0.0, 0.0, 0.0), source);
}

float4 LilScreenProcessSelectRuleColor(float4 maskId, float4 surfaceData, float4 custom0, int source)
{
    return LilScreenProcessSelectRuleColor(maskId, surfaceData, custom0, float4(0.0, 0.0, 0.0, 0.0), float4(0.0, 0.0, 0.0, 0.0), source);
}

#if LIL_SCREEN_PROCESS_RULE_MASK_GROUP_ENABLED
float LilScreenProcessEvaluateRuleMask(
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
    float scalar = LilScreenProcessSelectRuleScalar(maskId, surfaceData, custom0, objectCustom0, objectCustom1, source);
    float value = LilScreenProcessNormalizeRuleValue(ruleValues.x, source);
    float minValue = LilScreenProcessNormalizeRuleValue(ruleValues.y, source);
    float maxValue = LilScreenProcessNormalizeRuleValue(ruleValues.z, source);
    float tolerance = max(ruleValues.w, 0.0);
    float matchTolerance = LilScreenProcessResolveMatchTolerance(source, tolerance);
    if (LilScreenProcessIsByteRuleSource(source))
    {
        float scalarByte = LilScreenProcessDecodeByteValue(scalar);
        float valueByte = LilScreenProcessClampRuleByteValue(ruleValues.x);
        float minByte = LilScreenProcessClampRuleByteValue(ruleValues.y);
        float maxByte = LilScreenProcessClampRuleByteValue(ruleValues.z);

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
            return LilScreenProcessMatchByteValue(scalar, ruleValues.x, tolerance);
        }

        if (ruleOperator == 7)
        {
            return 1.0 - LilScreenProcessMatchByteValue(scalar, ruleValues.x, tolerance);
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
        float4 colorSample = LilScreenProcessSelectRuleColor(maskId, surfaceData, custom0, objectCustom0, objectCustom1, source);
        float colorDistance = distance(colorSample.rgb, matchColor.rgb);
        return colorDistance <= tolerance ? 1.0 : 0.0;
    }

    if (ruleOperator == 10)
    {
        return LilScreenProcessFlagsAny(scalar, ruleValues.x);
    }

    if (ruleOperator == 11)
    {
        return LilScreenProcessFlagsAll(scalar, ruleValues.x);
    }

    return saturate(scalar);
}

float LilScreenProcessCombineRuleMask(float mask, float ruleMask, int combine)
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

float LilScreenProcessResolveLegacyRuleSelection(float4 maskId, float4 surfaceData, float4 custom0, float4 objectCustom0, float4 objectCustom1)
{
    int source = (int)clamp(round(_LayerRuleSource), 0.0, 19.0);
    int mode = (int)clamp(round(_LayerRuleMode), 0.0, 3.0);
    float threshold = max(_LayerRuleParams.x, 0.0);
    float matchValue = _LayerRuleParams.z;
    float scalar = LilScreenProcessSelectRuleScalar(maskId, surfaceData, custom0, objectCustom0, objectCustom1, source);
    float selected = saturate(scalar);

    if (mode == 1)
    {
        selected = scalar >= threshold ? 1.0 : 0.0;
    }
    else if (mode == 2)
    {
        selected = LilScreenProcessIsByteRuleSource(source)
            ? LilScreenProcessMatchByteValue(scalar, matchValue, threshold)
            : LilScreenProcessMatchRawValue(scalar, LilScreenProcessNormalizeRuleValue(matchValue, source), LilScreenProcessResolveMatchTolerance(source, threshold));
    }
    else if (mode == 3)
    {
        float4 colorSample = LilScreenProcessSelectRuleColor(maskId, surfaceData, custom0, objectCustom0, objectCustom1, source);
        float colorDistance = distance(colorSample.rgb, _LayerRuleMatchColor.rgb);
        selected = colorDistance <= threshold ? 1.0 : 0.0;
    }

    return selected;
}

float LilScreenProcessResolveRuleMaskGroup(float4 maskId, float4 surfaceData, float4 custom0, float4 objectCustom0, float4 objectCustom1)
{
    float coverage = saturate(maskId.r);
#if LIL_SCREEN_PROCESS_RULE_MASK_GROUP_ENABLED
    int ruleCount = (int)clamp(round(_LayerRuleMaskCount), 0.0, (float)LIL_SCREEN_PROCESS_RULE_MASK_MAX);
    if (ruleCount <= 0)
    {
        return saturate(LilScreenProcessResolveLegacyRuleSelection(maskId, surfaceData, custom0, objectCustom0, objectCustom1)) * coverage;
    }

    float mask = 0.0;
    for (int ruleIndex = 0; ruleIndex < LIL_SCREEN_PROCESS_RULE_MASK_MAX; ruleIndex++)
    {
        float4 ruleData0 = _LayerRuleMaskData0[ruleIndex];
        float ruleActive = step((float)ruleIndex + 0.5, (float)ruleCount) * step(0.5, ruleData0.x);

        int source = (int)clamp(round(ruleData0.y), 0.0, 19.0);
        int ruleOperator = (int)clamp(round(ruleData0.z), 0.0, 11.0);
        int combine = (int)clamp(round(ruleData0.w), 0.0, 5.0);
        float ruleMask = LilScreenProcessEvaluateRuleMask(
            maskId,
            surfaceData,
            custom0,
            objectCustom0,
            objectCustom1,
            source,
            ruleOperator,
            _LayerRuleMaskData1[ruleIndex],
            _LayerRuleMaskColor[ruleIndex]);

        ruleMask = saturate(ruleMask) * coverage;
        if (_LayerRuleMaskData2[ruleIndex].y > 0.5)
        {
            ruleMask = saturate(coverage - ruleMask);
        }

        mask = lerp(mask, LilScreenProcessCombineRuleMask(mask, ruleMask, combine), ruleActive);
    }

    return saturate(mask) * coverage;
#else
    return saturate(LilScreenProcessResolveLegacyRuleSelection(maskId, surfaceData, custom0, objectCustom0, objectCustom1)) * coverage;
#endif
}

float LilScreenProcessResolveRuleMaskInternal(float2 uv, bool forceEnabled)
{
    if (!forceEnabled && _LayerRuleMaskEnabled <= 0.5)
    {
        return 1.0;
    }

    if (_HoMetadataBufferActive <= 0.5)
    {
        return 0.0;
    }

    float4 maskId = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaskIdTexture, sampler_PointClamp, uv);
    float coverage = saturate(maskId.r);
    if (forceEnabled && _LayerRuleMaskEnabled <= 0.5)
    {
        return coverage;
    }

    float4 surfaceData = SAMPLE_TEXTURE2D_X(_HoMetadataBufferSurfaceDataTexture, sampler_PointClamp, uv);
    float4 custom0 = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaterialCustom0_3Texture, sampler_PointClamp, uv);
    float4 objectCustom0 = SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture, sampler_PointClamp, uv);
    float4 objectCustom1 = SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom4_7Texture, sampler_PointClamp, uv);
    float selected = LilScreenProcessResolveRuleMaskGroup(maskId, surfaceData, custom0, objectCustom0, objectCustom1);
    float invert = saturate(_LayerRuleParams.w);
    return lerp(selected, saturate(coverage - selected), invert);
}

float LilScreenProcessResolveRuleLayerMask(float2 uv)
{
    return LilScreenProcessResolveRuleMaskInternal(uv, false);
}

float LilScreenProcessResolveRequiredRuleMask(float2 uv)
{
    return LilScreenProcessResolveRuleMaskInternal(uv, true);
}

bool LilScreenProcessShouldOutputRuleDebug()
{
    return _LayerRuleDebugOutput > 0.5;
}

half4 LilScreenProcessRuleDebugColor(float2 uv, bool forceEnabled, half alpha)
{
    half mask = (half)LilScreenProcessResolveRuleMaskInternal(uv, forceEnabled);
    return half4(mask, mask, mask, alpha);
}

float LilScreenProcessRuleCoverage(float2 uv)
{
    if (_HoMetadataBufferActive <= 0.5)
    {
        return 0.0;
    }

    return saturate(SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaskIdTexture, sampler_PointClamp, uv).r);
}

float2 LilScreenProcessRuleTexelSize()
{
    return _HoMetadataBufferMaskIdTexture_TexelSize.xy;
}

float2 LilScreenProcessRuleTextureSize()
{
    return _HoMetadataBufferMaskIdTexture_TexelSize.zw;
}

#endif
