#ifndef LIL_HO_CHARACTER_CAPTURE_COMMON_INCLUDED
#define LIL_HO_CHARACTER_CAPTURE_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Material capture passes should keep their own alpha/cutout/dissolve rules,
// use Tags { "LightMode" = "HoCharacterCapture" }, and normally use
// Blend One OneMinusSrcAlpha with the premultiplied outputs below so
// semi-transparent eyes and brows accumulate consistently.
#ifndef LIL_HO_CHARACTER_CAPTURE_HAS_CAPTURE_MODE
float _HoCharacterCaptureMode;
#endif

#ifndef LIL_HO_CHARACTER_CAPTURE_HAS_AOV_PROPERTIES
float _HoAovObjectCustomMask;
float _HoMetadataBufferGroupId;
#endif

struct LilHoCharacterCaptureOutput
{
    half4 eyeColor : SV_Target0;
    half4 eyeData : SV_Target1;
};

float LilHoCharacterCaptureByteToNormalized(float value)
{
    return saturate(round(clamp(value, 0.0, 255.0)) / 255.0);
}

float LilHoCharacterCaptureHasObjectBit(uint mask, uint bitIndex)
{
    return (float)((mask >> bitIndex) & 1u);
}

uint LilHoCharacterCaptureObjectMask()
{
    uint rendererUserValue = unity_RendererUserValue;
    if (rendererUserValue != 0u)
    {
        return rendererUserValue & 255u;
    }

    return (uint)round(saturate(_HoAovObjectCustomMask / 255.0) * 255.0);
}

float LilHoCharacterCaptureCharacterId()
{
    uint rendererUserValue = unity_RendererUserValue;
    if (rendererUserValue != 0u)
    {
        return (float)((rendererUserValue >> 8u) & 255u);
    }

    return _HoMetadataBufferGroupId;
}

float LilHoCharacterCaptureShouldDraw()
{
    uint objectMask = LilHoCharacterCaptureObjectMask();
    float isFace = LilHoCharacterCaptureHasObjectBit(objectMask, 1u);
    float isEye = LilHoCharacterCaptureHasObjectBit(objectMask, 3u);
    float faceMode = 1.0 - step(0.5, abs(_HoCharacterCaptureMode - 1.0));
    float eyeMode = 1.0 - step(0.5, abs(_HoCharacterCaptureMode - 2.0));
    return saturate(faceMode * isFace + eyeMode * isEye);
}

LilHoCharacterCaptureOutput LilHoCharacterBuildCaptureOutput(float4 color, float positionCSZ, float captureOpacity)
{
    float drawWeight = LilHoCharacterCaptureShouldDraw();
    clip(drawWeight - 0.5);

    float alpha = saturate(color.a);
    float captureAlpha = alpha * saturate(captureOpacity);
    float isFaceMode = 1.0 - step(0.5, abs(_HoCharacterCaptureMode - 1.0));
    float isEyeMode = 1.0 - step(0.5, abs(_HoCharacterCaptureMode - 2.0));
    float isEyeColorMode = saturate(isFaceMode + isEyeMode);
    float linearDepth = LinearEyeDepth(positionCSZ, _ZBufferParams);
    float characterId = LilHoCharacterCaptureByteToNormalized(LilHoCharacterCaptureCharacterId());

    LilHoCharacterCaptureOutput output;
    output.eyeColor = half4(lerp(color.rgb, color.rgb * alpha, isEyeMode) * isEyeColorMode, lerp(1.0, alpha, isEyeMode) * isEyeColorMode);
    output.eyeData = half4(isEyeMode * captureAlpha, linearDepth * captureAlpha * isEyeMode, characterId * captureAlpha * isEyeMode, captureAlpha * isEyeMode);
    return output;
}

LilHoCharacterCaptureOutput LilHoCharacterBuildCaptureOutput(float4 color, float positionCSZ)
{
    return LilHoCharacterBuildCaptureOutput(color, positionCSZ, 1.0);
}

#endif
