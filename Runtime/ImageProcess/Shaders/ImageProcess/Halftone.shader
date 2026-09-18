Shader "Hidden/lilToon/URP/ImageProcess/Halftone"
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
            Name "ImageProcess Halftone"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragHalftone

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ImageProcess/Shaders/ImageProcess/ImageProcessBlend.hlsl"

            float _Intensity;
            float _LayerBlendMode;
            // Ink colour (the dots). Alpha is unused.
            float4 _LayerColor;
            // x mode (0 Bayer, 1 round, 2 square, 3 diamond, 4 line), y input source,
            // z black point, w white point
            float4 _LayerParams0;
            // x cell size in pixels, y screen angle in degrees, z invert, w softness (cell fraction)
            float4 _LayerParams1;
            // xyz paper colour (what is *not* inked), w composite mode
            float4 _LayerParams2;
            // x coverage ceiling, y per-cell jitter, z Bayer matrix index (0:2x2 .. 3:8x8), w unused
            float4 _LayerParams3;
            // x diamond/ellipse aspect ratio, y output dither (8-bit LSB), zw unused
            float4 _LayerParams4;

            // ---- Halftone maths ----------------------------------------------------------------
            // Mirror of Runtime/ImageProcess/Renderer/Halftone/ImageProcessHalftoneMath.cs, which is
            // what .codex-research/halftone_sim tests by numeric integration. Keep the two in step:
            // a cell is the unit square [-0.5, 0.5]^2 and "coverage" is its inked fraction.

            #define HO_HALFTONE_MERGE_COVERAGE 0.7853981633974483  // pi/4: inscribed circle touches
            #define HO_HALFTONE_CELL_CORNER 0.7071067811865476     // half the cell diagonal

            float HoHalftoneBayer2(int x, int y)
            {
                // [[0, 2], [3, 1]]
                if (y == 0)
                {
                    return x == 0 ? 0.0 : 2.0;
                }

                return x == 0 ? 3.0 : 1.0;
            }

            float HoHalftoneBayer3(int x, int y)
            {
                // Published 3x3 dispersed-dot matrix: 0 7 3 / 6 5 2 / 4 1 8
                int index = y * 3 + x;
                if (index == 0) return 0.0;
                if (index == 1) return 7.0;
                if (index == 2) return 3.0;
                if (index == 3) return 6.0;
                if (index == 4) return 5.0;
                if (index == 5) return 2.0;
                if (index == 6) return 4.0;
                if (index == 7) return 1.0;
                return 8.0;
            }

            // Bayer matrix entry (0..n^2-1): M(2n) = 4 * M(n) + B with B = [[0, 2], [3, 1]].
            float HoHalftoneBayerValue(int size, int x, int y)
            {
                if (size == 2)
                {
                    return HoHalftoneBayer2(x, y);
                }

                if (size == 3)
                {
                    return HoHalftoneBayer3(x, y);
                }

                if (size == 4)
                {
                    return 4.0 * HoHalftoneBayer2(x & 1, y & 1) + HoHalftoneBayer2(x >> 1, y >> 1);
                }

                return 16.0 * HoHalftoneBayer2(x & 1, y & 1)
                    + 4.0 * HoHalftoneBayer2((x >> 1) & 1, (y >> 1) & 1)
                    + HoHalftoneBayer2(x >> 2, y >> 2);
            }

            int HoHalftoneBayerSize(int index)
            {
                if (index <= 0) return 2;
                if (index == 1) return 3;
                if (index == 2) return 4;
                return 8;
            }

            // (M + 0.5) / n^2: makes the matrix mean exactly the tone. Wrapping avoids the integer
            // modulus instruction (X3556): powers of two use a mask, and the 3x3 matrix uses a float
            // remainder, which is also correct for negative lattice coordinates.
            float HoHalftoneBayerThreshold(int index, float2 lattice)
            {
                int size = HoHalftoneBayerSize(index);
                int x;
                int y;
                if (size == 3)
                {
                    x = (int)(lattice.x - 3.0 * floor(lattice.x / 3.0));
                    y = (int)(lattice.y - 3.0 * floor(lattice.y / 3.0));
                }
                else
                {
                    x = ((int)floor(lattice.x)) & (size - 1);
                    y = ((int)floor(lattice.y)) & (size - 1);
                }

                return (HoHalftoneBayerValue(size, x, y) + 0.5) / (size * size);
            }

            // Rec.709 luma is index 0; the rest match the GradientMap effect's input source.
            float HoHalftoneResolveInput(float3 color, int source)
            {
                if (source == 1) return color.r;
                if (source == 2) return color.g;
                if (source == 3) return color.b;
                if (source == 4) return max(color.r, max(color.g, color.b));
                if (source == 5) return (color.r + color.g + color.b) * (1.0 / 3.0);
                if (source == 6) return max(color.r, max(color.g, color.b)) - min(color.r, min(color.g, color.b));
                return dot(color, float3(0.2126, 0.7152, 0.0722));
            }

            float HoHalftoneNormalizeTone(float value, float blackPoint, float whitePoint)
            {
                float range = whitePoint - blackPoint;
                float normalized = abs(range) > 1e-6
                    ? (value - blackPoint) / range
                    : (value >= whitePoint ? 1.0 : 0.0);
                return saturate(normalized);
            }

            // Circle radius whose area equals the tone, bridged to the corner radius past tangency.
            float HoHalftoneRoundRadius(float tone)
            {
                float clamped = saturate(tone);
                if (clamped <= HO_HALFTONE_MERGE_COVERAGE)
                {
                    return sqrt(clamped / 3.14159265358979);
                }

                float blend = (clamped - HO_HALFTONE_MERGE_COVERAGE) / (1.0 - HO_HALFTONE_MERGE_COVERAGE);
                return 0.5 + blend * (HO_HALFTONE_CELL_CORNER - 0.5);
            }

            // Diamond radius whose area equals the tone, bridged past the 50% chaining point.
            float HoHalftoneDiamondRadius(float tone, float ratio)
            {
                float clamped = saturate(tone);
                float areaScale = sqrt(max(ratio, 0.05));
                if (clamped <= 0.5)
                {
                    return areaScale * sqrt(clamped * 0.5);
                }

                float blend = (clamped - 0.5) * 2.0;
                return areaScale * (0.5 + blend * 0.5);
            }

            float HoHalftoneThreshold(float threshold, float distance, float softness, float scale)
            {
                if (softness <= 1e-6)
                {
                    return distance <= threshold ? 1.0 : 0.0;
                }

                float width = max(softness * scale, 1e-6);
                return saturate((threshold - distance) / width + 0.5);
            }

            // Bayer compares the tone against the threshold (the other modes compare a radius against
            // a distance), so it needs its own wrapper.
            float HoHalftoneThresholdDirect(float tone, float threshold, float softness, float size)
            {
                if (softness <= 1e-6)
                {
                    return tone >= threshold ? 1.0 : 0.0;
                }

                float width = max(softness * size, 1e-6);
                return saturate((tone - threshold) / width + 0.5);
            }

            float HoHalftoneHash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Ink coverage for one pixel. Mirrors ImageProcessHalftoneMath.Coverage(sample).
            // Single exit on purpose: several early returns make D3DCompiler warn X4000 at the call
            // site even though every path returns.
            float HoHalftoneCoverage(
                int mode,
                float tone,
                float2 pixel,
                float cell,
                float angleRadians,
                float softness,
                float jitter,
                float ratio,
                int bayerIndex)
            {
                float clampedTone = saturate(tone);
                float clampedCell = max(cell, 0.5);
                float cosAngle = cos(angleRadians);
                float sinAngle = sin(angleRadians);
                // Lattice coordinates: the screen is turned by `angle`, so rotate the pixel back.
                float2 rotated = float2(
                    pixel.x * cosAngle + pixel.y * sinAngle,
                    pixel.y * cosAngle - pixel.x * sinAngle) / clampedCell;

                float coverage;
                if (mode == 0)
                {
                    float threshold = HoHalftoneBayerThreshold(bayerIndex, rotated);
                    coverage = HoHalftoneThresholdDirect(clampedTone, threshold, softness, HoHalftoneBayerSize(bayerIndex));
                }
                else
                {
                    float2 cellIndex = floor(rotated);
                    float2 local = rotated - cellIndex - 0.5;

                    if (jitter > 0.0)
                    {
                        // Displace this cell's dot inside its own cell; the offset is a function of
                        // the (integer) cell index, so the pattern stays seamless.
                        float offsetX = HoHalftoneHash12(cellIndex) - 0.5;
                        float offsetY = HoHalftoneHash12(cellIndex + float2(17.0, -31.0)) - 0.5;
                        local.x -= offsetX * jitter;
                        local.y -= offsetY * jitter;
                    }

                    if (mode == 2)
                    {
                        coverage = HoHalftoneThreshold(0.5 * sqrt(clampedTone), max(abs(local.x), abs(local.y)), softness, 0.5);
                    }
                    else if (mode == 3)
                    {
                        float diamondRatio = max(ratio, 0.05);
                        float distance = abs(local.x) * diamondRatio + abs(local.y);
                        coverage = HoHalftoneThreshold(HoHalftoneDiamondRadius(clampedTone, diamondRatio), distance, softness, 1.0);
                    }
                    else if (mode == 4)
                    {
                        coverage = HoHalftoneThreshold(0.5 * clampedTone, abs(local.y), softness, 0.5);
                    }
                    else
                    {
                        coverage = HoHalftoneThreshold(HoHalftoneRoundRadius(clampedTone), length(local), softness, HO_HALFTONE_CELL_CORNER);
                    }
                }

                return coverage;
            }

            // Combine the ink/paper pair with the image. One channel.
            float HoHalftoneComposite(int composite, float image, float ink, float paper, float coverage)
            {
                float c = saturate(coverage);
                if (composite == 1)
                {
                    return lerp(paper, ink, c);
                }

                if (composite == 2)
                {
                    return image * lerp(paper, ink, c);
                }

                if (composite == 3)
                {
                    return image * lerp(paper, 1.0, c);
                }

                return lerp(image, ink, c);
            }

            half4 FragHalftone(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float amount = saturate(_Intensity);
                if (amount <= 0.0001)
                {
                    return source;
                }

                int mode = (int)clamp(round(_LayerParams0.x), 0.0, 4.0);
                int inputSource = (int)clamp(round(_LayerParams0.y), 0.0, 6.0);
                float tone = HoHalftoneResolveInput(source.rgb, inputSource);
                tone = HoHalftoneNormalizeTone(tone, _LayerParams0.z, _LayerParams0.w);
                if (_LayerParams1.z > 0.5)
                {
                    // Dark areas take the ink (normal print/comic); off = negative print.
                    tone = 1.0 - tone;
                }

                float2 pixel = floor(input.texcoord * _ScreenParams.xy);
                float coverage = HoHalftoneCoverage(
                    mode,
                    tone,
                    pixel,
                    max(_LayerParams1.x, 1.0),
                    radians(_LayerParams1.y),
                    max(_LayerParams1.w, 0.0),
                    saturate(_LayerParams3.y),
                    max(_LayerParams4.x, 0.05),
                    (int)clamp(round(_LayerParams3.z), 0.0, 3.0));
                coverage = saturate(coverage * saturate(_LayerParams3.x));

                int composite = (int)clamp(round(_LayerParams2.w), 0.0, 3.0);
                half3 printed = half3(
                    HoHalftoneComposite(composite, source.r, _LayerColor.r, _LayerParams2.r, coverage),
                    HoHalftoneComposite(composite, source.g, _LayerColor.g, _LayerParams2.g, coverage),
                    HoHalftoneComposite(composite, source.b, _LayerColor.b, _LayerParams2.b, coverage));

                half3 blended = ApplyLayerBlend(source.rgb, printed, _LayerBlendMode);
                half3 result = lerp(source.rgb, blended, amount);

                float ditherStrength = max(_LayerParams4.y, 0.0);
                if (ditherStrength > 0.0001)
                {
                    // Output dither: symmetric triangular noise, +-0.5 LSB per unit of strength, in
                    // display space (the same construction the GradientMap effect uses).
                    float3 noise = float3(
                        HoHalftoneHash12(pixel + float2(0.5, 0.5)),
                        HoHalftoneHash12(pixel + float2(37.7, 11.3)),
                        HoHalftoneHash12(pixel + float2(91.3, 73.1)));
                    noise = noise * 2.0 - 1.0;
                    noise = sign(noise) * (1.0 - sqrt(max(1.0 - abs(noise), 0.0)));
                    result += half3(noise * (ditherStrength * 0.5 / 255.0));
                }

                return half4(result, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
