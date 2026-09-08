using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SSGI
{
    [Serializable]
    public sealed class HoSSGIDebugModeParameter : VolumeParameter<HoSSGIDebugMode>
    {
        public HoSSGIDebugModeParameter(HoSSGIDebugMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoSSGIDebugMode from, HoSSGIDebugMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [VolumeComponentMenu("Lighting/Ho-SSGI/屏幕空间 GI")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class HoSSGIVolume : VolumeComponent, IPostProcessComponent
    {
        [InspectorName("启用")]
        public BoolParameter enable = new BoolParameter(true);

        [Header("追踪")]
        [InspectorName("射线数量")]
        public ClampedIntParameter rayCount = new ClampedIntParameter(8, 1, 32);

        [InspectorName("步数")]
        public ClampedIntParameter stepCount = new ClampedIntParameter(24, 4, 64);

        [InspectorName("最大射线长度")]
        public MinFloatParameter rayLength = new MinFloatParameter(4.0f, 0.01f);

        [InspectorName("厚度")]
        public ClampedFloatParameter thickness = new ClampedFloatParameter(0.08f, 0.0f, 1.0f);

        [Header("外观")]
        [InspectorName("GI 强度")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(1.0f, 0.0f, 4.0f);

        [InspectorName("Source 饱和度")]
        public ClampedFloatParameter sourceSaturation = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [Header("调试")]
        [InspectorName("调试模式")]
        public HoSSGIDebugModeParameter debugMode = new HoSSGIDebugModeParameter(HoSSGIDebugMode.Off);

        public bool IsActive() => enable.value;
        public bool IsTileCompatible() => false;
    }
}
