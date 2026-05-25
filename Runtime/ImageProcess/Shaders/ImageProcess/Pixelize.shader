Shader "Hidden/lilToon/URP/ImageProcess/Pixelize"
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
            Name "ImageProcess Pixelize"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPixelize

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0;
            float4 _LayerParams1;

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

                float scale = saturate(_LayerParams0.w);
                float2 pixelCount = max(_ScreenParams.xy * max(scale, 0.01), 1.0);
                if (_LayerParams1.x > 0.5)
                {
                    pixelCount.x = max(pixelCount.y * max(_LayerParams1.y, 0.01), 1.0);
                }

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
