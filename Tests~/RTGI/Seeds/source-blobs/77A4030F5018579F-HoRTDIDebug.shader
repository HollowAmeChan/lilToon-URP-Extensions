Shader "Hidden/lilToon/URP/RTDI/Debug"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            TEXTURE2D(_HoRTDIDiffuseTexture);TEXTURE2D(_HoRTDISpecularTexture);
            TEXTURE2D(_HoRTDIRawDiffuseTexture);TEXTURE2D(_HoRTDIRawSpecularTexture);
            float _HoRTDIDebugMode;
            float4 Frag(Varyings input):SV_Target
            {
                float4 diffuse=SAMPLE_TEXTURE2D(_HoRTDIDiffuseTexture,sampler_PointClamp,input.texcoord);
                if(_HoRTDIDebugMode>4.5)
                {
                    float3 raw=_HoRTDIDebugMode<5.5?SAMPLE_TEXTURE2D(_HoRTDIRawDiffuseTexture,sampler_PointClamp,input.texcoord).rgb:SAMPLE_TEXTURE2D(_HoRTDIRawSpecularTexture,sampler_PointClamp,input.texcoord).rgb;
                    return float4(raw,1);
                }
                if(_HoRTDIDebugMode>3.5)return diffuse.a>.5?float4(0,1,0,1):float4(1,0,0,1);
                float3 d=diffuse.rgb;
                float3 s=SAMPLE_TEXTURE2D(_HoRTDISpecularTexture,sampler_PointClamp,input.texcoord).rgb;
                return float4(_HoRTDIDebugMode<1.5?d:_HoRTDIDebugMode<2.5?s:d+s,1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
