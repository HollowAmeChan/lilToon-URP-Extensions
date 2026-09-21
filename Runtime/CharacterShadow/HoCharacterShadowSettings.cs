using System;
using UnityEngine;

namespace lilToon.URP.Extensions.CharacterShadow
{
    public enum HoCharacterShadowResolution { R512 = 512, R1024 = 1024, R2048 = 2048, R4096 = 4096 }
    public enum HoCharacterShadowDebugMode { Off, Atlas, Character }

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
        [Range(1, 16), Tooltip("同时存在的接收域上限（每域一张 tile）。")]
        public int maxCharacters = 16;
        [Range(2048, 16384), Tooltip("不自动降低单角色分辨率。超出图集容量的角色回退普通投影并在组件上说明。")]
        public int maxAtlasSize = 8192;
        [Range(0, 2), Tooltip("PCF 半径（texel）；0 为硬阴影。PCSS 关闭或退化时走这条 PCF。")]
        public float filterRadius = 1;
        [Min(0), Tooltip("投影深度偏移（单位：texel）。")]
        public float depthBias = 1;
        [Min(0), Tooltip("法线方向偏移（单位：texel）。")]
        public float normalBias = 1;

        [Header("PCSS 软阴影")]
        [Tooltip("启用 PCSS（blocker search + 按遮挡距离估算的可变半影）。关闭时回退上面的 3×3 PCF。")]
        public bool pcssEnabled = true;
        [Tooltip("采样档：只决定 blocker / filter 的采样数，不改变阴影形状。")]
        public HoCharacterShadowPcssQuality pcssQuality = HoCharacterShadowPcssQuality.High;
        [Range(0, 8), Tooltip("半影放大系数：PCSS 估出的半影半径再乘它；0 = 硬边（等价回退 PCF）。")]
        public float pcssSoftness = 2;
        [Range(0.25f, 16), Tooltip("blocker 搜索半径（texel）：越大越能找到更远的遮挡物，也越容易漏光。")]
        public float pcssBlockerSearchRadius = 4;
        [Range(1, 64), Tooltip("半影半径上限（texel）：最软能软到什么程度。")]
        public float pcssMaxPenumbraRadius = 12;
        [Range(0, 0.01f), Tooltip("blocker 判定的深度偏移（阴影空间 z）：压自遮挡与漏光。")]
        public float pcssDepthBias;

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
            filterRadius = Mathf.Clamp(filterRadius, 0.0f, 2.0f);
            depthBias = Mathf.Max(0.0f, depthBias);
            normalBias = Mathf.Max(0.0f, normalBias);
            pcssQuality = HoCharacterShadowShaderContract.ClampQuality((int)pcssQuality);
            pcssSoftness = Mathf.Clamp(pcssSoftness, 0.0f, 8.0f);
            pcssBlockerSearchRadius = Mathf.Clamp(pcssBlockerSearchRadius, 0.25f, 16.0f);
            pcssMaxPenumbraRadius = Mathf.Clamp(pcssMaxPenumbraRadius, 1.0f, 64.0f);
            pcssDepthBias = Mathf.Clamp(pcssDepthBias, 0.0f, 0.01f);
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
        internal float filterRadius;
        internal float depthBias;
        internal float normalBias;
        internal bool pcssEnabled;
        internal HoCharacterShadowPcssQuality pcssQuality;
        internal float pcssSoftness;
        internal float pcssBlockerRadius;
        internal float pcssMaxPenumbraRadius;
        internal float pcssDepthBias;
        internal int pcssBlockerSamples;
        internal int pcssFilterSamples;

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
                filterRadius = settings.filterRadius,
                depthBias = settings.depthBias,
                normalBias = settings.normalBias,
                pcssEnabled = settings.pcssEnabled,
                pcssSoftness = settings.pcssSoftness,
                pcssBlockerRadius = settings.pcssBlockerSearchRadius,
                pcssMaxPenumbraRadius = settings.pcssMaxPenumbraRadius,
                pcssDepthBias = settings.pcssDepthBias,
                pcssQuality = settings.pcssQuality
            };

            if (volume != null)
            {
                if (volume.resolution.overrideState) config.resolution = (int)volume.resolution.value;
                if (volume.pcssEnabled.overrideState) config.pcssEnabled = volume.pcssEnabled.value;
                if (volume.pcssQuality.overrideState) config.pcssQuality = volume.pcssQuality.value;
                if (volume.pcssSoftness.overrideState) config.pcssSoftness = volume.pcssSoftness.value;
                if (volume.pcssBlockerSearchRadius.overrideState) config.pcssBlockerRadius = volume.pcssBlockerSearchRadius.value;
                if (volume.pcssMaxPenumbraRadius.overrideState) config.pcssMaxPenumbraRadius = volume.pcssMaxPenumbraRadius.value;
                if (volume.pcssDepthBias.overrideState) config.pcssDepthBias = volume.pcssDepthBias.value;
            }

            HoCharacterShadowShaderContract.GetPcssSampleCounts(config.pcssQuality, out config.pcssBlockerSamples, out config.pcssFilterSamples);
            return config;
        }
    }
}
