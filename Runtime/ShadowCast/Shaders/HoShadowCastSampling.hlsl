#ifndef LILTOON_HO_SHADOW_CAST_SAMPLING_INCLUDED
#define LILTOON_HO_SHADOW_CAST_SAMPLING_INCLUDED

#define HO_SHADOW_CAST_MAX_LIGHTS 12
#define HO_SHADOW_CAST_MAX_SLICES 32
#define HO_SHADOW_CAST_LIGHT_DIRECTIONAL 0.0
#define HO_SHADOW_CAST_LIGHT_SPOT 1.0
#define HO_SHADOW_CAST_LIGHT_POINT 2.0
#define HO_SHADOW_CAST_MIN_ATTENUATION 0.35
#define HO_SHADOW_CAST_MIN_RECEIVER_ATTENUATION 0.15
#define HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_LIGHTS 4
#define HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_CASCADES 4
#define HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_SLICES 16
#define HO_SHADOW_CAST_MAX_PCSS_BLOCKER_SAMPLES 32
#define HO_SHADOW_CAST_MAX_PCSS_FILTER_SAMPLES 64

float _HoShadowCastActive;
int _HoShadowCastLightCount;
int _HoShadowCastSliceCount;
float4 _HoShadowCastAtlasSize;
float4 _HoShadowCastWorldToShadowRow0[HO_SHADOW_CAST_MAX_SLICES];
float4 _HoShadowCastWorldToShadowRow1[HO_SHADOW_CAST_MAX_SLICES];
float4 _HoShadowCastWorldToShadowRow2[HO_SHADOW_CAST_MAX_SLICES];
float4 _HoShadowCastWorldToShadowRow3[HO_SHADOW_CAST_MAX_SLICES];
float4 _HoShadowCastLightData0[HO_SHADOW_CAST_MAX_LIGHTS];
float4 _HoShadowCastLightData1[HO_SHADOW_CAST_MAX_LIGHTS];
float4 _HoShadowCastLightData2[HO_SHADOW_CAST_MAX_LIGHTS];
float4 _HoShadowCastLightAttenuation[HO_SHADOW_CAST_MAX_LIGHTS];
float4 _HoShadowCastLightColor[HO_SHADOW_CAST_MAX_LIGHTS];
float4 _HoShadowCastSliceData[HO_SHADOW_CAST_MAX_SLICES];
float4 _HoShadowCastPcssParams;
float4 _HoShadowCastPcssParams2;
float4 _HoShadowCastSecondDirectionalParams;
float4 _HoShadowCastSecondDirectionalCameraPosition;
float4 _HoShadowCastSecondDirectionalAtlasSize;
float4 _HoShadowCastSecondDirectionalPcssParams;
float4 _HoShadowCastSecondDirectionalWorldToShadowRow0[HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_SLICES];
float4 _HoShadowCastSecondDirectionalWorldToShadowRow1[HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_SLICES];
float4 _HoShadowCastSecondDirectionalWorldToShadowRow2[HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_SLICES];
float4 _HoShadowCastSecondDirectionalWorldToShadowRow3[HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_SLICES];
float4 _HoShadowCastSecondDirectionalLightData[HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_LIGHTS];
float4 _HoShadowCastSecondDirectionalSliceData[HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_SLICES];

TEXTURE2D_FLOAT(_HoShadowCastAtlas);
TEXTURE2D_FLOAT(_HoShadowCastSecondDirectionalAtlas);

float4 HoShadowCastTransformWorldToShadow(int sliceIndex, float3 positionWS)
{
    float4 position = float4(positionWS, 1.0);
    return float4(
        dot(_HoShadowCastWorldToShadowRow0[sliceIndex], position),
        dot(_HoShadowCastWorldToShadowRow1[sliceIndex], position),
        dot(_HoShadowCastWorldToShadowRow2[sliceIndex], position),
        dot(_HoShadowCastWorldToShadowRow3[sliceIndex], position));
}

float4 HoShadowCastTransformWorldToSecondDirectionalShadow(int sliceIndex, float3 positionWS)
{
    float4 position = float4(positionWS, 1.0);
    return float4(
        dot(_HoShadowCastSecondDirectionalWorldToShadowRow0[sliceIndex], position),
        dot(_HoShadowCastSecondDirectionalWorldToShadowRow1[sliceIndex], position),
        dot(_HoShadowCastSecondDirectionalWorldToShadowRow2[sliceIndex], position),
        dot(_HoShadowCastSecondDirectionalWorldToShadowRow3[sliceIndex], position));
}

float HoShadowCastLightInfluence(int lightIndex, float3 positionWS)
{
    float4 lightData0 = _HoShadowCastLightData0[lightIndex];
    float lightType = lightData0.x;

    if (lightType == HO_SHADOW_CAST_LIGHT_DIRECTIONAL)
    {
        return 1.0;
    }

    float4 lightData1 = _HoShadowCastLightData1[lightIndex];
    float3 lightPositionWS = lightData1.xyz;
    float3 lightToReceiver = positionWS - lightPositionWS;
    float distanceSqr = dot(lightToReceiver, lightToReceiver);
    float4 lightAttenuation = _HoShadowCastLightAttenuation[lightIndex];
    float rangeFactor = saturate(distanceSqr * lightAttenuation.x);
    if (rangeFactor >= 1.0)
    {
        return 0.0;
    }

    float rangeFade = saturate(1.0 - rangeFactor * rangeFactor);
    rangeFade *= rangeFade;
    rangeFade = pow(rangeFade, max(lightAttenuation.y, 0.001));

    if (lightType == HO_SHADOW_CAST_LIGHT_POINT)
    {
        return rangeFade;
    }

    float3 spotDirectionWS = normalize(_HoShadowCastLightData2[lightIndex].xyz);
    float receiverCosAngle = dot(lightToReceiver * rsqrt(max(distanceSqr, 0.000001)), spotDirectionWS);
    float2 spotAttenuation = lightAttenuation.zw;
    float spotFade = saturate(receiverCosAngle * spotAttenuation.x + spotAttenuation.y);
    return rangeFade * spotFade * spotFade;
}

int HoShadowCastPointFaceIndex(float3 lightToReceiver)
{
    float3 absDirection = abs(lightToReceiver);
    if (absDirection.x >= absDirection.y && absDirection.x >= absDirection.z)
    {
        return lightToReceiver.x >= 0.0 ? 0 : 1;
    }

    if (absDirection.y >= absDirection.x && absDirection.y >= absDirection.z)
    {
        return lightToReceiver.y >= 0.0 ? 2 : 3;
    }

    return lightToReceiver.z >= 0.0 ? 4 : 5;
}

float HoShadowCastCompareDepth(float rawDepth, float receiverDepth, float bias)
{
    if (rawDepth >= 0.99999)
    {
        return 1.0;
    }

#if UNITY_REVERSED_Z
    return rawDepth <= receiverDepth + bias ? 1.0 : 0.0;
#else
    return rawDepth >= receiverDepth - bias ? 1.0 : 0.0;
#endif
}

bool HoShadowCastIsBlocker(float rawDepth, float receiverDepth, float bias)
{
    if (rawDepth >= 0.99999)
    {
        return false;
    }

#if UNITY_REVERSED_Z
    return rawDepth > receiverDepth + bias;
#else
    return rawDepth < receiverDepth - bias;
#endif
}

float HoShadowCastPcssRotation(float2 atlasUv, float2 atlasSize)
{
    float2 pixel = floor(atlasUv * atlasSize);
    return frac(sin(dot(pixel, float2(12.9898, 78.233))) * 43758.5453) * 6.2831853;
}

float2 HoShadowCastPcssOffset(int index, int sampleCount, float rotation)
{
    float sampleIndex = (float)index + 0.5;
    float radius = sqrt(sampleIndex / max((float)sampleCount, 1.0));
    float angle = sampleIndex * 2.39996323 + rotation;
    return float2(cos(angle), sin(angle)) * radius;
}

float HoShadowCastSampleRawDepth(float2 atlasUv, bool secondDirectional)
{
    return secondDirectional
        ? SAMPLE_TEXTURE2D_LOD(_HoShadowCastSecondDirectionalAtlas, sampler_PointClamp, atlasUv, 0).r
        : SAMPLE_TEXTURE2D_LOD(_HoShadowCastAtlas, sampler_PointClamp, atlasUv, 0).r;
}

float HoShadowCastSampleManualPcf(float3 sliceCoord, float4 sliceData, float4 atlasSize, float4 pcssParams2, bool secondDirectional)
{
    float2 texelSize = atlasSize.zw;
    float2 atlasMin = sliceData.xy + texelSize * 0.5;
    float2 atlasMax = sliceData.xy + sliceData.zz - texelSize * 0.5;
    if (any(sliceCoord.xy < 0.0) || any(sliceCoord.xy > 1.0))
    {
        return 1.0;
    }

    float2 atlasUv = sliceData.xy + sliceCoord.xy * sliceData.z;
    float result = 0.0;
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float2 sampleUv = clamp(atlasUv + float2(x, y) * texelSize, atlasMin, atlasMax);
            result += HoShadowCastCompareDepth(HoShadowCastSampleRawDepth(sampleUv, secondDirectional), sliceCoord.z, pcssParams2.x);
        }
    }

    return result * (1.0 / 9.0);
}

float HoShadowCastSamplePcss(float3 sliceCoord, float4 sliceData, float4 atlasSize, float4 pcssParams, float4 pcssParams2, bool secondDirectional, float radiusScale)
{
    float2 texelSize = atlasSize.zw;
    float2 atlasMin = sliceData.xy + texelSize * 0.5;
    float2 atlasMax = sliceData.xy + sliceData.zz - texelSize * 0.5;
    if (any(sliceCoord.xy < 0.0) || any(sliceCoord.xy > 1.0))
    {
        return 1.0;
    }

    float2 atlasUv = sliceData.xy + sliceCoord.xy * sliceData.z;
#if UNITY_REVERSED_Z
    float nearDepthFactor = 1.0 - sliceCoord.z;
#else
    float nearDepthFactor = sliceCoord.z;
#endif
    float nearPlaneRadiusScale = smoothstep(0.02, 0.12, nearDepthFactor);
    radiusScale = saturate(radiusScale) * nearPlaneRadiusScale;
    if (pcssParams.x < 0.5 || pcssParams.y <= 0.0 || radiusScale <= 0.001)
    {
        return HoShadowCastSampleManualPcf(sliceCoord, sliceData, atlasSize, pcssParams2, secondDirectional);
    }

    int blockerSampleCount = min((int)round(pcssParams2.y), HO_SHADOW_CAST_MAX_PCSS_BLOCKER_SAMPLES);
    int filterSampleCount = min((int)round(pcssParams2.z), HO_SHADOW_CAST_MAX_PCSS_FILTER_SAMPLES);
    if (blockerSampleCount <= 0 || filterSampleCount <= 0)
    {
        return HoShadowCastSampleManualPcf(sliceCoord, sliceData, atlasSize, pcssParams2, secondDirectional);
    }

    float rotation = HoShadowCastPcssRotation(atlasUv, atlasSize.xy);
    float blockerDepthSum = 0.0;
    int blockerCount = 0;
    float blockerRadius = max(pcssParams.z, 0.0) * radiusScale;
    [loop]
    for (int blockerIndex = 0; blockerIndex < HO_SHADOW_CAST_MAX_PCSS_BLOCKER_SAMPLES; blockerIndex++)
    {
        if (blockerIndex >= blockerSampleCount)
        {
            break;
        }

        float2 sampleUv = clamp(atlasUv + HoShadowCastPcssOffset(blockerIndex, blockerSampleCount, rotation) * texelSize * blockerRadius, atlasMin, atlasMax);
        float rawDepth = HoShadowCastSampleRawDepth(sampleUv, secondDirectional);
        if (HoShadowCastIsBlocker(rawDepth, sliceCoord.z, pcssParams2.x))
        {
            blockerDepthSum += rawDepth;
            blockerCount++;
        }
    }

    if (blockerCount <= 0)
    {
        return HoShadowCastSampleManualPcf(sliceCoord, sliceData, atlasSize, pcssParams2, secondDirectional);
    }

    float averageBlockerDepth = blockerDepthSum / (float)blockerCount;
#if UNITY_REVERSED_Z
    float penumbra = saturate((averageBlockerDepth - sliceCoord.z) / max(sliceCoord.z, 0.0001));
#else
    float penumbra = saturate((sliceCoord.z - averageBlockerDepth) / max(averageBlockerDepth, 0.0001));
#endif
    float filterRadius = min(max(pcssParams.w, 0.0), pcssParams.y * pcssParams.w * penumbra) * radiusScale;
    if (filterRadius <= 0.001)
    {
        return HoShadowCastCompareDepth(HoShadowCastSampleRawDepth(atlasUv, secondDirectional), sliceCoord.z, pcssParams2.x);
    }

    float result = 0.0;
    [loop]
    for (int filterIndex = 0; filterIndex < HO_SHADOW_CAST_MAX_PCSS_FILTER_SAMPLES; filterIndex++)
    {
        if (filterIndex >= filterSampleCount)
        {
            break;
        }

        float2 sampleUv = clamp(atlasUv + HoShadowCastPcssOffset(filterIndex, filterSampleCount, rotation + 1.731) * texelSize * filterRadius, atlasMin, atlasMax);
        result += HoShadowCastCompareDepth(HoShadowCastSampleRawDepth(sampleUv, secondDirectional), sliceCoord.z, pcssParams2.x);
    }

    return result / (float)filterSampleCount;
}

float HoShadowCastSampleAtlas(float3 sliceCoord, float4 sliceData)
{
    return HoShadowCastSamplePcss(sliceCoord, sliceData, _HoShadowCastAtlasSize, _HoShadowCastPcssParams, _HoShadowCastPcssParams2, false, 1.0);
}

float HoShadowCastSampleAtlasScaled(float3 sliceCoord, float4 sliceData, float radiusScale)
{
    return HoShadowCastSamplePcss(sliceCoord, sliceData, _HoShadowCastAtlasSize, _HoShadowCastPcssParams, _HoShadowCastPcssParams2, false, radiusScale);
}

float HoShadowCastSampleSecondDirectionalAtlas(float3 sliceCoord, float4 sliceData)
{
    return HoShadowCastSamplePcss(sliceCoord, sliceData, _HoShadowCastSecondDirectionalAtlasSize, _HoShadowCastSecondDirectionalPcssParams, _HoShadowCastPcssParams2, true, 1.0);
}

float HoShadowCastSampleSliceScaled(int sliceIndex, float3 positionWS, float radiusScale)
{
    if (sliceIndex < 0 || sliceIndex >= _HoShadowCastSliceCount || sliceIndex >= HO_SHADOW_CAST_MAX_SLICES)
    {
        return 1.0;
    }

    float4 shadowCoord = HoShadowCastTransformWorldToShadow(sliceIndex, positionWS);
    if (abs(shadowCoord.w) <= 0.00001)
    {
        return 1.0;
    }

    shadowCoord.xyz /= shadowCoord.w;

    if (shadowCoord.z <= 0.0 || shadowCoord.z >= 1.0)
    {
        return 1.0;
    }

    float4 sliceData = _HoShadowCastSliceData[sliceIndex];
    return HoShadowCastSampleAtlasScaled(shadowCoord.xyz, sliceData, radiusScale);
}

float HoShadowCastSampleSlice(int sliceIndex, float3 positionWS)
{
    return HoShadowCastSampleSliceScaled(sliceIndex, positionWS, 1.0);
}

float HoShadowCastSampleSecondDirectionalSlice(int sliceIndex, float3 positionWS)
{
    int lightCount = min((int)round(_HoShadowCastSecondDirectionalParams.y), HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_LIGHTS);
    int cascadeCount = min((int)round(_HoShadowCastSecondDirectionalParams.z), HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_CASCADES);
    int sliceCount = min(lightCount * cascadeCount, HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_SLICES);
    if (sliceIndex < 0 || sliceIndex >= sliceCount)
    {
        return 1.0;
    }

    float4 shadowCoord = HoShadowCastTransformWorldToSecondDirectionalShadow(sliceIndex, positionWS);
    if (abs(shadowCoord.w) <= 0.00001)
    {
        return 1.0;
    }

    shadowCoord.xyz /= shadowCoord.w;

    if (shadowCoord.z <= 0.0 || shadowCoord.z >= 1.0)
    {
        return 1.0;
    }

    float4 sliceData = _HoShadowCastSecondDirectionalSliceData[sliceIndex];
    return HoShadowCastSampleSecondDirectionalAtlas(shadowCoord.xyz, sliceData);
}

float HoShadowCastSecondDirectionalAttenuation(float3 positionWS)
{
    if (_HoShadowCastSecondDirectionalParams.x < 0.5)
    {
        return 1.0;
    }

    int lightCount = min((int)round(_HoShadowCastSecondDirectionalParams.y), HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_LIGHTS);
    int cascadeCount = min((int)round(_HoShadowCastSecondDirectionalParams.z), HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_CASCADES);
    if (lightCount <= 0 || cascadeCount <= 0)
    {
        return 1.0;
    }

    float3 cameraToReceiver = positionWS - _HoShadowCastSecondDirectionalCameraPosition.xyz;
    float distanceSqr = dot(cameraToReceiver, cameraToReceiver);
    float attenuation = 1.0;
    [loop]
    for (int lightIndex = 0; lightIndex < lightCount; lightIndex++)
    {
        float4 lightData = _HoShadowCastSecondDirectionalLightData[lightIndex];
        int firstSlice = (int)round(lightData.x);
        int lightCascadeCount = min((int)round(lightData.y), cascadeCount);
        float shadowStrength = saturate(lightData.z);
        int cascadeIndex = max(lightCascadeCount - 1, 0);
        [unroll]
        for (int i = 0; i < HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_CASCADES; i++)
        {
            if (i >= lightCascadeCount)
            {
                break;
            }

            if (distanceSqr <= _HoShadowCastSecondDirectionalSliceData[firstSlice + i].w)
            {
                cascadeIndex = i;
                break;
            }
        }

        float lightShadow = max(HoShadowCastSampleSecondDirectionalSlice(firstSlice + cascadeIndex, positionWS), HO_SHADOW_CAST_MIN_ATTENUATION);
        attenuation *= lerp(1.0, lightShadow, shadowStrength);
    }

    return saturate(attenuation);
}

float HoShadowCastLightShadowAttenuation(int lightIndex, float3 positionWS)
{
    float influence = HoShadowCastLightInfluence(lightIndex, positionWS);
    if (influence <= 0.0)
    {
        return 1.0;
    }

    float4 lightData0 = _HoShadowCastLightData0[lightIndex];
    int firstSlice = (int)round(lightData0.y);
    float lightType = lightData0.x;
    int sliceCount = min((int)round(lightData0.z), 6);
    float shadowStrength = saturate(lightData0.w * influence);
    float lightShadow = 1.0;

    if (lightType == HO_SHADOW_CAST_LIGHT_POINT && sliceCount >= 6)
    {
        float3 lightPositionWS = _HoShadowCastLightData1[lightIndex].xyz;
        float lightRange = max(_HoShadowCastLightData1[lightIndex].w, 0.0001);
        float3 lightToReceiver = positionWS - lightPositionWS;
        float pointDistance = length(lightToReceiver);
        float pointNearRadiusScale = smoothstep(0.10, 0.35, pointDistance / lightRange);
        int faceIndex = HoShadowCastPointFaceIndex(lightToReceiver);
        lightShadow = HoShadowCastSampleSliceScaled(firstSlice + faceIndex, positionWS, pointNearRadiusScale);
    }
    else
    {
        [loop]
        for (int sliceOffset = 0; sliceOffset < sliceCount; sliceOffset++)
        {
            lightShadow = min(lightShadow, HoShadowCastSampleSlice(firstSlice + sliceOffset, positionWS));
        }
    }

    lightShadow = max(lightShadow, HO_SHADOW_CAST_MIN_ATTENUATION);
    return lerp(1.0, lightShadow, shadowStrength);
}

float HoShadowCastDirectionalAttenuation(float3 positionWS)
{
    if (_HoShadowCastActive < 0.5 || _HoShadowCastLightCount <= 0 || _HoShadowCastSliceCount <= 0)
    {
        return 1.0;
    }

    float attenuation = 1.0;
    int lightCount = min(_HoShadowCastLightCount, HO_SHADOW_CAST_MAX_LIGHTS);
    [loop]
    for (int lightIndex = 0; lightIndex < lightCount; lightIndex++)
    {
        if (_HoShadowCastLightData0[lightIndex].x != HO_SHADOW_CAST_LIGHT_DIRECTIONAL)
        {
            continue;
        }

        attenuation *= HoShadowCastLightShadowAttenuation(lightIndex, positionWS);
    }

    return saturate(attenuation);
}

float HoShadowCastPunctualAttenuation(float3 positionWS)
{
    if (_HoShadowCastActive < 0.5 || _HoShadowCastLightCount <= 0 || _HoShadowCastSliceCount <= 0)
    {
        return 1.0;
    }

    float attenuation = 1.0;
    int lightCount = min(_HoShadowCastLightCount, HO_SHADOW_CAST_MAX_LIGHTS);
    [loop]
    for (int lightIndex = 0; lightIndex < lightCount; lightIndex++)
    {
        if (_HoShadowCastLightData0[lightIndex].x == HO_SHADOW_CAST_LIGHT_DIRECTIONAL)
        {
            continue;
        }

        attenuation *= HoShadowCastLightShadowAttenuation(lightIndex, positionWS);
    }

    return saturate(attenuation);
}

float HoShadowCastAttenuation(float3 positionWS)
{
    float attenuation = HoShadowCastDirectionalAttenuation(positionWS) * HoShadowCastPunctualAttenuation(positionWS) * HoShadowCastSecondDirectionalAttenuation(positionWS);
    return max(attenuation, HO_SHADOW_CAST_MIN_RECEIVER_ATTENUATION);
}

#endif
