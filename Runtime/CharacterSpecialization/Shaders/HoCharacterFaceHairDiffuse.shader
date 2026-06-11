Shader "Hidden/lilToon-HoCharacterSpecialization/URP/FaceHairDiffuse"
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
            Name "HoCharacter FaceHair Source"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _HoMetadataBufferActive;
            TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture);
            TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture);
            TEXTURE2D_X(_HoMetadataBufferSurfaceColorTexture);

            struct FaceHairSourceOutput
            {
                half4 color : SV_Target0;
                half4 depth : SV_Target1;
            };

            FaceHairSourceOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float face = SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture, sampler_PointClamp, uv).g;
                float4 surfaceColor = SAMPLE_TEXTURE2D_X(_HoMetadataBufferSurfaceColorTexture, sampler_LinearClamp, uv);
                float4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
                float mask = step(0.5, _HoMetadataBufferActive) * saturate(face * surfaceColor.a) * step(0.0001, normalDepth.a);

                FaceHairSourceOutput output;
                output.color = half4(surfaceColor.rgb * mask, mask);
                output.depth = half4(normalDepth.a * mask, 0.0, 0.0, mask);
                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Name "HoCharacter FaceHair Blur"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _HoCharacterFaceHairDiffuseBlurParams; // x radius px in source texture, y phase
            TEXTURE2D_X(_lilHoCharacterFaceHairDiffuseDepthTexture);
            static const float LIL_HOCHARACTER_GOLDEN_ANGLE = 2.39996323;
            static const int LIL_HOCHARACTER_FACE_HAIR_FAST_GAUSSIAN_SAMPLES = 40;

            struct FaceHairBlurOutput
            {
                half4 color : SV_Target0;
                half4 depth : SV_Target1;
            };

            FaceHairBlurOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float radiusPx = max(_HoCharacterFaceHairDiffuseBlurParams.x, 0.0);
                float phase = _HoCharacterFaceHairDiffuseBlurParams.y;
                float2 radiusUv = _BlitTexture_TexelSize.xy * radiusPx;

                float4 centerColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float4 centerDepth = SAMPLE_TEXTURE2D_X(_lilHoCharacterFaceHairDiffuseDepthTexture, sampler_LinearClamp, uv);
                float4 colorSum = centerColor * 1.6;
                float4 depthSum = centerDepth * 1.6;
                float weightSum = 1.6;

                [unroll]
                for (int i = 0; i < LIL_HOCHARACTER_FACE_HAIR_FAST_GAUSSIAN_SAMPLES; i++)
                {
                    float sample01 = ((float)i + 0.5) / (float)LIL_HOCHARACTER_FACE_HAIR_FAST_GAUSSIAN_SAMPLES;
                    float radius01 = sqrt(sample01);
                    float angle = (float)i * LIL_HOCHARACTER_GOLDEN_ANGLE + phase;
                    float s;
                    float c;
                    sincos(angle, s, c);
                    float weight = exp2(-radius01 * radius01 * 4.0);
                    float2 sampleUv = uv + float2(c, s) * radiusUv * radius01;
                    colorSum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUv) * weight;
                    depthSum += SAMPLE_TEXTURE2D_X(_lilHoCharacterFaceHairDiffuseDepthTexture, sampler_LinearClamp, sampleUv) * weight;
                    weightSum += weight;
                }

                FaceHairBlurOutput output;
                output.color = half4(colorSum / max(weightSum, 0.0001));
                output.depth = half4(depthSum / max(weightSum, 0.0001));
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
