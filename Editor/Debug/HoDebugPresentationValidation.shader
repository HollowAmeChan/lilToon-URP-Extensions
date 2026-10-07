Shader "Hidden/HoDebugPresentationValidation"
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
            float _ValidationRival;
            float _LayerMaskDebugOutput;
            half4 Frag(Varyings input) : SV_Target
            {
                if (_LayerMaskDebugOutput > .5) return _ValidationRival > 0 ? half4(1, 1, 0, 1) : half4(0, 1, 1, 1);
                return _ValidationRival > 0 ? half4(1, 1, 0, 1) : half4(input.texcoord, 0.25, 1);
            }
            ENDHLSL
        }
    }
}
