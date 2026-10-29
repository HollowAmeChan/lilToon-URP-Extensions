using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PlanarReflection
{
    [Serializable]
    public sealed class HoPlanarReflectionDebugModeParameter : VolumeParameter<HoPlanarReflectionDebugMode>
    {
        public HoPlanarReflectionDebugModeParameter(HoPlanarReflectionDebugMode value, bool overrideState = false)
            : base(value, overrideState) { }

        public override void Interp(HoPlanarReflectionDebugMode from, HoPlanarReflectionDebugMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

#if UNITY_2023_1_OR_NEWER
    [VolumeComponentMenu("Post-processing/Ho-PlanarReflection/平面反射"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#else
    [VolumeComponentMenuForRenderPipeline("Post-processing/Ho-PlanarReflection/平面反射", typeof(UniversalRenderPipeline))]
#endif
#if UNITY_2023_3_OR_NEWER
    [VolumeRequiresRendererFeatures(typeof(HoPlanarReflectionRendererFeature))]
#endif
    [Serializable]
    public sealed class HoPlanarReflectionVolume : VolumeComponent, IPostProcessComponent
    {
        [InspectorName("启用")]
        [Tooltip("覆盖 RendererFeature 的总开关；未勾选 Override 时沿用 RendererFeature 设置。")]
        public BoolParameter enable = new BoolParameter(true);

        [InspectorName("Game View")]
        public BoolParameter renderGameView = new BoolParameter(true);

        [InspectorName("Scene View")]
        public BoolParameter renderSceneView = new BoolParameter(true);

        [InspectorName("最大反射面数")]
        [Tooltip("0 表示不限制。")]
        public ClampedIntParameter maxSurfacesPerCamera = new ClampedIntParameter(0, 0, 64);

        [InspectorName("特殊全屏合成")]
        [Tooltip("不透明地板通常保持关闭；水面/特殊透明面可在 Volume 中打开。")]
        public BoolParameter compositeEnabled = new BoolParameter(false);

        [InspectorName("合成强度")]
        public ClampedFloatParameter compositeStrength = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [InspectorName("UV 扰动")]
        public ClampedFloatParameter distortion = new ClampedFloatParameter(0.018f, 0.0f, 0.1f);

        [InspectorName("边缘延拓")]
        public ClampedFloatParameter edgeExtendDistance = new ClampedFloatParameter(0.035f, 0.0f, 0.25f);

        [InspectorName("翻转 Y")]
        public BoolParameter compositeFlipY = new BoolParameter(false);

        [InspectorName("最小光滑度")]
        public ClampedFloatParameter minSmoothness = new ClampedFloatParameter(0.65f, 0.0f, 1.0f);

        [InspectorName("深度门控")]
        public BoolParameter enableDepthGate = new BoolParameter(false);

        [InspectorName("深度容差")]
        public MinFloatParameter depthTolerance = new MinFloatParameter(0.0f);

        [InspectorName("反射曝光")]
        public ClampedFloatParameter reflectionExposure = new ClampedFloatParameter(0.0f, -4.0f, 4.0f);

        [InspectorName("反射模糊像素")]
        public ClampedFloatParameter reflectionBlurRadiusPixels = new ClampedFloatParameter(0.0f, 0.0f, 32.0f);

        [InspectorName("反射色调")]
        public ColorParameter tint = new ColorParameter(Color.white);

        [InspectorName("调试模式")]
        public HoPlanarReflectionDebugModeParameter debugMode = new HoPlanarReflectionDebugModeParameter(HoPlanarReflectionDebugMode.Off);

        [InspectorName("调试远裁剪")]
        public MinFloatParameter debugDepthFar = new MinFloatParameter(100.0f);

        [InspectorName("调试扰动倍率")]
        public MinFloatParameter debugDistortionScale = new MinFloatParameter(32.0f);

        public bool IsActive() => active;
        public bool IsTileCompatible() => false;
    }
}
