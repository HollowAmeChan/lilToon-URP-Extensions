#ifndef HO_RTDI_PRIMARY_HISTORY_INCLUDED
#define HO_RTDI_PRIMARY_HISTORY_INCLUDED
// Shared DI/GI primary-guide history, 5 uints / 20 bytes. Materials retain
// their existing 8-bit response; each normal retains two 16-bit oct coordinates.
struct HoRTDIPrimaryHistory
{
    uint depth;
    uint shadingNormal;
    uint geometricNormal;
    uint diffuse;
    uint specular;
};
#endif
