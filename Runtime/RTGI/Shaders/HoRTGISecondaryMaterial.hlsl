// Material-hit adapter for the reference BrdfRayTracing -> ShadeSecondarySurfaces
// chain. Attribute fetch follows its SurfaceRayTracingCommon closest-hit route;
// lilToon material components are called directly, without forward lighting.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "UnityRayTracingMeshUtils.cginc"
#include "HoRTGISecondarySurface.hlsl"

float3 HoGIUnpackEnvironmentNormal(float4 normalSample, float scale)
{
    return UnpackNormalScale(normalSample, scale);
}

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
float _HoRTGIEnvironment;
float _HoRTGIEnvironmentPBRMode;
float _HoRTGIEnvironmentNormalMap;
float _HoRTGIEnvironmentEmission;

SAMPLER(sampler_BumpMap);
SAMPLER(sampler_PBRMap);
SAMPLER(sampler_MetallicGlossMap);
SAMPLER(sampler_ReflectanceTex);
SAMPLER(sampler_SmoothnessTex);
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

// The Environment material reads each map from the original mesh UV. Keep
// those transforms distinct from the lilToon main-UV chains below.
void HoGIEvaluateEnvironment(float2 uv, float3 normalWS, float4 tangentOS,
    inout HoRTGISecondarySurface surface)
{
    float4 baseSample = _MainTex.SampleLevel(sampler_MainTex, TRANSFORM_TEX(uv, _MainTex), 0);
    surface.baseColor = baseSample.rgb * _Color.rgb;
    float metallic;
    float reflectanceWeight;
    float smoothness;
    [branch] if (_HoRTGIEnvironmentPBRMode >= 0.5)
    {
        float4 packed = _PBRMap.SampleLevel(sampler_PBRMap, TRANSFORM_TEX(uv, _PBRMap), 0);
        metallic = packed.r;
        reflectanceWeight = packed.g;
        smoothness = packed.a;
    }
    else
    {
        metallic = _MetallicGlossMap.SampleLevel(sampler_MetallicGlossMap, TRANSFORM_TEX(uv, _MetallicGlossMap), 0).r;
        reflectanceWeight = _ReflectanceTex.SampleLevel(sampler_ReflectanceTex, TRANSFORM_TEX(uv, _ReflectanceTex), 0).r;
        smoothness = _SmoothnessTex.SampleLevel(sampler_SmoothnessTex, TRANSFORM_TEX(uv, _SmoothnessTex), 0).r;
    }

    surface.metallic = saturate(_Metallic * metallic);
    surface.reflectance = saturate(_Reflectance * reflectanceWeight);
    surface.perceptualRoughness = 1.0 - saturate(_Smoothness * smoothness);
    if (_HoRTGIEnvironmentNormalMap > 0.5 && dot(tangentOS.xyz, tangentOS.xyz) > 1e-10)
    {
        float3x3 objectToWorld = (float3x3)ObjectToWorld3x4();
        float3 tangentWS = normalize(mul(objectToWorld, tangentOS.xyz));
        float handedness = tangentOS.w * (determinant(objectToWorld) < 0 ? -1 : 1);
        float3 bitangentWS = handedness * cross(normalWS, tangentWS);
        float4 normalSample = _BumpMap.SampleLevel(sampler_BumpMap, TRANSFORM_TEX(uv, _BumpMap), 0);
        float3 normalTS = HoGIUnpackEnvironmentNormal(normalSample, _BumpScale);
        normalWS = normalize(mul(normalTS, float3x3(tangentWS, bitangentWS, normalWS)));
    }
    float faceSign = _FlipNormal > 0.5 && HitKind() == HIT_KIND_TRIANGLE_BACK_FACE ? -1.0 : 1.0;
    surface.normalWS = normalWS * faceSign;
    if (_HoRTGIEnvironmentEmission > 0.5)
    {
        float3 emission = _EmissionMap.SampleLevel(sampler_EmissionMap, TRANSFORM_TEX(uv, _EmissionMap), 0).rgb;
        surface.emission = max(0, emission * _EmissionColor.rgb);
    }
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
    [branch] if (_HoRTGIEnvironment > 0.5)
    {
        HoGIEvaluateEnvironment(HoGIUV(tri, weights, kVertexAttributeTexCoord0), n, tangent, payload);
        payload.geometricNormalWS = geo;
        payload.status = 1;
        return;
    }
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
