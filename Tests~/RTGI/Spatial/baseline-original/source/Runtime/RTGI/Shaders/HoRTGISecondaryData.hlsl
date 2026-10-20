#ifndef HO_RTGI_SECONDARY_DATA_INCLUDED
#define HO_RTGI_SECONDARY_DATA_INCLUDED
#include "../../SurfaceBuffer/Shaders/HoSurfaceBufferCommon.hlsl"
// GI-only per-ray data, not a duplicate of the current primary GBuffer.
// 48 B: flags 0 invalid primary, 1 material hit, 2 environment, 3 opaque black/rejected direction.
struct HoRTGISecondaryData
{
    float3 positionWS; uint normal;
    uint diffuseAlbedo, specularRoughness;
    float3 emission; float pdf;
    uint geometricNormal, flags;
};
uint HoGIPackNormal(float3 n) { uint2 b=(uint2)round(saturate(HoSurfaceOctEncode(n))*65535);return b.x|(b.y<<16); }
float3 HoGIUnpackNormal(uint n) { return HoSurfaceOctDecode(float2(n&65535,n>>16)/65535.0); }
uint HoGIPackColor(float4 v) { uint4 b=(uint4)round(saturate(v)*255);return b.x|(b.y<<8)|(b.z<<16)|(b.w<<24); }
#endif
