Shader "Hidden/lilToon/URP/ImageProcess/DownScaleResolution"
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
            Name "ImageProcess DownScale Resolution"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDownScaleResolution

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0;

            float2 ResolveResolution()
            {
                int resolutionType = (int)round(_LayerParams0.x);
                if (resolutionType == 2)
                {
                    return float2(320.0, 240.0);
                }

                if (resolutionType == 3)
                {
                    return float2(640.0, 480.0);
                }

                if (resolutionType == 4)
                {
                    return float2(854.0, 480.0);
                }

                if (resolutionType == 5)
                {
                    return float2(1280.0, 720.0);
                }

                if (resolutionType == 6)
                {
                    return float2(1920.0, 1080.0);
                }

                if (resolutionType == 1 && _LayerParams0.y > 1.0 && _LayerParams0.z > 1.0)
                {
                    return float2(_LayerParams0.y, _LayerParams0.z);
                }

                return _ScreenParams.xy;
            }

            half4 FragDownScaleResolution(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float intensity = saturate(_Intensity);
                if (intensity <= 0.0001)
                {
                    return source;
                }

                float2 resolution = max(ResolveResolution(), 1.0);
                float downScale = max(_LayerParams0.w, 1.0);
                float2 pixelCount = max(resolution / downScale, 1.0);
                float2 snappedUV = (floor(input.texcoord * pixelCount) + 0.5) / pixelCount;
                half4 downScaled = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, snappedUV);
                return lerp(source, downScaled, intensity);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
