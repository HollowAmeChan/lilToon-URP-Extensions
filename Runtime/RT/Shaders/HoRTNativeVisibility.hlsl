#ifndef HO_RT_NATIVE_VISIBILITY_INCLUDED
#define HO_RT_NATIVE_VISIBILITY_INCLUDED
#include "HoRTVisibilityPayload.hlsl"
RaytracingAccelerationStructure _HoRTSceneAccelerationStructure;
StructuredBuffer<uint4> _HoRTSceneInstances;
float4 _HoRTSceneState;

[shader("miss")]
void HoRTVisibilityMiss(inout HoRTVisibilityHit hit : SV_RayPayload)
{ hit=(HoRTVisibilityHit)0; }

HoRTVisibilityHit HoRTNativeVisibility(float3 origin, float3 direction, float minimum, float maximum, uint mask, bool closest)
{
    HoRTVisibilityHit hit=(HoRTVisibilityHit)0;
    hit.status=4;
    if(_HoRTSceneState.x<.5)return hit;
    hit.status=3;
    float magnitude=dot(direction,direction);
    if(!all(isfinite(origin))||!all(isfinite(direction))||!isfinite(minimum)||!isfinite(maximum)
        ||magnitude<1e-20||!isfinite(magnitude)||minimum<0||maximum<=minimum||mask>255)return hit;
    RayDesc ray;ray.Origin=origin;ray.Direction=direction*rsqrt(magnitude);ray.TMin=minimum;ray.TMax=maximum;
    // The AS policy disables this flag for explicitly two-sided instances.
    uint flags=RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES|RAY_FLAG_CULL_BACK_FACING_TRIANGLES;
    if(!closest)flags|=RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH;
    TraceRay(_HoRTSceneAccelerationStructure,flags,mask,0,1,0,ray,hit);
    if(hit.status==1)
    {
        uint4 metadata=hit.instance<(uint)_HoRTSceneState.y?_HoRTSceneInstances[hit.instance]:0;
        if(metadata.w==0||hit.primitive>=metadata.z)
        {hit=(HoRTVisibilityHit)0;hit.status=2;}
    }
    return hit;
}

HoRTVisibilityHit HoRTTraceClosestVisibility(float3 origin, float3 direction, float minimum, float maximum, uint mask)
{return HoRTNativeVisibility(origin,direction,minimum,maximum,mask,true);}

// The shortcut deliberately exposes only status. The first accepted traversal
// hit has no nearest-distance guarantee and must never feed SIGMA's blocker input.
uint HoRTTraceOcclusion(float3 origin, float3 direction, float minimum, float maximum, uint mask)
{return HoRTNativeVisibility(origin,direction,minimum,maximum,mask,false).status;}
#endif
