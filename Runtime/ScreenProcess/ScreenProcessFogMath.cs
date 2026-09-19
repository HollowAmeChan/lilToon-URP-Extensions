using System;

namespace lilToon.URP.Extensions.PostProcessing
{
    /// <summary>
    /// Fog maths for <see cref="ScreenProcessEffect.DepthFog"/>: depth falloff, height window/falloff
    /// and the depth-buffer conversions the shader needs.
    /// </summary>
    /// <remarks>
    /// Two reasons this lives apart from the shader:
    /// <list type="bullet">
    /// <item><description>It uses no <c>UnityEngine</c> types, so it can be compiled and executed
    /// outside Unity for numeric verification (see .codex-research/depth_fog_sim/fog_math_check).</description></item>
    /// <item><description>The editor UI and the presets read the same mode constants, so a preset
    /// cannot silently disagree with the shader about what mode 2 means.</description></item>
    /// </list>
    /// This is a <b>compositing</b> fog, not a physical one: the "density" values are curve
    /// parameters for a layer alpha, not physical quantities.
    /// </remarks>
    internal static class ScreenProcessFogMath
    {
        // Mode values live in the public enums (ScreenProcessFogMode.cs) so the inspector, the
        // presets and this maths cannot drift apart; the shader mirrors the same numbers.

        /// <summary>
        /// Depth falloff: 0 at/before <paramref name="start"/>, approaching 1 with distance.
        /// <paramref name="far"/> is only used by the linear mode; the exponential modes are driven
        /// by <paramref name="density"/>.
        /// </summary>
        internal static float DepthAlpha(ScreenProcessFogDepthMode mode, float distance, float start, float far, float density)
        {
            float d = distance < 0.0f ? 0.0f : distance;

            if (mode == ScreenProcessFogDepthMode.Linear)
            {
                float span = far - start;
                if (span <= 1e-5f)
                {
                    return d >= far ? 1.0f : 0.0f;
                }

                return Saturate((d - start) / span);
            }

            float scaled = density * Max(d - start, 0.0f);
            if (mode == ScreenProcessFogDepthMode.ExponentialSquared)
            {
                scaled *= scaled;
            }

            return 1.0f - Exp(-scaled);
        }

        /// <summary>
        /// Height multiplier for the fog layer alpha. Callers skip the height slot entirely when it
        /// is switched off, so there is no "off" value here.
        /// </summary>
        /// <param name="a">
        /// Window mode: the height where the fog is fully dense. Falloff mode: the base (ground)
        /// height.
        /// </param>
        /// <param name="b">
        /// Window mode: the height where the fog has faded out. Falloff mode: the exponential rate
        /// per world unit.
        /// </param>
        /// <param name="hardness">1 = smooth, larger = sharper transition (the house S-curve).</param>
        internal static float HeightAlpha(ScreenProcessFogHeightMode mode, float height, float a, float b, float hardness)
        {
            bool windowMode = mode == ScreenProcessFogHeightMode.WindowBelow ||
                mode == ScreenProcessFogHeightMode.WindowAbove;
            bool reverse = mode == ScreenProcessFogHeightMode.WindowAbove ||
                mode == ScreenProcessFogHeightMode.FalloffAbove;

            if (windowMode)
            {
                float low = Min(a, b);
                float high = Max(a, b);
                float span = high - low;
                float t = span <= 1e-5f
                    ? (height >= high ? 1.0f : 0.0f)
                    : Saturate((height - low) / span);
                t = t * t * (3.0f - 2.0f * t);
                t = ApplyHardness(t, hardness);

                // Without reverse the fog is dense below "a" and gone above "b".
                return reverse ? t : 1.0f - t;
            }

            // Exponential falloff away from the base height: fully dense at/below the base, decaying
            // upwards (the "ground fog" case). Reversed it is dense at/above the base and decays
            // downwards, which is the "sea of clouds" case.
            float distanceFromBase = reverse ? Max(a - height, 0.0f) : Max(height - a, 0.0f);
            return Exp(-Max(b, 0.0f) * distanceFromBase);
        }

        /// <summary>Whether a height mode is the exponential falloff shape (as opposed to a window).</summary>
        internal static bool IsFalloffHeightMode(ScreenProcessFogHeightMode mode)
        {
            return mode == ScreenProcessFogHeightMode.FalloffBelow || mode == ScreenProcessFogHeightMode.FalloffAbove;
        }

        /// <summary>
        /// One channel of ScreenProcess's blend mode: the same formulas the shared table
        /// (<c>Runtime/ImageProcess/Shaders/ImageProcess/ImageProcessBlend.hlsl</c>, <c>ApplyLayerBlend</c>)
        /// uses, with the same mode numbering. Kept here so the two-slot composite order can be verified.
        /// </summary>
        /// <remarks>
        /// 只镜像 20 个**可分离**模式（0..19）。20..23 是 Photoshop 的不可分离模式
        /// （Hue / Saturation / Color / Luminosity），定义上就要跨通道运算，单通道镜像没有意义 ——
        /// 这几个模式只在着色器里有实现，检查里也只在 HLSL 侧验证。
        /// </remarks>
        internal static float BlendChannel(ScreenProcessBlendMode blendMode, float baseValue, float layerValue)
        {
            switch ((int)blendMode)
            {
                case 0: return layerValue;
                case 1: return Max(baseValue + layerValue, 0.0f);
                case 2: return baseValue * layerValue;
                case 3: return 1.0f - (1.0f - baseValue) * (1.0f - layerValue);
                case 4: return Min(baseValue, layerValue);
                case 5: return ColorBurn(baseValue, layerValue);
                case 6: return Max(baseValue + layerValue - 1.0f, 0.0f);
                case 7: return Max(baseValue, layerValue);
                case 8: return ColorDodge(baseValue, layerValue);
                case 9: return Overlay(baseValue, layerValue);
                case 10: return SoftLight(baseValue, layerValue);
                case 11: return Overlay(layerValue, baseValue);
                case 12: return VividLight(baseValue, layerValue);
                case 13: return Max(baseValue + 2.0f * layerValue - 1.0f, 0.0f);
                case 14: return Lerp(Min(baseValue, 2.0f * layerValue), Max(baseValue, 2.0f * (layerValue - 0.5f)), Step(0.5f, layerValue));
                case 15: return Step(0.5f, VividLight(baseValue, layerValue));
                case 16: return Abs(baseValue - layerValue);
                case 17: return baseValue + layerValue - 2.0f * baseValue * layerValue;
                case 18: return Max(baseValue - layerValue, 0.0f);
                case 19: return Max(baseValue / Max(layerValue, 0.0001f), 0.0f);
                default: return layerValue;
            }
        }

        /// <summary>
        /// The two slots of the effect are applied in order (depth first, height second) with the
        /// layer's blend mode and a per-slot alpha, so a single pass can carry both.
        /// </summary>
        internal static float CompositeSlot(ScreenProcessBlendMode blendMode, float baseValue, float fogValue, float alpha)
        {
            float blended = BlendChannel(blendMode, baseValue, fogValue);
            return baseValue + (blended - baseValue) * Saturate(alpha);
        }

        // ---- 共享混合表里可分离模式的逐通道镜像（公式与 HLSL 一字不差）-----------------------------

        private static float ColorBurn(float baseValue, float layerValue)
        {
            return Max(1.0f - (1.0f - baseValue) / Max(layerValue, 0.0001f), 0.0f);
        }

        private static float ColorDodge(float baseValue, float layerValue)
        {
            return Max(baseValue / Max(1.0f - layerValue, 0.0001f), 0.0f);
        }

        private static float Overlay(float baseValue, float layerValue)
        {
            return Lerp(2.0f * baseValue * layerValue, 1.0f - 2.0f * (1.0f - baseValue) * (1.0f - layerValue), Step(0.5f, baseValue));
        }

        private static float SoftLight(float baseValue, float layerValue)
        {
            float dark = baseValue - (1.0f - 2.0f * layerValue) * baseValue * (1.0f - baseValue);
            float light = baseValue + (2.0f * layerValue - 1.0f) * (Sqrt(Saturate(baseValue)) - baseValue);
            return Lerp(dark, light, Step(0.5f, layerValue));
        }

        private static float VividLight(float baseValue, float layerValue)
        {
            float burn = ColorBurn(baseValue, 2.0f * layerValue);
            float dodge = ColorDodge(baseValue, 2.0f * (layerValue - 0.5f));
            return Lerp(burn, dodge, Step(0.5f, layerValue));
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Step(float edge, float value) => value >= edge ? 1.0f : 0.0f;

        private static float Sqrt(float value) => (float)Math.Sqrt(value);

        /// <summary>The house height-fade hardness remap (same formula the outline height fade uses).</summary>
        internal static float ApplyHardness(float value, float hardness)
        {
            float exponent = Max(hardness, 0.0001f);
            float a = Pow(Saturate(value), exponent);
            float b = Pow(Saturate(1.0f - value), exponent);
            return a / Max(a + b, 0.0001f);
        }

        /// <summary>Layer alpha from the depth and height terms.</summary>
        internal static float CombineAlpha(float depthAlpha, float heightAlpha, float maxOpacity)
        {
            return Saturate(depthAlpha * heightAlpha) * Saturate(maxOpacity);
        }

        /// <summary>
        /// Converts a linear eye depth (GeometryBuffer's normal-depth alpha) back to a device depth,
        /// i.e. the exact inverse of Unity's <c>LinearEyeDepth(depth, _ZBufferParams)</c>.
        /// </summary>
        /// <remarks>
        /// <c>_ZBufferParams</c> is <c>{ f/n - 1, 1, 1/n - 1/f, 1/f }</c> with reversed Z and
        /// <c>{ 1 - f/n, f/n, 1/f - 1/n, 1/n }</c> without (core <c>Common.hlsl:1180-1215</c>).
        /// Neither direction works for orthographic projections, which is why the shader forces the
        /// camera-depth path when the camera is orthographic.
        /// </remarks>
        internal static float DeviceDepthFromLinearEye(float linearEyeDepth, float zBufferParamZ, float zBufferParamW)
        {
            float linear = Max(linearEyeDepth, 1e-6f);
            if (Abs(zBufferParamZ) <= 1e-9f)
            {
                return 0.0f;
            }

            return (1.0f / linear - zBufferParamW) / zBufferParamZ;
        }

        /// <summary>Unity's <c>LinearEyeDepth(depth, _ZBufferParams)</c>, for round-trip checks.</summary>
        internal static float LinearEyeFromDeviceDepth(float deviceDepth, float zBufferParamZ, float zBufferParamW)
        {
            return 1.0f / Max(zBufferParamZ * deviceDepth + zBufferParamW, 1e-6f);
        }

        /// <summary>Builds <c>_ZBufferParams.zw</c> the way Unity does, for tests and documentation.</summary>
        internal static void ZBufferParamsZW(float near, float far, bool reversedZ, out float z, out float w)
        {
            if (reversedZ)
            {
                z = 1.0f / near - 1.0f / far;
                w = 1.0f / far;
                return;
            }

            z = 1.0f / far - 1.0f / near;
            w = 1.0f / near;
        }

        private static float Saturate(float value)
        {
            if (value < 0.0f)
            {
                return 0.0f;
            }

            return value > 1.0f ? 1.0f : value;
        }

        private static float Min(float a, float b) => a < b ? a : b;

        private static float Max(float a, float b) => a > b ? a : b;

        private static float Abs(float value) => value < 0.0f ? -value : value;

        private static float Pow(float value, float exponent) => (float)Math.Pow(value, exponent);

        private static float Exp(float value) => (float)Math.Exp(value);
    }
}
