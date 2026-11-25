using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    [Serializable]
    public struct HoCharacterRimConfiguration
    {
        public bool enabled;
        [Min(1)] public int maxLightsPerCharacter;
        [Min(0)] public int shadowBudget;
        public int atlasSize;
        [Min(0)] public float depthBias;
        [Min(0)] public float normalBias;

        public static HoCharacterRimConfiguration Default => new HoCharacterRimConfiguration
        {
            enabled = false, maxLightsPerCharacter = 8, shadowBudget = 16,
            atlasSize = 2048, depthBias = 1, normalBias = 1
        };

        internal HoCharacterRimConfiguration Sanitized()
        {
            var value = this;
            // A safety budget, not a two-source material interface. Buffer storage grows to the selected population.
            value.maxLightsPerCharacter = Mathf.Clamp(value.maxLightsPerCharacter, 1, 256);
            value.shadowBudget = Mathf.Clamp(value.shadowBudget, 0, 256);
            int atlasLimit = Mathf.Max(128, Mathf.Min(16384, SystemInfo.maxTextureSize));
            value.atlasSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Clamp(value.atlasSize, 128, atlasLimit)), 128, atlasLimit);
            value.depthBias = HoCharacterRimLight.Finite(value.depthBias) ? Mathf.Max(0, value.depthBias) : 1;
            value.normalBias = HoCharacterRimLight.Finite(value.normalBias) ? Mathf.Max(0, value.normalBias) : 1;
            return value;
        }
    }

    // Mirrored by HoRimLightData in HoCharacterRimSampling.hlsl. All weights are applied once.
    [StructLayout(LayoutKind.Sequential)]
    internal struct HoCharacterRimGpuLight
    {
        internal Vector4 directionWidth; // world direction toward source, width
        internal Vector4 colorSoftness; // linear HDR RGB * source intensity, edge softness
        internal Vector4 region; // existing AC typed query (kind/value/domain/expected ID)
        internal Vector4 shadow; // x valid shadow, y strength, z normal offset (world), w comparison depth bias
        internal Vector4 tile; // atlas UV min, UV extent
        internal Matrix4x4 worldToShadow;
        internal const int Stride = 144;
    }
}
