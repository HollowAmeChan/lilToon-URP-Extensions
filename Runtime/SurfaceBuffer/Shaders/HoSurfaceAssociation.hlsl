#ifndef HO_SURFACE_ASSOCIATION_INCLUDED
#define HO_SURFACE_ASSOCIATION_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D_X_FLOAT(_HoSurfaceAssociationSurfaceDepth);
TEXTURE2D_X_FLOAT(_HoSurfaceAssociationGeometryDepth);
TEXTURE2D_X(_HoSurfaceAssociationWriter);
TEXTURE2D_X(_HoSurfaceAssociationGeometry);
// SB present, GB present, identical pixel-centre sample domain, reserved.
float4 _HoSurfaceAssociationState;

#define HO_SURFACE_ASSOCIATION_UNAVAILABLE 0u
#define HO_SURFACE_ASSOCIATION_NO_WRITER 1u
#define HO_SURFACE_ASSOCIATION_NO_GEOMETRY 2u
#define HO_SURFACE_ASSOCIATION_SAMPLE_DOMAIN 3u
#define HO_SURFACE_ASSOCIATION_DEPTH_MISMATCH 4u
#define HO_SURFACE_ASSOCIATION_MATCH 5u
#define HO_SURFACE_ASSOCIATION_OUT_OF_BOUNDS 6u

// Explicit overloads can be used by compute/ray consumers without raster camera globals.
// clipParameters = near, far, orthographic, reversed Z.
float HoSurfaceDeviceToEyeDepth(float rawDepth, float4 zBufferParameters, float4 clipParameters)
{
    if (clipParameters.z > 0.5)
    {
        if (clipParameters.w > 0.5) rawDepth = 1.0 - rawDepth;
        return lerp(clipParameters.x, clipParameters.y, rawDepth);
    }
    return LinearEyeDepth(rawDepth, zBufferParameters);
}

float4 HoSurfaceRasterClipParameters()
{
    float reversedZ = 0;
    #if UNITY_REVERSED_Z
        reversedZ = 1;
    #endif
    return float4(_ProjectionParams.yz, unity_OrthoParams.w, reversedZ);
}

float HoSurfaceDeviceToEyeDepth(float rawDepth)
{
    return HoSurfaceDeviceToEyeDepth(rawDepth, _ZBufferParams, HoSurfaceRasterClipParameters());
}

uint HoSurfaceAssociationStatus(float2 uv, float4 zBufferParameters, float4 clipParameters, out float surfaceEyeDepth)
{
    surfaceEyeDepth = 0;
    if (any(uv < 0) || any(uv >= 1)) return HO_SURFACE_ASSOCIATION_OUT_OF_BOUNDS;
    if (_HoSurfaceAssociationState.x < 0.5 || _HoSurfaceAssociationState.y < 0.5)
        return HO_SURFACE_ASSOCIATION_UNAVAILABLE;
    // No spatial interpolation and no treating a resolved MSAA sample as a centre sample.
    if (_HoSurfaceAssociationState.z < 0.5) return HO_SURFACE_ASSOCIATION_SAMPLE_DOMAIN;
    if (SAMPLE_TEXTURE2D_X_LOD(_HoSurfaceAssociationWriter, sampler_PointClamp, uv, 0).a < 0.5)
        return HO_SURFACE_ASSOCIATION_NO_WRITER;
    if (SAMPLE_TEXTURE2D_X_LOD(_HoSurfaceAssociationGeometry, sampler_PointClamp, uv, 0).a <= 0.0001)
        return HO_SURFACE_ASSOCIATION_NO_GEOMETRY;
    float sb = SAMPLE_TEXTURE2D_X_LOD(_HoSurfaceAssociationSurfaceDepth, sampler_PointClamp, uv, 0).r;
    float gb = SAMPLE_TEXTURE2D_X_LOD(_HoSurfaceAssociationGeometryDepth, sampler_PointClamp, uv, 0).r;
    surfaceEyeDepth = HoSurfaceDeviceToEyeDepth(sb, zBufferParameters, clipParameters);
    float geometryEyeDepth = HoSurfaceDeviceToEyeDepth(gb, zBufferParameters, clipParameters);
    float tolerance = max(0.0001, abs(surfaceEyeDepth) * 0.00001);
    return abs(surfaceEyeDepth - geometryEyeDepth) <= tolerance
        ? HO_SURFACE_ASSOCIATION_MATCH : HO_SURFACE_ASSOCIATION_DEPTH_MISMATCH;
}

uint HoSurfaceAssociationStatus(float2 uv, out float surfaceEyeDepth)
{
    return HoSurfaceAssociationStatus(uv, _ZBufferParams, HoSurfaceRasterClipParameters(), surfaceEyeDepth);
}

float3 HoSurfaceAssociationDebugColor(uint status)
{
    if (status == HO_SURFACE_ASSOCIATION_MATCH) return float3(0, 1, 0);
    if (status == HO_SURFACE_ASSOCIATION_NO_WRITER) return float3(1, 0, 0);
    if (status == HO_SURFACE_ASSOCIATION_NO_GEOMETRY) return float3(0, 0, 1);
    if (status == HO_SURFACE_ASSOCIATION_SAMPLE_DOMAIN) return float3(1, 1, 0);
    if (status == HO_SURFACE_ASSOCIATION_DEPTH_MISMATCH) return float3(1, 0.5, 0);
    return float3(1, 0, 1);
}
#endif
