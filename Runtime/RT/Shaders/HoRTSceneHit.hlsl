#ifndef HO_RT_SCENE_HIT_INCLUDED
#define HO_RT_SCENE_HIT_INCLUDED
struct HoRTSceneHit
{
    uint status; // 0 miss, 1 hit, 2 invalid metadata, 3 invalid ray, 4 unavailable
    float distance;
    uint instanceId, submesh, materialId, primitiveIndex;
    float2 barycentrics;
    uint frontFace;
    float3 positionWS;
};

#endif
