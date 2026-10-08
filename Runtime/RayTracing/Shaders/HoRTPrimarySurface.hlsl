#ifndef HO_RT_PRIMARY_SURFACE_INCLUDED
#define HO_RT_PRIMARY_SURFACE_INCLUDED
#include "../../SurfaceBuffer/Shaders/HoSurfaceAssociation.hlsl"
#include "../../SurfaceBuffer/Shaders/HoSurfaceBufferCommon.hlsl"

TEXTURE2D_X(_HoRTPrimaryColor);
TEXTURE2D_X(_HoRTPrimaryNormal);
TEXTURE2D_X(_HoRTPrimaryMaterial);
TEXTURE2D_X(_HoRTPrimaryReflection);
TEXTURE2D_X(_HoRTPrimaryOwner);
float4 _HoRTPrimaryState; // attributes, supported camera, optional SB owner, reserved
float4 _HoRTPrimarySize;
float4x4 _HoRTPrimaryInverseViewProjection;
float4 _HoRTPrimaryCameraPosition;
float4 _HoRTPrimaryTowardsEye;
float4 _HoRTPrimaryZBufferParameters;
float4 _HoRTPrimaryClipParameters;

#define HO_RT_PRIMARY_UNSUPPORTED_CAMERA 7u
#define HO_RT_PRIMARY_MISSING_ATTRIBUTES 8u
#define HO_RT_PRIMARY_INVALID_DATA 9u

struct HoRTPrimarySurface
{
    uint status;
    float3 positionWS;
    float3 geometricNormalWS;
    float3 shadingNormalWS;
    float3 viewDirectionWS;
    float eyeDepth;
    float3 baseColor; // raw linear HDR SB main colour, not automatically a physical albedo
    float perceptualRoughness;
    float metallic;
    float reflectance;
    float3 emission;
    uint ownerAvailable;
    uint owner; // SB identity, not RT instance/primitive identity
};

bool HoRTPrimaryFinite3(float3 v) { return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z); }

bool HoRTTryLoadPrimarySurface(uint2 pixel, out HoRTPrimarySurface surface)
{
    surface = (HoRTPrimarySurface)0;
    if (_HoSurfaceAssociationState.x < .5 || _HoSurfaceAssociationState.y < .5) return false;
    if (any(pixel >= (uint2)_HoRTPrimarySize.xy)) { surface.status = HO_SURFACE_ASSOCIATION_OUT_OF_BOUNDS; return false; }
    if (_HoRTPrimaryState.y < .5) { surface.status = HO_RT_PRIMARY_UNSUPPORTED_CAMERA; return false; }
    float2 uv = (float2(pixel) + .5) * _HoRTPrimarySize.zw;
    float eyeDepth;
    surface.status = HoSurfaceAssociationStatus(uv, _HoRTPrimaryZBufferParameters, _HoRTPrimaryClipParameters, eyeDepth);
    if (surface.status != HO_SURFACE_ASSOCIATION_MATCH) return false;
    if (_HoRTPrimaryState.x < .5) { surface.status = HO_RT_PRIMARY_MISSING_ATTRIBUTES; return false; }

    HoRTPrimarySurface value = (HoRTPrimarySurface)0;
    value.status = HO_SURFACE_ASSOCIATION_MATCH;
    float rawDepth = SAMPLE_TEXTURE2D_X_LOD(_HoSurfaceAssociationSurfaceDepth, sampler_PointClamp, uv, 0).r;
    #if !UNITY_REVERSED_Z
        rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
    #endif
    value.positionWS = ComputeWorldSpacePosition(uv, rawDepth, _HoRTPrimaryInverseViewProjection);
    value.eyeDepth = eyeDepth;
    float4 normal = SAMPLE_TEXTURE2D_X_LOD(_HoRTPrimaryNormal, sampler_PointClamp, uv, 0);
    value.geometricNormalWS = HoSurfaceOctDecode(normal.ba);
    value.shadingNormalWS = HoSurfaceOctDecode(normal.rg);
    value.viewDirectionWS = _HoRTPrimaryClipParameters.z > .5 ? normalize(_HoRTPrimaryTowardsEye.xyz)
        : normalize(_HoRTPrimaryCameraPosition.xyz - value.positionWS);
    value.baseColor = SAMPLE_TEXTURE2D_X_LOD(_HoRTPrimaryColor, sampler_PointClamp, uv, 0).rgb;
    float4 material = SAMPLE_TEXTURE2D_X_LOD(_HoRTPrimaryMaterial, sampler_PointClamp, uv, 0);
    // Old/custom SB writers must opt in to the BA mesh-normal contract explicitly.
    if (material.a < .5) { surface.status = HO_RT_PRIMARY_MISSING_ATTRIBUTES; return false; }
    value.perceptualRoughness = material.r; value.metallic = material.g;
    value.reflectance = SAMPLE_TEXTURE2D_X_LOD(_HoRTPrimaryReflection, sampler_PointClamp, uv, 0).r;
    value.emission = SAMPLE_TEXTURE2D_X_LOD(_HoSurfaceAssociationWriter, sampler_PointClamp, uv, 0).rgb;
    value.ownerAvailable = _HoRTPrimaryState.z > .5 ? 1u : 0u;
    if (value.ownerAvailable != 0) value.owner = HoSurfaceOwnerDecode(SAMPLE_TEXTURE2D_X_LOD(_HoRTPrimaryOwner, sampler_PointClamp, uv, 0).rg);
    if (!HoRTPrimaryFinite3(value.positionWS) || !HoRTPrimaryFinite3(value.geometricNormalWS) || dot(value.geometricNormalWS, value.geometricNormalWS) < .5
        || !HoRTPrimaryFinite3(value.shadingNormalWS) || !HoRTPrimaryFinite3(value.viewDirectionWS) || !HoRTPrimaryFinite3(value.baseColor)
        || !HoRTPrimaryFinite3(value.emission) || !IsFinite(value.eyeDepth) || !IsFinite(value.perceptualRoughness)
        || !IsFinite(value.metallic) || !IsFinite(value.reflectance))
    { surface.status = HO_RT_PRIMARY_INVALID_DATA; return false; }
    surface = value;
    return true;
}

bool HoRTTryLoadPrimarySurfaceAtUV(float2 uv, out HoRTPrimarySurface surface)
{
    if (any(uv != uv) || any(uv < 0) || any(uv >= 1))
    { surface = (HoRTPrimarySurface)0; surface.status = HO_SURFACE_ASSOCIATION_OUT_OF_BOUNDS; return false; }
    return HoRTTryLoadPrimarySurface((uint2)(uv * _HoRTPrimarySize.xy), surface);
}
#endif
