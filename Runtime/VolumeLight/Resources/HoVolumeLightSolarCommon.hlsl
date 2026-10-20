#ifndef HO_VOLUME_LIGHT_SOLAR_COMMON
#define HO_VOLUME_LIGHT_SOLAR_COMMON
// Air samples always use the light-space atlas, including SCREEN variants.
#define _SURFACE_TYPE_TRANSPARENT 1
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
#include "../../Atmosphere/Shaders/HoAtmosphereMedium.hlsl"

float4 _HoVolumeLightParams; // intensity, complete working distance, steps, anisotropy
float4 _HoVolumeLightShadow;
float4 _HoVolumeLightSampling; // jitter enabled, temporal sequence, width, height
int _HoVolumeLightDebug;
TEXTURE2D(_HoVolumeLightBlueNoise);

float VolumeShadow(float3 p)
{
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    if (_HoVolumeLightShadow.y < .5 || _HoVolumeLightDebug == 2) return 1;
    half cascade = _HoVolumeLightShadow.x > .5 ? ComputeCascadeIndex(p) : 0;
    float4 coord = float4(mul(_MainLightWorldToShadow[cascade], float4(p, 1)).xyz, 0);
    return lerp(MainLightRealtimeShadow(coord), 1, GetMainLightShadowFade(p));
    #else
    return 1;
    #endif
}
float VolumeDeviceDepth(float raw)
{
    #if UNITY_REVERSED_Z
    return raw;
    #else
    return lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
    #endif
}
struct VolumeRay { float3 origin, direction; float start, end; };
VolumeRay VolumeRayAt(float2 uv, float rawDepth, float4x4 inverseVP, float3 cameraPosition, bool ortho)
{
    #if UNITY_REVERSED_Z
    float nearDepth = 1, farDepth = 0;
    #else
    float nearDepth = UNITY_NEAR_CLIP_VALUE, farDepth = 1;
    #endif
    float3 nearWS = ComputeWorldSpacePosition(uv, nearDepth, inverseVP);
    float3 farWS = ComputeWorldSpacePosition(uv, farDepth, inverseVP);
    VolumeRay ray;
    ray.origin = ortho ? nearWS : cameraPosition;
    ray.direction = normalize(farWS - ray.origin);
    float3 surfaceWS = ComputeWorldSpacePosition(uv, VolumeDeviceDepth(rawDepth), inverseVP);
    ray.start = max(0, dot(nearWS - ray.origin, ray.direction));
    ray.end = min(_HoVolumeLightParams.y, max(0, dot(surfaceWS - ray.origin, ray.direction)));
    return ray;
}
bool VolumeInterval(inout VolumeRay ray)
{
    #if !defined(HO_VOLUME_LIGHT_LEGACY_INTERVAL)
    // Clip integration only. The light view and upstream casters stay intact.
    float3 o = mul(_HoAtmosphereWorldToRegion, float4(ray.origin, 1)).xyz;
    float3 d = mul((float3x3)_HoAtmosphereWorldToRegion, ray.direction);
    [unroll] for (int axis = 0; axis < 3; ++axis)
    {
        float extent = _HoAtmosphereMediumHalfSize[axis];
        if (abs(d[axis]) < 1e-8)
        {
            if (abs(o[axis]) > extent) return false;
        }
        else
        {
            float a = (-extent - o[axis]) / d[axis], b = (extent - o[axis]) / d[axis];
            ray.start = max(ray.start, min(a, b)); ray.end = min(ray.end, max(a, b));
        }
    }
    #endif
    return ray.end > ray.start;
}
float4 VolumeIntegrate(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    VolumeRay ray = VolumeRayAt(input.texcoord, SampleSceneDepth(input.texcoord), UNITY_MATRIX_I_VP, _WorldSpaceCameraPos, unity_OrthoParams.w > .5);
    if (!VolumeInterval(ray)) return 0;
    #if defined(HO_VOLUME_LIGHT_REFERENCE_STEPS)
    int count = HO_VOLUME_LIGHT_REFERENCE_STEPS;
    #else
    int count = (int)_HoVolumeLightParams.z;
    #endif
    float stepLength = (ray.end - ray.start) / count;
    float offset = .5;
    if (_HoVolumeLightSampling.x > .5)
    {
        uint2 pixel = (uint2)input.positionCS.xy;
        float noise = LOAD_TEXTURE2D(_HoVolumeLightBlueNoise, pixel % 64).r * (255.0 / 256.0) + (.5 / 256.0);
        offset = frac(noise + _HoVolumeLightSampling.y * .61803398875);
    }
    Light sun = GetMainLight();
    float phase = HoAtmospherePhase(dot(sun.direction, ray.direction), _HoVolumeLightParams.w);
    float3 result = 0;
    float transmission = 1;
    [loop] for (int step = 0; step < count; ++step)
    {
        float3 p = ray.origin + ray.direction * (ray.start + (step + offset) * stepLength);
        HoAtmosphereMediumSample medium = HoAtmosphereSampleMedium(p, _HoAtmosphereMediumTime);
        if (medium.activation <= 0) continue;
        float opticalDepth = medium.activation * medium.density * stepLength;
        float amount = opticalDepth;
        if (_HoAtmosphereMediumOptics.y > .5)
        {
            float stepTransmission = exp(-opticalDepth);
            amount = transmission * (opticalDepth < .001 ? opticalDepth * (1 - .5 * opticalDepth) : 1 - stepTransmission);
            transmission *= stepTransmission;
        }
        result += amount * medium.tint * _HoAtmosphereMediumAlbedo.rgb * VolumeShadow(p);
    }
    // Geometric scattering interval centre, not the opaque surface behind the fog.
    return float4(max(0, result * sun.color * phase * _HoVolumeLightParams.x), (ray.start + ray.end) * .5);
}
float4 VolumeForward(Varyings input) : SV_Target { return float4(VolumeIntegrate(input).rgb, 0); }
#endif
