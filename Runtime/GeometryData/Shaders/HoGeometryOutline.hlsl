#ifndef HO_GEOMETRY_OUTLINE_INCLUDED
#define HO_GEOMETRY_OUTLINE_INCLUDED
struct HoGDOutlineEntry { uint offset; uint count; uint valid; uint reserved; };
StructuredBuffer<HoGDOutlineEntry> _HoGDOutlineEntries;
StructuredBuffer<float4> _HoGDOutlineValues;
uint _HoGDOutlineEntryCount;
float _HoGDOutlineAvailable;
bool HoGDOutlineVertexColor(uint vertexId,out float4 encoded)
{
    encoded=0;
    if(_HoGDOutlineAvailable<0.5)return false;
    #if defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_USE_RENDERINGLAYER_ARRAY)
        return false;
    #else
        uint slot=unity_RendererUserValue>>16u;
        if(slot==0u || slot>=_HoGDOutlineEntryCount)return false;
        HoGDOutlineEntry entry=_HoGDOutlineEntries[slot];
        if(entry.valid==0u || vertexId>=entry.count)return false;
        encoded=_HoGDOutlineValues[entry.offset+vertexId];
        return true;
    #endif
}
#endif
