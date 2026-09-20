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

            float4 _HoCharacterOptions; // x eye enabled, y shadow enabled, z same character only, w debug mode（源趟只读 w）
            TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture);
            // 脸标签的覆盖率（= 这张脸在这个像素上占多少），来自 OB 身份池打包的位平面：
            // 它同时充当"受光脸的采样权重"和"捕获覆盖率"，所以不再需要 SurfaceColor 那一份。
            TEXTURE2D_X(_lilHoCharacterObjectSemantic0_3Texture);
            // 脸色扩散的底色来源：强制脸捕获的 MRT0 —— CaptureFace 的材质把**算完光照的 color**
            // 写在这里（alpha=1），所以模糊的是"前发后面那张受光脸"，不再是不受光的 SurfaceColor。
            // 眼透开着时这张图里会混进眼睛像素（两趟捕获共用 MRT0），已知并接受。
            TEXTURE2D_X(_lilHoCharacterEyeColorTexture);

            struct FaceHairSourceOutput
            {
                half4 color : SV_Target0;
                half4 depth : SV_Target1;
            };

            FaceHairSourceOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float face = SAMPLE_TEXTURE2D_X(_lilHoCharacterObjectSemantic0_3Texture, sampler_PointClamp, uv).g;
                float4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
                float3 faceLit = SAMPLE_TEXTURE2D_X(_lilHoCharacterEyeColorTexture, sampler_LinearClamp, uv).rgb;
                float mask = saturate(face) * step(0.0001, normalDepth.a);

                FaceHairSourceOutput output;
                int debugMode = (int)round(_HoCharacterOptions.w);
                if (debugMode == 18)
                {
                    // ① 捕获到的受光脸（原始采样）：不乘语义遮罩，整屏直出这趟真正采到的东西；
                    // alpha 仍是同一个 mask，别的阶段（②③④）不受影响（它们看的是正常分支）。
                    output.color = half4(faceLit, mask);
                    output.depth = half4(normalDepth.a * mask, 0.0, 0.0, mask);
                    return output;
                }

                output.color = half4(faceLit * mask, mask);
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
