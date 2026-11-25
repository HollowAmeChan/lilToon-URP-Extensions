#ifndef HO_RT_CUTOUT_ALPHA_INCLUDED
#define HO_RT_CUTOUT_ALPHA_INCLUDED
// HoRTCutoutSupport defines this bounded domain. The including hit material
// declares the canonical alpha uniforms, textures and sampler. Domain 0 retains
// the old chained UV/tint ordering; 1=base*tint, 2=Cloth arithmetic, 3=Eye R coverage.
float HoRTEvaluateCutoutAlpha(float2 uv)
{
    float2 baseUV=uv*_MainTex_ST.xy+_MainTex_ST.zw;
    if(_HoRTAlphaDomain>2.5)
        return saturate(_AlphaMask.SampleLevel(sampler_AlphaMask,baseUV,0).r*_Color.a);
    float alpha=_MainTex.SampleLevel(sampler_MainTex,baseUV,0).a;
    bool cloth=_HoRTAlphaDomain>1.5;
    if(!cloth)alpha*=_Color.a;
    if(_AlphaMaskMode>0)
    {
        float2 maskUV=cloth?uv:baseUV;
        float mask=saturate(_AlphaMask.SampleLevel(sampler_MainTex,maskUV*_AlphaMask_ST.xy+_AlphaMask_ST.zw,0).r*_AlphaMaskScale+_AlphaMaskValue);
        if(_AlphaMaskMode==1)alpha=mask;
        if(_AlphaMaskMode==2)alpha*=mask;
        if(_AlphaMaskMode==3)alpha=saturate(alpha+mask);
        if(_AlphaMaskMode==4)alpha=saturate(alpha-mask);
    }
    return cloth?saturate(alpha)*saturate(_Color.a):alpha;
}
#endif
