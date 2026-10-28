#ifndef HO_RT_VISIBILITY_PAYLOAD_INCLUDED
#define HO_RT_VISIBILITY_PAYLOAD_INCLUDED
// Status: 0 miss, 1 hit, 2 metadata mismatch, 3 invalid ray, 4 unavailable.
// Only status 1 carries a hit payload. Distance is in world metres.
struct HoRTVisibilityHit
{
    uint status;
    float distance;
    uint instance;
    uint primitive;
    float2 barycentrics;
    uint frontFace;
    uint triangleNormal; // signed world-space flat face normal, octahedral 2x16 UNorm
    float3 smoothOffset; // optional outward vertex-tangent reconstruction, world metres
};
uint HoRTVisibilityPackNormal(float3 normal)
{
    normal/=max(1e-20,abs(normal.x)+abs(normal.y)+abs(normal.z));
    float2 oct=normal.xy;
    if(normal.z<0)oct=(1-abs(oct.yx))*float2(oct.x>=0?1:-1,oct.y>=0?1:-1);
    uint2 packed=(uint2)round(saturate(oct*.5+.5)*65535);
    return packed.x|(packed.y<<16);
}
float3 HoRTVisibilityUnpackNormal(uint packed)
{
    float2 oct=float2(packed&65535u,packed>>16)/65535.0*2-1;
    float3 normal=float3(oct,1-abs(oct.x)-abs(oct.y));
    float t=saturate(-normal.z);normal.xy+=normal.xy>=0?-t.xx:t.xx;
    return normalize(normal);
}
#endif
