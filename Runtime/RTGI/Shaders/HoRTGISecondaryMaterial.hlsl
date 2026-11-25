// Numeric secondary surfaces. No material-package includes or lighting layers.
#include "HoRTGISecondaryInputs.hlsl"
#include "HoRTGISecondaryTension.hlsl"
#include "HoRTGISecondaryNormals.hlsl"
#include "../../RT/Shaders/HoRTCutoutAlpha.hlsl"

[shader("anyhit")]
void HoRTGIAlphaHit(inout HoRTGISecondarySurface payload : SV_RayPayload,HoGIAttributes attributes : SV_IntersectionAttributes)
{
    if(_HoRTGICutout<.5)return;
    uint3 tri=UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    float3 weights=float3(1-attributes.barycentrics.x-attributes.barycentrics.y,attributes.barycentrics);
    if(HoRTEvaluateCutoutAlpha(HoGIUV(tri,weights,kVertexAttributeTexCoord0))<_Cutoff)IgnoreHit();
}

void HoGIPBR(float2 uv,inout HoRTGISecondarySurface surface)
{
    surface.metallic=saturate(_Metallic);surface.reflectance=saturate(_Reflectance);
    float smoothness=saturate(_Smoothness);
    if(_HoRTPBRMode>1.5)
    {
        float4 packed=_PBRMap.SampleLevel(sampler_MainTex,HoGITransformUV(uv,_PBRMap_ST),0);
        if(_HoRTMaterialFamily==2)packed=_PBRMap.SampleLevel(sampler_PBRMap,HoGITransformUV(uv,_PBRMap_ST),0);
        surface.metallic=saturate(packed.r*_HoRTMetallicMapScale);
        surface.reflectance*=saturate(packed.g);smoothness=saturate(packed.a*_HoRTSmoothnessMapScale);
    }
    else if(_HoRTPBRMode>.5)
    {
        if(_HoRTPBRFlags.y>.5)
        {
            float value=_HoRTMaterialFamily==2?_MetallicGlossMap.SampleLevel(sampler_MetallicGlossMap,HoGITransformUV(uv,_MetallicGlossMap_ST),0).r
                :_MetallicGlossMap.SampleLevel(sampler_MainTex,HoGITransformUV(uv,_MetallicGlossMap_ST),0).r;
            surface.metallic=saturate(_HoRTMetallicMapScale*value);
        }
        if(_HoRTPBRFlags.z>.5)
        {
            float value=_HoRTMaterialFamily==2?_ReflectanceTex.SampleLevel(sampler_ReflectanceTex,HoGITransformUV(uv,_ReflectanceTex_ST),0).r
                :_ReflectanceTex.SampleLevel(sampler_MainTex,HoGITransformUV(uv,_ReflectanceTex_ST),0).r;
            surface.reflectance*=saturate(value);
        }
        if(_HoRTPBRFlags.w>.5)
        {
            float value=_HoRTMaterialFamily==2||_HoRTMaterialFamily==5||_HoRTMaterialFamily==6?_SmoothnessTex.SampleLevel(sampler_SmoothnessTex,HoGITransformUV(uv,_SmoothnessTex_ST),0).r
                :_SmoothnessTex.SampleLevel(sampler_MainTex,HoGITransformUV(uv,_SmoothnessTex_ST),0).r;
            smoothness=saturate(_HoRTSmoothnessMapScale*value);
        }
    }
    surface.perceptualRoughness=1-smoothness;
}

float2 HoGIEmissionUV(float2 uv,float4 st,float4 motion)
{
    uv=HoGITransformUV(uv,st)-.5;
    float angle=motion.z+motion.w*_Time.y;
    float sine,cosine;sincos(angle,sine,cosine);
    uv=mul(float2x2(cosine,-sine,sine,cosine),uv)+.5;
    return uv+frac(motion.xy*_Time.y);
}
float3 HoGIEmission(uint3 tri,float3 weights,float2 uv,float3 albedo,float3 viewTS,float nv)
{
    if(_HoRTEmissionMode<.5)return 0;
    if(_HoRTEmissionMode<1.5)
        return max(0,(_HoRTMaterialFamily==3?_EmissionMap.SampleLevel(sampler_MainTex,HoGITransformUV(uv,_EmissionMap_ST),0)
            :_EmissionMap.SampleLevel(sampler_EmissionMap,HoGITransformUV(uv,_EmissionMap_ST),0)).rgb*_EmissionColor.rgb);
    if(_HoRTEmissionMode>2.5)
    {
        float4 emission=_EmissionMap.SampleLevel(sampler_EmissionMap,HoGITransformUV(uv,_EmissionMap_ST),0)*_EmissionColor;
        return max(0,emission.rgb*lerp(1,albedo,saturate(_EmissionMainStrength))*emission.a);
    }
    // Bounded transitional legacy data evaluation keeps current assets working
    // until explicit migration, without requiring a lilToon installation.
    float2 emissionUV=uv;
    if(_EmissionMap_UVMode>.5&&_EmissionMap_UVMode<3.5)
        emissionUV=HoGIUV(tri,weights,(uint)kVertexAttributeTexCoord0+(uint)round(_EmissionMap_UVMode));
    else if(_EmissionMap_UVMode>3.5)emissionUV=nv.xx;
    if(_EmissionParallaxDepth!=0)emissionUV+=_EmissionParallaxDepth*viewTS.xy/(viewTS.z+.5);
    float4 color=_EmissionColor*_EmissionMap.SampleLevel(sampler_EmissionMap,HoGIEmissionUV(emissionUV,_EmissionMap_ST,_EmissionMap_ScrollRotate),0);
    color*=_EmissionBlendMask.SampleLevel(sampler_MainTex,HoGIEmissionUV(uv,_EmissionBlendMask_ST,_EmissionBlendMask_ScrollRotate),0);
    if(_EmissionUseGrad>.5)color*=_EmissionGradTex.SampleLevel(sampler_LinearRepeat,float2(_EmissionGradSpeed*_Time.y,.5),0);
    color.rgb*=lerp(1,albedo,_EmissionMainStrength);
    float blink=sin(_Time.y*_EmissionBlink.z+_EmissionBlink.w)*.5+.5;
    if(_EmissionBlink.y>.5)blink=round(blink);
    return max(0,color.rgb*_EmissionBlend*lerp(1,blink,_EmissionBlink.x)*color.a);
}

[shader("closesthit")]
void HoRTGIClosestHit(inout HoRTGISecondarySurface payload : SV_RayPayload,HoGIAttributes attributes : SV_IntersectionAttributes)
{
    bool visibilityOnly=payload.status==HO_RTGI_VISIBILITY_QUERY;
    payload=(HoRTGISecondarySurface)0;payload.distance=RayTCurrent();payload.status=2;
    if(visibilityOnly||_HoRTGISecondarySupported<.5)return;
    uint3 tri=UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    float3 weights=float3(1-attributes.barycentrics.x-attributes.barycentrics.y,attributes.barycentrics);
    float3 p0=UnityRayTracingFetchVertexAttribute3(tri.x,kVertexAttributePosition);
    float3 p1=UnityRayTracingFetchVertexAttribute3(tri.y,kVertexAttributePosition);
    float3 p2=UnityRayTracingFetchVertexAttribute3(tri.z,kVertexAttributePosition);
    float3 edge1=mul((float3x3)ObjectToWorld3x4(),p1-p0),edge2=mul((float3x3)ObjectToWorld3x4(),p2-p0);
    float3 geo=HoGISafeNormal(cross(edge1,edge2),float3(0,1,0));
    if(dot(geo,WorldRayDirection())>0)geo=-geo;
    float face=HitKind()==HIT_KIND_TRIANGLE_FRONT_FACE?1:-1;
    float2 uv=HoGIUV(tri,weights,kVertexAttributeTexCoord0);
    float3 color=_MainTex.SampleLevel(sampler_MainTex,HoGITransformUV(uv,_MainTex_ST),0).rgb;
    if(_HoRTMaterialFamily>2.5&&_HoRTMaterialFamily<3.5)
    {
        float3 hsv=RgbToHsv(max(color,0));hsv.x=frac(hsv.x+_HoRTColorAdjustment.x);
        hsv.y=saturate(hsv.y*_HoRTColorAdjustment.y);hsv.z*=max(_HoRTColorAdjustment.z,0);
        color=PositivePow(max(HsvToRgb(hsv),0),rcp(max(_HoRTColorAdjustment.w,.001)));
    }
    float4 tension=HoGIReadTension(tri,weights);
    payload.baseColor=HoGITensionAlbedo(color*_Color.rgb,uv,tension);
    float3 n;
    float3 viewTS=0;
    if(_HoRTMaterialFamily==1)
    {
        // Keep the bounded legacy producer's existing attribute/frame policy.
        n=UnityRayTracingFetchVertexAttribute3(tri.x,kVertexAttributeNormal)*weights.x
            +UnityRayTracingFetchVertexAttribute3(tri.y,kVertexAttributeNormal)*weights.y
            +UnityRayTracingFetchVertexAttribute3(tri.z,kVertexAttributeNormal)*weights.z;
        n=HoGISafeNormal(mul(n,(float3x3)WorldToObject3x4()),geo);
        if(dot(n,geo)<0)n=-n;
        float4 tangent=UnityRayTracingFetchVertexAttribute4(tri.x,kVertexAttributeTangent)*weights.x
            +UnityRayTracingFetchVertexAttribute4(tri.y,kVertexAttributeTangent)*weights.y
            +UnityRayTracingFetchVertexAttribute4(tri.z,kVertexAttributeTangent)*weights.z;
        float3 t=mul((float3x3)ObjectToWorld3x4(),tangent.xyz);t-=n*dot(n,t);
        if(dot(t,t)>1e-10&&abs(tangent.w)>.5)
        {
            t=normalize(t);
            float handedness=tangent.w*(determinant((float3x3)ObjectToWorld3x4())<0?-1:1);
            float3 b=cross(n,t)*sign(handedness);
            viewTS=mul(float3x3(t,b,n),-WorldRayDirection());
            if(_HoRTNormalEnabled>.5)
            {
                float3 normalTS=UnpackNormalScale(_BumpMap.SampleLevel(sampler_BumpMap,HoGITransformUV(uv,_BumpMap_ST),0),_BumpScale);
                if(_HoRTMaterialFamily==1)normalTS=UnpackNormalScale(_BumpMap.SampleLevel(sampler_MainTex,HoGITransformUV(uv,_BumpMap_ST),0),_BumpScale);
                normalTS=HoGITensionNormal(normalTS,uv,tension);
                n=HoGISafeNormal(mul(normalTS,float3x3(t,b,n)),n);
            }
        }
    }
    else
    {
        HoGISpecializedFrame frame=HoGIGetSpecializedFrame(tri,weights,face);
        n=frame.fallback;
        if(frame.mapped&&_HoRTNormalEnabled>.5)
        {
            float3 normalTS=UnpackNormalScale(_BumpMap.SampleLevel(sampler_BumpMap,HoGITransformUV(uv,_BumpMap_ST),0),_BumpScale);
            normalTS=HoGITensionNormal(normalTS,uv,tension);
            n=HoGISafeNormal(mul(normalTS,float3x3(frame.tangent,frame.bitangent,frame.normal)),frame.fallback);
        }
        n*=frame.facing;
    }
    payload.normalWS=n;payload.geometricNormalWS=geo;
    HoGIPBR(uv,payload);
    payload.emission=HoGIEmission(tri,weights,uv,payload.baseColor,viewTS,abs(dot(n,-WorldRayDirection())));payload.status=1;
}
