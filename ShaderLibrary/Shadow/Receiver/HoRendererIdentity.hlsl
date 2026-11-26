#ifndef HO_RENDERER_IDENTITY_INCLUDED
#define HO_RENDERER_IDENTITY_INCLUDED
// Draw-local identity. No screen-space texture or CS-owned bits are involved.
// Call after UNITY_SETUP_INSTANCE_ID, with instancing_options renderinglayer.
uint HoRendererIdentity() { return unity_RendererUserValue & 0xffffu; }
uint HoRendererIdentityGroup(uint identity) { return (identity >> 8u) & 255u; }
uint HoRendererIdentitySlot(uint identity) { return identity & 255u; }
#endif
