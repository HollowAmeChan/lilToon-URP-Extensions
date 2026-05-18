#ifndef LILTOON_HO_SHADOW_CAST_SAMPLING_INCLUDED
#define LILTOON_HO_SHADOW_CAST_SAMPLING_INCLUDED

#define HO_SHADOW_CAST_MAX_LIGHTS 12
#define HO_SHADOW_CAST_MAX_SLICES 32
#define HO_SHADOW_CAST_LIGHT_DIRECTIONAL 0.0
#define HO_SHADOW_CAST_LIGHT_SPOT 1.0
#define HO_SHADOW_CAST_LIGHT_POINT 2.0
#define HO_SHADOW_CAST_MIN_ATTENUATION 0.35
#define HO_SHADOW_CAST_MIN_RECEIVER_ATTENUATION 0.15

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

TEXTURE2D_SHADOW(_HoShadowCastAtlas);

float4 HoShadowCastTransformWorldToShadow(int sliceIndex, float3 positionWS)
{
    float4 position = float4(positionWS, 1.0);
    return float4(
        dot(_HoShadowCastWorldToShadowRow0[sliceIndex], position),
        dot(_HoShadowCastWorldToShadowRow1[sliceIndex], position),
        dot(_HoShadowCastWorldToShadowRow2[sliceIndex], position),
        dot(_HoShadowCastWorldToShadowRow3[sliceIndex], position));
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
    float rangeFactor = saturate(distanceSqr * _HoShadowCastLightAttenuation[lightIndex].x);
    if (rangeFactor >= 1.0)
    {
        return 0.0;
    }

    float rangeFade = saturate(1.0 - rangeFactor * rangeFactor);
    rangeFade *= rangeFade;

    if (lightType == HO_SHADOW_CAST_LIGHT_POINT)
    {
        return rangeFade;
    }

    float3 spotDirectionWS = normalize(_HoShadowCastLightData2[lightIndex].xyz);
    float receiverCosAngle = dot(lightToReceiver * rsqrt(max(distanceSqr, 0.000001)), spotDirectionWS);
    float2 spotAttenuation = _HoShadowCastLightAttenuation[lightIndex].zw;
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

float HoShadowCastSampleAtlas(float3 sliceCoord, float4 sliceData)
{
    float2 texelSize = _HoShadowCastAtlasSize.zw;
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
            result += SAMPLE_TEXTURE2D_SHADOW(_HoShadowCastAtlas, sampler_LinearClampCompare, float3(sampleUv, sliceCoord.z));
        }
    }

    return result * (1.0 / 9.0);
}

float HoShadowCastSampleSlice(int sliceIndex, float3 positionWS)
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
    return HoShadowCastSampleAtlas(shadowCoord.xyz, sliceData);
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
        int faceIndex = HoShadowCastPointFaceIndex(positionWS - lightPositionWS);
        lightShadow = HoShadowCastSampleSlice(firstSlice + faceIndex, positionWS);
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
    float attenuation = HoShadowCastDirectionalAttenuation(positionWS) * HoShadowCastPunctualAttenuation(positionWS);
    return max(attenuation, HO_SHADOW_CAST_MIN_RECEIVER_ATTENUATION);
}

#endif
