#ifndef HO_AC_CORRELATED_COMPOSE_INCLUDED
#define HO_AC_CORRELATED_COMPOSE_INCLUDED
#include "HoACRawIdentity.hlsl"
TEXTURE2D_X(_HoACSemanticWrittenCoverageTexture);
TEXTURE2D_X(_HoACSemanticWeightedCoverageTexture);
TEXTURE2D_X(_HoACSemanticAssociationStatusTexture);
float _HoACSemanticPrecisionActive;
bool HoAC_CorrelatedValid(float2 uv)
{
    // Inactive cameras have no status dependency or binding. Do not rely on HLSL logical short-circuiting.
    [branch] if (_HoACSemanticPrecisionActive <= 0.5) return false;
    return SAMPLE_TEXTURE2D_X(_HoACSemanticAssociationStatusTexture,sampler_PointClamp,uv).b < 1e-5;
}
float3 HoAC_CorrelatedLaneFromTags(uint bit, uint4 tags, float4 c, float4 w, float4 v)
{
    if (bit >= 32u) return 0;
    uint mask = 1u << bit;
    float3 total = 0;
    [unroll] for (uint i = 0; i < 4; i++)
    {
        if ((tags[i] & mask) != 0u)
            total += float3(c[i], min(w[i], c[i]), min(v[i], min(w[i], c[i])));
    }
    return saturate(total);
}
float3 HoAC_CorrelatedLane(float2 uv,uint bit)
{
    if(bit>=32u) return 0;
    float4 id0=SAMPLE_TEXTURE2D_X(_HoObjectBufferId0Texture,sampler_PointClamp,uv);
    float4 id1=SAMPLE_TEXTURE2D_X(_HoObjectBufferId1Texture,sampler_PointClamp,uv);
    float4 c=SAMPLE_TEXTURE2D_X(_HoObjectBufferCoverageTexture,sampler_PointClamp,uv);
    [branch] if (_HoACRawInputsAvailable > 0.5)
    {
        id0=SAMPLE_TEXTURE2D_X(_HoACRawIdentityId0Texture,sampler_PointClamp,uv);
        id1=SAMPLE_TEXTURE2D_X(_HoACRawIdentityId1Texture,sampler_PointClamp,uv);
        c=SAMPLE_TEXTURE2D_X(_HoACRawIdentityCoverageTexture,sampler_PointClamp,uv);
    }
    float4 w=SAMPLE_TEXTURE2D_X(_HoACSemanticWrittenCoverageTexture,sampler_PointClamp,uv);
    float4 v=SAMPLE_TEXTURE2D_X(_HoACSemanticWeightedCoverageTexture,sampler_PointClamp,uv);
    uint ids[4]={HoObjectBufferDecodeIdExact(id0.rg),HoObjectBufferDecodeIdExact(id0.ba),HoObjectBufferDecodeIdExact(id1.rg),HoObjectBufferDecodeIdExact(id1.ba)};
    uint4 tags=0;
    [unroll] for(uint i=0;i<4;i++)
    {
        if(ids[i]!=0u) tags[i]=HoObjectBufferLoadPart(ids[i]).tags;
    }
    return HoAC_CorrelatedLaneFromTags(bit,tags,c,w,v);
}
float HoAC_ComposeCorrelated(uint mode,float3 cwv)
{
    if(mode==1u || mode==4u) return cwv.z;
    if(mode==3u) return saturate(cwv.z+cwv.x-cwv.y);
    return cwv.x; // ObjectOnly and Union: Scalar V1 can only write its owner's existing memberships.
}
#endif
