Shader "Hidden/lilToon/URP/MaterialGradient/Demo"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _DemoRampTex ("Demo Ramp", 2D) = "white" {}
        [Enum(U,0,V,1)] _RampAxis ("Ramp Axis", Float) = 0
        _RampPower ("Ramp Power", Range(0.1, 4.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/MaterialGradient/Shaders/HoMaterialGradientSampling.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _RampAxis;
                float _RampPower;
            CBUFFER_END

            TEXTURE2D(_DemoRampTex);
            SAMPLER(sampler_DemoRampTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float axisSelector = step(0.5, _RampAxis);
                float t = lerp(input.uv.x, input.uv.y, axisSelector);
                t = pow(saturate(t), max(_RampPower, 0.0001));

                half4 ramp = HoSampleGradient(TEXTURE2D_ARGS(_DemoRampTex, sampler_DemoRampTex), t);
                return ramp * (half4)_BaseColor;
            }
            ENDHLSL
        }
    }

    CustomEditor "lilToon.URP.Extensions.Editor.MaterialGradient.HoMaterialGradientShaderGUI"
    Fallback Off
}
