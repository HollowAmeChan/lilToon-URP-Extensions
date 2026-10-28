#ifndef HO_RT_CUTOUT_ALPHA_INCLUDED
#define HO_RT_CUTOUT_ALPHA_INCLUDED
// HoRTCutoutSupport defines this bounded domain. The including hit material
// declares the original lilToon alpha uniforms, textures and sampler.
float HoRTEvaluateCutoutAlpha(float2 uv, bool backFace)
{
    if(backFace&&_ShiftBackfaceUV>0)uv.x+=1;
    uv=uv*_MainTex_ST.xy+_MainTex_ST.zw;
    float si,co;sincos(_MainTex_ScrollRotate.z,si,co);float2 rotated=uv-.5;
    uv=float2(rotated.x*co-rotated.y*si,rotated.x*si+rotated.y*co)+.5;
    float alpha=_MainTex.SampleLevel(sampler_MainTex,uv,0).a*_Color.a;
    if(_AlphaMaskMode>0)
    {
        float mask=saturate(_AlphaMask.SampleLevel(sampler_MainTex,uv*_AlphaMask_ST.xy+_AlphaMask_ST.zw,0).r*_AlphaMaskScale+_AlphaMaskValue);
        if(_AlphaMaskMode==1)alpha=mask;
        if(_AlphaMaskMode==2)alpha*=mask;
        if(_AlphaMaskMode==3)alpha=saturate(alpha+mask);
        if(_AlphaMaskMode==4)alpha=saturate(alpha-mask);
    }
    return alpha;
}
#endif
