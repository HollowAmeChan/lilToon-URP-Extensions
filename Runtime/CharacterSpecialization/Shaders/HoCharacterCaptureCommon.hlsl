#ifndef LIL_HO_CHARACTER_CAPTURE_COMMON_INCLUDED
#define LIL_HO_CHARACTER_CAPTURE_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoObjectBufferIdPass.hlsl"

// Material capture passes should keep their own cutout/dissolve rules and use
// Tags { "LightMode" = "HoCharacterCapture" }. Material alpha is intentionally
// not used as capture coverage; OB identity regions define full capture areas.
#ifndef LIL_HO_CHARACTER_CAPTURE_HAS_CAPTURE_MODE
float _HoCharacterCaptureMode;
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

// 角色语义只在 OB 里：RSUV 上的 16 bit 就是这个 renderer 的 partId（组 8 + 槽位 8），
// 部件行表里存着它的**标签位掩码**。所以"这个 pass 该不该画"= 该部件有没有对应标签，
// 角色 ID = 同一个 partId 的组字节 —— 与屏幕空间那边（OB 身份池层 0 的组字节）同一套编号。
// OB 不在 renderer 里时表是空的：标签恒 0 ⇒ 整支不画（这是正确行为，不是退化）。
uint LilHoCharacterCaptureTags()
{
    return HoObjectBufferLoadPart(unity_RendererUserValue).tags;
}

float LilHoCharacterCaptureHasTag(uint tags, uint tagBit)
{
    return (tags & tagBit) != 0u ? 1.0 : 0.0;
}

float LilHoCharacterCaptureCharacterId()
{
    // HoObjectBufferGroup 写进 RSUV 的就是 partId = group << 8 | slot。
    return (float)HoObjectBufferGroupId(unity_RendererUserValue);
}

float LilHoCharacterCaptureShouldDraw()
{
    uint tags = LilHoCharacterCaptureTags();
    float isFace = LilHoCharacterCaptureHasTag(tags, HO_OBJECT_TAG_FACE);
    float isEye = LilHoCharacterCaptureHasTag(tags, HO_OBJECT_TAG_EYE);
    float faceMode = 1.0 - step(0.5, abs(_HoCharacterCaptureMode - 1.0));
    float eyeMode = 1.0 - step(0.5, abs(_HoCharacterCaptureMode - 2.0));
    return saturate(faceMode * isFace + eyeMode * isEye);
}

LilHoCharacterCaptureOutput LilHoCharacterBuildCaptureOutput(float4 color, float positionCSZ, float captureOpacity)
{
    float drawWeight = LilHoCharacterCaptureShouldDraw();
    clip(drawWeight - 0.5);

    float captureAlpha = saturate(captureOpacity);
    float isFaceMode = 1.0 - step(0.5, abs(_HoCharacterCaptureMode - 1.0));
    float isEyeMode = 1.0 - step(0.5, abs(_HoCharacterCaptureMode - 2.0));
    float isEyeColorMode = saturate(isFaceMode + isEyeMode);
    float linearDepth = LinearEyeDepth(positionCSZ, _ZBufferParams);
    float characterId = LilHoCharacterCaptureByteToNormalized(LilHoCharacterCaptureCharacterId());

    LilHoCharacterCaptureOutput output;
    float colorAlpha = lerp(1.0, captureAlpha, isEyeMode) * isEyeColorMode;
    output.eyeColor = half4(color.rgb * colorAlpha, colorAlpha);
    output.eyeData = half4(isEyeMode * captureAlpha, linearDepth * captureAlpha * isEyeMode, characterId * captureAlpha * isEyeMode, captureAlpha * isEyeMode);
    return output;
}

LilHoCharacterCaptureOutput LilHoCharacterBuildCaptureOutput(float4 color, float positionCSZ)
{
    return LilHoCharacterBuildCaptureOutput(color, positionCSZ, 1.0);
}

#endif
