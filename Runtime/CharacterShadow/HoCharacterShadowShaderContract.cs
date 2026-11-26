using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.CharacterShadow
{
    /// <summary>PCSS 采样档。只决定 blocker / filter 的采样数，不改变阴影形状。</summary>
    public enum HoCharacterShadowPcssQuality
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Ultra = 3
    }

    /// <summary>
    /// CS 里 C# 与 HLSL 共享的数值契约。
    /// <para>
    /// **采样上限必须与 `ShaderLibrary/Shadow/Receiver/HoCharacterShadowSampling.hlsl` 里的
    /// `HO_CS_MAX_PCSS_BLOCKER_SAMPLES` / `HO_CS_MAX_PCSS_FILTER_SAMPLES` 一致** —— HLSL 侧用它们定
    /// 循环上界，C# 侧用它们夹住档位请求。编译器无法把两边关联起来，所以
    /// `HoCharacterShadowValidation.Validate()` 会解析那份 HLSL 逐条比对（batch 里也跑，防漂移）。
    /// </para>
    /// </summary>
    public static class HoCharacterShadowShaderContract
    {
        public const int PcssBlockerSamples = 32;
        public const int PcssFilterSamples = 64;
        private static readonly int SampleOffsetsId = Shader.PropertyToID("_HoCSSampleOffsets");
        private static readonly int SampleOffsetCountsId = Shader.PropertyToID("_HoCSSampleOffsetCounts");
        private static readonly Dictionary<int, Vector4[]> SamplingPatterns = new Dictionary<int, Vector4[]>();

        // xy = fixed/PCSS filter; zw = blocker-search offsets (its explicit first tap is separate).
        // This is the same antipodal pattern as the shader, evaluated once per budget instead
        // of doing sqrt/sin/cos for every tap of every receiving pixel, even with PCSS disabled.
        private static Vector4[] GetSamplingPattern(int blockerSamples, int filterSamples)
        {
            int key = (blockerSamples << 8) | filterSamples;
            if (SamplingPatterns.TryGetValue(key, out Vector4[] pattern)) return pattern;
            pattern = new Vector4[PcssFilterSamples];
            for (int i = 0; i < pattern.Length; i++)
            {
                Vector2 filter = i < filterSamples ? CalculateSampleOffset(i, filterSamples) : Vector2.zero;
                Vector2 blocker = i < blockerSamples - 1 ? CalculateSampleOffset(i, blockerSamples - 1) : Vector2.zero;
                pattern[i] = new Vector4(filter.x, filter.y, blocker.x, blocker.y);
            }
            SamplingPatterns.Add(key, pattern);
            return pattern;
        }

        private static Vector2 CalculateSampleOffset(int index, int sampleCount)
        {
            if ((sampleCount & 1) != 0)
            {
                if (index == 0) return Vector2.zero;
                index--; sampleCount--;
            }
            float pair = Mathf.Floor(index * 0.5f);
            float radius = Mathf.Sqrt((pair + 0.5f) / Mathf.Max(Mathf.Ceil(sampleCount * 0.5f), 1));
            float angle = pair * 2.39996323f;
            float direction = (index & 1) == 0 ? 1 : -1;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius * direction);
        }

        public static void BindSamplingPattern(RasterCommandBuffer cmd, Vector4 parameters)
        {
            int blockers = Mathf.Clamp(Mathf.RoundToInt(parameters.y), 1, PcssBlockerSamples);
            int filters = Mathf.Clamp(Mathf.RoundToInt(parameters.z), 1, PcssFilterSamples);
            cmd.SetGlobalVectorArray(SampleOffsetsId, GetSamplingPattern(blockers, filters));
            cmd.SetGlobalVector(SampleOffsetCountsId, new Vector4(filters, blockers - 1, 0, 0));
        }

        // Direct-graphics validation and tools can bind the exact same pattern without a render pass.
        public static void BindSamplingPattern(Vector4 parameters)
        {
            int blockers = Mathf.Clamp(Mathf.RoundToInt(parameters.y), 1, PcssBlockerSamples);
            int filters = Mathf.Clamp(Mathf.RoundToInt(parameters.z), 1, PcssFilterSamples);
            Shader.SetGlobalVectorArray(SampleOffsetsId, GetSamplingPattern(blockers, filters));
            Shader.SetGlobalVector(SampleOffsetCountsId, new Vector4(filters, blockers - 1, 0, 0));
        }

        public static HoCharacterShadowPcssQuality ClampQuality(int value)
        {
            return (HoCharacterShadowPcssQuality)Mathf.Clamp(value, 0, 3);
        }

        /// <summary>档位 → (blocker, filter) 采样数；结果保证不超过上面两个上限。</summary>
        public static void GetPcssSampleCounts(HoCharacterShadowPcssQuality quality, out int blockerSamples, out int filterSamples)
        {
            switch (quality)
            {
                case HoCharacterShadowPcssQuality.Low:
                    blockerSamples = 8;
                    filterSamples = 16;
                    break;
                case HoCharacterShadowPcssQuality.Medium:
                    blockerSamples = 16;
                    filterSamples = 32;
                    break;
                case HoCharacterShadowPcssQuality.Ultra:
                    blockerSamples = 32;
                    filterSamples = 64;
                    break;
                default:
                    blockerSamples = 24;
                    filterSamples = 48;
                    break;
            }

            blockerSamples = Mathf.Min(blockerSamples, PcssBlockerSamples);
            filterSamples = Mathf.Min(filterSamples, PcssFilterSamples);
        }
    }
}
