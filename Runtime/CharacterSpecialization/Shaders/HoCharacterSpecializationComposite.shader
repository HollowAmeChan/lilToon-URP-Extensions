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
            float4 _HoCharacterEyeAngleParams; // x strength, y yaw range deg, z pitch range deg, w softness deg
            float4 _HoCharacterHairShadowParams; // x opacity, y distance px, z angle deg, w softness px
            float4 _HoCharacterHairShadowParams1; // x spread px, y blend mode, z use reveal area
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
            TEXTURE2D_X(_lilHoCharacterSemanticMaskBlurred0_3Texture);
            TEXTURE2D_X(_lilHoCharacterSemanticMaskBlurred4_7Texture);
            float _HoCharacterSemanticMaskBlurValid;
            float4 _HoCharacterSemanticMaskOptions; // x copy exists, y hair shadow, z face hair diffuse, w eye reveal
            TEXTURE2D_X(_lilHoCharacterEyeColorTexture);
            TEXTURE2D_X(_lilHoCharacterEyeDataTexture);
            TEXTURE2D(_lilHoCharacterEyeAngleTable);
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

            // The semantic channels are a single-sample 0/1 field (objectCustom is RGBA(bits) by
            // contract). Reads resolve to the feature's shared anti-aliased copy when the user enabled
            // it for this effect, and to the raw bit otherwise - one place decides the source, and the
            // per-effect choice is a user-facing option rather than hidden code. The copy is produced
            // by our own pass, independent of any camera MSAA / FXAA / TAA setting.
            // channel: 0 = FrontHair (objectCustom0.b), 1 = Face (objectCustom0.g),
            //          2 = EyeRevealArea (objectCustom4.r).
            float SampleSemanticBit(float2 uv, int channel, float useAntiAliased)
            {
                if (_HoCharacterSemanticMaskBlurValid > 0.5 && useAntiAliased > 0.5)
                {
                    if (channel == 2)
                    {
                        return SAMPLE_TEXTURE2D_X(_lilHoCharacterSemanticMaskBlurred4_7Texture, sampler_LinearClamp, uv).r;
                    }

                    float4 blurred = SAMPLE_TEXTURE2D_X(_lilHoCharacterSemanticMaskBlurred0_3Texture, sampler_LinearClamp, uv);
                    return channel == 0 ? blurred.b : blurred.g;
                }

                if (channel == 2)
                {
                    return SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom4_7Texture, sampler_LinearClamp, uv).r;
                }

                float4 bits = SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture, sampler_LinearClamp, uv);
                return channel == 0 ? bits.b : bits.g;
            }

            float SampleSemanticSpread(float2 uv, float radiusPx, int channel, float useAntiAliased)
            {
                float mask = SampleSemanticBit(uv, channel, useAntiAliased);
                if (radiusPx <= 0.0001)
                {
                    return mask;
                }

                float2 texel = MetadataTexelSize() * radiusPx;
                mask = max(mask, SampleSemanticBit(uv + float2( texel.x, 0.0), channel, useAntiAliased));
                mask = max(mask, SampleSemanticBit(uv + float2(-texel.x, 0.0), channel, useAntiAliased));
                mask = max(mask, SampleSemanticBit(uv + float2(0.0,  texel.y), channel, useAntiAliased));
                mask = max(mask, SampleSemanticBit(uv + float2(0.0, -texel.y), channel, useAntiAliased));
                mask = max(mask, SampleSemanticBit(uv + float2( texel.x,  texel.y), channel, useAntiAliased));
                mask = max(mask, SampleSemanticBit(uv + float2(-texel.x,  texel.y), channel, useAntiAliased));
                mask = max(mask, SampleSemanticBit(uv + float2( texel.x, -texel.y), channel, useAntiAliased));
                mask = max(mask, SampleSemanticBit(uv + float2(-texel.x, -texel.y), channel, useAntiAliased));
                return mask;
            }

            // A flat box, not a gaussian. The filter runs on a single-sample 0/1 mask, so the
            // penumbra can only ever be a staircase, and its step height is set by the kernel's
            // footprint. At the same width a gaussian concentrates its weight mid-ramp and steps
            // ~2.2x harder than a box (measured per-texel step profiles across a vertical edge):
            //   old 9 integer taps (2px) : 0.285 0.000 0.430 0.000 0.285 0.000  (plateaus, 0.43)
            //   gaussian spiral (2px)    : 0.033 0.243 0.441 0.248 0.035          (max step 0.441)
            //   this box (2px)           : 0.200 0.200 0.200 0.200 0.200          (max step 0.200)
            // 1/width is the best a binary input allows at that width. A linear blur fills in the
            // low-frequency part of the mask but can never move the edge *off* the metadata texel
            // grid - the sub-texel phase was never written into the buffer - so 柔化像素 stays the
            // only dial that trades edge tightness for smoothness.
            static const int LIL_HOCHARACTER_HAIR_MASK_MAX_TAPS = 8;

            float SampleSemanticBlur(float2 uv, float softnessPx, float spreadPx, int channel, float useAntiAliased)
            {
                // Reading a single-sample 0/1 mask only stops being a hard edge once the filter
                // spreads it over at least one texel, so the read always keeps a 1px floor -
                // 柔化像素 softens from there (radius 0.5px still steps 0.84 inside one pixel).
                float side = 2.0 * max(softnessPx, 1.0) + 1.0;
                int taps = (int)clamp(round(side), 3.0, (float)LIL_HOCHARACTER_HAIR_MASK_MAX_TAPS);
                // Keep the spacing near or under 2 texels: wider apart and the bilinear taps stop
                // reaching every column, which is what turns a ramp back into flat plateaus.
                float spacing = side / (float)taps;
                float2 tapStep = MetadataTexelSize() * spacing;
                float2 firstTap = MetadataTexelSize() * (spacing - side) * 0.5;
                float sum = 0.0;
                [loop]
                for (int y = 0; y < taps; y++)
                {
                    [loop]
                    for (int x = 0; x < taps; x++)
                    {
                        sum += SampleSemanticSpread(uv + firstTap + tapStep * float2((float)x, (float)y), spreadPx, channel, useAntiAliased);
                    }
                }

                return saturate(sum / max((float)(taps * taps), 1.0));
            }

            // A silhouette *clip* is a different job from a penumbra: it only needs to be sub-texel,
            // so it reads a fixed small linear blur and never the 柔化半径. Widening it here would
            // fade the projection out before it reaches the source and hollow out the contact
            // region. When the shared anti-aliased copy is being used that is already satisfied by a
            // single read of it; the 1px box is the fallback otherwise.
            static const float LIL_HOCHARACTER_SEMANTIC_EDGE_PX = 1.0;

            float SampleSemanticEdge(float2 uv, int channel, float useAntiAliased)
            {
                if (_HoCharacterSemanticMaskBlurValid > 0.5 && useAntiAliased > 0.5)
                {
                    return SampleSemanticBit(uv, channel, useAntiAliased);
                }

                return SampleSemanticBlur(uv, LIL_HOCHARACTER_SEMANTIC_EDGE_PX, 0.0, channel, useAntiAliased);
            }

            float SampleRevealArea(float2 uv, float useAntiAliased)
            {
                return lerp(1.0, SampleSemanticEdge(uv, 2, useAntiAliased), saturate(_HoCharacterHairShadowParams1.z));
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
                // The eye reveal's own anti-aliasing choice (options.w).
                float frontHair = SampleSemanticEdge(uv, 0, _HoCharacterSemanticMaskOptions.w);
                float eyeAlpha = SampleEyeAlpha(uv);
                float revealArea = SampleRevealArea(uv, _HoCharacterSemanticMaskOptions.w);
                float hairDepth = normalDepth.a;
                float rawEyeAlpha = max(eyeData.r, 0.0001);
                float eyeDepth = eyeData.g / rawEyeAlpha;
                float eyeCharacterId = eyeData.b / rawEyeAlpha;
                float depthBias = max(_HoCharacterEyeRevealParams.w, 0.0);
                float hairInFront = step(0.0001, eyeDepth) * step(hairDepth, eyeDepth + depthBias);
                float same = SameCharacter(maskId.g, eyeCharacterId);
                return saturate(frontHair * eyeAlpha * revealArea * hairInFront * same * _HoCharacterEyeRevealParams.x);
            }

            float ResolveEyeAngleFactor(float2 uv)
            {
                float strength = saturate(_HoCharacterEyeAngleParams.x);
                if (strength <= 0.0001)
                {
                    return 1.0;
                }

                // 与 RevealEyeMask 的 SameCharacter 同源：用眼睛捕获里的角色 ID（预乘取回）作为表的行号，
                // 避免 maskId.g（前发像素的角色 ID）与设置骨骼的 Group 错位导致的空行。
                float4 eyeData = SAMPLE_TEXTURE2D_X(_lilHoCharacterEyeDataTexture, sampler_PointClamp, uv);
                float charId = round((eyeData.b / max(eyeData.r, 0.0001)) * 255.0);
                float2 yawPitch = SAMPLE_TEXTURE2D(_lilHoCharacterEyeAngleTable, sampler_PointClamp, float2((charId + 0.5) / 256.0, 0.5)).xy;
                // 某轴 range 为 0 表示该轴不参与衰减。
                float2 activeAxis = step(0.001, abs(_HoCharacterEyeAngleParams.yz));
                float2 range = max(abs(_HoCharacterEyeAngleParams.yz), 0.0001);
                float2 normalizedAngle = abs(yawPitch) / range;
                float2 softness = max(abs(_HoCharacterEyeAngleParams.w), 0.0001) / range;
                // 超出范围的轴直接衰减到 0（视锥外眼透完全关闭），柔化带位于范围边缘内侧。
                float2 axisFactor = 1.0 - smoothstep(max(float2(0.0, 0.0), float2(1.0, 1.0) - softness), float2(1.0, 1.0), normalizedAngle);
                axisFactor = lerp(float2(1.0, 1.0), axisFactor, activeAxis);
                // 视锥内 = 1（白），视锥外 = 0（黑）；柔化为 0 时是硬边二元。
                return lerp(1.0, axisFactor.x * axisFactor.y, strength);
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
                // The hair shadow's own anti-aliasing choice (options.y) covers everything it reads:
                // the penumbra source and the receiver gate.
                float useAntiAliased = _HoCharacterSemanticMaskOptions.y;
                float shiftedHair = SampleSemanticBlur(shiftedUv, softnessPx, spreadPx, 0, useAntiAliased);
                // The receiver is what clips the projection at the hairline, so it needs the
                // sub-texel read too - otherwise the band's upper boundary stays binary even though
                // its lower (penumbra) boundary is smooth.
                float receiver = saturate(SampleSemanticEdge(uv, 1, useAntiAliased) + revealMask);

                float4 currentId = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaskIdTexture, sampler_PointClamp, uv);
                float4 shiftedId = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaskIdTexture, sampler_PointClamp, shiftedUv);
                float same = SameCharacter(currentId.g, shiftedId.g);
                return saturate(shiftedHair * receiver * same);
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

                // The face hair diffuse's own anti-aliasing choice (options.z).
                float frontHair = SampleSemanticEdge(uv, 0, _HoCharacterSemanticMaskOptions.z);
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
                // 模糊后的受光脸颜色（乘颜色乘之前的那一层）：先除掉 alpha 的
                // mask 权重，blurredColor.rgb 里带的就是"受光脸 × mask"的加权和。
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
                float eyeAngleFactor = saturate(ResolveEyeAngleFactor(uv));
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

                if (debugMode == 16)
                {
                    // 真实因子：正面接近白，绕角色转向侧面时渐暗 → 白=1，灰=0.3x，黑=0。
                    return half4(eyeAngleFactor, eyeAngleFactor, eyeAngleFactor, source.a);
                }

                if (debugMode == 17)
                {
                    // 表原始信息：R = |平转角|/180，G = |俯仰角|/180，B = 强度（>0 表示修正参数已进入渲染）。
                    float4 debugEyeData = SAMPLE_TEXTURE2D_X(_lilHoCharacterEyeDataTexture, sampler_PointClamp, uv);
                    float debugCharId = round((debugEyeData.b / max(debugEyeData.r, 0.0001)) * 255.0);
                    float2 debugYawPitch = SAMPLE_TEXTURE2D(_lilHoCharacterEyeAngleTable, sampler_PointClamp, float2((debugCharId + 0.5) / 256.0, 0.5)).xy;
                    return half4(abs(debugYawPitch.x) / 180.0, abs(debugYawPitch.y) / 180.0, saturate(_HoCharacterEyeAngleParams.x), source.a);
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

                if (debugMode == 18)
                {
                    if (_HoCharacterFaceHairDiffuseOptions.y <= 0.5)
                    {
                        return half4(0.0, 0.0, 0.0, source.a);
                    }

                    // ① 捕获到的受光脸（原始采样）：源趟在 debug 18 下把这次采样原样写进源色纹理的 rgb
                    // （HoCharacterFaceHairDiffuse.shader pass 0），这里只是把它显示出来。
                    return half4(SAMPLE_TEXTURE2D_X(_lilHoCharacterFaceHairDiffuseSourceColorTexture, sampler_PointClamp, uv).rgb, source.a);
                }

                if (debugMode == 19)
                {
                    if (_HoCharacterFaceHairDiffuseOptions.y <= 0.5)
                    {
                        return half4(0.0, 0.0, 0.0, source.a);
                    }

                    // ② 模糊后（乘颜色乘之前）：blurredColor.rgb / blurredColor.a，与 debug 7 同内容，
                    // 这里按"阶段顺序"再给一个入口。
                    return half4(ResolveFaceHairDiffuseColor(uv), source.a);
                }

                if (debugMode == 20)
                {
                    if (_HoCharacterFaceHairDiffuseOptions.y <= 0.5)
                    {
                        return half4(0.0, 0.0, 0.0, source.a);
                    }

                    // ③ 乘颜色乘之后（正式合成用的那层颜色）。
                    return half4(ResolveFaceHairDiffuseColor(uv) * (half3)_HoCharacterFaceHairDiffuseTintColor.rgb, source.a);
                }

                if (debugMode == 21)
                {
                    // ④ 最终合成：本支按正式合成的数学与顺序叠到当前画面上（不叠眼透/前发投影/轮廓，
                    // 也不受它们影响），用来单独判断落色。要看整帧最终画面请用 关闭 +「启用脸色扩散」。
                    half3 faceHairDiffuseOnly = source.rgb;
                    float faceHairDiffuseDebugAmount = faceHairDiffuseMask * saturate(_HoCharacterFaceHairDiffuseParams.x) * saturate(_HoCharacterFaceHairDiffuseTintColor.a) * saturate(_HoCharacterFaceHairDiffuseOptions.x);
                    if (faceHairDiffuseDebugAmount > 0.0001)
                    {
                        half3 faceHairDiffuseDebugColor = ResolveFaceHairDiffuseColor(uv) * (half3)_HoCharacterFaceHairDiffuseTintColor.rgb;
                        faceHairDiffuseOnly = BlendFaceHairDiffuse(faceHairDiffuseOnly, faceHairDiffuseDebugColor, faceHairDiffuseDebugAmount);
                    }

                    return half4(faceHairDiffuseOnly, source.a);
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

                half3 color = lerp(source.rgb, eyeColor.rgb, revealMask * eyeAngleFactor);
                float shadowAmount = shadowMask * saturate(_HoCharacterHairShadowParams.x);
                if (shadowAmount > 0.0001)
                {
                    half3 shadowColor = (half3)_HoCharacterHairShadowColor.rgb;
                    if (round(_HoCharacterHairShadowParams1.y) < 0.5)
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
                    // 扩散的是**受光脸**（模糊后的），颜色乘只在这一步乘上去 —— 它是层色，不是被扩散的内容。
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
