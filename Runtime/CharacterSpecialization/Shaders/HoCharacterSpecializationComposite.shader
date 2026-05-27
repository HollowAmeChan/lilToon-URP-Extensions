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

            float _HoMetadataBufferActive;
            float4 _HoCharacterEyeRevealParams; // x strength, y feather px, z dilation px, w depth bias
            float4 _HoCharacterHairShadowParams; // x opacity, y distance px, z angle deg, w softness px
            float4 _HoCharacterHairShadowParams1; // x spread px, y keep off hair, z blend mode, w use reveal area
            float4 _HoCharacterHairShadowParams2; // x perspective strength, y reference depth, z min scale
            float4 _HoCharacterHairShadowColor;
            float4 _HoCharacterFaceHairDiffuseParams; // x strength, y radius px, z depth tolerance, w blend mode
            float4 _HoCharacterFaceHairDiffuseLevels; // x black, y white, z inverse range
            float4 _HoCharacterFaceHairDiffuseTintColor;
            float4 _HoCharacterFaceHairDiffuseOptions; // x final enabled, y textures ready
            float4 _HoCharacterOptions; // x eye enabled, y shadow enabled, z same character only, w debug mode

            TEXTURE2D_X(_HoMetadataBufferMaskIdTexture);
            TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture);
            TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture);
            TEXTURE2D_X(_HoMetadataBufferObjectCustom4_7Texture);
            TEXTURE2D_X(_lilHoCharacterEyeColorTexture);
            TEXTURE2D_X(_lilHoCharacterEyeDataTexture);
            TEXTURE2D_X(_lilHoCharacterFaceHairDiffuseSourceColorTexture);
            TEXTURE2D_X(_lilHoCharacterFaceHairDiffuseColorTexture);
            TEXTURE2D_X(_lilHoCharacterFaceHairDiffuseDepthTexture);
            float4 _HoMetadataBufferMaskIdTexture_TexelSize;

            float2 MetadataTexelSize()
            {
                return _HoMetadataBufferMaskIdTexture_TexelSize.xy;
            }

            float SameCharacter(float a, float b)
            {
                float same = 1.0 - step((0.5 / 255.0) + 0.00001, abs(a - b));
                return lerp(1.0, same, saturate(_HoCharacterOptions.z));
            }

            float SampleFrontHair(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture, sampler_PointClamp, uv).b;
            }

            float SampleFace(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture, sampler_PointClamp, uv).g;
            }

            float SampleRevealArea(float2 uv)
            {
                float area = SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom4_7Texture, sampler_PointClamp, uv).r;
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

                float2 texel = MetadataTexelSize() * radiusPx;
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

                float2 texel = MetadataTexelSize() * featherPx;
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

                float4 maskId = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaskIdTexture, sampler_PointClamp, uv);
                float4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
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

                float2 texel = MetadataTexelSize() * radiusPx;
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

                float2 texel = MetadataTexelSize() * softnessPx;
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

            float ResolveHairShadowDistanceScale(float2 uv)
            {
                float strength = saturate(_HoCharacterHairShadowParams2.x);
                float referenceDepth = max(_HoCharacterHairShadowParams2.y, 0.0);
                if (strength <= 0.0001 || referenceDepth <= 0.0001)
                {
                    return 1.0;
                }

                float receiverDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv).a;
                float hasDepth = step(0.0001, receiverDepth);
                float minScale = saturate(_HoCharacterHairShadowParams2.z);
                float perspectiveScale = clamp(referenceDepth / max(receiverDepth, 0.0001), minScale, 1.0);
                float scale = lerp(1.0, perspectiveScale, hasDepth * strength);
                return lerp(scale, 1.0, unity_OrthoParams.w);
            }

            float ResolveHairShadowMask(float2 uv, float revealMask)
            {
                if (_HoCharacterOptions.y <= 0.5)
                {
                    return 0.0;
                }

                float distancePx = max(_HoCharacterHairShadowParams.y, 0.0) * ResolveHairShadowDistanceScale(uv);
                float angleRadians = radians(_HoCharacterHairShadowParams.z);
                float2 offset = float2(cos(angleRadians), sin(angleRadians)) * distancePx * MetadataTexelSize();
                float2 shiftedUv = uv - offset;

                float spreadPx = max(_HoCharacterHairShadowParams1.x, 0.0);
                float softnessPx = max(_HoCharacterHairShadowParams.w, 0.0);
                float shiftedHair = SampleHairBlur(shiftedUv, softnessPx, spreadPx);
                float originalHair = SampleFrontHair(uv);
                float receiver = saturate(SampleFace(uv) + revealMask);
                float keepOffHair = saturate(_HoCharacterHairShadowParams1.y);

                float4 currentId = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaskIdTexture, sampler_PointClamp, uv);
                float4 shiftedId = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaskIdTexture, sampler_PointClamp, shiftedUv);
                float same = SameCharacter(currentId.g, shiftedId.g);
                return saturate((shiftedHair - originalHair * keepOffHair) * receiver * same);
            }

            float RemapFaceHairDiffuseMask(float value)
            {
                float blackPoint = saturate(_HoCharacterFaceHairDiffuseLevels.x);
                float invRange = max(_HoCharacterFaceHairDiffuseLevels.z, 0.0001);
                float mask = saturate((value - blackPoint) * invRange);
                return mask * mask * (3.0 - 2.0 * mask);
            }

            half3 BlendFaceHairDiffuse(half3 source, half3 tint, float amount)
            {
                float blendMode = round(_HoCharacterFaceHairDiffuseParams.w);
                half3 result = tint;
                if (blendMode > 0.5 && blendMode < 1.5)
                {
                    result = source + tint;
                }
                else if (blendMode >= 1.5)
                {
                    half3 source01 = saturate(source);
                    half3 tint01 = saturate(tint);
                    result = 1.0 - (1.0 - source01) * (1.0 - tint01);
                }

                return lerp(source, max(result, 0.0), saturate(amount));
            }

            float ResolveFaceHairDiffuseMask(float2 uv)
            {
                if (_HoCharacterFaceHairDiffuseOptions.y <= 0.5)
                {
                    return 0.0;
                }

                float frontHair = SampleFrontHair(uv);
                float4 blurredColor = SAMPLE_TEXTURE2D_X(_lilHoCharacterFaceHairDiffuseColorTexture, sampler_LinearClamp, uv);
                float blurMask = saturate(blurredColor.a);
                float levelsMask = RemapFaceHairDiffuseMask(blurMask);
                if (levelsMask <= 0.0001)
                {
                    return 0.0;
                }

                float4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
                float4 blurredDepth = SAMPLE_TEXTURE2D_X(_lilHoCharacterFaceHairDiffuseDepthTexture, sampler_LinearClamp, uv);
                float rawDepthMask = max(blurredDepth.a, 0.0001);
                float faceDepth = blurredDepth.r / rawDepthMask;
                float hairDepth = normalDepth.a;
                float depthTolerance = max(_HoCharacterFaceHairDiffuseParams.z, 0.0001);
                float depthDelta = hairDepth - faceDepth;
                float depthGate = step(0.0001, hairDepth) * step(0.0001, faceDepth);
                depthGate *= 1.0 - smoothstep(depthTolerance, depthTolerance * 2.0, depthDelta);
                return saturate(frontHair * levelsMask * depthGate);
            }

            half3 ResolveFaceHairDiffuseColor(float2 uv)
            {
                float4 blurredColor = SAMPLE_TEXTURE2D_X(_lilHoCharacterFaceHairDiffuseColorTexture, sampler_LinearClamp, uv);
                float blurMask = max(blurredColor.a, 0.0001);
                return (half3)(blurredColor.rgb / blurMask);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_HoMetadataBufferActive <= 0.5)
                {
                    return source;
                }

                half4 eyeColor = SAMPLE_TEXTURE2D_X(_lilHoCharacterEyeColorTexture, sampler_LinearClamp, uv);
                float revealMask = ResolveEyeRevealMask(uv);
                float shadowMask = ResolveHairShadowMask(uv, revealMask);
                float faceHairDiffuseMask = ResolveFaceHairDiffuseMask(uv);
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

                if (debugMode == 5)
                {
                    if (_HoCharacterFaceHairDiffuseOptions.y <= 0.5)
                    {
                        return half4(0.0, 0.0, 0.0, source.a);
                    }

                    half sourceMask = SAMPLE_TEXTURE2D_X(_lilHoCharacterFaceHairDiffuseSourceColorTexture, sampler_PointClamp, uv).a;
                    return half4(sourceMask, sourceMask, sourceMask, source.a);
                }

                if (debugMode == 6)
                {
                    if (_HoCharacterFaceHairDiffuseOptions.y <= 0.5)
                    {
                        return half4(0.0, 0.0, 0.0, source.a);
                    }

                    half blurMask = SAMPLE_TEXTURE2D_X(_lilHoCharacterFaceHairDiffuseColorTexture, sampler_LinearClamp, uv).a;
                    return half4(blurMask, blurMask, blurMask, source.a);
                }

                if (debugMode == 7)
                {
                    if (_HoCharacterFaceHairDiffuseOptions.y <= 0.5)
                    {
                        return half4(0.0, 0.0, 0.0, source.a);
                    }

                    return half4(ResolveFaceHairDiffuseColor(uv), source.a);
                }

                if (debugMode == 8)
                {
                    return half4(faceHairDiffuseMask, faceHairDiffuseMask, faceHairDiffuseMask, source.a);
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

                float faceHairDiffuseAmount = faceHairDiffuseMask * saturate(_HoCharacterFaceHairDiffuseParams.x) * saturate(_HoCharacterFaceHairDiffuseTintColor.a) * saturate(_HoCharacterFaceHairDiffuseOptions.x);
                if (faceHairDiffuseAmount > 0.0001)
                {
                    half3 faceHairDiffuseColor = ResolveFaceHairDiffuseColor(uv) * (half3)_HoCharacterFaceHairDiffuseTintColor.rgb;
                    color = BlendFaceHairDiffuse(color, faceHairDiffuseColor, faceHairDiffuseAmount);
                }

                return half4(color, source.a);
            }
            ENDHLSL
        }

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

            float4 _HoCharacterFaceHairDiffuseBlurParams; // x radius px in source texture, yz direction
            TEXTURE2D_X(_lilHoCharacterFaceHairDiffuseDepthTexture);

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
                float2 direction = _HoCharacterFaceHairDiffuseBlurParams.yz;
                float2 stepUv = _BlitTexture_TexelSize.xy * direction * radiusPx;

                float4 colorSum = 0.0;
                float4 depthSum = 0.0;
                float weightSum = 0.0;

                [unroll]
                for (int i = -6; i <= 6; i++)
                {
                    float distance01 = abs((float)i) / 6.0;
                    float weight = exp2(-distance01 * distance01 * 4.5);
                    float2 sampleUv = uv + stepUv * ((float)i / 6.0);
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
