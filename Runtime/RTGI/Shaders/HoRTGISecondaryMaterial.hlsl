// Material-hit adapter for the reference BrdfRayTracing -> ShadeSecondarySurfaces
// chain. Attribute fetch follows its SurfaceRayTracingCommon closest-hit route;
// lilToon material components are called directly, without forward lighting.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "UnityRayTracingMeshUtils.cginc"
#include "HoRTGISecondarySurface.hlsl"
// This bounded entry must be updated together with lilToon's shared HLSL.
#define LIL_PASS_RTGI_SECONDARY
#define LIL_RENDER 0
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
#include "../../RT/Shaders/HoRTCutoutAlpha.hlsl"
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
    if(HoRTEvaluateCutoutAlpha(uv)<_Cutoff)IgnoreHit();
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
    fd.uvMain=lilCalcUV(fd.uv0,_MainTex_ST);
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
        fd.N=normalize(mul(normalmap,fd.TBN));
    }
    fd.albedo=fd.col.rgb;fd.invLighting=1;
    fd.nvabs=abs(dot(fd.N,fd.V));fd.uvRim=fd.nvabs.xx;
    float4 emission=lilEmissionComponent(fd LIL_SAMP_IN(sampler_MainTex));
    lilReadPBRInputs(fd LIL_SAMP_IN(sampler_MainTex));
    payload.status=1;payload.normalWS=fd.N;payload.geometricNormalWS=geo;
    payload.baseColor=fd.col.rgb;payload.perceptualRoughness=1-fd.smoothness;
    payload.metallic=fd.metallic;payload.reflectance=fd.dielectricF0;
    payload.emission=max(0,emission.rgb*emission.a);
}
