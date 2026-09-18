using System;

namespace lilToon.URP.Extensions.PostProcessing
{
    /// <summary>Halftone pattern family. Mirrored by <c>Halftone.shader</c>; keep the numbering.</summary>
    public enum ImageProcessHalftoneMode
    {
        /// <summary>Ordered dithering with a Bayer matrix (2x2 / 3x3 / 4x4 / 8x8).</summary>
        Bayer = 0,
        /// <summary>Round AM screen dot; area proportional to tone until the dots touch.</summary>
        Round = 1,
        /// <summary>Square dot; cell coverage is exactly the tone.</summary>
        Square = 2,
        /// <summary>Diamond/elliptical dot; chains at 50% like a real print screen.</summary>
        Diamond = 3,
        /// <summary>Line screen (engraving style); coverage is exactly the tone.</summary>
        Line = 4
    }

    /// <summary>Which quantity of the incoming pixel indexes the screen.</summary>
    /// <remarks>Same encoding as the GradientMap effect's input source, so the two agree.</remarks>
    public enum ImageProcessHalftoneInput
    {
        Luminance = 0,
        Red = 1,
        Green = 2,
        Blue = 3,
        Maximum = 4,
        Average = 5,
        Saturation = 6
    }

    /// <summary>How the ink/paper pair is combined with the incoming image.</summary>
    public enum ImageProcessHalftoneComposite
    {
        /// <summary>Print the ink colour over the image; untouched where there is no ink.</summary>
        OverInk = 0,
        /// <summary>Two-colour print: the image is replaced by ink/paper (pop art, B/W comic).</summary>
        TwoTone = 1,
        /// <summary>Overprint: the image keeps its shading and is tinted by the ink/paper ramp.</summary>
        Multiply = 2,
        /// <summary>Screen: the dots keep the image colour, the gaps take the paper tint.</summary>
        Mask = 3
    }

    /// <summary>
    /// Coverage maths of the Halftone effect, with no engine types so it can be tested directly
    /// (see .codex-research/halftone_sim). The parameter list mirrors the shader's
    /// <c>HoHalftoneCoverage</c> argument for argument.
    /// </summary>
    /// <remarks>
    /// Conventions, shared with <c>Halftone.shader</c>:
    /// <list type="bullet">
    /// <item>a cell is the unit square [-0.5, 0.5]^2, so its area is 1 and "coverage" is the inked
    /// fraction of the cell;</item>
    /// <item>dot radius is chosen so the inked area equals the tone (<c>r = sqrt(tone/pi)</c> for a
    /// circle, <c>sqrt(tone)</c>/2 for a square, <c>sqrt(tone/2)</c> for a diamond) up to the point
    /// where neighbouring dots merge; past that point the radius is bridged to "covers the cell" so
    /// the ramp still reaches solid;</item>
    /// <item>Bayer thresholds use <c>(M + 0.5) / n^2</c>, which makes the matrix mean exactly the
    /// tone.</item>
    /// </list>
    /// </remarks>
    internal static class ImageProcessHalftoneMath
    {
        public const int ModeCount = 5;
        public const int InputCount = 7;
        public const int CompositeCount = 4;

        /// <summary>pi/4: coverage at which an inscribed circle touches the cell edges.</summary>
        public const float MergeCoverageCircle = 0.7853981633974483f;

        /// <summary>Half the cell diagonal: the normalised distance from the centre to a corner.</summary>
        public const float CellCorner = 0.7071067811865476f;

        private const float Epsilon = 1e-6f;

        /// <summary>Matrix sizes of the Bayer modes, indexed by <c>parameters3.z</c>.</summary>
        private static readonly int[] BayerSizes = { 2, 3, 4, 8 };

        public static int BayerSizeCount => BayerSizes.Length;

        public static int BayerSize(int index)
        {
            int clamped = index < 0 ? 0 : (index >= BayerSizes.Length ? BayerSizes.Length - 1 : index);
            return BayerSizes[clamped];
        }

        /// <summary>
        /// Bayer threshold in (0, 1) for the matrix cell <c>(x, y)</c>. Built from the classic
        /// recursion <c>M(2n) = 4 * M(n) + B</c> with <c>B = [[0, 2], [3, 1]]</c> for the powers of
        /// two, plus the published 3x3 matrix. Verified to be a permutation of 0..n^2-1 by
        /// .codex-research/halftone_sim.
        /// </summary>
        public static float BayerThreshold(int sizeIndex, int x, int y)
        {
            int size = BayerSize(sizeIndex);
            int value = BayerValue(size, Mod(x, size), Mod(y, size));
            return (value + 0.5f) / (size * size);
        }

        /// <summary>Raw matrix entry (0..n^2-1). Exposed for the harness.</summary>
        public static int BayerValue(int size, int x, int y)
        {
            switch (size)
            {
                case 2:
                    return Bayer2(x, y);
                case 3:
                    // Published 3x3 dispersed-dot matrix (not a power of two, so no recursion).
                    return Bayer3(x, y);
                case 4:
                    return 4 * Bayer2(x & 1, y & 1) + Bayer2(x >> 1, y >> 1);
                default:
                    return 16 * Bayer2(x & 1, y & 1)
                        + 4 * Bayer2((x >> 1) & 1, (y >> 1) & 1)
                        + Bayer2(x >> 2, y >> 2);
            }
        }

        private static int Bayer2(int x, int y)
        {
            // [[0, 2], [3, 1]]
            if (y == 0)
            {
                return x == 0 ? 0 : 2;
            }

            return x == 0 ? 3 : 1;
        }

        private static int Bayer3(int x, int y)
        {
            // [[0, 7, 3], [6, 5, 2], [4, 1, 8]]
            int index = y * 3 + x;
            switch (index)
            {
                case 0: return 0;
                case 1: return 7;
                case 2: return 3;
                case 3: return 6;
                case 4: return 5;
                case 5: return 2;
                case 6: return 4;
                case 7: return 1;
                default: return 8;
            }
        }

        /// <summary>Which quantity indexes the screen; Rec.709 luma is index 0.</summary>
        public static float ResolveInput(float r, float g, float b, int source)
        {
            switch (source)
            {
                case 1: return r;
                case 2: return g;
                case 3: return b;
                case 4: return Math.Max(r, Math.Max(g, b));
                case 5: return (r + g + b) * (1.0f / 3.0f);
                case 6: return Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                default: return (0.2126f * r) + (0.7152f * g) + (0.0722f * b);
            }
        }

        /// <summary>Black/white point window, clamped to 0..1.</summary>
        public static float NormalizeTone(float value, float blackPoint, float whitePoint)
        {
            float range = whitePoint - blackPoint;
            float normalized = Math.Abs(range) > Epsilon
                ? (value - blackPoint) / range
                : (value >= whitePoint ? 1.0f : 0.0f);
            return Clamp01(normalized);
        }

        /// <summary>Ink coverage (0 = paper, 1 = solid ink) for one pixel.</summary>
        public static float Coverage(
            int mode,
            float tone,
            float x,
            float y,
            float cell,
            float angle,
            float softness,
            float jitter,
            float ratio,
            int bayerIndex)
        {
            float clampedTone = Clamp01(tone);
            float clampedCell = Math.Max(cell, 0.5f);
            softness = Math.Max(softness, 0.0f);

            // Screen coordinates: the screen is turned by `angle`, so lattice coordinates are the
            // pixel coordinates rotated back by -angle (R(-a) = transpose of R(a)).
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);
            float rotatedX = ((x * cos) + (y * sin)) / clampedCell;
            float rotatedY = ((y * cos) - (x * sin)) / clampedCell;

            if (mode == (int)ImageProcessHalftoneMode.Bayer)
            {
                int size = BayerSize(bayerIndex);
                float threshold = BayerThreshold(bayerIndex, FloorToInt(rotatedX), FloorToInt(rotatedY));
                return ThresholdDirect(clampedTone, threshold, softness, size);
            }

            float cellX = Floor(rotatedX);
            float cellY = Floor(rotatedY);
            float localX = rotatedX - cellX - 0.5f;
            float localY = rotatedY - cellY - 0.5f;

            if (jitter > 0.0f)
            {
                // Displace this cell's dot inside its own cell: seamless, because the offset is a
                // function of the (integer) cell index only.
                float offsetX = Hash12(cellX, cellY) - 0.5f;
                float offsetY = Hash12(cellX + 17.0f, cellY - 31.0f) - 0.5f;
                localX -= offsetX * jitter;
                localY -= offsetY * jitter;
            }

            switch (mode)
            {
                case (int)ImageProcessHalftoneMode.Square:
                {
                    // Coverage = (2r)^2 = tone  =>  r = sqrt(tone) / 2, and r = 0.5 covers the cell.
                    float radius = 0.5f * (float)Math.Sqrt(clampedTone);
                    float distance = Math.Max(Math.Abs(localX), Math.Abs(localY));
                    return Threshold(radius, distance, softness, 0.5f);
                }
                case (int)ImageProcessHalftoneMode.Diamond:
                {
                    // Diamond {|x| + |y| <= r} has area 2r^2 => r = sqrt(tone / 2) up to the 50%
                    // chaining point, then bridged to r = 1 (the whole cell).
                    float diamondRatio = Math.Max(ratio, 0.05f);
                    float distance = (Math.Abs(localX) * diamondRatio) + Math.Abs(localY);
                    float radius = DiamondRadius(clampedTone, diamondRatio);
                    return Threshold(radius, distance, softness, 1.0f);
                }
                case (int)ImageProcessHalftoneMode.Line:
                {
                    // A band of width `tone` across the cell: coverage is exactly the tone.
                    return Threshold(0.5f * clampedTone, Math.Abs(localY), softness, 0.5f);
                }
                default:
                {
                    // Round: area pi*r^2 = tone => r = sqrt(tone / pi) up to the tangency point
                    // (tone = pi/4), then bridged from r = 0.5 to the corner radius 0.7071.
                    float distance = (float)Math.Sqrt((localX * localX) + (localY * localY));
                    return Threshold(RoundRadius(clampedTone), distance, softness, CellCorner);
                }
            }
        }

        /// <summary>Circle radius for a tone, area-matched up to the merge point.</summary>
        public static float RoundRadius(float tone)
        {
            float clamped = Clamp01(tone);
            if (clamped <= MergeCoverageCircle)
            {
                return (float)Math.Sqrt(clamped / Math.PI);
            }

            float blend = (clamped - MergeCoverageCircle) / (1.0f - MergeCoverageCircle);
            return 0.5f + (blend * (CellCorner - 0.5f));
        }

        /// <summary>Diamond radius for a tone, area-matched up to the 50% chaining point.</summary>
        public static float DiamondRadius(float tone, float ratio)
        {
            float clamped = Clamp01(tone);
            // The ratio stretches the diamond horizontally, which scales its area by 1/ratio:
            // keep the area match by dividing the radius by sqrt(1/ratio) ... i.e. multiply by
            // sqrt(ratio) so the inked area still equals the tone.
            float areaScale = (float)Math.Sqrt(Math.Max(ratio, 0.05f));
            if (clamped <= 0.5f)
            {
                return areaScale * (float)Math.Sqrt(clamped * 0.5f);
            }

            float blend = (clamped - 0.5f) * 2.0f;
            return areaScale * (0.5f + (blend * 0.5f));
        }

        /// <summary>Combine the ink/paper pair with the image. One channel.</summary>
        public static float Composite(int composite, float image, float ink, float paper, float coverage)
        {
            float c = Clamp01(coverage);
            switch (composite)
            {
                case (int)ImageProcessHalftoneComposite.TwoTone:
                    return Lerp(paper, ink, c);
                case (int)ImageProcessHalftoneComposite.Multiply:
                    return image * Lerp(paper, ink, c);
                case (int)ImageProcessHalftoneComposite.Mask:
                    return image * Lerp(paper, 1.0f, c);
                default:
                    return Lerp(image, ink, c);
            }
        }

        /// <summary>
        /// Hash used for the per-cell jitter; verbatim mirror of the shader's
        /// <c>frac(float3(p.xyx) * 0.1031)</c> construction (one scalar multiply, not per component).
        /// </summary>
        public static float Hash12(float x, float y)
        {
            float px = Frac(x * 0.1031f);
            float py = Frac(y * 0.1031f);
            float pz = px; // p.xyx
            float dot = (px * (py + 33.33f)) + (py * (pz + 33.33f)) + (pz * (px + 33.33f));
            px += dot;
            py += dot;
            pz += dot;
            return Frac((px + py) * pz);
        }

        private static float Threshold(float threshold, float distance, float softness, float scale)
        {
            if (softness <= Epsilon)
            {
                return distance <= threshold ? 1.0f : 0.0f;
            }

            // Linear transition of `softness` (in cell units) around the threshold. `scale` maps the
            // caller's distance range onto the cell so one softness value behaves the same in every
            // mode.
            float width = Math.Max(softness * scale, Epsilon);
            return Clamp01(((threshold - distance) / width) + 0.5f);
        }

        /// <summary>
        /// Bayer compares the tone against the threshold (the other modes compare a radius against a
        /// distance), so it needs its own wrapper.
        /// </summary>
        private static float ThresholdDirect(float tone, float threshold, float softness, float size)
        {
            if (softness <= Epsilon)
            {
                return tone >= threshold ? 1.0f : 0.0f;
            }

            float width = Math.Max(softness * size, Epsilon);
            return Clamp01(((tone - threshold) / width) + 0.5f);
        }

        private static float Frac(float value)
        {
            return value - (float)Math.Floor(value);
        }

        private static float Floor(float value)
        {
            return (float)Math.Floor(value);
        }

        private static int FloorToInt(float value)
        {
            return (int)Math.Floor(value);
        }

        private static int Mod(int value, int modulo)
        {
            int result = value % modulo;
            return result < 0 ? result + modulo : result;
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + ((b - a) * t);
        }

        private static float Clamp01(float value)
        {
            if (value < 0.0f)
            {
                return 0.0f;
            }

            return value > 1.0f ? 1.0f : value;
        }
    }
}
