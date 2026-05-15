Shader "Hidden/lilToon-Shoost/URP/Shoost/LevelAdjustment"
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
            Name "Shoost Level Adjustment"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0;
            float4 _LayerParams1;
            float4 _LayerParams2;
            float4 _LayerParams3;

            float ApplyLevels(float color, float inputBlack, float inputWhite, float inputGamma, float outputBlack, float outputWhite)
            {
                if (abs(inputBlack) + abs(inputWhite) + abs(inputGamma) + abs(outputBlack) + abs(outputWhite) <= 0.0001)
                {
                    return color;
                }

                float range = max(inputWhite - inputBlack, 0.0001);
                color = saturate((color - inputBlack) / range);
                color = pow(max(color, 0.0001), 1.0 / max(inputGamma, 0.0001));
                return lerp(outputBlack, outputWhite, color);
            }

            float3 ApplyChannelLevels(float3 color, int channel, float inputBlack, float inputWhite, float inputGamma, float outputBlack, float outputWhite)
            {
                float3 result = color;
                float value = channel == 1 ? color.r : channel == 2 ? color.g : color.b;
                value = ApplyLevels(value, inputBlack, inputWhite, inputGamma, outputBlack, outputWhite);

                if (channel == 1)
                {
                    result.r = value;
                }
                else if (channel == 2)
                {
                    result.g = value;
                }
                else
                {
                    result.b = value;
                }

                return result;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                int channel = clamp((int)round(_LayerParams1.y), 0, 3);
                float3 result = source.rgb;
                bool isUninitialized = abs(_LayerParams0.x) + abs(_LayerParams0.y) + abs(_LayerParams0.z) + abs(_LayerParams0.w) + abs(_LayerParams1.x) + abs(_LayerParams2.x) + abs(_LayerParams2.y) + abs(_LayerParams2.z) + abs(_LayerParams2.w) + abs(_LayerParams3.x) <= 0.0001;

                if (isUninitialized)
                {
                    return source;
                }

                if (channel == 0)
                {
                    result.r = ApplyLevels(result.r, _LayerParams0.x, _LayerParams0.y, _LayerParams0.z, _LayerParams0.w, _LayerParams1.x);
                    result.g = ApplyLevels(result.g, _LayerParams0.x, _LayerParams0.y, _LayerParams0.z, _LayerParams0.w, _LayerParams1.x);
                    result.b = ApplyLevels(result.b, _LayerParams0.x, _LayerParams0.y, _LayerParams0.z, _LayerParams0.w, _LayerParams1.x);
                }
                else
                {
                    result = ApplyChannelLevels(
                        result,
                        channel,
                        _LayerParams2.x,
                        _LayerParams2.y,
                        _LayerParams2.z,
                        _LayerParams2.w,
                        _LayerParams3.x);
                }

                float blend = saturate(_Intensity);
                float3 hdrResidual = max(source.rgb - saturate(source.rgb), 0.0);
                return half4(lerp(source.rgb, max(result, 0.0) + hdrResidual, blend), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
