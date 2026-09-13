using UnityEngine;

namespace lilToon.URP.Extensions.ShadowCast
{
    /// <summary>
    /// Punctual ShadowCast capacity tier. The selected tier drives both the collected light/slice
    /// limits and the shader keyword that sizes the material side arrays.
    /// </summary>
    public enum HoShadowCastLightCapacity
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    /// <summary>
    /// Punctual light/slice limits of one <see cref="HoShadowCastLightCapacity"/> tier.
    /// </summary>
    public readonly struct HoShadowCastCapacityLimits
    {
        public HoShadowCastCapacityLimits(int lightCount, int sliceCount)
        {
            LightCount = lightCount;
            SliceCount = sliceCount;
        }

        public readonly int LightCount;
        public readonly int SliceCount;
    }

    /// <summary>
    /// C# side of the ShadowCast shader contract. Every value mirrors a constant in
    /// <c>Runtime/ShadowCast/Shaders/HoShadowCastShaderContract.hlsl</c>; the editor validator
    /// (<c>Editor/ShadowCast/HoShadowCastShaderContractValidator.cs</c>) parses that file and
    /// reports drift.
    /// </summary>
    public static class HoShadowCastShaderContract
    {
        // Punctual light capacity tiers: how many additional lights may be sampled at once. The tier is
        // the per-pixel cost knob and bounds the shader sampling loops.
        public const int LowLights = 12;
        public const int MediumLights = 24;
        public const int HighLights = 48;

        /// <summary>
        /// Global array length for the punctual light arrays, and the frame buffer size the collector
        /// writes into.
        /// </summary>
        /// <remarks>
        /// Fixed on purpose: Unity caches the length of a global array slot for the whole editor session
        /// and only allows smaller uploads afterwards ("Property (_HoShadowCastLightData0) exceeds
        /// previous array size (48 vs 12). Cap to previous size. Restart Unity to recreate the arrays.").
        /// The publisher therefore always uploads exactly these lengths and the capacity tier only bounds
        /// the collection and the shader sampling loops, which is what makes switching tiers at runtime
        /// safe. Do not make this value depend on the selected tier.
        /// Increasing these values requires one Unity editor restart (already allocated slots cannot
        /// grow); lowering them does not.
        /// </remarks>
        public const int ArrayLights = HighLights;

        /// <summary>
        /// Global array length for the punctual slice arrays. See <see cref="ArrayLights"/>.
        /// </summary>
        /// <remarks>
        /// The slice count is deliberately not tiered: the atlas geometry decides how many slices fit
        /// (see <see cref="GetSliceCapacity"/>) and this value is only the hard ceiling the fixed arrays
        /// can address.
        /// </remarks>
        public const int ArraySlices = 128;

        /// <summary>Second directional light atlas: fixed capacity, not part of the tier selection.</summary>
        public const int SecondDirectionalLights = 4;

        public const int SecondDirectionalCascades = 4;
        public const int SecondDirectionalSlices = SecondDirectionalLights * SecondDirectionalCascades;

        /// <summary>One shadow slice per point light cubemap face.</summary>
        public const int PointLightSlices = 6;

        /// <summary>PCSS sample ceilings. <see cref="GetPcssSampleCounts"/> must stay within them.</summary>
        public const int PcssBlockerSamples = 32;

        public const int PcssFilterSamples = 64;

        /// <summary>Light type ids published in <c>_HoShadowCastLightData0.x</c>.</summary>
        public const float LightTypeIdDirectional = 0.0f;

        public const float LightTypeIdSpot = 1.0f;
        public const float LightTypeIdPoint = 2.0f;

        public const string MediumKeywordName = "HO_SHADOW_CAST_CAPACITY_MEDIUM";
        public const string HighKeywordName = "HO_SHADOW_CAST_CAPACITY_HIGH";

        public static HoShadowCastLightCapacity ClampCapacity(int value)
        {
            return (HoShadowCastLightCapacity)Mathf.Clamp(
                value,
                (int)HoShadowCastLightCapacity.Low,
                (int)HoShadowCastLightCapacity.High);
        }

        /// <summary>
        /// Runtime capacity of a tier. <see cref="HoShadowCastCapacityLimits.LightCount"/> is the tier cap
        /// for simultaneously sampled lights; <see cref="HoShadowCastCapacityLimits.SliceCount"/> is the
        /// hard array ceiling for slices, which the atlas geometry fills up (see
        /// <see cref="GetSliceCapacity"/>).
        /// </summary>
        public static HoShadowCastCapacityLimits GetLimits(HoShadowCastLightCapacity capacity)
        {
            switch (capacity)
            {
                case HoShadowCastLightCapacity.High:
                    return new HoShadowCastCapacityLimits(HighLights, ArraySlices);
                case HoShadowCastLightCapacity.Medium:
                    return new HoShadowCastCapacityLimits(MediumLights, ArraySlices);
                default:
                    return new HoShadowCastCapacityLimits(LowLights, ArraySlices);
            }
        }

        /// <summary>
        /// How many slices of the given resolution fit into a square atlas of <paramref name="atlasSize"/>.
        /// Slices are packed as squares, so the exact tile count is
        /// <c>floor(atlasSize / resolution)^2</c>, clamped to the <see cref="ArraySlices"/> ceiling.
        /// </summary>
        /// <remarks>
        /// This reports the capacity for a scene of one light type; mixed spot/point scenes are packed by
        /// <c>HoShadowCastAtlasPacker</c>, which stays the authority at runtime. Editor readouts use this
        /// so the shown capacity follows the configured atlas size and slice resolutions.
        /// </remarks>
        public static int GetSliceCapacity(int atlasSize, int resolution)
        {
            atlasSize = Mathf.Max(1, atlasSize);
            resolution = Mathf.Clamp(resolution, 1, atlasSize);
            int slicesPerRow = atlasSize / resolution;
            return Mathf.Clamp(slicesPerRow * slicesPerRow, 1, ArraySlices);
        }

        /// <summary>
        /// Concurrent point lights the atlas can hold at the given face resolution, bounded by the tier
        /// light cap (each point light needs <see cref="PointLightSlices"/> slices).
        /// </summary>
        public static int GetPointLightCapacity(HoShadowCastLightCapacity capacity, int atlasSize, int pointFaceResolution)
        {
            int slices = GetSliceCapacity(atlasSize, pointFaceResolution);
            return Mathf.Clamp(slices / PointLightSlices, 0, GetLimits(capacity).LightCount);
        }

        /// <summary>
        /// Tier keyword enabled globally by the feature. Low is the untouched state and has no keyword.
        /// </summary>
        public static string GetKeywordName(HoShadowCastLightCapacity capacity)
        {
            switch (capacity)
            {
                case HoShadowCastLightCapacity.High:
                    return HighKeywordName;
                case HoShadowCastLightCapacity.Medium:
                    return MediumKeywordName;
                default:
                    return string.Empty;
            }
        }

        public static void GetPcssSampleCounts(HoShadowCastPcssQuality quality, out int blockerSamples, out int filterSamples)
        {
            switch (quality)
            {
                case HoShadowCastPcssQuality.Low:
                    blockerSamples = 8;
                    filterSamples = 16;
                    break;
                case HoShadowCastPcssQuality.High:
                    blockerSamples = 24;
                    filterSamples = 48;
                    break;
                case HoShadowCastPcssQuality.Ultra:
                    blockerSamples = 32;
                    filterSamples = 64;
                    break;
                default:
                    blockerSamples = 16;
                    filterSamples = 32;
                    break;
            }
        }
    }
}
