Shader "Hidden/lilToon-HoCharacterSpecialization/URP/SubjectOutline"
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
            Name "HoCharacter SubjectOutline Source"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _HoCharacterSubjectOutlineSourceParams; // x semantic channel index (0..7)
            // 角色语义位平面（OB 身份池 + 覆盖率打包）：一位一个通道，值是覆盖率 —— 已经是连续场，
            // 所以这里点采样，没有再"读抗锯齿副本"这回事。
            TEXTURE2D_X(_lilHoCharacterObjectSemantic0_3Texture);
            TEXTURE2D_X(_lilHoCharacterObjectSemantic4_7Texture);

            float SampleSemanticChannel(float2 uv, int channelIndex)
            {
                int clampedIndex = (int)clamp((float)channelIndex, 0.0, 7.0);
                float4 lowValues = SAMPLE_TEXTURE2D_X(_lilHoCharacterObjectSemantic0_3Texture, sampler_PointClamp, uv);
                float4 highValues = SAMPLE_TEXTURE2D_X(_lilHoCharacterObjectSemantic4_7Texture, sampler_PointClamp, uv);
                if (clampedIndex == 0)
                {
                    return lowValues.r;
                }

                if (clampedIndex == 1)
                {
                    return lowValues.g;
                }

                if (clampedIndex == 2)
                {
                    return lowValues.b;
                }

                if (clampedIndex == 3)
                {
                    return lowValues.a;
                }

                if (clampedIndex == 4)
                {
                    return highValues.r;
                }

                if (clampedIndex == 5)
                {
                    return highValues.g;
                }

                if (clampedIndex == 6)
                {
                    return highValues.b;
                }

                return highValues.a;
            }

            float Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                int channelIndex = (int)round(_HoCharacterSubjectOutlineSourceParams.x);
                float subject = SampleSemanticChannel(input.texcoord, channelIndex);
                float mask = saturate(subject);
                return mask;
            }
            ENDHLSL
        }

        Pass
        {
            Name "HoCharacter SubjectOutline Blur"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #define USE_FULL_PRECISION_BLIT_TEXTURE
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _HoCharacterSubjectOutlineBlurParams; // x radius px in source texture, y phase
            static const float LIL_HOCHARACTER_GOLDEN_ANGLE = 2.39996323;
            static const int LIL_HOCHARACTER_SUBJECT_OUTLINE_SAMPLES = 64;

            float Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float radiusPx = max(_HoCharacterSubjectOutlineBlurParams.x, 0.0);
                float phase = _HoCharacterSubjectOutlineBlurParams.y;
                float2 radiusUv = _BlitTexture_TexelSize.xy * radiusPx;

                float center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).r;
                float sum = center * 1.8;
                float weightSum = 1.8;

                [unroll]
                for (int i = 0; i < LIL_HOCHARACTER_SUBJECT_OUTLINE_SAMPLES; i++)
                {
                    float sample01 = ((float)i + 0.5) / (float)LIL_HOCHARACTER_SUBJECT_OUTLINE_SAMPLES;
                    float radius01 = sqrt(sample01);
                    float angle = (float)i * LIL_HOCHARACTER_GOLDEN_ANGLE + phase;
                    float s;
                    float c;
                    sincos(angle, s, c);
                    float weight = exp2(-radius01 * radius01 * 4.6);
                    float2 sampleUv = uv + float2(c, s) * radiusUv * radius01;
                    sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUv).r * weight;
                    weightSum += weight;
                }

                return sum / max(weightSum, 0.0001);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
