Shader "Hidden/lilToon/URP/RTGI/Debug"
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
            TEXTURE2D(_HoRTGIDiffuseTexture);
            float _HoRTGIDebugMode;
            float4 Frag(Varyings input):SV_Target
            {
                float4 diffuse=SAMPLE_TEXTURE2D(_HoRTGIDiffuseTexture,sampler_PointClamp,input.texcoord);
                if(_HoRTGIDebugMode>1.5)return diffuse.a>.5?float4(0,1,0,1):float4(1,0,0,1);
                float3 d=diffuse.rgb;
                return float4(d,1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
