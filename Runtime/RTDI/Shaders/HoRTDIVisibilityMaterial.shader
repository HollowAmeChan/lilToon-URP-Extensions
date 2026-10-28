Shader "Hidden/HoRTDI/VisibilityMaterial"
{
    Properties
    {
        _MainTex("Main",2D)="white"{}
        _Color("Color",Color)=(1,1,1,1)
        _MainTex_ScrollRotate("UV",Vector)=(0,0,0,0)
        _AlphaMask("Alpha mask",2D)="white"{}
        _AlphaMaskMode("Mask mode",Float)=0
        _AlphaMaskScale("Mask scale",Float)=1
        _AlphaMaskValue("Mask value",Float)=0
        _Cutoff("Cutoff",Float)=.5
        _ShiftBackfaceUV("Back UV",Float)=0
        _HoRTDICutout("Cutout enabled",Float)=0
    }
    SubShader
    {
        Pass
        {
            Name "HoRTDIVisibility"
            Tags { "LightMode"="HoRTDIVisibility" }
            HLSLPROGRAM
            #pragma raytracing HoRTDIVisibility
            #include "UnityRayTracingMeshUtils.cginc"
            #include "HoRTDIVisibilityPayload.hlsl"
            Texture2D<float4> _MainTex,_AlphaMask;
            SamplerState sampler_MainTex;
            float4 _MainTex_ST,_AlphaMask_ST,_MainTex_ScrollRotate,_Color;
            float _Cutoff,_AlphaMaskMode,_AlphaMaskScale,_AlphaMaskValue,_ShiftBackfaceUV,_HoRTDICutout;
            #include "../../RT/Shaders/HoRTCutoutAlpha.hlsl"
            struct HoRTDIAlphaAttributes { float2 barycentrics; };
            [shader("anyhit")]
            void HoRTDIAlphaHit(inout HoRTDIVisibilityPayload payload : SV_RayPayload, HoRTDIAlphaAttributes attributes : SV_IntersectionAttributes)
            {
                if(_HoRTDICutout<.5)return;
                uint3 tri=UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
                float3 weights=float3(1-attributes.barycentrics.x-attributes.barycentrics.y,attributes.barycentrics);
                float2 uv=UnityRayTracingFetchVertexAttribute2(tri.x,kVertexAttributeTexCoord0)*weights.x
                    +UnityRayTracingFetchVertexAttribute2(tri.y,kVertexAttributeTexCoord0)*weights.y
                    +UnityRayTracingFetchVertexAttribute2(tri.z,kVertexAttributeTexCoord0)*weights.z;
                if(HoRTEvaluateCutoutAlpha(uv,HitKind()==HIT_KIND_TRIANGLE_BACK_FACE)<_Cutoff)IgnoreHit();
            }
            [shader("closesthit")]
            void HoRTDIOccluded(inout HoRTDIVisibilityPayload payload : SV_RayPayload,HoRTDIAlphaAttributes attributes : SV_IntersectionAttributes)
            {payload.blocked=1;}
            ENDHLSL
        }
    }
}
