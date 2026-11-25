Shader "Hidden/Ho-Validation/AODisableProbe"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_HoAOTexture); SAMPLER(sampler_HoAOTexture);
        float _HoAOAvailable;
        struct V { float4 positionCS:SV_POSITION; };
        struct A { float3 positionOS:POSITION; };
        V Vert(A a) { V v; v.positionCS=TransformObjectToHClip(a.positionOS); return v; }
        V Fullscreen(uint id:SV_VertexID) { V v; v.positionCS=GetFullScreenTriangleVertexPosition(id); return v; }
        float4 Frag(V v):SV_Target
        {
            float raw=SAMPLE_TEXTURE2D(_HoAOTexture,sampler_HoAOTexture,float2(.5,.5)).r;
            return float4(raw,_HoAOAvailable,_HoAOAvailable>.5?raw:1,1);
        }
        ENDHLSL
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma vertex Fullscreen
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
