Shader "Hidden/lilToon-Shoost/URP/Shoost/Pixelize"
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
            Name "Shoost Pixelize"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPixelize

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0;
            float4 _LayerParams1;

            float2 GetPresetResolution(float typeValue)
            {
                int resolutionType = (int)round(typeValue);
                if (resolutionType == 1)
                {
                    return float2(320.0, 240.0);
                }

                if (resolutionType == 2)
                {
                    return float2(640.0, 480.0);
                }

                if (resolutionType == 3)
                {
                    return float2(854.0, 480.0);
                }

                if (resolutionType == 4)
                {
                    return float2(1280.0, 720.0);
                }

                if (resolutionType == 5)
                {
                    return float2(1920.0, 1080.0);
                }

                float2 screenResolution = _ScreenParams.xy;
                if (_LayerParams0.y > 1.0 && _LayerParams0.z > 1.0)
                {
                    screenResolution = float2(_LayerParams0.y, _LayerParams0.z);
                }

                return screenResolution;
            }

            half4 SamplePixelated(float2 uv, float2 pixelCount)
            {
                float2 snappedUV = (floor(uv * pixelCount) + 0.5) / pixelCount;
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, snappedUV);
            }

            half4 FragPixelize(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float intensity = saturate(_Intensity);
                if (intensity <= 0.0001)
                {
                    return source;
                }

                float2 resolution = GetPresetResolution(_LayerParams0.x);
                float scale = saturate(_LayerParams0.w);
                resolution *= max(scale, 0.01);

                float aspect = _LayerParams1.x > 0.5 ? max(_LayerParams1.y, 0.01) : (_ScreenParams.x / max(_ScreenParams.y, 1.0));
                resolution.x *= aspect;
                float2 pixelCount = max(resolution, 1.0);

                half4 pixelated = SamplePixelated(input.texcoord, pixelCount);
                if (_LayerParams1.z > 0.5 && _LayerParams1.w > 0.0001)
                {
                    float2 offset = (_LayerParams1.w / pixelCount) * 0.5;
                    half4 sum = pixelated;
                    sum += SamplePixelated(input.texcoord + float2(offset.x, 0.0), pixelCount);
                    sum += SamplePixelated(input.texcoord - float2(offset.x, 0.0), pixelCount);
                    sum += SamplePixelated(input.texcoord + float2(0.0, offset.y), pixelCount);
                    sum += SamplePixelated(input.texcoord - float2(0.0, offset.y), pixelCount);
                    pixelated = sum * 0.2;
                }

                return lerp(source, pixelated, intensity);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
