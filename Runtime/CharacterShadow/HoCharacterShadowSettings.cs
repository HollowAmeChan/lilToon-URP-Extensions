using System;
using UnityEngine;

namespace lilToon.URP.Extensions.CharacterShadow
{
    public enum HoCharacterShadowResolution { R512 = 512, R1024 = 1024, R2048 = 2048, R4096 = 4096 }
    public enum HoCharacterShadowDebugMode { Off, Atlas, Character }

    /// <summary>
    /// Ho-CharacterShadow 的兜底默认值与结构设置。逐相机可覆盖的部分（启用 / 单角色分辨率 / 调试）
    /// 在 <see cref="HoCharacterShadowVolume"/>：这里的值只在 Volume 未覆盖时生效。
    /// </summary>
    [Serializable]
    public sealed class HoCharacterShadowSettings
    {
        [Tooltip("Volume 未覆盖时是否启用 CS。")]
        public bool enabled = true;
        [Tooltip("每个接收域的方形深度图分辨率（Volume 未覆盖时生效）。")]
        public HoCharacterShadowResolution resolution = HoCharacterShadowResolution.R2048;
        [Range(1, 16), Tooltip("同时存在的接收域上限（每域一张 tile）。")]
        public int maxCharacters = 16;
        [Range(2048, 16384), Tooltip("不自动降低单角色分辨率。超出图集容量的角色回退普通投影并在组件上说明。")]
        public int maxAtlasSize = 8192;
        [Range(0, 2), Tooltip("PCF 半径（texel）；0 为硬阴影。")]
        public float filterRadius = 1;
        [Min(0), Tooltip("投影深度偏移（单位：texel）。")]
        public float depthBias = 1;
        [Min(0), Tooltip("法线方向偏移（单位：texel）。")]
        public float normalBias = 1;
        [Tooltip("Volume 未覆盖时的调试模式；调试入口在 Ho-CharacterShadow Volume 的「调试」分组。")]
        public HoCharacterShadowDebugMode debugMode;
        [Range(0, 15), Tooltip("Volume 未覆盖时 Character 模式放大的接收域编号。")]
        public int debugCharacter;
        [Tooltip("Volume 未覆盖时是否在 Scene View 显示调试画面。")]
        public bool debugInSceneView = true;
        [Tooltip("Volume 未覆盖时是否在 Game View 显示调试画面。")]
        public bool debugInGameView;
        [Tooltip("调试直出用的 fullscreen shader；留空时用包内的 Hidden/Ho-CharacterShadow/Debug。")]
        public Shader debugShader;
    }
}
