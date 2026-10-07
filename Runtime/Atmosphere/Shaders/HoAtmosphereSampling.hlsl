#ifndef HO_ATMOSPHERE_SAMPLING_INCLUDED
#define HO_ATMOSPHERE_SAMPLING_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

TEXTURECUBE(_HoAtmosphereRadianceTexture);
SAMPLER(sampler_HoAtmosphereRadianceTexture);
float4 _HoAtmosphereParameters;
float4 _HoAtmosphereTint;
float4 _HoAtmosphereConstantColor;

bool HoAtmosphereAvailable() { return _HoAtmosphereParameters.w > 0.5; }

float3 HoAtmosphereSampleRadiance(float3 directionWS)
{
    if (!HoAtmosphereAvailable()) return 0;
    float len2 = dot(directionWS, directionWS);
    if (len2 < 1e-12) return 0;
    directionWS *= rsqrt(len2);
    float s = _HoAtmosphereParameters.x;
    float c = _HoAtmosphereParameters.y;
    directionWS.xz = float2(c * directionWS.x - s * directionWS.z, s * directionWS.x + c * directionWS.z);
    float3 radiance;
    if (_HoAtmosphereConstantColor.a > 0.5)
        radiance = _HoAtmosphereConstantColor.rgb;
    else
        radiance = SAMPLE_TEXTURECUBE_LOD(_HoAtmosphereRadianceTexture, sampler_HoAtmosphereRadianceTexture, directionWS, 0).rgb;
    return max(radiance, 0) * _HoAtmosphereTint.rgb * _HoAtmosphereParameters.z;
}
#endif
