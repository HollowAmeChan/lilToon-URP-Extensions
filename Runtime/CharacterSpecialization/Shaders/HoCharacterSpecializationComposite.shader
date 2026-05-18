Shader "Hidden/lilToon-HoCharacterSpecialization/URP/Composite"
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
            Name "HoCharacter Composite"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _lilHoAovActive;
            float4 _HoCharacterEyeRevealParams; // x strength, y feather px, z dilation px, w depth bias
            float4 _HoCharacterHairShadowParams; // x opacity, y distance px, z angle deg, w softness px
            float4 _HoCharacterHairShadowParams1; // x spread px, y keep off hair, z blend mode, w use reveal area
            float4 _HoCharacterHairShadowColor;
            float4 _HoCharacterOptions; // x eye enabled, y shadow enabled, z same character only, w debug mode

            TEXTURE2D_X(_lilHoAovMaskIdTexture);
            TEXTURE2D_X(_lilHoAovNormalDepthTexture);
            TEXTURE2D_X(_lilHoAovObjectCustom0_3Texture);
            TEXTURE2D_X(_lilHoAovObjectCustom4_7Texture);
            TEXTURE2D_X(_lilHoCharacterEyeColorTexture);
            TEXTURE2D_X(_lilHoCharacterEyeDataTexture);
            float4 _lilHoAovMaskIdTexture_TexelSize;

            float2 AovTexelSize()
            {
                return _lilHoAovMaskIdTexture_TexelSize.xy;
            }

            float SameCharacter(float a, float b)
            {
                float same = 1.0 - step((0.5 / 255.0) + 0.00001, abs(a - b));
                return lerp(1.0, same, saturate(_HoCharacterOptions.z));
            }

            float SampleFrontHair(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_lilHoAovObjectCustom0_3Texture, sampler_PointClamp, uv).b;
            }

            float SampleFace(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_lilHoAovObjectCustom0_3Texture, sampler_PointClamp, uv).g;
            }

            float SampleRevealArea(float2 uv)
            {
                float area = SAMPLE_TEXTURE2D_X(_lilHoAovObjectCustom4_7Texture, sampler_PointClamp, uv).r;
                return lerp(1.0, area, saturate(_HoCharacterHairShadowParams1.w));
            }

            float SampleEyeAlphaRaw(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_lilHoCharacterEyeDataTexture, sampler_PointClamp, uv).r;
            }

            float SampleDilatedEyeAlpha(float2 uv, float radiusPx)
            {
                float alpha = SampleEyeAlphaRaw(uv);
                if (radiusPx <= 0.0001)
                {
                    return alpha;
                }

                float2 texel = AovTexelSize() * radiusPx;
                alpha = max(alpha, SampleEyeAlphaRaw(uv + float2( texel.x, 0.0)));
                alpha = max(alpha, SampleEyeAlphaRaw(uv + float2(-texel.x, 0.0)));
                alpha = max(alpha, SampleEyeAlphaRaw(uv + float2(0.0,  texel.y)));
                alpha = max(alpha, SampleEyeAlphaRaw(uv + float2(0.0, -texel.y)));
                alpha = max(alpha, SampleEyeAlphaRaw(uv + float2( texel.x,  texel.y)));
                alpha = max(alpha, SampleEyeAlphaRaw(uv + float2(-texel.x,  texel.y)));
                alpha = max(alpha, SampleEyeAlphaRaw(uv + float2( texel.x, -texel.y)));
                alpha = max(alpha, SampleEyeAlphaRaw(uv + float2(-texel.x, -texel.y)));
                return alpha;
            }

            float SampleEyeAlpha(float2 uv)
            {
                float dilationPx = max(_HoCharacterEyeRevealParams.z, 0.0);
                float featherPx = max(_HoCharacterEyeRevealParams.y, 0.0);
                float alpha = SampleDilatedEyeAlpha(uv, dilationPx);
                if (featherPx <= 0.0001)
                {
                    return alpha;
                }

                float2 texel = AovTexelSize() * featherPx;
                float sum = alpha * 0.24;
                sum += SampleDilatedEyeAlpha(uv + float2( texel.x, 0.0), dilationPx) * 0.095;
                sum += SampleDilatedEyeAlpha(uv + float2(-texel.x, 0.0), dilationPx) * 0.095;
                sum += SampleDilatedEyeAlpha(uv + float2(0.0,  texel.y), dilationPx) * 0.095;
                sum += SampleDilatedEyeAlpha(uv + float2(0.0, -texel.y), dilationPx) * 0.095;
                sum += SampleDilatedEyeAlpha(uv + float2( texel.x,  texel.y), dilationPx) * 0.095;
                sum += SampleDilatedEyeAlpha(uv + float2(-texel.x,  texel.y), dilationPx) * 0.095;
                sum += SampleDilatedEyeAlpha(uv + float2( texel.x, -texel.y), dilationPx) * 0.095;
                sum += SampleDilatedEyeAlpha(uv + float2(-texel.x, -texel.y), dilationPx) * 0.095;
                return saturate(sum);
            }

            float ResolveEyeRevealMask(float2 uv)
            {
                if (_HoCharacterOptions.x <= 0.5)
                {
                    return 0.0;
                }

                float4 maskId = SAMPLE_TEXTURE2D_X(_lilHoAovMaskIdTexture, sampler_PointClamp, uv);
                float4 normalDepth = SAMPLE_TEXTURE2D_X(_lilHoAovNormalDepthTexture, sampler_PointClamp, uv);
                float4 eyeData = SAMPLE_TEXTURE2D_X(_lilHoCharacterEyeDataTexture, sampler_PointClamp, uv);
                float frontHair = SampleFrontHair(uv);
                float eyeAlpha = SampleEyeAlpha(uv);
                float revealArea = SampleRevealArea(uv);
                float hairDepth = normalDepth.a;
                float rawEyeAlpha = max(eyeData.r, 0.0001);
                float eyeDepth = eyeData.g / rawEyeAlpha;
                float eyeCharacterId = eyeData.b / rawEyeAlpha;
                float depthBias = max(_HoCharacterEyeRevealParams.w, 0.0);
                float hairInFront = step(0.0001, eyeDepth) * step(hairDepth, eyeDepth + depthBias);
                float same = SameCharacter(maskId.g, eyeCharacterId);
                return saturate(frontHair * eyeAlpha * revealArea * hairInFront * same * _HoCharacterEyeRevealParams.x);
            }

            float SampleHairSpread(float2 uv, float radiusPx)
            {
                float mask = SampleFrontHair(uv);
                if (radiusPx <= 0.0001)
                {
                    return mask;
                }

                float2 texel = AovTexelSize() * radiusPx;
                mask = max(mask, SampleFrontHair(uv + float2( texel.x, 0.0)));
                mask = max(mask, SampleFrontHair(uv + float2(-texel.x, 0.0)));
                mask = max(mask, SampleFrontHair(uv + float2(0.0,  texel.y)));
                mask = max(mask, SampleFrontHair(uv + float2(0.0, -texel.y)));
                mask = max(mask, SampleFrontHair(uv + float2( texel.x,  texel.y)));
                mask = max(mask, SampleFrontHair(uv + float2(-texel.x,  texel.y)));
                mask = max(mask, SampleFrontHair(uv + float2( texel.x, -texel.y)));
                mask = max(mask, SampleFrontHair(uv + float2(-texel.x, -texel.y)));
                return mask;
            }

            float SampleHairBlur(float2 uv, float softnessPx, float spreadPx)
            {
                float center = SampleHairSpread(uv, spreadPx);
                if (softnessPx <= 0.0001)
                {
                    return center;
                }

                float2 texel = AovTexelSize() * softnessPx;
                float mask = center * 0.24;
                mask += SampleHairSpread(uv + float2( texel.x, 0.0), spreadPx) * 0.095;
                mask += SampleHairSpread(uv + float2(-texel.x, 0.0), spreadPx) * 0.095;
                mask += SampleHairSpread(uv + float2(0.0,  texel.y), spreadPx) * 0.095;
                mask += SampleHairSpread(uv + float2(0.0, -texel.y), spreadPx) * 0.095;
                mask += SampleHairSpread(uv + float2( texel.x,  texel.y), spreadPx) * 0.095;
                mask += SampleHairSpread(uv + float2(-texel.x,  texel.y), spreadPx) * 0.095;
                mask += SampleHairSpread(uv + float2( texel.x, -texel.y), spreadPx) * 0.095;
                mask += SampleHairSpread(uv + float2(-texel.x, -texel.y), spreadPx) * 0.095;
                return saturate(mask);
            }

            float ResolveHairShadowMask(float2 uv, float revealMask)
            {
                if (_HoCharacterOptions.y <= 0.5)
                {
                    return 0.0;
                }

                float distancePx = max(_HoCharacterHairShadowParams.y, 0.0);
                float angleRadians = radians(_HoCharacterHairShadowParams.z);
                float2 offset = float2(cos(angleRadians), sin(angleRadians)) * distancePx * AovTexelSize();
                float2 shiftedUv = uv - offset;

                float spreadPx = max(_HoCharacterHairShadowParams1.x, 0.0);
                float softnessPx = max(_HoCharacterHairShadowParams.w, 0.0);
                float shiftedHair = SampleHairBlur(shiftedUv, softnessPx, spreadPx);
                float originalHair = SampleFrontHair(uv);
                float receiver = saturate(SampleFace(uv) + revealMask);
                float keepOffHair = saturate(_HoCharacterHairShadowParams1.y);

                float4 currentId = SAMPLE_TEXTURE2D_X(_lilHoAovMaskIdTexture, sampler_PointClamp, uv);
                float4 shiftedId = SAMPLE_TEXTURE2D_X(_lilHoAovMaskIdTexture, sampler_PointClamp, shiftedUv);
                float same = SameCharacter(currentId.g, shiftedId.g);
                return saturate((shiftedHair - originalHair * keepOffHair) * receiver * same);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_lilHoAovActive <= 0.5)
                {
                    return source;
                }

                half4 eyeColor = SAMPLE_TEXTURE2D_X(_lilHoCharacterEyeColorTexture, sampler_LinearClamp, uv);
                float revealMask = ResolveEyeRevealMask(uv);
                float shadowMask = ResolveHairShadowMask(uv, revealMask);
                int debugMode = (int)round(_HoCharacterOptions.w);
                if (debugMode == 1)
                {
                    return half4(eyeColor.rgb, source.a);
                }

                if (debugMode == 2)
                {
                    half eyeAlpha = SAMPLE_TEXTURE2D_X(_lilHoCharacterEyeDataTexture, sampler_PointClamp, uv).r;
                    return half4(eyeAlpha, eyeAlpha, eyeAlpha, source.a);
                }

                if (debugMode == 3)
                {
                    return half4(revealMask, revealMask, revealMask, source.a);
                }

                if (debugMode == 4)
                {
                    return half4(shadowMask, shadowMask, shadowMask, source.a);
                }

                half3 color = lerp(source.rgb, eyeColor.rgb, revealMask);
                float shadowAmount = shadowMask * saturate(_HoCharacterHairShadowParams.x);
                if (shadowAmount > 0.0001)
                {
                    half3 shadowColor = (half3)_HoCharacterHairShadowColor.rgb;
                    if (round(_HoCharacterHairShadowParams1.z) < 0.5)
                    {
                        color *= lerp(half3(1.0, 1.0, 1.0), shadowColor, shadowAmount);
                    }
                    else
                    {
                        color = lerp(color, shadowColor, shadowAmount);
                    }
                }

                return half4(color, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
