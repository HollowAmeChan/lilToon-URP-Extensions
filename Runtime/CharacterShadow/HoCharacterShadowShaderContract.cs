using UnityEngine;

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
    /// **采样上限必须与 `Runtime/CharacterShadow/Shaders/HoCharacterShadowSampling.hlsl` 里的
    /// `HO_CS_MAX_PCSS_BLOCKER_SAMPLES` / `HO_CS_MAX_PCSS_FILTER_SAMPLES` 一致** —— HLSL 侧用它们定
    /// 循环上界，C# 侧用它们夹住档位请求。编译器无法把两边关联起来，所以
    /// `HoCharacterShadowValidation.Validate()` 会解析那份 HLSL 逐条比对（batch 里也跑，防漂移）。
    /// </para>
    /// </summary>
    public static class HoCharacterShadowShaderContract
    {
        public const int PcssBlockerSamples = 16;
        public const int PcssFilterSamples = 32;

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
                    blockerSamples = 4;
                    filterSamples = 8;
                    break;
                case HoCharacterShadowPcssQuality.Medium:
                    blockerSamples = 8;
                    filterSamples = 16;
                    break;
                case HoCharacterShadowPcssQuality.Ultra:
                    blockerSamples = 16;
                    filterSamples = 32;
                    break;
                default:
                    blockerSamples = 12;
                    filterSamples = 24;
                    break;
            }

            blockerSamples = Mathf.Min(blockerSamples, PcssBlockerSamples);
            filterSamples = Mathf.Min(filterSamples, PcssFilterSamples);
        }
    }
}
