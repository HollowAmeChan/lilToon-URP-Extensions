using System;
using UnityEngine;

namespace lilToon.URP.Extensions.CharacterShadow
{
    public enum HoCharacterShadowResolution { R512 = 512, R1024 = 1024, R2048 = 2048, R4096 = 4096 }
    public enum HoCharacterShadowDebugMode { Off, Atlas, Character, [InspectorName("All")] All = 255 }

    /// <summary>
    /// Ho-CharacterShadow 的兜底默认值与结构设置。逐相机可覆盖的部分（启用 / 单角色分辨率 / 软阴影 / 调试）
    /// 在 <see cref="HoCharacterShadowVolume"/>：这里的值只在 Volume 未覆盖时生效。
    /// </summary>
    [Serializable]
    public sealed class HoCharacterShadowSettings
    {
        [Tooltip("Volume 未覆盖时是否启用 CS。")]
        public bool enabled = true;
        [Tooltip("每个接收域的方形深度图分辨率（Volume 未覆盖时生效）。")]
        public HoCharacterShadowResolution resolution = HoCharacterShadowResolution.R2048;
        [Range(1, 16), Tooltip("同时存在的接收域上限（每域一张 tile）。容量不足时不降低分辨率，多出来的接收域回退普通天光投影。")]
        public int maxCharacters = 16;
        [Range(2048, 16384), Tooltip("图集边长上限。可容纳 tile 数 = (本值 / 单角色分辨率)²，再与「同时接收域上限」取小。")]
        public int maxAtlasSize = 8192;
        [Range(0, 0.2f), Tooltip("独立角色阴影的固定 PCF 请求半径（米）。按当前 tile 换算成 1 到 2 个 texel。")]
        public float softnessRadius = 0.005f;
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

        /// <summary>把字段夹到各自的有效区间；<see cref="HoCharacterShadowRenderConfig.Resolve"/> 每次都会调。</summary>
        public void Validate()
        {
            maxCharacters = Mathf.Clamp(maxCharacters, 1, 16);
            maxAtlasSize = Mathf.Clamp(maxAtlasSize, 2048, 16384);
            softnessRadius = Mathf.Clamp(softnessRadius, 0.0f, 0.2f);
            depthBias = Mathf.Max(0.0f, depthBias);
            normalBias = Mathf.Max(0.0f, normalBias);
        }
    }

    /// <summary>
    /// 每相机解析一次的渲染配置：feature 兜底值 + Volume 覆盖。
    /// <see cref="HoCharacterShadowFrame.Build"/> 只读它，不再直接读 settings，避免"哪些值是覆盖过的"散在两处。
    /// </summary>
    internal sealed class HoCharacterShadowRenderConfig
    {
        internal int resolution;
        internal int maxCharacters;
        internal int maxAtlasSize;
        internal float softnessRadiusWorld;
        internal float depthBias;
        internal float normalBias;
        internal float filterPaddingTexels = 1.0f;

        internal static HoCharacterShadowRenderConfig Resolve(HoCharacterShadowSettings settings, HoCharacterShadowVolume volume)
        {
            if (settings == null)
            {
                settings = new HoCharacterShadowSettings();
            }

            settings.Validate();
            var config = new HoCharacterShadowRenderConfig
            {
                resolution = (int)settings.resolution,
                maxCharacters = settings.maxCharacters,
                maxAtlasSize = settings.maxAtlasSize,
                softnessRadiusWorld = settings.softnessRadius,
                depthBias = settings.depthBias,
                normalBias = settings.normalBias
            };

            if (volume != null)
            {
                if (volume.resolution.overrideState) config.resolution = (int)volume.resolution.value;
                if (volume.softnessRadius.overrideState) config.softnessRadiusWorld = volume.softnessRadius.value;
            }

            return config;
        }
    }
}
