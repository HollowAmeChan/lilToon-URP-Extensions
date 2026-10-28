Shader "Hidden/HoRTShadow/ActivationProbe"
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
            float _HoRTShadowActive;
            float4 Frag(Varyings input):SV_Target { return _HoRTShadowActive.xxxx; }
            ENDHLSL
        }
    }
}
