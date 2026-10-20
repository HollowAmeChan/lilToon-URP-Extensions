#ifndef HO_ATMOSPHERE_MEDIUM_INCLUDED
#define HO_ATMOSPHERE_MEDIUM_INCLUDED
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
TEXTURE3D(_HoAtmosphereMediumTexture);
SAMPLER(sampler_HoAtmosphereMediumTexture);
float4x4 _HoAtmosphereWorldToRegion;
float4x4 _HoAtmosphereRegionToWorld;
float4 _HoAtmosphereMediumHalfSize;
float4 _HoAtmosphereMediumDensity;
float4 _HoAtmosphereMediumTint;
float4 _HoAtmosphereMediumNoise;
float4 _HoAtmosphereMediumWind;
float4 _HoAtmosphereMediumAlbedo;
float4 _HoAtmosphereMediumOptics; // anisotropy, path extinction
float _HoAtmosphereMediumTime;

struct HoAtmosphereMediumSample { float activation; float density; float3 tint; };
float HoAtmospherePhase(float cosine, float overrideG)
{
    float g = overrideG > 1 ? _HoAtmosphereMediumOptics.x : overrideG;
    g = clamp(g, -.9, .9);
    return (1 - g * g) / (4 * PI * pow(max(1e-4, 1 + g * g - 2 * g * cosine), 1.5));
}
float HoAtmosphereMediumActivation(float3 positionWS)
{
    if (_HoAtmosphereMediumDensity.w < .5) return 0;
    float3 p = mul(_HoAtmosphereWorldToRegion, float4(positionWS, 1)).xyz;
    float3 gap = _HoAtmosphereMediumHalfSize.xyz - abs(p);
    if (any(gap < 0)) return 0;
    // Distance to the closest transformed box face, in world metres (also supports shear).
    float3 inverseScale = float3(length(_HoAtmosphereWorldToRegion[0].xyz), length(_HoAtmosphereWorldToRegion[1].xyz), length(_HoAtmosphereWorldToRegion[2].xyz));
    float3 faceDistance = gap / max(inverseScale, 1e-8);
    float distanceToEdge = min(faceDistance.x, min(faceDistance.y, faceDistance.z));
    float blend = _HoAtmosphereMediumHalfSize.w;
    float edge = blend > 0 ? smoothstep(0, 1, saturate(distanceToEdge / blend)) : 1;
    return edge * _HoAtmosphereMediumDensity.y;
}
HoAtmosphereMediumSample HoAtmosphereSampleMedium(float3 positionWS, float time)
{
    HoAtmosphereMediumSample result;
    result.activation = HoAtmosphereMediumActivation(positionWS);
    result.density = _HoAtmosphereMediumDensity.x;
    result.tint = _HoAtmosphereMediumTint.rgb;
    if (_HoAtmosphereMediumNoise.z > .5)
    {
        float3 uvw = frac((positionWS - _HoAtmosphereMediumWind.xyz * time) * _HoAtmosphereMediumNoise.x);
        float noise = SAMPLE_TEXTURE3D_LOD(_HoAtmosphereMediumTexture, sampler_HoAtmosphereMediumTexture, uvw, 0).r;
        result.density *= lerp(1, saturate(noise), _HoAtmosphereMediumNoise.y);
    }
    return result;
}
#endif
