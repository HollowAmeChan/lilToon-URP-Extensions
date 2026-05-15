Shader "Hidden/lilToon-Shoost/URP/Shoost/GrainCustom"
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
            Name "Shoost Grain Custom"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGrainCustom

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0;
            float _LayerTextureEnabled;
            TEXTURE2D_X(_LayerTexture);

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float3 Hash32(float2 p)
            {
                return float3(
                    Hash12(p),
                    Hash12(p + 17.17),
                    Hash12(p + 43.43));
            }

            float3 DecodeGrainNoise(float4 sampleValue)
            {
                return (sampleValue.r + sampleValue.g + sampleValue.b) > 0.0001 ? sampleValue.rgb : sampleValue.aaa;
            }

            half4 FragGrainCustom(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float amount = saturate(_Intensity) * saturate(_LayerParams0.x);
                if (amount <= 0.0001)
                {
                    return source;
                }

                float grainSize = max(_LayerParams0.y, 0.3);
                float lumContrib = _LayerParams0.z > 0.0001 ? saturate(_LayerParams0.z) : 0.9;
                float colored = step(0.5, _LayerParams0.w);
                float2 tiling = max(_ScreenParams.xy / (128.0 * grainSize), 1.0);
                float frame = floor(_Time.y * 60.0);
                float2 phase = frac(float2(frame * 0.06711056, frame * 0.00583715));
                float2 grainUV = input.texcoord * tiling + phase;

                float3 brightNoise;
                float3 darkNoise;
                if (_LayerTextureEnabled > 0.5)
                {
                    brightNoise = DecodeGrainNoise(SAMPLE_TEXTURE2D_X(_LayerTexture, sampler_PointRepeat, grainUV));
                    darkNoise = DecodeGrainNoise(SAMPLE_TEXTURE2D_X(_LayerTexture, sampler_PointRepeat, grainUV + 0.5));
                }
                else
                {
                    float2 cell = floor(grainUV * 128.0);
                    brightNoise = Hash32(cell);
                    darkNoise = Hash32(cell + 71.71);
                }

                float luma = sqrt(dot(saturate(source.rgb), float3(0.2126729, 0.7151522, 0.0721750)));
                float lumaMask = 1.0 - lumContrib * luma;
                float grainWeight = amount * lumaMask;

                brightNoise = lerp(brightNoise.rrr, brightNoise, colored);
                darkNoise = lerp(darkNoise.rrr, darkNoise, colored);

                float3 grained = source.rgb + grainWeight * (source.rgb * (1.0 - darkNoise) - source.rgb);
                grained += grained * brightNoise * grainWeight;
                return half4(saturate(grained), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
