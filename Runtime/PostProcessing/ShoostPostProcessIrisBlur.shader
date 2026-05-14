Shader "Hidden/lilToon-Shoost/URP/Shoost/IrisBlur"
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float _Intensity;
        float4 _LayerColor;
        float4 _LayerParams0;
        float4 _LayerParams1;
        float4 _LayerParams2;
        float4 _LayerParams3;
        float _Radius;
        float _ScreenRatio;
        float2 _Center;
        float _CenterSize;
        float _Smoothness;
        float _BlurOffsetR;
        float _BlurOffsetG;
        float _BlurOffsetB;
        float _Distance;
        float _Angle;
        TEXTURE2D_X(_BlurredTex);
        SAMPLER(sampler_BlurredTex);

        static const int DiscKernelSampleNum_LQ = 12;
        static const float2 DiscKernel_LQ[DiscKernelSampleNum_LQ] =
        {
            float2(-0.326212, -0.40581),
            float2(-0.840144, -0.07358),
            float2(-0.695914, 0.457137),
            float2(-0.203345, 0.620716),
            float2(0.96234, -0.194983),
            float2(0.473434, -0.480026),
            float2(0.519456, 0.767022),
            float2(0.185461, -0.893124),
            float2(0.507431, 0.064425),
            float2(0.89642, 0.412458),
            float2(-0.32194, -0.932615),
            float2(-0.791559, -0.59771)
        };

        static const int DiscKernelSampleNum_HQ = 28;
        static const float3 DiscKernel_HQ[DiscKernelSampleNum_HQ] =
        {
            float3(0.62463, 0.54337, 0.82790),
            float3(-0.13414, -0.94488, 0.95435),
            float3(0.38772, -0.43475, 0.58253),
            float3(0.12126, -0.19282, 0.22778),
            float3(-0.20388, 0.11133, 0.23230),
            float3(0.83114, -0.29218, 0.88100),
            float3(0.10759, -0.57839, 0.58831),
            float3(0.28285, 0.79036, 0.83945),
            float3(-0.36622, 0.39516, 0.53876),
            float3(0.75591, 0.21916, 0.78704),
            float3(-0.52610, 0.02386, 0.52664),
            float3(-0.88216, -0.24471, 0.91547),
            float3(-0.48888, -0.29330, 0.57011),
            float3(0.44014, -0.08558, 0.44838),
            float3(0.21179, 0.51373, 0.55567),
            float3(0.05483, 0.95701, 0.95858),
            float3(-0.59001, -0.70509, 0.91938),
            float3(-0.80065, 0.24631, 0.83768),
            float3(-0.19424, -0.18402, 0.26757),
            float3(-0.43667, 0.76751, 0.88304),
            float3(0.21666, 0.11602, 0.24577),
            float3(0.15696, -0.85600, 0.87027),
            float3(-0.75821, 0.58363, 0.95682),
            float3(0.99284, -0.02904, 0.99327),
            float3(-0.22234, -0.57907, 0.62029),
            float3(0.55052, -0.66984, 0.86704),
            float3(0.46431, 0.28115, 0.54280),
            float3(-0.07214, 0.60554, 0.60982)
        };

        float2 ResolveCenter()
        {
            float2 center = _Center;
            if (abs(center.x) + abs(center.y) < 0.0001)
            {
                center = float2(0.5, 0.5);
            }

            return center;
        }

        float ComputeMask(float2 uv)
        {
            float2 center = ResolveCenter();
            float2 delta = uv - center;
            delta.x *= max(_ScreenRatio, 0.0001);

            float innerRadius = saturate(_CenterSize);
            float feather = max(_Smoothness, 0.0001);
            float edge0 = max(innerRadius - feather, 0.0001);
            float edge1 = max(innerRadius, edge0 + 0.0001);
            return saturate(smoothstep(edge0, edge1, length(delta)) * saturate(_Intensity));
        }

        float2 ComputeRgbDirection()
        {
            float2 direction = float2(cos(_Angle), sin(_Angle));
            return direction * _Distance * 0.02;
        }

        half3 SampleRgbBlur(float2 uv, float2 kernelOffset)
        {
            float2 rgbDirection = ComputeRgbDirection();
            half r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + kernelOffset + (rgbDirection * _BlurOffsetR)).r;
            half g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + kernelOffset + (rgbDirection * _BlurOffsetG)).g;
            half b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + kernelOffset + (rgbDirection * _BlurOffsetB)).b;
            return half3(r, g, b);
        }

        half4 FragIrisHQ(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float mask = ComputeMask(input.texcoord);
            float2 poissonScale = _BlitTexture_TexelSize.xy * max(_Radius, 0.0001) * 30.0 * mask;
            poissonScale.x *= max(_ScreenRatio, 0.0001);
            half3 sum = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;

            [unroll]
            for (int i = 0; i < DiscKernelSampleNum_HQ; i++)
            {
                float2 sampleUV = input.texcoord + (DiscKernel_HQ[i].xy * poissonScale);
                half3 sampleColor = sum;
                if (_LayerParams3.x > 0.5)
                {
                    sampleColor = SampleRgbBlur(sampleUV, float2(0.0, 0.0));
                }
                else
                {
                    sampleColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUV).rgb;
                }

                sum += sampleColor;
            }

            return half4(sum / (1.0 + DiscKernelSampleNum_HQ), mask);
        }

        half4 FragIrisLQ(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float mask = ComputeMask(input.texcoord);
            float2 poissonScale = _BlitTexture_TexelSize.xy * max(_Radius, 0.0001) * 30.0 * mask;
            poissonScale.x *= max(_ScreenRatio, 0.0001);
            half3 sum = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;

            [unroll]
            for (int i = 0; i < DiscKernelSampleNum_LQ; i++)
            {
                float2 sampleUV = input.texcoord + (DiscKernel_LQ[i] * poissonScale);
                half3 sampleColor = sum;
                if (_LayerParams3.x > 0.5)
                {
                    sampleColor = SampleRgbBlur(sampleUV, float2(0.0, 0.0));
                }
                else
                {
                    sampleColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUV).rgb;
                }

                sum += sampleColor;
            }

            return half4(sum / (1.0 + DiscKernelSampleNum_LQ), mask);
        }

        half4 FragBlend(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            half4 sourceColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
            half4 blurredColor = SAMPLE_TEXTURE2D_X(_BlurredTex, sampler_BlurredTex, input.texcoord);
            half3 finalColor = lerp(sourceColor.rgb, blurredColor.rgb, saturate(blurredColor.a));
            return half4(finalColor, sourceColor.a);
        }
        ENDHLSL

        Pass
        {
            Name "Shoost Iris Blur High"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragIrisHQ
            #pragma shader_feature_local _ ENABLE_RGBSPLIT
            ENDHLSL
        }

        Pass
        {
            Name "Shoost Iris Blur Low"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragIrisLQ
            #pragma shader_feature_local _ ENABLE_RGBSPLIT
            ENDHLSL
        }

        Pass
        {
            Name "Shoost Iris Blur Blend"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlend
            ENDHLSL
        }
    }

    Fallback Off
}
