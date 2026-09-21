#ifndef HO_CHARACTER_SHADOW_SAMPLING_INCLUDED
#define HO_CHARACTER_SHADOW_SAMPLING_INCLUDED
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoRendererIdentity.hlsl"
#define HO_CS_MAX_SLICES 16
TEXTURE2D_FLOAT(_HoCSAtlas);
float _HoCSActive;
int _HoCSCount;
float _HoCSFilterRadius;
float4 _HoCSAtlasSize;
float4 _HoCSGroupSlices[64];
float4 _HoCSPartMasks[HO_CS_MAX_SLICES * 64];
float4x4 _HoCSWorldToShadow[HO_CS_MAX_SLICES];
float4x4 _HoCSWorldToBounds[HO_CS_MAX_SLICES];
float4 _HoCSTileRects[HO_CS_MAX_SLICES];
float4 _HoCSParameters[HO_CS_MAX_SLICES];

float HoCSCompare(float2 uv, float receiverDepth)
{
    float depth = SAMPLE_TEXTURE2D_LOD(_HoCSAtlas, sampler_PointClamp, uv, 0).r;
    #if UNITY_REVERSED_Z
        return receiverDepth >= depth ? 1.0 : 0.0;
    #else
        return receiverDepth <= depth ? 1.0 : 0.0;
    #endif
}

float HoCSResolveMainCast(float3 positionWS, float sceneCast)
{
    if (_HoCSActive < 0.5) return sceneCast;
    uint identity = HoRendererIdentity();
    uint group = HoRendererIdentityGroup(identity);
    if (group == 0u) return sceneCast;
    int slice = (int)_HoCSGroupSlices[group / 4u][group % 4u] - 1;
    if (slice < 0 || slice >= min(_HoCSCount, HO_CS_MAX_SLICES)) return sceneCast;
    uint slot = HoRendererIdentitySlot(identity);
    if (_HoCSPartMasks[slice * 64 + slot / 4u][slot % 4u] < 0.5) return sceneCast;
    float4 parameters = _HoCSParameters[slice];
    if (parameters.z < 0.5) return sceneCast;
    float3 local = mul(_HoCSWorldToBounds[slice], float4(positionWS, 1)).xyz;
    float edge = 0.5 - max(abs(local.x), max(abs(local.y), abs(local.z)));
    if (edge <= 0) return sceneCast;
    float3 shadow = mul(_HoCSWorldToShadow[slice], float4(positionWS, 1)).xyz;
    if (any(shadow < 0) || any(shadow > 1)) return sceneCast;
    float4 tile = _HoCSTileRects[slice];
    float2 uv = tile.xy + shadow.xy * tile.zw;
    float2 lo = tile.xy + _HoCSAtlasSize.xy * 0.5;
    float2 hi = tile.xy + tile.zw - _HoCSAtlasSize.xy * 0.5;
    float visibility = 0;
    if (_HoCSFilterRadius < 0.001)
        visibility = HoCSCompare(clamp(uv, lo, hi), shadow.z);
    else
    {
        [unroll] for (int y = -1; y <= 1; y++)
        [unroll] for (int x = -1; x <= 1; x++)
            visibility += HoCSCompare(clamp(uv + float2(x, y) * _HoCSAtlasSize.xy * _HoCSFilterRadius, lo, hi), shadow.z) / 9.0;
    }
    // Same level as MainLightRealtimeShadow: shadow strength once, no extra light/AO terms.
    float localCast = lerp(1.0, visibility, parameters.y);
    float blend = parameters.x > 0 ? smoothstep(0, parameters.x, edge) : 1;
    return lerp(sceneCast, localCast, blend);
}
#endif
