// Material-hit adapter for the reference BrdfRayTracing -> ShadeSecondarySurfaces
// chain. Attribute fetch follows its SurfaceRayTracingCommon closest-hit route;
// lilToon material components are called directly, without forward lighting.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "UnityRayTracingMeshUtils.cginc"
#include "HoRTGISecondarySurface.hlsl"
#define LIL_URP
#define LIL_RENDER 0
#define LIL_FEATURE_EMISSION_1ST
#define LIL_FEATURE_EMISSION_2ND
#define LIL_FEATURE_EmissionMap
#define LIL_FEATURE_EmissionBlendMask
#define LIL_FEATURE_EmissionGradTex
#define LIL_FEATURE_Emission2ndMap
#define LIL_FEATURE_Emission2ndBlendMask
#define LIL_FEATURE_Emission2ndGradTex
#define LIL_FEATURE_EMISSION_GRADATION
#define LIL_FEATURE_ANIMATE_EMISSION_UV
#define LIL_FEATURE_ANIMATE_EMISSION_MASK_UV
#define LIL_FEATURE_ANIMATE_MAIN_UV
#define LIL_FEATURE_AlphaMask
#define LIL_FEATURE_NORMAL_1ST
#define LIL_FEATURE_NORMAL_2ND
#define LIL_FEATURE_BumpMap
#define LIL_FEATURE_Bump2ndMap
#define LIL_FEATURE_Bump2ndScaleMask
#define LIL_FEATURE_SmoothnessTex
#define LIL_FEATURE_MetallicGlossMap
#include "Packages/jp.lilxyzw.liltoon/Shader/Includes/lil_common_macro.hlsl"
#undef LIL_MATRIX_M
float4x4 HoGIObjectToWorld() { float3x4 m=ObjectToWorld3x4();return float4x4(m[0],m[1],m[2],float4(0,0,0,1)); }
#define LIL_MATRIX_M HoGIObjectToWorld()
// No screen derivatives at a secondary hit. Like the reference hit material,
// explicitly sample LOD 0 until ray-cone filtering is introduced.
#undef LIL_SAMPLE_2D
#define LIL_SAMPLE_2D(tex,samp,uv) tex.SampleLevel(samp,uv,0)
#undef LIL_SAMPLE_2D_GRAD
#define LIL_SAMPLE_2D_GRAD(tex,samp,uv,dx,dy) tex.SampleLevel(samp,uv,0)
#undef LIL_SAMPLE_2D_ST
#define LIL_SAMPLE_2D_ST(tex,samp,uv) tex.SampleLevel(samp,(uv)*tex##_ST.xy+tex##_ST.zw,0)
#undef LIL_SAMPLE_1D
#define LIL_SAMPLE_1D(tex,samp,uv) tex.SampleLevel(samp,float2(uv,.5),0)
#include "Packages/jp.lilxyzw.liltoon/Shader/Includes/lil_common.hlsl"
#include "Packages/jp.lilxyzw.liltoon/Shader/Includes/lil_common_frag.hlsl"

float _HoRTGISecondarySupported,_HoRTGICutout;
// LIL_RENDER=0 keeps the opaque material component layout; its optional alpha
// uniforms are therefore explicit for this private any-hit adapter.
float4 _AlphaMask_ST;
float _AlphaMaskMode,_AlphaMaskScale,_AlphaMaskValue;
struct HoRTGIAttributes { float2 barycentrics; };
float2 HoGIUV(uint3 tri, float3 weights, uint attribute)
{
    return UnityRayTracingFetchVertexAttribute2(tri.x,attribute)*weights.x
         + UnityRayTracingFetchVertexAttribute2(tri.y,attribute)*weights.y
         + UnityRayTracingFetchVertexAttribute2(tri.z,attribute)*weights.z;
}

// Same bounded alpha chain as DI's verified native visibility material.
[shader("anyhit")]
void HoRTGIAlphaHit(inout HoRTGISecondarySurface payload : SV_RayPayload,
    HoRTGIAttributes attributes : SV_IntersectionAttributes)
{
    if(_HoRTGICutout<.5)return;
    uint3 tri=UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    float3 weights=float3(1-attributes.barycentrics.x-attributes.barycentrics.y,attributes.barycentrics);
    float2 uv=HoGIUV(tri,weights,kVertexAttributeTexCoord0);
    if(HitKind()==HIT_KIND_TRIANGLE_BACK_FACE&&_ShiftBackfaceUV>0)uv.x+=1;
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
    if(alpha<_Cutoff)IgnoreHit();
}

[shader("closesthit")]
void HoRTGIClosestHit(inout HoRTGISecondarySurface payload : SV_RayPayload,
    HoRTGIAttributes attributes : SV_IntersectionAttributes)
{
    bool visibilityOnly=payload.status==HO_RTGI_VISIBILITY_QUERY;
    payload=(HoRTGISecondarySurface)0;
    payload.distance=RayTCurrent();
    payload.status=2;
    if(visibilityOnly||_HoRTGISecondarySupported<.5)return;
    uint3 tri=UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    float3 weights=float3(1-attributes.barycentrics.x-attributes.barycentrics.y,attributes.barycentrics);
    float3 p0=UnityRayTracingFetchVertexAttribute3(tri.x,kVertexAttributePosition);
    float3 p1=UnityRayTracingFetchVertexAttribute3(tri.y,kVertexAttributePosition);
    float3 p2=UnityRayTracingFetchVertexAttribute3(tri.z,kVertexAttributePosition);
    float3 n=UnityRayTracingFetchVertexAttribute3(tri.x,kVertexAttributeNormal)*weights.x
            +UnityRayTracingFetchVertexAttribute3(tri.y,kVertexAttributeNormal)*weights.y
            +UnityRayTracingFetchVertexAttribute3(tri.z,kVertexAttributeNormal)*weights.z;
    float4 tangent=UnityRayTracingFetchVertexAttribute4(tri.x,kVertexAttributeTangent)*weights.x
                  +UnityRayTracingFetchVertexAttribute4(tri.y,kVertexAttributeTangent)*weights.y
                  +UnityRayTracingFetchVertexAttribute4(tri.z,kVertexAttributeTangent)*weights.z;
    float3 edge1=mul((float3x3)ObjectToWorld3x4(),p1-p0);
    float3 edge2=mul((float3x3)ObjectToWorld3x4(),p2-p0);
    float3 geo=normalize(cross(edge1,edge2));
    if(dot(geo,WorldRayDirection())>0)geo=-geo;
    n=normalize(mul(n,(float3x3)WorldToObject3x4()));
    if(dot(n,geo)<0)n=-n;
    lilFragData fd=lilInitFragData();
    fd.positionWS=WorldRayOrigin()+WorldRayDirection()*RayTCurrent();
    fd.positionOS=p0*weights.x+p1*weights.y+p2*weights.z;
    fd.N=n;fd.origN=n;fd.V=-WorldRayDirection();fd.facing=HitKind()==HIT_KIND_TRIANGLE_FRONT_FACE?1:-1;
    fd.uv0=HoGIUV(tri,weights,kVertexAttributeTexCoord0);
    fd.uv1=HoGIUV(tri,weights,kVertexAttributeTexCoord1);
    fd.uv2=HoGIUV(tri,weights,kVertexAttributeTexCoord2);
    fd.uv3=HoGIUV(tri,weights,kVertexAttributeTexCoord3);
    float2 mainUV=fd.uv0;
    if(_HoRTGICutout>.5&&HitKind()==HIT_KIND_TRIANGLE_BACK_FACE&&_ShiftBackfaceUV>0)mainUV.x+=1;
    fd.uvMain=lilCalcUV(mainUV,_MainTex_ST,_MainTex_ScrollRotate);
    // Same main chain as the opaque SB producer; no ramp/shadow/matcap/rim lighting.
    OVERRIDE_MAIN
    if(dot(tangent.xyz,tangent.xyz)>1e-10)
    {
        fd.T=normalize(mul((float3x3)ObjectToWorld3x4(),tangent.xyz));
        float handedness=tangent.w*(determinant((float3x3)ObjectToWorld3x4())<0?-1:1);
        fd.B=normalize(cross(n,fd.T))*handedness;fd.TBN=float3x3(fd.T,fd.B,n);
        LIL_GET_PARALLAX_DATA(unused,fd);
        float3 normalmap=float3(0,0,1);
        OVERRIDE_NORMAL_1ST
        OVERRIDE_NORMAL_2ND
        fd.N=normalize(mul(normalmap,fd.TBN));
    }
    fd.albedo=fd.col.rgb;fd.invLighting=1;
    fd.nvabs=abs(dot(fd.N,fd.V));fd.uvRim=fd.nvabs.xx;
    float4 first=lilEmissionComponent(fd LIL_SAMP_IN(sampler_MainTex));
    float4 second=lilEmission2ndComponent(fd LIL_SAMP_IN(sampler_MainTex));
    float smoothness=_Smoothness*LIL_SAMPLE_2D(_SmoothnessTex,sampler_MainTex,fd.uvMain).r;
    float metallic=_Metallic*LIL_SAMPLE_2D(_MetallicGlossMap,sampler_MainTex,fd.uvMain).r;
    payload.status=1;payload.normalWS=fd.N;payload.geometricNormalWS=geo;
    payload.baseColor=fd.col.rgb;payload.perceptualRoughness=saturate(1-smoothness);
    payload.metallic=saturate(metallic);payload.reflectance=saturate(_Reflectance);
    payload.emission=max(0,first.rgb*first.a+second.rgb*second.a);
}
