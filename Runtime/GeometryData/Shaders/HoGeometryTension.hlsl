#ifndef HO_GEOMETRY_TENSION_INCLUDED
#define HO_GEOMETRY_TENSION_INCLUDED
struct HoGDTensionEntry { uint offset; uint count; uint valid; uint reserved; };
StructuredBuffer<HoGDTensionEntry> _HoGDTensionEntries;
StructuredBuffer<float4> _HoGDTensionValues;
uint _HoGDTensionEntryCount;
float _HoGDTensionAvailable;
float4 HoGDTensionVertex(uint vertexId)
{
    if(_HoGDTensionAvailable<0.5)return 0;
    #if defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_USE_RENDERINGLAYER_ARRAY)
        return 0;
    #else
        uint slot=unity_RendererUserValue>>16u;
        if(slot==0u || slot>=_HoGDTensionEntryCount)return 0;
        HoGDTensionEntry entry=_HoGDTensionEntries[slot];
        if(entry.valid==0u || vertexId>=entry.count)return 0;
        return _HoGDTensionValues[entry.offset+vertexId];
    #endif
}
#endif
