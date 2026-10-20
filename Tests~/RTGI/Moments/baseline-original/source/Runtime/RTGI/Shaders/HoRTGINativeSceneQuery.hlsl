#ifndef HO_RTGI_NATIVE_SCENE_QUERY_INCLUDED
#define HO_RTGI_NATIVE_SCENE_QUERY_INCLUDED
// Native RAB visibility must execute the same material any-hit as secondary
// generation. Do not import the inline implementation which culls non-opaque.
#define HO_RT_SCENE_QUERY_INCLUDED
#include "../../RayTracing/Shaders/HoRTSceneHit.hlsl"
#include "HoRTGISecondarySurface.hlsl"
RaytracingAccelerationStructure _HoRTSceneAccelerationStructure;
float4 _HoRTSceneState;

// GI raygen uses only status/distance from this adapter, never metadata IDs.
HoRTSceneHit HoRTTraceScene(float3 origin,float3 direction,float tMin,float tMax,uint mask)
{
    HoRTSceneHit result=(HoRTSceneHit)0;
    if(_HoRTSceneState.x<.5){result.status=4;return result;}
    float lengthSquared=dot(direction,direction);
    if(!all(isfinite(origin))||!all(isfinite(direction))||!isfinite(tMin)||!isfinite(tMax)||!isfinite(lengthSquared)||lengthSquared<1e-12||tMin<0||tMax<=tMin||mask>255)
    {result.status=3;return result;}
    if(_HoRTSceneState.z<.5||mask==0)return result;
    RayDesc ray;ray.Origin=origin;ray.Direction=direction*rsqrt(lengthSquared);ray.TMin=tMin;ray.TMax=tMax;
    HoRTGISecondarySurface hit=(HoRTGISecondarySurface)0;hit.status=HO_RTGI_VISIBILITY_QUERY;
    TraceRay(_HoRTSceneAccelerationStructure,0,mask,0,1,0,ray,hit);
    result.status=hit.status==0?0:1;result.distance=hit.distance;
    return result;
}
#endif
