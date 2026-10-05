#ifndef HO_AC_SEMANTIC_COMPOSE_INCLUDED
#define HO_AC_SEMANTIC_COMPOSE_INCLUDED

// Current pixel-level contract. No owner gate or sample-level association is implied.
float HoAC_ComposeSemantic(uint mode, float objectCoverage, float2 surface, bool surfaceAvailable)
{
    if (!surfaceAvailable) return objectCoverage;
    float written = saturate(surface.x);
    float value = saturate(surface.y);
    if (mode == 1u) return value;
    if (mode == 2u) return saturate(max(objectCoverage, value));
    if (mode == 3u) return saturate(value + (1.0 - written) * objectCoverage);
    if (mode == 4u) return saturate(objectCoverage * value);
    return objectCoverage;
}

TEXTURE2D_X(_HoSurfaceSemanticLane0Texture);
TEXTURE2D_X(_HoSurfaceSemanticLane1Texture);
TEXTURE2D_X(_HoSurfaceSemanticLane2Texture);
TEXTURE2D_X(_HoSurfaceSemanticLane3Texture);
TEXTURE2D_X(_HoSurfaceSemanticOwnerTexture);

float4 HoAC_SampleSurfaceLane(float2 uv, uint lane)
{
    if (lane < 2u) return SAMPLE_TEXTURE2D_X(_HoSurfaceSemanticLane0Texture, sampler_PointClamp, uv);
    if (lane < 4u) return SAMPLE_TEXTURE2D_X(_HoSurfaceSemanticLane1Texture, sampler_PointClamp, uv);
    if (lane < 6u) return SAMPLE_TEXTURE2D_X(_HoSurfaceSemanticLane2Texture, sampler_PointClamp, uv);
    return SAMPLE_TEXTURE2D_X(_HoSurfaceSemanticLane3Texture, sampler_PointClamp, uv);
}

// x = written; y = effective value. A matching ID with zero value remains an explicit write.
float2 HoAC_ReadSurfaceLane(float2 uv, uint lane, uint declaredId, bool available)
{
    if (!available || declaredId == 0u || lane >= 8u) return float2(0.0, 0.0);
    float4 packed = HoAC_SampleSurfaceLane(uv, lane);
    float2 pair = (lane & 1u) == 0u ? packed.rg : packed.ba;
    uint id = (uint)round(saturate(pair.x) * 255.0);
    return id == declaredId ? float2(1.0, saturate(pair.y)) : float2(0.0, 0.0);
}

uint HoAC_ReadSemanticOwner(float2 uv)
{
    float2 pair = SAMPLE_TEXTURE2D_X(_HoSurfaceSemanticOwnerTexture, sampler_PointClamp, uv).rg;
    return ((uint)round(saturate(pair.x) * 255.0) << 8) | (uint)round(saturate(pair.y) * 255.0);
}

#endif
