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

            float _HoMetadataBufferActive;
            TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture);

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float subject = SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture, sampler_PointClamp, input.texcoord).r;
                float mask = step(0.5, _HoMetadataBufferActive) * saturate(subject);
                return half4(mask, 0.0, 0.0, 1.0);
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
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _HoCharacterSubjectOutlineBlurParams; // x radius px in source texture, y phase
            static const float LIL_HOCHARACTER_GOLDEN_ANGLE = 2.39996323;
            static const int LIL_HOCHARACTER_SUBJECT_OUTLINE_SAMPLES = 64;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float radiusPx = max(_HoCharacterSubjectOutlineBlurParams.x, 0.0);
                float phase = _HoCharacterSubjectOutlineBlurParams.y;
                float2 radiusUv = _BlitTexture_TexelSize.xy * radiusPx;

                float center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).r;
                float maskSum = center * 1.8;
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
                    maskSum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUv).r * weight;
                    weightSum += weight;
                }

                float mask = maskSum / max(weightSum, 0.0001);
                return half4(mask, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
