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
    uint reserved;
};
#endif
