#ifndef HO_RT_SHADOW_SAMPLING_INCLUDED
#define HO_RT_SHADOW_SAMPLING_INCLUDED
// This include is consumed before toon ramps. It never thresholds visibility,
// mixes normal lighting into V or samples across another surface's identity.
TEXTURE2D(_HoRTShadowRawTexture);
TEXTURE2D(_HoRTShadowVisibilityTexture);
TEXTURE2D_FLOAT(_HoRTShadowDepthTexture);
float _HoRTShadowActive,_HoRTShadowMainLightStrength;
float4 _HoRTShadowSize;
float _HoRTShadowConsumerDebugMode;

// Keep the actual consumer's rejection reason available to its own fragment
// shader. A valid producer image alone cannot prove this lookup succeeds.
uint HoRTShadowMainConsumptionStatus(float3 positionWS,uint owner,out float visibility)
{
    visibility=0;
    if(_HoRTShadowActive<.5)return 1;
    if(owner==0)return 2;
    float4 clip=TransformWorldToHClip(positionWS);
    if(clip.w<=0)return 3;
    float2 uv=clip.xy/clip.w*.5+.5;
    #if UNITY_UV_STARTS_AT_TOP
        uv.y=1-uv.y;
    #endif
    if(any(uv<0)||any(uv>=1))return 3;
    uint2 pixel=(uint2)(uv*_HoRTShadowSize.xy);
    float4 raw=LOAD_TEXTURE2D(_HoRTShadowRawTexture,pixel);
    if(raw.z!=1)return 4;
    if((uint)raw.w!=owner)return 5;
    float depth=LOAD_TEXTURE2D(_HoRTShadowDepthTexture,pixel).r;
    float eyeDepth;
    if(unity_OrthoParams.w>.5)
    {
        #if UNITY_REVERSED_Z
            depth=1-depth;
        #endif
        eyeDepth=lerp(_ProjectionParams.y,_ProjectionParams.z,depth);
    }
    else eyeDepth=LinearEyeDepth(depth,_ZBufferParams);
    float fragmentDepth=-TransformWorldToView(positionWS).z;
    if(abs(eyeDepth-fragmentDepth)>max(.0001,abs(eyeDepth)*.00001))return 6;
    visibility=saturate(LOAD_TEXTURE2D(_HoRTShadowVisibilityTexture,pixel).r);
    return 0;
}

bool HoRTShadowTryMainVisibility(float3 positionWS,uint owner,out float visibility)
{
    return HoRTShadowMainConsumptionStatus(positionWS,owner,visibility)==0;
}

float3 HoRTShadowConsumptionDebugColor(float3 positionWS,uint owner)
{
    float visibility;
    uint status=HoRTShadowMainConsumptionStatus(positionWS,owner,visibility);
    if(status==0)return float3(0,lerp(.25,1,visibility),0);
    if(status==1)return float3(1,0,0);
    if(status==2)return float3(0,0,1);
    if(status==3)return float3(1,1,0);
    if(status==4)return float3(.5,0,1);
    if(status==5)return float3(0,1,1);
    return float3(1,.25,0);
}

bool HoRTShadowTryMainCast(float3 positionWS,uint owner,out float cast)
{
    float visibility;
    bool valid=HoRTShadowTryMainVisibility(positionWS,owner,visibility);
    cast=valid?lerp(1,visibility,saturate(_HoRTShadowMainLightStrength)):0;
    return valid;
}
#endif
