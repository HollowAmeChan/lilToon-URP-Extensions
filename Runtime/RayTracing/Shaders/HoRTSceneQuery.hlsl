#ifndef HO_RT_SCENE_QUERY_INCLUDED
#define HO_RT_SCENE_QUERY_INCLUDED
#include "UnityRayQuery.cginc"
RaytracingAccelerationStructure _HoRTSceneAccelerationStructure;
StructuredBuffer<uint4> _HoRTSceneInstances; // submesh, material ID, primitive count, valid
float4 _HoRTSceneState; // built input, metadata capacity, active instances, reserved

struct HoRTSceneHit
{
    uint status; // 0 miss, 1 hit, 2 invalid metadata, 3 invalid ray, 4 unavailable
    float distance;
    uint instanceId, submesh, materialId, primitiveIndex;
    float2 barycentrics;
    uint frontFace;
    float3 positionWS;
};

HoRTSceneHit HoRTTraceScene(float3 origin, float3 direction, float tMin, float tMax, uint mask)
{
    HoRTSceneHit hit = (HoRTSceneHit)0;
    if (_HoRTSceneState.x < .5) { hit.status = 4; return hit; }
    float lengthSquared = dot(direction, direction);
    if (!all(isfinite(origin)) || !all(isfinite(direction)) || !isfinite(tMin) || !isfinite(tMax)
        || !isfinite(lengthSquared) || lengthSquared < 1e-12 || tMin < 0 || tMax <= tMin || mask > 255)
    { hit.status = 3; return hit; }
    if (_HoRTSceneState.z < .5 || mask == 0) return hit;
    direction *= rsqrt(lengthSquared);
    RayDesc ray; ray.Origin = origin; ray.Direction = direction; ray.TMin = tMin; ray.TMax = tMax;
    const uint flags = RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES;
    UnityRayQuery<flags> query;
    query.TraceRayInline(_HoRTSceneAccelerationStructure, flags, mask, ray);
    while (query.Proceed()) { }
    if (query.CommittedStatus() != COMMITTED_TRIANGLE_HIT) return hit;
    uint id = query.CommittedInstanceID(); uint primitive = query.CommittedPrimitiveIndex();
    if (id == 0 || id >= (uint)_HoRTSceneState.y) { hit.status = 2; return hit; }
    uint4 entry = _HoRTSceneInstances[id];
    if (entry.w == 0 || primitive >= entry.z) { hit.status = 2; return hit; }
    hit.status = 1; hit.distance = query.CommittedRayT(); hit.instanceId = id;
    hit.submesh = entry.x; hit.materialId = entry.y; hit.primitiveIndex = primitive;
    hit.barycentrics = query.CommittedTriangleBarycentrics(); hit.frontFace = query.CommittedTriangleFrontFace() ? 1 : 0;
    hit.positionWS = origin + direction * hit.distance;
    return hit;
}
#endif
