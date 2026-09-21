using System;
using UnityEngine;

namespace lilToon.URP.Extensions.CharacterShadow
{
    public enum HoCharacterShadowResolution { R512 = 512, R1024 = 1024, R2048 = 2048, R4096 = 4096 }
    public enum HoCharacterShadowDebugMode { Off, Atlas, Character }

    [Serializable]
    public sealed class HoCharacterShadowSettings
    {
        public bool enabled = true;
        public HoCharacterShadowResolution resolution = HoCharacterShadowResolution.R2048;
        [Range(1, 16)] public int maxCharacters = 16;
        [Range(2048, 16384), Tooltip("不自动降低单角色分辨率。超出图集容量的角色回退普通投影并在组件上说明。")]
        public int maxAtlasSize = 8192;
        [Range(0, 2), Tooltip("PCF 半径（texel）；0 为硬阴影。")]
        public float filterRadius = 1;
        [Min(0)] public float depthBias = 1;
        [Min(0)] public float normalBias = 1;
        public HoCharacterShadowDebugMode debugMode;
        [Range(0, 15)] public int debugCharacter;
        public Shader debugShader;
    }
}
