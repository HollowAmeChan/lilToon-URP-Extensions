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

float HoSurfaceDeviceToEyeDepth(float rawDepth)
{
    if (unity_OrthoParams.w > 0.5)
    {
        #if UNITY_REVERSED_Z
            rawDepth = 1.0 - rawDepth;
        #endif
        return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
    }
    return LinearEyeDepth(rawDepth, _ZBufferParams);
}

uint HoSurfaceAssociationStatus(float2 uv, out float surfaceEyeDepth)
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
    surfaceEyeDepth = HoSurfaceDeviceToEyeDepth(sb);
    float geometryEyeDepth = HoSurfaceDeviceToEyeDepth(gb);
    float tolerance = max(0.0001, abs(surfaceEyeDepth) * 0.00001);
    return abs(surfaceEyeDepth - geometryEyeDepth) <= tolerance
        ? HO_SURFACE_ASSOCIATION_MATCH : HO_SURFACE_ASSOCIATION_DEPTH_MISMATCH;
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
