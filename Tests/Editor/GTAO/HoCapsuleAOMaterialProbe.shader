Shader "Hidden/HoRP/Tests/MaterialAOOffProbe"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        _AOSource ("Material AO Source", Float) = 0
        _UseAODarkening ("Material AO Darkening", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _BaseColor;
            float _AOSource, _UseAODarkening;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vertex(Attributes input)
            {
                Varyings output; output.positionCS=TransformObjectToHClip(input.positionOS.xyz); return output;
            }
            float4 Fragment(Varyings input) : SV_Target
            {
                // The receiver overlay belongs to the pipeline. This material deliberately
                // samples no AO producer when its own AO source/darkening are disabled.
                return _BaseColor;
            }
            ENDHLSL
        }
    }
}
