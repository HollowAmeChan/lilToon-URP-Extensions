using System;

namespace lilToon.URP.Extensions.PostProcessing
{
    /// <summary>
    /// Bakes a Unity-style gradient (up to <see cref="MaxKeys"/> colour keys and alpha keys) into
    /// a 1D ramp buffer, as RGBA in display space.
    /// </summary>
    /// <remarks>
    /// This type deliberately uses no <c>UnityEngine</c> types: it is the core that both the
    /// runtime ramp cache and the editor preview call, and it can be compiled and executed on its
    /// own for numeric verification (see .codex-research/gradient_map_sim/bake_core_check).
    ///
    /// Semantics:
    /// <list type="bullet">
    /// <item><description>Colour keys and alpha keys are interpolated independently, like <c>Gradient.Evaluate</c>.</description></item>
    /// <item><description>Blend mode interpolates between the two surrounding keys; <paramref name="fixedMode"/> keeps each key's colour until the next key (the flat blocks the Unity gradient editor draws in Fixed mode).</description></item>
    /// <item><description><paramref name="space"/> selects the interpolation space: 0 display, 1 linear light, 2 Oklab. The baked samples are always converted back to display space, so the shader just samples the texture.</description></item>
    /// <item><description>Because the ramp is baked, the interpolation space costs nothing at render time and the bake is not limited to two stops per segment.</description></item>
    /// </list>
    /// </remarks>
    internal static class ImageProcessGradientRampBaker
    {
        /// <summary>Unity's gradient key limit.</summary>
        internal const int MaxKeys = 8;

        internal const int SpaceDisplay = 0;
        internal const int SpaceLinearLight = 1;
        internal const int SpaceOklab = 2;
        internal const int SpaceCount = 3;

        /// <summary>One colour key, in display space (sRGB).</summary>
        internal struct ColorKey
        {
            public float Time;
            public float Red;
            public float Green;
            public float Blue;

            public ColorKey(float time, float red, float green, float blue)
            {
                Time = time;
                Red = red;
                Green = green;
                Blue = blue;
            }
        }

        internal struct AlphaKey
        {
            public float Time;
            public float Alpha;

            public AlphaKey(float time, float alpha)
            {
                Time = time;
                Alpha = alpha;
            }
        }

        /// <summary>
        /// Copies the keys into <paramref name="destination"/> sorted by time and clamped to [0,1],
        /// returning how many were copied. Stable insertion sort: identical times keep their order.
        /// </summary>
        internal static int NormalizeColorKeys(ColorKey[] source, int count, ColorKey[] destination)
        {
            if (source == null || destination == null)
            {
                return 0;
            }

            int limit = Math.Min(Math.Min(count, source.Length), Math.Min(destination.Length, MaxKeys));
            for (int i = 0; i < limit; i++)
            {
                ColorKey key = source[i];
                key.Time = Clamp01(key.Time);

                int insert = i;
                while (insert > 0 && destination[insert - 1].Time > key.Time)
                {
                    destination[insert] = destination[insert - 1];
                    insert--;
                }

                destination[insert] = key;
            }

            return limit;
        }

        internal static int NormalizeAlphaKeys(AlphaKey[] source, int count, AlphaKey[] destination)
        {
            if (source == null || destination == null)
            {
                return 0;
            }

            int limit = Math.Min(Math.Min(count, source.Length), Math.Min(destination.Length, MaxKeys));
            for (int i = 0; i < limit; i++)
            {
                AlphaKey key = source[i];
                key.Time = Clamp01(key.Time);

                int insert = i;
                while (insert > 0 && destination[insert - 1].Time > key.Time)
                {
                    destination[insert] = destination[insert - 1];
                    insert--;
                }

                destination[insert] = key;
            }

            return limit;
        }

        /// <summary>
        /// Bakes <paramref name="resolution"/> RGBA samples into <paramref name="destination"/>
        /// (length must be at least <c>resolution * 4</c>), row order = ramp position order.
        /// </summary>
        internal static void Bake(
            ColorKey[] colorKeys,
            int colorCount,
            AlphaKey[] alphaKeys,
            int alphaCount,
            bool fixedMode,
            int space,
            int resolution,
            float[] destination)
        {
            if (destination == null || resolution <= 0 || destination.Length < resolution * 4)
            {
                return;
            }

            var sortedColors = new ColorKey[MaxKeys];
            var sortedAlphas = new AlphaKey[MaxKeys];
            int sortedColorCount = NormalizeColorKeys(colorKeys, colorCount, sortedColors);
            int sortedAlphaCount = NormalizeAlphaKeys(alphaKeys, alphaCount, sortedAlphas);

            float divisor = resolution > 1 ? resolution - 1 : 1;
            for (int x = 0; x < resolution; x++)
            {
                Evaluate(
                    sortedColors,
                    sortedColorCount,
                    sortedAlphas,
                    sortedAlphaCount,
                    fixedMode,
                    space,
                    x / divisor,
                    out float red,
                    out float green,
                    out float blue,
                    out float alpha);

                int index = x * 4;
                destination[index] = red;
                destination[index + 1] = green;
                destination[index + 2] = blue;
                destination[index + 3] = alpha;
            }
        }

        /// <summary>Evaluates one ramp position. Output is display-space RGBA.</summary>
        internal static void Evaluate(
            ColorKey[] colorKeys,
            int colorCount,
            AlphaKey[] alphaKeys,
            int alphaCount,
            bool fixedMode,
            int space,
            float t,
            out float red,
            out float green,
            out float blue,
            out float alpha)
        {
            t = Clamp01(t);

            if (colorCount <= 0)
            {
                red = green = blue = 1.0f;
            }
            else if (colorCount == 1 || colorKeys[0].Time >= t)
            {
                red = colorKeys[0].Red;
                green = colorKeys[0].Green;
                blue = colorKeys[0].Blue;
            }
            else if (colorKeys[colorCount - 1].Time <= t)
            {
                red = colorKeys[colorCount - 1].Red;
                green = colorKeys[colorCount - 1].Green;
                blue = colorKeys[colorCount - 1].Blue;
            }
            else
            {
                int index = 0;
                while (index < colorCount - 1 && colorKeys[index + 1].Time <= t)
                {
                    index++;
                }

                ColorKey from = colorKeys[index];
                ColorKey to = colorKeys[index + 1];
                float span = to.Time - from.Time;
                float local = fixedMode || span <= 0.0f ? 0.0f : Clamp01((t - from.Time) / span);
                Interpolate(from.Red, from.Green, from.Blue, to.Red, to.Green, to.Blue, local, space,
                    out red, out green, out blue);
            }

            if (alphaCount <= 0)
            {
                alpha = 1.0f;
            }
            else if (alphaCount == 1 || alphaKeys[0].Time >= t)
            {
                alpha = alphaKeys[0].Alpha;
            }
            else if (alphaKeys[alphaCount - 1].Time <= t)
            {
                alpha = alphaKeys[alphaCount - 1].Alpha;
            }
            else
            {
                int index = 0;
                while (index < alphaCount - 1 && alphaKeys[index + 1].Time <= t)
                {
                    index++;
                }

                AlphaKey from = alphaKeys[index];
                AlphaKey to = alphaKeys[index + 1];
                float span = to.Time - from.Time;
                float local = fixedMode || span <= 0.0f ? 0.0f : Clamp01((t - from.Time) / span);
                alpha = Clamp01(from.Alpha + (to.Alpha - from.Alpha) * local);
            }

            red = Clamp01(red);
            green = Clamp01(green);
            blue = Clamp01(blue);
        }

        private static void Interpolate(
            float fromRed,
            float fromGreen,
            float fromBlue,
            float toRed,
            float toGreen,
            float toBlue,
            float local,
            int space,
            out float red,
            out float green,
            out float blue)
        {
            if (space == SpaceLinearLight)
            {
                float r = SrgbToLinear(fromRed) + (SrgbToLinear(toRed) - SrgbToLinear(fromRed)) * local;
                float g = SrgbToLinear(fromGreen) + (SrgbToLinear(toGreen) - SrgbToLinear(fromGreen)) * local;
                float b = SrgbToLinear(fromBlue) + (SrgbToLinear(toBlue) - SrgbToLinear(fromBlue)) * local;
                red = LinearToSrgb(r);
                green = LinearToSrgb(g);
                blue = LinearToSrgb(b);
                return;
            }

            if (space == SpaceOklab)
            {
                LinearSrgbToOklab(SrgbToLinear(fromRed), SrgbToLinear(fromGreen), SrgbToLinear(fromBlue),
                    out float fromL, out float fromA, out float fromB);
                LinearSrgbToOklab(SrgbToLinear(toRed), SrgbToLinear(toGreen), SrgbToLinear(toBlue),
                    out float toL, out float toA, out float toB);

                OklabToLinearSrgb(
                    fromL + (toL - fromL) * local,
                    fromA + (toA - fromA) * local,
                    fromB + (toB - fromB) * local,
                    out float r, out float g, out float b);

                red = LinearToSrgb(r);
                green = LinearToSrgb(g);
                blue = LinearToSrgb(b);
                return;
            }

            red = fromRed + (toRed - fromRed) * local;
            green = fromGreen + (toGreen - fromGreen) * local;
            blue = fromBlue + (toBlue - fromBlue) * local;
        }

        // Transfer functions and the Oklab transform. The Oklab matrices are the linear-sRGB form
        // derived in .codex-research/gradient_map_sim/oklab_check.py from the W3C CSS Color 4
        // sample conversions (Ottosson 2020, recalculated for a consistent reference white).

        internal static float SrgbToLinear(float value)
        {
            if (value <= 0.04045f)
            {
                return value / 12.92f;
            }

            return Pow((value + 0.055f) / 1.055f, 2.4f);
        }

        internal static float LinearToSrgb(float value)
        {
            if (value <= 0.0031308f)
            {
                return value * 12.92f;
            }

            return 1.055f * Pow(value, 1.0f / 2.4f) - 0.055f;
        }

        internal static void LinearSrgbToOklab(float r, float g, float b, out float l, out float a, out float labB)
        {
            float coneL = 0.4122214695f * r + 0.5363325373f * g + 0.0514459933f * b;
            float coneM = 0.2119034958f * r + 0.6806995506f * g + 0.1073969535f * b;
            float coneS = 0.0883024592f * r + 0.2817188391f * g + 0.6299787017f * b;

            coneL = Cbrt(coneL);
            coneM = Cbrt(coneM);
            coneS = Cbrt(coneS);

            l = 0.2104542683f * coneL + 0.7936177747f * coneM - 0.0040720430f * coneS;
            a = 1.9779985324f * coneL - 2.4285922420f * coneM + 0.4505937096f * coneS;
            labB = 0.0259040425f * coneL + 0.7827717125f * coneM - 0.8086757549f * coneS;
        }

        internal static void OklabToLinearSrgb(float l, float a, float labB, out float r, out float g, out float b)
        {
            float coneL = l + 0.3963377774f * a + 0.2158037573f * labB;
            float coneM = l - 0.1055613458f * a - 0.0638541728f * labB;
            float coneS = l - 0.0894841775f * a - 1.2914855480f * labB;

            coneL = coneL * coneL * coneL;
            coneM = coneM * coneM * coneM;
            coneS = coneS * coneS * coneS;

            r = 4.0767416361f * coneL - 3.3077115393f * coneM + 0.2309699032f * coneS;
            g = -1.2684379733f * coneL + 2.6097573493f * coneM - 0.3413193760f * coneS;
            b = -0.0041960761f * coneL - 0.7034186179f * coneM + 1.7076146941f * coneS;
        }

        private static float Clamp01(float value)
        {
            if (value < 0.0f)
            {
                return 0.0f;
            }

            return value > 1.0f ? 1.0f : value;
        }

        /// <summary>Sign-preserving cube root; C# has no MathF.Cbrt on the Unity profile.</summary>
        private static float Cbrt(float value)
        {
            float magnitude = Pow(value < 0.0f ? -value : value, 1.0f / 3.0f);
            return value < 0.0f ? -magnitude : magnitude;
        }

        private static float Pow(float value, float exponent)
        {
            return (float)Math.Pow(value, exponent);
        }
    }
}
