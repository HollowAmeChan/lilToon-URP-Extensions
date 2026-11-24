using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SubsurfaceScattering
{
    [Serializable]
    public sealed class HoSubsurfaceScatteringRenderScaleParameter : VolumeParameter<HoSubsurfaceScatteringRenderScale>
    {
        public HoSubsurfaceScatteringRenderScaleParameter(HoSubsurfaceScatteringRenderScale value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class HoSubsurfaceScatteringQualityParameter : VolumeParameter<HoSubsurfaceScatteringQuality>
    {
        public HoSubsurfaceScatteringQualityParameter(HoSubsurfaceScatteringQuality value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class HoSubsurfaceScatteringTransmissionBlendModeParameter : VolumeParameter<HoSubsurfaceScatteringTransmissionBlendMode>
    {
        public HoSubsurfaceScatteringTransmissionBlendModeParameter(HoSubsurfaceScatteringTransmissionBlendMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class HoSubsurfaceScatteringDebugModeParameter : VolumeParameter<HoSubsurfaceScatteringDebugMode>
    {
        public HoSubsurfaceScatteringDebugModeParameter(HoSubsurfaceScatteringDebugMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    // Profile IDs and table slots are discrete. Select the whole table instead of interpolating IDs.
    [Serializable]
    public sealed class HoSubsurfaceScatteringProfilesParameter : VolumeParameter<HoSubsurfaceScatteringProfileSettings[]>
    {
        public HoSubsurfaceScatteringProfilesParameter(HoSubsurfaceScatteringProfileSettings[] value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    [VolumeComponentMenu(HoVolumeNames.SSS)]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#if UNITY_2023_3_OR_NEWER
    [VolumeRequiresRendererFeatures(typeof(HoSubsurfaceScatteringRendererFeature))]
#endif
#if UNITY_6000_3_OR_NEWER
    [DisplayInfo(name = HoVolumeNames.SSS)]
#endif
    public sealed class HoSubsurfaceScatteringVolume : VolumeComponent, IPostProcessComponent
    {
        public HoSubsurfaceScatteringVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = HoVolumeNames.SSS;
#endif
        }

        [InspectorName("启用"), Tooltip("覆盖当前相机的 SSS 总开关。未勾选覆盖的参数沿用 RendererFeature 设置。Renderer 需包含 SSS 及其 AC/OB/SB/GeometryBuffer 输入。")]
        public BoolParameter enable = new BoolParameter(true);

        [InspectorName("场景视图"), Tooltip("是否在 Scene View 运行整个 SSS 效果，不仅是调试输出。")]
        public BoolParameter renderInSceneView = new BoolParameter(true);

        [InspectorName("渲染缩放"), Tooltip("控制扩散纹理的分辨率；较低分辨率减少带宽，但会降低边缘精度。")]
        public HoSubsurfaceScatteringRenderScaleParameter renderScale = new HoSubsurfaceScatteringRenderScaleParameter(HoSubsurfaceScatteringRenderScale.Half);

        [InspectorName("质量"), Tooltip("Burley profile disk gather 的采样数：低 8、标准 16、高 24。")]
        public HoSubsurfaceScatteringQualityParameter quality = new HoSubsurfaceScatteringQualityParameter(HoSubsurfaceScatteringQuality.Medium);

        [InspectorName("强度"), Tooltip("全局 SSS 合成强度，材质厚度仍参与逐像素权重。")]
        public ClampedFloatParameter strength = new ClampedFloatParameter(0.45f, 0.0f, 2.0f);

        [InspectorName("材质配置"), Tooltip("按 SurfaceBuffer 的 SSS Profile ID 选择材质参数。最多使用前 8 项，默认 1 号为皮肤。整张表统一覆盖，不插值配置 ID；未命中时使用下方回退参数。")]
        public HoSubsurfaceScatteringProfilesParameter profiles = new HoSubsurfaceScatteringProfilesParameter(HoSubsurfaceScatteringSettings.CreateDefaultProfiles());

        [InspectorName("深度容差"), Tooltip("双边滤波允许的深度差；较低值减少跨越遮挡边界的扩散。")]
        public ClampedFloatParameter depthTolerance = new ClampedFloatParameter(0.08f, 0.0001f, 2.0f);

        [InspectorName("法线容差"), Tooltip("双边滤波允许的法线差；较高值更容易跨过法线变化。")]
        public ClampedFloatParameter normalTolerance = new ClampedFloatParameter(0.35f, 0.01f, 1.0f);

        [InspectorName("扩散半径"), Tooltip("Profile 未命中时使用的屏幕空间扩散半径，单位为像素。")]
        public ClampedFloatParameter radius = new ClampedFloatParameter(8.0f, 0.0f, 24.0f);

        [InspectorName("扩散颜色"), Tooltip("Profile 未命中时使用的全局散射颜色。")]
        public ColorParameter color = new ColorParameter(new Color(1.0f, 0.43f, 0.32f, 1.0f));

        [InspectorName("保留源色"), Tooltip("Profile 未命中时保留原始漫反射输入的比例。")]
        public ClampedFloatParameter sourcePreserve = new ClampedFloatParameter(0.25f, 0.0f, 1.0f);

        [InspectorName("透射强度"), Tooltip("Profile 未命中时使用的方向性透射强度。")]
        public ClampedFloatParameter transmissionStrength = new ClampedFloatParameter(0.18f, 0.0f, 1.0f);

        [InspectorName("透射半径"), Tooltip("Profile 未命中时使用的透射半径，单位为屏幕像素。")]
        public ClampedFloatParameter transmissionRadius = new ClampedFloatParameter(5.0f, 0.0f, 24.0f);

        [InspectorName("透射颜色"), Tooltip("Profile 未命中时使用的透射着色。")]
        public ColorParameter transmissionColor = new ColorParameter(new Color(1.0f, 0.38f, 0.22f, 1.0f));

        [InspectorName("透射采样数"), Tooltip("方向性透射的屏幕空间采样预算。")]
        public ClampedIntParameter transmissionSamples = new ClampedIntParameter(16, 2, 32);

        [InspectorName("主光方向混合"), Tooltip("0 使用法线屏幕投影，1 使用 URP 主光方向屏幕投影。")]
        public ClampedFloatParameter transmissionMainLightDirection = new ClampedFloatParameter(0.35f, 0.0f, 1.0f);

        [InspectorName("深度权重"), Tooltip("提高从更浅的皮肤区域向当前像素透射的权重。")]
        public ClampedFloatParameter transmissionDepthWeight = new ClampedFloatParameter(0.45f, 0.0f, 2.0f);

        [InspectorName("轮廓增强"), Tooltip("增强耳缘、鼻翼、手指等视角轮廓处的透射。")]
        public ClampedFloatParameter transmissionEdgeBoost = new ClampedFloatParameter(0.35f, 0.0f, 2.0f);

        [InspectorName("轮廓权重"), Tooltip("控制视角轮廓因子对透射的影响。")]
        public ClampedFloatParameter transmissionRimWeight = new ClampedFloatParameter(0.75f, 0.0f, 1.0f);

        [InspectorName("混合模式"), Tooltip("方向性透射与当前散射颜色的混合方式。")]
        public HoSubsurfaceScatteringTransmissionBlendModeParameter transmissionBlendMode = new HoSubsurfaceScatteringTransmissionBlendModeParameter(HoSubsurfaceScatteringTransmissionBlendMode.SoftTint);

        [InspectorName("染色量"), Tooltip("透射颜色注入的强度。")]
        public ClampedFloatParameter transmissionTintInjection = new ClampedFloatParameter(0.35f, 0.0f, 1.0f);

        [InspectorName("平滑"), Tooltip("透射结果的平滑程度。")]
        public ClampedFloatParameter transmissionSmoothing = new ClampedFloatParameter(0.45f, 0.0f, 1.0f);

        [InspectorName("调试模式"), Tooltip("输出 SSS 的参与遮罩、漫反射输入、扩散、透射或配置数据。Off 保持正常画面。")]
        public HoSubsurfaceScatteringDebugModeParameter debugMode = new HoSubsurfaceScatteringDebugModeParameter(HoSubsurfaceScatteringDebugMode.Off);

        public bool IsActive() => enable.value;
        public bool IsTileCompatible() => false;

        internal void ApplyTo(HoSubsurfaceScatteringSettings target)
        {
            if (!active) return;
            Apply(enable, ref target.enabled);
            Apply(renderInSceneView, ref target.renderInSceneView);
            Apply(renderScale, ref target.renderScale);
            Apply(quality, ref target.quality);
            Apply(strength, ref target.strength);
            Apply(depthTolerance, ref target.depthTolerance);
            Apply(normalTolerance, ref target.normalTolerance);
            Apply(radius, ref target.radius);
            Apply(color, ref target.color);
            Apply(sourcePreserve, ref target.sourcePreserve);
            Apply(transmissionStrength, ref target.transmissionStrength);
            Apply(transmissionRadius, ref target.transmissionRadius);
            Apply(transmissionColor, ref target.transmissionColor);
            Apply(transmissionSamples, ref target.transmissionSamples);
            Apply(transmissionMainLightDirection, ref target.transmissionMainLightDirection);
            Apply(transmissionDepthWeight, ref target.transmissionDepthWeight);
            Apply(transmissionEdgeBoost, ref target.transmissionEdgeBoost);
            Apply(transmissionRimWeight, ref target.transmissionRimWeight);
            Apply(transmissionBlendMode, ref target.transmissionBlendMode);
            Apply(transmissionTintInjection, ref target.transmissionTintInjection);
            Apply(transmissionSmoothing, ref target.transmissionSmoothing);
            Apply(debugMode, ref target.debugMode);
            if (profiles.overrideState) target.CopyProfilesFrom(profiles.value);
        }

        private static void Apply<T>(VolumeParameter<T> parameter, ref T target)
        {
            if (parameter.overrideState) target = parameter.value;
        }
    }
}
