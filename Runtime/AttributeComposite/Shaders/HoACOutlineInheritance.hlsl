#ifndef HO_AC_OUTLINE_INHERITANCE_INCLUDED
#define HO_AC_OUTLINE_INHERITANCE_INCLUDED
// RG = original renderer owner bytes; B = its semantic weight; A = visible share of that shell.
// This is independent of physical OB identity and of future SurfaceBuffer identity overrides.
TEXTURE2D_X(_HoACOutlineOwnerTexture);
float _HoACOutlineInheritanceActive;
float4 HoAC_ReadOutlineOwner(float2 uv)
{
    [branch] if (_HoACOutlineInheritanceActive <= 0.5) return 0;
    return SAMPLE_TEXTURE2D_X(_HoACOutlineOwnerTexture, sampler_PointClamp, uv);
}
uint HoAC_OutlineOriginalOwner(float4 packet)
{
    return ((uint)round(saturate(packet.r) * 255.0) << 8u) | (uint)round(saturate(packet.g) * 255.0);
}
float HoAC_ApplyOutlineSemantic(float physical, uint mode, uint bit, float4 packet, uint tags, bool surfaceAvailable)
{
    float share = saturate(packet.a);
    float member = bit < 32u && ((tags & (1u << bit)) != 0u) ? 1 : 0;
    float value = member;
    if (surfaceAvailable && (mode == 1u || mode == 3u || mode == 4u)) value *= saturate(packet.b);
    // The visible shell replaces the hidden physical pixel fraction; it is not an additional layer.
    return saturate(physical * (1-share) + value * share);
}
#endif
