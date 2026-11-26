#ifndef HOSHADER_HO_BUFFER_ENCODING_INCLUDED
#define HOSHADER_HO_BUFFER_ENCODING_INCLUDED

// Pure Ho buffer encoding ABI. No resources, material properties, URP includes,
// vertex/fragment entry points, alpha policy or MRT declarations belong here.
// Owner: low 16 bits, RG = high byte / low byte. MSAA uses one UNorm16 sample.
float2 HoSurfaceOwnerEncode(uint identityId)
{
    uint id = identityId & 0xFFFFu;
    return float2((float)((id >> 8) & 0xFFu) / 255.0, (float)(id & 0xFFu) / 255.0);
}

uint HoSurfaceOwnerDecode(float2 encoded)
{
    uint high = (uint)round(saturate(encoded.x) * 255.0);
    uint low = (uint)round(saturate(encoded.y) * 255.0);
    return (high << 8) | low;
}

float HoBufferOwnerSampleEncode(uint identityId)
{
    return (float)(identityId & 0xFFFFu) / 65535.0;
}

float4 HoBufferOwnerPairEncode(uint ownerA, uint ownerB)
{
    return float4(HoSurfaceOwnerEncode(ownerA), HoSurfaceOwnerEncode(ownerB));
}

// Surface oct has two established arithmetic forms. Preserve both call-site
// formulas; they use the same [0,1] storage and the same decoder below.
float2 HoSurfaceOctEncode(float3 n)
{
    n /= max(1e-6, abs(n.x) + abs(n.y) + abs(n.z));
    float2 res = n.xy;
    if (n.z < 0.0)
    {
        res = (1.0 - abs(n.yx)) * float2(n.x >= 0.0 ? 1.0 : -1.0, n.y >= 0.0 ? 1.0 : -1.0);
    }
    return res * 0.5 + 0.5;
}

float2 HoBufferNormalOctEncode(float3 n)
{
    // The existing Unity PackNormalOctQuadEncode expression, followed by [0,1].
    n *= rcp(max(dot(abs(n), 1.0), 1e-6));
    float t = saturate(-n.z);
    return (n.xy + float2(n.x >= 0.0 ? t : -t, n.y >= 0.0 ? t : -t)) * 0.5 + 0.5;
}

float3 HoSurfaceOctDecode(float2 encoded)
{
    float2 f = encoded * 2.0 - 1.0;
    float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float t = saturate(-n.z);
    n.xy += n.xy >= 0.0 ? -t.xx : t.xx;
    return normalize(n);
}

// Geometry/outline buffers use RGB normals, unlike SurfaceBuffer's RG oct.
// Normalization, depth convention and coverage remain producer responsibilities.
float3 HoBufferNormalRGBEncode(float3 normalWS)
{
    return normalWS * 0.5 + 0.5;
}

// Semantic/profile bytes clamp; selection bytes below intentionally keep low bits.
float HoBufferByteEncode(float value)
{
    return round(clamp(value, 0.0, 255.0)) / 255.0;
}

float2 HoBufferSemanticLaneEncode(float semanticId, float tagMask, uint partTags, float weight)
{
    uint mask = (uint)round(max(tagMask, 0.0));
    if ((partTags & mask) == 0u) return 0.0;
    // A matching lane with zero weight is still an explicit write.
    return float2(HoBufferByteEncode(semanticId), weight);
}

float4 HoBufferSemanticPairEncode(float2 semanticIds, float2 tagMasks, uint partTags, float weight)
{
    return float4(HoBufferSemanticLaneEncode(semanticIds.x, tagMasks.x, partTags, weight),
                  HoBufferSemanticLaneEncode(semanticIds.y, tagMasks.y, partTags, weight));
}

float4 HoBufferSelectionRowEncode(uint idA, float coverageA, uint idB, float coverageB)
{
    return float4((float)(idA & 0xFFu) / 255.0, saturate(coverageA),
                  (float)(idB & 0xFFu) / 255.0, saturate(coverageB));
}

void HoBufferSelectionRowDecode(float4 packed, out uint idA, out float coverageA, out uint idB, out float coverageB)
{
    idA = (uint)round(saturate(packed.r) * 255.0);
    coverageA = packed.g;
    idB = (uint)round(saturate(packed.b) * 255.0);
    coverageB = packed.a;
}

#endif
