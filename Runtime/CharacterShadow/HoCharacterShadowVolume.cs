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

        [InspectorName("单角色分辨率"), Tooltip("每个接收域一张方形深度图的分辨率。可容纳 tile 数 = (图集边长上限/本值)²；容量不足的接收域回退普通天光投影。"
            + "它同时决定软阴影半径覆盖的世界尺寸：分辨率越高，同样 texel 半径覆盖的世界范围越小。")]
        public HoCharacterShadowResolutionParameter resolution = new HoCharacterShadowResolutionParameter(HoCharacterShadowResolution.R2048);

        [InspectorName("启用 PCSS"), Tooltip("blocker search + 按遮挡距离估算的可变半影。关闭后只剩「阴影软边」那一档固定半径滤波。")]
        public BoolParameter pcssEnabled = new BoolParameter(true);

        [InspectorName("阴影软边"), Tooltip("世界单位（米）：始终生效的基础滤波半径，用来盖掉几何锯齿（发丝/低模剪影）；PCSS 在它之上再加半影。0 = 完全硬边。tile 越细同样半径吃掉的 texel 越多，想更软又不出颗粒就降分辨率或提高 PCSS 质量档。")]
        public ClampedFloatParameter softnessRadius = new ClampedFloatParameter(0.005f, 0.0f, 0.2f);

        [InspectorName("PCSS 质量档"), Tooltip("只决定 blocker / filter 的采样数，不改变阴影形状。")]
        public HoCharacterShadowPcssQualityParameter pcssQuality = new HoCharacterShadowPcssQualityParameter(HoCharacterShadowPcssQuality.Ultra);

        [InspectorName("半影放大"), Tooltip("PCSS 估出的半影半径再乘它；0 = 只用「阴影软边」那一档（等价回退 PCF）。")]
        public ClampedFloatParameter pcssSoftness = new ClampedFloatParameter(2.0f, 0.0f, 8.0f);

        [InspectorName("Blocker 搜索半径"), Tooltip("世界单位（米）。它至少要接近半影半径上限，否则半影里的遮挡物会被漏采样、估算值乱跳（表现成斑点）。")]
        public ClampedFloatParameter pcssBlockerSearchRadius = new ClampedFloatParameter(0.02f, 0.001f, 0.2f);

        [InspectorName("半影半径上限"), Tooltip("世界单位（米）：最软能软到什么程度。超出采样预算时会被收窄以免出颗粒（想更软就提高质量档或降低单角色分辨率）。")]
        public ClampedFloatParameter pcssMaxPenumbraRadius = new ClampedFloatParameter(0.04f, 0.001f, 0.2f);

        [InspectorName("Blocker 深度偏移"), Tooltip("判定 blocker 时加在接收深度上的偏移（阴影空间 z）：压自遮挡与深度抖动。留一点默认值，避免 blocker 数量在相邻像素间跳变。")]
        public ClampedFloatParameter pcssDepthBias = new ClampedFloatParameter(0.0005f, 0.0f, 0.02f);

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
