#ifndef HO_RTGI_GRID_INCLUDED
#define HO_RTGI_GRID_INCLUDED
// grid: full width/height, integral pixel step (1 or 2), representative
// displacement from the scaled GI centre in full pixel units (0 or .5).
uint2 HoGIGridPrimaryPixel(uint2 pixel,float4 grid)
{
    uint step=(uint)grid.z;
    return pixel*step+step/2;
}
float2 HoGIGridPrimaryUV(uint2 pixel,float4 grid)
{
    return (float2(HoGIGridPrimaryPixel(pixel,grid))+.5)/grid.xy;
}
float2 HoGIGridToPrimaryUV(float2 uv,float4 grid){return uv+grid.w/grid.xy;}
float2 HoGIPrimaryToGridUV(float2 uv,float4 grid){return uv-grid.w/grid.xy;}
#endif
