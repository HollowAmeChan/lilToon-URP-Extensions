Shader "Hidden/lilToon/URP/RayTracing/PrimarySurfaceValidation"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "HoRTPrimarySurfaceValidation.hlsl"
            float4 Frag(Varyings input) : SV_Target { return HoRTValidationPixel((uint2)input.positionCS.xy); }
            ENDHLSL
        }
    }
    Fallback Off
}
