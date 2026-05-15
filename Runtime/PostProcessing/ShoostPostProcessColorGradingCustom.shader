Shader "Hidden/lilToon-Shoost/URP/Shoost/ColorGradingCustom"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Shoost Color Grading"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0;
            float4 _LayerParams1;
            float4 _LayerParams2;

            float ShoostLuminance(float3 color)
            {
                return dot(color, float3(0.2126729, 0.7151522, 0.0721750));
            }

            float3 SrgbToLinear(float3 color)
            {
                color = max(color, 0.0);
                float3 high = pow((color + 0.055) / 1.055, 2.4);
                float3 low = color / 12.92;
                return lerp(high, low, step(color, float3(0.04045, 0.04045, 0.04045)));
            }

            float3 PrepareLift(float4 color)
            {
                float3 lift = SrgbToLinear(saturate(color.rgb)) * 0.15;
                float luminance = ShoostLuminance(lift);
                return lift - luminance + color.w;
            }

            float3 PrepareGamma(float4 color)
            {
                float3 gamma = SrgbToLinear(saturate(color.rgb)) * 0.8;
                float luminance = ShoostLuminance(gamma);
                return rcp(max(gamma - luminance + color.w + 1.0, 0.001));
            }

            float3 PrepareGain(float4 color)
            {
                float3 gain = SrgbToLinear(saturate(color.rgb)) * 0.8;
                float luminance = ShoostLuminance(gain);
                return gain - luminance + color.w + 1.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float3 lift = PrepareLift(_LayerParams0);
                float3 gamma = PrepareGamma(_LayerParams1);
                float3 gain = PrepareGain(_LayerParams2);
                float3 graded = pow(max(source.rgb + lift, 0.0), gamma) * gain;

                return half4(lerp(source.rgb, graded, saturate(_Intensity)), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
