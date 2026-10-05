#ifndef HO_GEOMETRY_REFERENCE_FRAME_INCLUDED
#define HO_GEOMETRY_REFERENCE_FRAME_INCLUDED
// Matches HoGeometryFrameData: 64 bytes. Row 0 and unavailable frames are invalid.
struct HoGDObjectFrame { float4 originValid; float4 forward; float4 right; float4 up; };
StructuredBuffer<HoGDObjectFrame> _HoGDObjectFrames;
StructuredBuffer<uint> _HoGDIdentityFrameSlots;
uint _HoGDObjectFrameCount;
float4 _HoGDObserverPosition;

bool HoGDIdentityFrameValid(uint identityId)
{
    if(identityId == 0u || identityId >= 65536u)return false;
    uint slot = _HoGDIdentityFrameSlots[identityId];
    return slot > 0u && slot < _HoGDObjectFrameCount && _HoGDObjectFrames[slot].originValid.w > 0.5;
}
float2 HoGDObjectViewAngles(uint identityId, float3 observerPosition)
{
    if (!HoGDIdentityFrameValid(identityId)) return 0.0;
    HoGDObjectFrame frame = _HoGDObjectFrames[_HoGDIdentityFrameSlots[identityId]];
    if (frame.originValid.w < 0.5) return 0.0;
    float3 direction = observerPosition - frame.originValid.xyz;
    if (dot(direction, direction) < 1e-8) return 0.0;
    direction = normalize(direction);
    float forward = dot(direction, frame.forward.xyz);
    // Preserve the existing eye consumer's atan2 convention (including orthographic views).
    return float2(atan2(dot(direction, frame.right.xyz), forward),
        atan2(dot(direction, frame.up.xyz), forward)) * 57.29577951308232;
}
#endif
