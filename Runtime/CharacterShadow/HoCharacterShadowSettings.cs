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
        [Range(0, 0.2f), Tooltip("**最低软度**（世界单位，米）：软阴影的滤波半径永远不会比它更小，用来盖掉几何锯齿"
            + "（发丝/低模剪影）。0 = 允许硬边。注意 tile 越细，同样世界半径吃掉的 texel 越多、越吃采样。")]
        public float softnessRadius = 0.005f;
        [Min(0), Tooltip("投影深度偏移（单位：texel）。")]
        public float depthBias = 1;
        [Min(0), Tooltip("法线方向偏移（单位：texel）。")]
        public float normalBias = 1;

        [Header("PCSS 软阴影")]
        [Tooltip("启用 PCSS（blocker search + 按遮挡距离估算的可变半影）。关闭时回退上面的 3×3 PCF。")]
        public bool pcssEnabled = true;
        [Tooltip("采样档：只决定 blocker / filter 的采样数，不改变阴影形状。")]
        public HoCharacterShadowPcssQuality pcssQuality = HoCharacterShadowPcssQuality.Ultra;
        [Range(0, 8), Tooltip("半影放大系数：PCSS 估出的半影半径再乘它；0 = 只用最低软度（等价回退 PCF）。")]
        public float pcssSoftness = 2;
        [Range(0.001f, 0.2f), Tooltip("blocker 搜索半径（**世界单位，米**）。它至少要接近半影半径上限，否则半影里的遮挡物会被漏采样、"
            + "估算值乱跳（表现成斑点）。")]
        public float pcssBlockerSearchRadius = 0.02f;
        [Range(0.001f, 0.2f), Tooltip("半影半径上限（**世界单位，米**）：最软能软到什么程度。用世界单位是为了跟 tile 分辨率解耦"
            + "（texel 当单位的话，4096 的 tile 上同一个数值只有 7mm，看着还是硬边）。")]
        public float pcssMaxPenumbraRadius = 0.04f;
        [Range(0, 0.02f), Tooltip("blocker 判定的深度偏移（阴影空间 z，1 ≈ 盒子整个深度范围）：压自遮挡与深度抖动。"
            + "默认给一点，避免 blocker 数量在相邻像素之间跳变（那会变成斑点）。")]
        public float pcssDepthBias = 0.0005f;

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
            pcssQuality = HoCharacterShadowShaderContract.ClampQuality((int)pcssQuality);
            pcssSoftness = Mathf.Clamp(pcssSoftness, 0.0f, 8.0f);
            pcssBlockerSearchRadius = Mathf.Clamp(pcssBlockerSearchRadius, 0.001f, 0.2f);
            pcssMaxPenumbraRadius = Mathf.Clamp(pcssMaxPenumbraRadius, 0.001f, 0.2f);
            pcssDepthBias = Mathf.Clamp(pcssDepthBias, 0.0f, 0.02f);
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
        internal bool pcssEnabled;
        internal HoCharacterShadowPcssQuality pcssQuality;
        internal float pcssSoftness;
        internal float pcssBlockerRadiusWorld;
        internal float pcssMaxPenumbraRadiusWorld;
        internal float pcssDepthBias;
        internal int pcssBlockerSamples;
        internal int pcssFilterSamples;

        /// <summary>被 Volume 覆盖掉的 PCSS 字段个数：&gt;0 说明"改 feature 上的值没用"，UI 靠它提示。</summary>
        internal int pcssVolumeOverrides;

        /// <summary>这一帧实际生效的 PCSS 状态（发布在 LastCullStatus 里，用来回答"为什么关不掉"）。</summary>
        internal string pcssStatus;

        /// <summary>「PCF 半径」这个 texel 单位的旧旋钮还留一个 texel 值：只给投影拟合留边界余量用。</summary>
        internal float pcssFilterRadiusTexels = 1.0f;

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
                normalBias = settings.normalBias,
                pcssEnabled = settings.pcssEnabled,
                pcssSoftness = settings.pcssSoftness,
                pcssBlockerRadiusWorld = settings.pcssBlockerSearchRadius,
                pcssMaxPenumbraRadiusWorld = settings.pcssMaxPenumbraRadius,
                pcssDepthBias = settings.pcssDepthBias,
                pcssQuality = settings.pcssQuality
            };

            if (volume != null)
            {
                if (volume.resolution.overrideState) config.resolution = (int)volume.resolution.value;
                if (volume.softnessRadius.overrideState) { config.softnessRadiusWorld = volume.softnessRadius.value; config.pcssVolumeOverrides++; }
                if (volume.pcssEnabled.overrideState) { config.pcssEnabled = volume.pcssEnabled.value; config.pcssVolumeOverrides++; }
                if (volume.pcssQuality.overrideState) { config.pcssQuality = volume.pcssQuality.value; config.pcssVolumeOverrides++; }
                if (volume.pcssSoftness.overrideState) { config.pcssSoftness = volume.pcssSoftness.value; config.pcssVolumeOverrides++; }
                if (volume.pcssBlockerSearchRadius.overrideState) { config.pcssBlockerRadiusWorld = volume.pcssBlockerSearchRadius.value; config.pcssVolumeOverrides++; }
                if (volume.pcssMaxPenumbraRadius.overrideState) { config.pcssMaxPenumbraRadiusWorld = volume.pcssMaxPenumbraRadius.value; config.pcssVolumeOverrides++; }
                if (volume.pcssDepthBias.overrideState) { config.pcssDepthBias = volume.pcssDepthBias.value; config.pcssVolumeOverrides++; }
            }

            HoCharacterShadowShaderContract.GetPcssSampleCounts(config.pcssQuality, out config.pcssBlockerSamples, out config.pcssFilterSamples);
            config.pcssStatus =
                (config.pcssEnabled ? "on" : "off")
                + ",min=" + config.softnessRadiusWorld.ToString("0.###") + "m"
                + ",max=" + config.pcssMaxPenumbraRadiusWorld.ToString("0.###") + "m"
                + ",soft=" + config.pcssSoftness.ToString("0.##")
                + "," + config.pcssQuality
                + ",vol=" + config.pcssVolumeOverrides;
            return config;
        }
    }
}
