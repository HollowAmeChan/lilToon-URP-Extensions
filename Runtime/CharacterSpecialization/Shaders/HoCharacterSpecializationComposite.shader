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
            float4 _HoCharacterSubjectOutlineParams; // x strength, y radius px, z rotation rad, w flow rad/sec
            float4 _HoCharacterSubjectOutlineLevels; // x black, y white, z inverse range
            float4 _HoCharacterSubjectOutlineColor;
            float4 _HoCharacterSubjectOutlineFogColor;
            float4 _HoCharacterSubjectOutlineFogParams; // x hue shift turns, y saturation, z value, w softness exponent
            float4 _HoCharacterSubjectOutlineHeightFadeParams; // x mode, y ground world y, z fade start distance, w inverse fade distance
            float4 _HoCharacterSubjectOutlineOptions; // x final enabled, y textures ready, z fill mode, w height fade hardness
            float4 _HoCharacterEnhancedOutlineParams; // x strength, y radius px
            float4 _HoCharacterEnhancedOutlineFogColor;
            float4 _HoCharacterEnhancedOutlineFogParams; // x hue shift turns, y saturation, z value, w softness exponent
            float4 _HoCharacterEnhancedOutlineHeightFadeParams; // x mode, y ground world y, z fade start distance, w inverse fade distance
            float4 _HoCharacterEnhancedOutlineOptions; // x final enabled, y textures ready, z source channel, w height fade hardness
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
            TEXTURE2D_X(_lilHoCharacterSubjectOutlineSourceTexture);
            TEXTURE2D_X(_lilHoCharacterSubjectOutlineTexture);
            TEXTURE2D_X(_lilHoCharacterEnhancedOutlineSourceTexture);
            TEXTURE2D_X(_lilHoCharacterEnhancedOutlineTexture);
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

            float SampleSubject(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture, sampler_PointClamp, uv).r;
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

            float RemapSubjectOutlineMask(float value)
            {
                float blackPoint = saturate(_HoCharacterSubjectOutlineLevels.x);
                if (_HoCharacterSubjectOutlineLevels.w > 0.5)
                {
                    return step(blackPoint, value);
                }

                float invRange = max(_HoCharacterSubjectOutlineLevels.z, 0.0001);
                float mask = saturate((value - blackPoint) * invRange);
                return mask * mask * (3.0 - 2.0 * mask);
            }

            float SampleSubjectOutlineSourceMask(float2 uv)
            {
                if (_HoCharacterSubjectOutlineOptions.y <= 0.5)
                {
                    return 0.0;
                }

                return SAMPLE_TEXTURE2D_X(_lilHoCharacterSubjectOutlineSourceTexture, sampler_PointClamp, uv).r;
            }

            float4 SampleSubjectOutlineBlurData(float2 uv)
            {
                if (_HoCharacterSubjectOutlineOptions.y <= 0.5)
                {
                    return float4(0.0, 0.0, 0.0, 0.0);
                }

                return SAMPLE_TEXTURE2D_X(_lilHoCharacterSubjectOutlineTexture, sampler_LinearClamp, uv);
            }

            float SampleEnhancedOutlineSourceMask(float2 uv)
            {
                if (_HoCharacterEnhancedOutlineOptions.y <= 0.5)
                {
                    return 0.0;
                }

                return SAMPLE_TEXTURE2D_X(_lilHoCharacterEnhancedOutlineSourceTexture, sampler_PointClamp, uv).r;
            }

            float4 SampleEnhancedOutlineBlurData(float2 uv)
            {
                if (_HoCharacterEnhancedOutlineOptions.y <= 0.5)
                {
                    return float4(0.0, 0.0, 0.0, 0.0);
                }

                return SAMPLE_TEXTURE2D_X(_lilHoCharacterEnhancedOutlineTexture, sampler_LinearClamp, uv);
            }

            float ResolveSubjectOutlineEdgeSdf(float sourceMask, float blurMask)
            {
                return max(blurMask - sourceMask, 0.0);
            }

            float ResolveSubjectOutlineMask(float edgeSdf)
            {
                return RemapSubjectOutlineMask(edgeSdf);
            }

            float ResolveSubjectOutlineFogMask(float edgeSdf)
            {
                return pow(saturate(edgeSdf), max(_HoCharacterSubjectOutlineFogParams.w, 0.0001));
            }

            float ResolveEnhancedOutlineFogMask(float edgeSdf)
            {
                return pow(saturate(edgeSdf), max(_HoCharacterEnhancedOutlineFogParams.w, 0.0001));
            }

            float ApplySubjectOutlineHeightFadeHardness(float value)
            {
                float hardness = max(_HoCharacterSubjectOutlineOptions.w, 0.0001);
                float a = pow(saturate(value), hardness);
                float b = pow(saturate(1.0 - value), hardness);
                return a / max(a + b, 0.0001);
            }

            float ApplyEnhancedOutlineHeightFadeHardness(float value)
            {
                float hardness = max(_HoCharacterEnhancedOutlineOptions.w, 0.0001);
                float a = pow(saturate(value), hardness);
                float b = pow(saturate(1.0 - value), hardness);
                return a / max(a + b, 0.0001);
            }

            float ResolveSubjectOutlineHeightFade(float heightWeight, float weightedWorldY)
            {
                float fadeMode = round(_HoCharacterSubjectOutlineHeightFadeParams.x);
                if (fadeMode < 0.5)
                {
                    return 1.0;
                }

                float hasHeight = step(0.0001, heightWeight);
                float worldY = weightedWorldY / max(heightWeight, 0.0001);
                float groundDistance = abs(worldY - _HoCharacterSubjectOutlineHeightFadeParams.y);
                float fadeT = saturate((groundDistance - _HoCharacterSubjectOutlineHeightFadeParams.z) * _HoCharacterSubjectOutlineHeightFadeParams.w);
                fadeT = fadeT * fadeT * (3.0 - 2.0 * fadeT);
                fadeT = ApplySubjectOutlineHeightFadeHardness(fadeT);
                float fade = fadeMode < 1.5 ? fadeT : 1.0 - fadeT;
                return lerp(1.0, fade, hasHeight);
            }

            float ResolveEnhancedOutlineHeightFade(float heightWeight, float weightedWorldY)
            {
                float fadeMode = round(_HoCharacterEnhancedOutlineHeightFadeParams.x);
                if (fadeMode < 0.5)
                {
                    return 1.0;
                }

                float hasHeight = step(0.0001, heightWeight);
                float worldY = weightedWorldY / max(heightWeight, 0.0001);
                float groundDistance = abs(worldY - _HoCharacterEnhancedOutlineHeightFadeParams.y);
                float fadeT = saturate((groundDistance - _HoCharacterEnhancedOutlineHeightFadeParams.z) * _HoCharacterEnhancedOutlineHeightFadeParams.w);
                fadeT = fadeT * fadeT * (3.0 - 2.0 * fadeT);
                fadeT = ApplyEnhancedOutlineHeightFadeHardness(fadeT);
                float fade = fadeMode < 1.5 ? fadeT : 1.0 - fadeT;
                return lerp(1.0, fade, hasHeight);
            }

            float2 ResolveSubjectOutlineNormal(float blurMask)
            {
                float2 gradient = float2(ddx(blurMask), ddy(blurMask));
                float gradientLength = length(gradient);
                if (gradientLength <= 0.000001)
                {
                    return float2(0.0, 1.0);
                }

                // The blurred subject field falls off outward, so invert the gradient to get the outward direction.
                return -gradient / gradientLength;
            }

            half3 HoCharacterSubjectOutlineHsvToRgb(float3 hsv)
            {
                float3 rgb = saturate(abs(frac(hsv.x + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
                rgb = rgb * rgb * (3.0 - 2.0 * rgb);
                return (half3)(hsv.z * lerp(float3(1.0, 1.0, 1.0), rgb, hsv.y));
            }

            float3 HoCharacterSubjectOutlineRgbToHsv(float3 rgb)
            {
                float4 k = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(rgb.bg, k.wz), float4(rgb.gb, k.xy), step(rgb.b, rgb.g));
                float4 q = lerp(float4(p.xyw, rgb.r), float4(rgb.r, p.yzx), step(p.x, rgb.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            half3 ResolveSubjectOutlineHueColor(float2 normal)
            {
                float rotation = _HoCharacterSubjectOutlineParams.z + _Time.y * _HoCharacterSubjectOutlineParams.w;
                float s;
                float c;
                sincos(rotation, s, c);
                float2 rotated = float2(
                    normal.x * c - normal.y * s,
                    normal.x * s + normal.y * c);
                float hue = frac(atan2(rotated.y, rotated.x) * 0.159154943 + 0.5);
                return HoCharacterSubjectOutlineHsvToRgb(float3(hue, 1.0, 1.0));
            }

            half3 ResolveSubjectOutlineColor(float2 normal)
            {
                float fillMode = round(_HoCharacterSubjectOutlineOptions.z);
                if (fillMode < 0.5)
                {
                    return (half3)_HoCharacterSubjectOutlineColor.rgb;
                }

                return ResolveSubjectOutlineHueColor(normal);
            }

            half3 ResolveSubjectOutlineFogColor(half3 sourceColor)
            {
                float3 tintedColor = max((float3)sourceColor * _HoCharacterSubjectOutlineFogColor.rgb, 0.0);
                float3 hsv = HoCharacterSubjectOutlineRgbToHsv(tintedColor);
                hsv.x = frac(hsv.x + _HoCharacterSubjectOutlineFogParams.x);
                hsv.y = saturate(hsv.y * max(_HoCharacterSubjectOutlineFogParams.y, 0.0));
                hsv.z = max(hsv.z * max(_HoCharacterSubjectOutlineFogParams.z, 0.0), 0.0);
                return HoCharacterSubjectOutlineHsvToRgb(hsv);
            }

            half3 ResolveEnhancedOutlineFogColor(half3 sourceColor)
            {
                float3 tintedColor = max((float3)sourceColor * _HoCharacterEnhancedOutlineFogColor.rgb, 0.0);
                float3 hsv = HoCharacterSubjectOutlineRgbToHsv(tintedColor);
                hsv.x = frac(hsv.x + _HoCharacterEnhancedOutlineFogParams.x);
                hsv.y = saturate(hsv.y * max(_HoCharacterEnhancedOutlineFogParams.y, 0.0));
                hsv.z = max(hsv.z * max(_HoCharacterEnhancedOutlineFogParams.z, 0.0), 0.0);
                return HoCharacterSubjectOutlineHsvToRgb(hsv);
            }

            half3 BlendOutlineFog(half3 baseColor, half3 fogColor, float amount)
            {
                float alpha = saturate(amount);
                half3 base01 = saturate(baseColor);
                half3 fog01 = saturate(fogColor);
                half3 screen = 1.0 - (1.0 - base01) * (1.0 - fog01);
                half3 hdrLift = max(fogColor - fog01, 0.0);
                half3 target = max(baseColor, screen + hdrLift);
                return lerp(baseColor, target, alpha);
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
                float subjectOutlineSourceMask = SampleSubjectOutlineSourceMask(uv);
                float4 subjectOutlineBlurData = SampleSubjectOutlineBlurData(uv);
                float subjectOutlineBlurMask = subjectOutlineBlurData.r;
                float subjectOutlineEdgeSdf = ResolveSubjectOutlineEdgeSdf(subjectOutlineSourceMask, subjectOutlineBlurMask);
                float subjectOutlineMask = ResolveSubjectOutlineMask(subjectOutlineEdgeSdf);
                float2 subjectOutlineNormal = ResolveSubjectOutlineNormal(subjectOutlineBlurMask);
                float subjectOutlineHeightFade = ResolveSubjectOutlineHeightFade(subjectOutlineBlurData.b, subjectOutlineBlurData.g);
                float enhancedOutlineSourceMask = SampleEnhancedOutlineSourceMask(uv);
                float4 enhancedOutlineBlurData = SampleEnhancedOutlineBlurData(uv);
                float enhancedOutlineBlurMask = enhancedOutlineBlurData.r;
                float enhancedOutlineEdgeSdf = ResolveSubjectOutlineEdgeSdf(enhancedOutlineSourceMask, enhancedOutlineBlurMask);
                float enhancedOutlineFogMask = ResolveEnhancedOutlineFogMask(enhancedOutlineEdgeSdf);
                float enhancedOutlineHeightFade = ResolveEnhancedOutlineHeightFade(enhancedOutlineBlurData.b, enhancedOutlineBlurData.g);
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

                if (debugMode == 9)
                {
                    if (_HoCharacterSubjectOutlineOptions.y <= 0.5)
                    {
                        return half4(0.0, 0.0, 0.0, source.a);
                    }

                    return half4(subjectOutlineSourceMask, subjectOutlineSourceMask, subjectOutlineSourceMask, source.a);
                }

                if (debugMode == 10)
                {
                    if (_HoCharacterSubjectOutlineOptions.y <= 0.5)
                    {
                        return half4(0.0, 0.0, 0.0, source.a);
                    }

                    return half4(subjectOutlineBlurMask, subjectOutlineBlurMask, subjectOutlineBlurMask, source.a);
                }

                if (debugMode == 11)
                {
                    return half4(subjectOutlineMask, subjectOutlineMask, subjectOutlineMask, source.a);
                }

                if (debugMode == 12)
                {
                    if (_HoCharacterSubjectOutlineOptions.y <= 0.5)
                    {
                        return half4(0.0, 0.0, 0.0, source.a);
                    }

                    return half4(ResolveSubjectOutlineHueColor(subjectOutlineNormal), source.a);
                }

                if (debugMode == 13)
                {
                    if (_HoCharacterEnhancedOutlineOptions.y <= 0.5)
                    {
                        return half4(0.0, 0.0, 0.0, source.a);
                    }

                    return half4(enhancedOutlineSourceMask, enhancedOutlineSourceMask, enhancedOutlineSourceMask, source.a);
                }

                if (debugMode == 14)
                {
                    if (_HoCharacterEnhancedOutlineOptions.y <= 0.5)
                    {
                        return half4(0.0, 0.0, 0.0, source.a);
                    }

                    return half4(enhancedOutlineBlurMask, enhancedOutlineBlurMask, enhancedOutlineBlurMask, source.a);
                }

                if (debugMode == 15)
                {
                    return half4(enhancedOutlineFogMask * enhancedOutlineHeightFade, enhancedOutlineFogMask * enhancedOutlineHeightFade, enhancedOutlineFogMask * enhancedOutlineHeightFade, source.a);
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

                float enhancedOutlineFogAmount = enhancedOutlineFogMask * enhancedOutlineHeightFade * saturate(_HoCharacterEnhancedOutlineParams.x) * saturate(_HoCharacterEnhancedOutlineFogColor.a) * saturate(_HoCharacterEnhancedOutlineOptions.x);
                if (enhancedOutlineFogAmount > 0.0001)
                {
                    half3 enhancedFogColor = ResolveEnhancedOutlineFogColor(source.rgb);
                    color = BlendOutlineFog(color, enhancedFogColor, enhancedOutlineFogAmount);
                }

                float subjectOutlineStyle = round(_HoCharacterSubjectOutlineOptions.z);
                if (subjectOutlineStyle > 1.5)
                {
                    float subjectOutlineFogMask = ResolveSubjectOutlineFogMask(subjectOutlineEdgeSdf);
                    float subjectOutlineFogAmount = subjectOutlineFogMask * subjectOutlineHeightFade * saturate(_HoCharacterSubjectOutlineParams.x) * saturate(_HoCharacterSubjectOutlineFogColor.a) * saturate(_HoCharacterSubjectOutlineOptions.x);
                    if (subjectOutlineFogAmount > 0.0001)
                    {
                        half3 fogColor = ResolveSubjectOutlineFogColor(source.rgb);
                        color = BlendOutlineFog(color, fogColor, subjectOutlineFogAmount);
                    }
                }
                else
                {
                    float subjectOutlineStyleAlpha = subjectOutlineStyle < 0.5 ? saturate(_HoCharacterSubjectOutlineColor.a) : 1.0;
                    float subjectOutlineAmount = subjectOutlineMask * subjectOutlineHeightFade * saturate(_HoCharacterSubjectOutlineParams.x) * subjectOutlineStyleAlpha * saturate(_HoCharacterSubjectOutlineOptions.x);
                    if (subjectOutlineAmount > 0.0001)
                    {
                        half3 outlineColor = ResolveSubjectOutlineColor(subjectOutlineNormal);
                        color = lerp(color, outlineColor, subjectOutlineAmount);
                    }
                }

                return half4(color, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
