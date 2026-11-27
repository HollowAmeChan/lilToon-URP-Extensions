using System;
using lilToon.URP.Extensions.CharacterShadow;
using UnityEngine;

namespace lilToon.URP.Extensions.Shadows
{
    [Serializable]
    public sealed class HoShadowsCharacterSettings
    {
        public HoCharacterShadowResolution resolution = HoCharacterShadowResolution.R2048;
        [Range(1, 16)] public int maxCharacters = 16;
        [Range(2048, 16384)] public int maxAtlasSize = 8192;
        [Min(0)] public float depthBias = 1;
        [Min(0)] public float normalBias = 1;

        internal void SetAtlasSettings(HoCharacterShadowSettings target)
        {
            target.resolution = resolution;
            target.maxCharacters = maxCharacters;
            target.maxAtlasSize = maxAtlasSize;
            target.depthBias = depthBias;
            target.normalBias = normalBias;
            target.pcssEnabled = false;
        }
    }
}
