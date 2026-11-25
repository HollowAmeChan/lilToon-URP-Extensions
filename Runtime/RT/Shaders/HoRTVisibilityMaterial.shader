Shader "Hidden/HoRT/VisibilityMaterial"
{
    Properties
    {
        _MainTex("Main",2D)="white"{}
        [HDR] _Color("Canonical linear tint",Color)=(1,1,1,1)
        _AlphaMask("Alpha mask",2D)="white"{}
        _AlphaMaskMode("Mask mode",Float)=0
        _AlphaMaskScale("Mask scale",Float)=1
        _AlphaMaskValue("Mask value",Float)=0
        _Cutoff("Cutoff",Float)=.5
        _HoRTCutout("Cutout enabled",Float)=0
        _HoRTAlphaDomain("Alpha domain",Float)=0
    }
    SubShader
    {
        Pass
        {
            Name "HoRTVisibility"
            Tags { "LightMode"="HoRTVisibility" }
            HLSLPROGRAM
            #pragma raytracing HoRTVisibility
            #include "UnityRayTracingMeshUtils.cginc"
            #include "HoRTVisibilityPayload.hlsl"
            Texture2D<float4> _MainTex,_AlphaMask;
            SamplerState sampler_MainTex,sampler_AlphaMask;
            float4 _MainTex_ST,_AlphaMask_ST,_Color;
            float _Cutoff,_AlphaMaskMode,_AlphaMaskScale,_AlphaMaskValue,_HoRTCutout,_HoRTAlphaDomain;
            #include "HoRTCutoutAlpha.hlsl"
            struct HoRTVisibilityAttributes { float2 barycentrics; };
            [shader("anyhit")]
            void HoRTVisibilityAlpha(inout HoRTVisibilityHit hit : SV_RayPayload, HoRTVisibilityAttributes attributes : SV_IntersectionAttributes)
            {
                if(_HoRTCutout<.5)return;
                uint3 tri=UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
                float3 weights=float3(1-attributes.barycentrics.x-attributes.barycentrics.y,attributes.barycentrics);
                float2 uv=UnityRayTracingFetchVertexAttribute2(tri.x,kVertexAttributeTexCoord0)*weights.x
                    +UnityRayTracingFetchVertexAttribute2(tri.y,kVertexAttributeTexCoord0)*weights.y
                    +UnityRayTracingFetchVertexAttribute2(tri.z,kVertexAttributeTexCoord0)*weights.z;
                if(HoRTEvaluateCutoutAlpha(uv)<_Cutoff)IgnoreHit();
            }
            [shader("closesthit")]
            void HoRTVisibilityClosest(inout HoRTVisibilityHit hit : SV_RayPayload, HoRTVisibilityAttributes attributes : SV_IntersectionAttributes)
            {
                hit.status=1;hit.distance=RayTCurrent();hit.instance=InstanceID();hit.primitive=PrimitiveIndex();
                hit.barycentrics=attributes.barycentrics;hit.frontFace=HitKind()==HIT_KIND_TRIANGLE_FRONT_FACE;
            }
            ENDHLSL
        }
    }
}
