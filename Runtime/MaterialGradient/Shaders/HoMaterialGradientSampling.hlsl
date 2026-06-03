#ifndef HO_MATERIAL_GRADIENT_SAMPLING_INCLUDED
#define HO_MATERIAL_GRADIENT_SAMPLING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

float2 HoGradientUv(float t)
{
    return float2(saturate(t), 0.5);
}

half4 HoSampleGradient(TEXTURE2D_PARAM(gradientTexture, sampler_gradientTexture), float t)
{
    return SAMPLE_TEXTURE2D(gradientTexture, sampler_gradientTexture, HoGradientUv(t));
}

half3 HoSampleGradientRgb(TEXTURE2D_PARAM(gradientTexture, sampler_gradientTexture), float t)
{
    return HoSampleGradient(TEXTURE2D_ARGS(gradientTexture, sampler_gradientTexture), t).rgb;
}

half HoSampleGradientAlpha(TEXTURE2D_PARAM(gradientTexture, sampler_gradientTexture), float t)
{
    return HoSampleGradient(TEXTURE2D_ARGS(gradientTexture, sampler_gradientTexture), t).a;
}

#endif
