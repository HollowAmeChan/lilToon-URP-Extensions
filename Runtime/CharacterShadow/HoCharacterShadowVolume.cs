using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterShadow
{
    [Serializable]
    public sealed class HoCharacterShadowResolutionParameter : VolumeParameter<HoCharacterShadowResolution>
    {
        public HoCharacterShadowResolutionParameter(HoCharacterShadowResolution value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterShadowResolution from, HoCharacterShadowResolution to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterShadowDebugModeParameter : VolumeParameter<HoCharacterShadowDebugMode>
    {
        public HoCharacterShadowDebugModeParameter(HoCharacterShadowDebugMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterShadowDebugMode from, HoCharacterShadowDebugMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterShadowPcssQualityParameter : VolumeParameter<HoCharacterShadowPcssQuality>
    {
        public HoCharacterShadowPcssQualityParameter(HoCharacterShadowPcssQuality value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterShadowPcssQuality from, HoCharacterShadowPcssQuality to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    /// <summary>
    /// Per-camera overrides for Ho-CharacterShadow: enable, per-receiver resolution and the debug view.
    /// The RendererFeature keeps the fallback values (used when a field here is not overridden) and the
    /// structural settings (timing, shader); receiver declarations stay on the HoCharacterShadow component.
    /// </summary>
    [Serializable]
    [VolumeComponentMenu("Post-processing/Ho-CharacterShadow/逐物体阴影")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class HoCharacterShadowVolume : VolumeComponent, IPostProcessComponent
    {
        [InspectorName("启用"), Tooltip("该相机是否使用局部高精度天光投影。未勾选覆盖时用 Ho-CharacterShadow RendererFeature 的兜底值。")]
        public BoolParameter enable = new BoolParameter(true);

        [InspectorName("单角色分辨率"), Tooltip("每个接收域一张方形深度图的分辨率。图集容量按它换算；容量不足的接收域回退普通天光投影。")]
        public HoCharacterShadowResolutionParameter resolution = new HoCharacterShadowResolutionParameter(HoCharacterShadowResolution.R2048);

        [InspectorName("启用 PCSS"), Tooltip("blocker search + 按遮挡距离估算的可变半影。关闭时回退固定半径的 3×3 PCF。")]
        public BoolParameter pcssEnabled = new BoolParameter(true);

        [InspectorName("PCSS 质量档"), Tooltip("只决定 blocker / filter 的采样数，不改变阴影形状。")]
        public HoCharacterShadowPcssQualityParameter pcssQuality = new HoCharacterShadowPcssQualityParameter(HoCharacterShadowPcssQuality.High);

        [InspectorName("半影放大"), Tooltip("PCSS 估出的半影半径再乘它；0 = 硬边（等价回退 PCF）。")]
        public ClampedFloatParameter pcssSoftness = new ClampedFloatParameter(2.0f, 0.0f, 8.0f);

        [InspectorName("Blocker 搜索半径"), Tooltip("单位 texel。越大越能找到更远的遮挡物，也越容易漏光。")]
        public ClampedFloatParameter pcssBlockerSearchRadius = new ClampedFloatParameter(4.0f, 0.25f, 16.0f);

        [InspectorName("半影半径上限"), Tooltip("单位 texel。最软能软到什么程度。")]
        public ClampedFloatParameter pcssMaxPenumbraRadius = new ClampedFloatParameter(12.0f, 1.0f, 64.0f);

        [InspectorName("Blocker 深度偏移"), Tooltip("判定 blocker 时加在接收深度上的偏移（阴影空间 z）：压自遮挡与漏光。")]
        public ClampedFloatParameter pcssDepthBias = new ClampedFloatParameter(0.0f, 0.0f, 0.01f);

        [InspectorName("调试模式"), Tooltip("直出替换画面，不改材质输出。Off 无输出；Atlas 看整张图集；Character 按编号放大单个接收域的 tile。")]
        public HoCharacterShadowDebugModeParameter debugMode = new HoCharacterShadowDebugModeParameter(HoCharacterShadowDebugMode.Off);

        [InspectorName("Debug In Scene View"), Tooltip("是否在 Scene View 中显示调试画面。")]
        public BoolParameter debugInSceneView = new BoolParameter(true);

        [InspectorName("Debug In Game View"), Tooltip("是否在 Game View 中显示调试画面。会替换最终画面，默认关闭。")]
        public BoolParameter debugInGameView = new BoolParameter(false);

        [InspectorName("单角色 tile"), Tooltip("Character 模式下要放大的接收域编号，编号见 Ho-CharacterShadow 组件 Inspector 的「图集 Tile」。")]
        public ClampedIntParameter debugCharacter = new ClampedIntParameter(0, 0, 15);

        public bool IsActive()
        {
            return enable.value;
        }

        public bool IsTileCompatible()
        {
            return false;
        }

        /// <summary>
        /// The volume component of the camera currently being rendered, or null when no volume stack
        /// exists. Fields whose overrideState is false must fall back to the RendererFeature settings.
        /// </summary>
        public static HoCharacterShadowVolume Resolve()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            return stack != null ? stack.GetComponent<HoCharacterShadowVolume>() : null;
        }
    }
}
