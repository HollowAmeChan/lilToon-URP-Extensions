#ifndef HO_CHARACTER_RIM_SAMPLING_INCLUDED
#define HO_CHARACTER_RIM_SAMPLING_INCLUDED
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoRendererIdentity.hlsl"
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/AttributeComposite/Shaders/HoACQuery.hlsl"

struct HoRimLightData
{
    float4 directionWidth;
    float4 colorSoftness;
    float4 region;
    float4 shadow;
    float4 tile;
    float4x4 worldToShadow;
};
StructuredBuffer<HoRimLightData> _HoRimLights;
float4 _HoRimGroupRanges[256];
float _HoRimActive;
uint _HoRimLightCount;
float4 _HoRimAtlasSize; // inverse width/height, width/height
TEXTURE2D_FLOAT(_HoRimShadowAtlas);

// Object-only AC queries evaluated against THIS draw's identity. No union of pixel layers.
// Group scoping is performed before this function. Alias identities retain their canonical part.
float HoRimReceivesIdentity(uint identity, float4 query)
{
    if (identity == 0u) return 0;
    uint kind = (uint)round(query.x), value = (uint)round(query.y);
    if (kind == 2u) return value > 0u && value < 256u && HoRendererIdentityGroup(identity) == value ? 1 : 0;
    HoObjectPartData part = HoObjectBufferLoadPart(identity);
    if (kind == 3u) return value >= 256u && value <= 65535u && (identity == value || part.reserved == value) ? 1 : 0;
    if (kind == 7u)
    {
        if (value == 0u || value > (uint)_HoObjectBufferSelectionCount) return 0;
        uint groupId = HoRendererIdentityGroup(identity), slot = HoRendererIdentitySlot(identity);
        HoObjectGroupData group = _HoObjectBufferGroups[groupId];
        uint row = group.rowBase + slot;
        if (slot >= group.slotCount || row >= (uint)_HoOBSelectionMembershipCount) return 0;
        uint bit = value - 1u;
        return (_HoOBSelectionMembership[row * 8u + (bit >> 5u)] & (1u << (bit & 31u))) != 0u ? 1 : 0;
    }
    return 0;
}

float HoRimShadowVisibility(HoRimLightData source, float3 positionWS, float3 normalWS)
{
    if (source.shadow.x < .5) return 1;
    float3 p = mul(source.worldToShadow, float4(positionWS + normalWS * source.shadow.z, 1)).xyz;
    // Requested shadows fail closed outside their valid projection; never sample another tile.
    if (any(p < 0) || any(p > 1)) return 1 - saturate(source.shadow.y);
    float2 uv = source.tile.xy + p.xy * source.tile.zw;
    float2 lo = source.tile.xy + _HoRimAtlasSize.xy * .5;
    float2 hi = source.tile.xy + source.tile.zw - _HoRimAtlasSize.xy * .5;
    float visibility = 0;
    [unroll] for (int y = -1; y <= 1; ++y)
    [unroll] for (int x = -1; x <= 1; ++x)
    {
        float raw = SAMPLE_TEXTURE2D_LOD(_HoRimShadowAtlas, sampler_PointClamp,
            clamp(uv + float2(x, y) * _HoRimAtlasSize.xy, lo, hi), 0).r;
        #if UNITY_REVERSED_Z
            visibility += step(raw, p.z + source.shadow.w);
        #else
            visibility += step(p.z - source.shadow.w, raw);
        #endif
    }
    return lerp(1, visibility / 9, source.shadow.y);
}

float HoRimEdgeGateAA(float edge, float width, float softness, float edgeDerivative)
{
    float low = .8 - .6 * saturate(width), high = .9 - .4 * saturate(width);
    float midpoint = (low + high) * .5;
    float halfWidth = max((high - low) * 2 * saturate(softness), max(edgeDerivative, 1e-4)) * .5;
    return smoothstep(midpoint - halfWidth, midpoint + halfWidth, edge);
}

float HoRimEdgeGate(float edge, float width, float softness)
{
    return HoRimEdgeGateAA(edge, width, softness, fwidth(edge));
}

// Raw dedicated-light HDR response only. Material tint/mask/alpha/receive strength belong to caller.
float3 HoRimEvaluateLights(float3 positionWS, float3 N, float3 V, uint identity)
{
    if (_HoRimActive < .5 || identity == 0u || _HoRimLightCount == 0u) return 0;
    uint groupId = HoRendererIdentityGroup(identity);
    if (groupId == 0u) return 0;
    float4 range = _HoRimGroupRanges[groupId];
    uint begin = min((uint)range.x, _HoRimLightCount);
    uint end = min(begin + (uint)range.y, _HoRimLightCount);
    // Camera backward axis matches the source formula; projection is independent of perspective rays.
    float3 B = UNITY_MATRIX_V[2].xyz;
    B *= rsqrt(max(dot(B, B), 1e-8));
    float edge = 1 - saturate(abs(dot(N, V)));
    float edgeDerivative = fwidth(edge);
    float3 result = 0;
    [loop] for (uint i = begin; i < end; ++i)
    {
        HoRimLightData source = _HoRimLights[i];
        if (HoRimReceivesIdentity(identity, source.region) < .5) continue;
        float3 D = source.directionWidth.xyz - B * dot(B, source.directionWidth.xyz);
        float lengthSquared = dot(D, D);
        // Parallel-to-view source has no defined side: contribute zero instead of choosing a new light.
        if (lengthSquared < 1e-8) continue;
        D *= rsqrt(lengthSquared);
        float gate = HoRimEdgeGateAA(edge, source.directionWidth.w, source.colorSoftness.w, edgeDerivative) * saturate(dot(N, D));
        if (gate > 0)
            result += source.colorSoftness.rgb * gate * HoRimShadowVisibility(source, positionWS, N);
    }
    return result;
}
#endif
