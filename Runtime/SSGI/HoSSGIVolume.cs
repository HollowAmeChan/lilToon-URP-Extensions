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

    [VolumeComponentMenu(HoVolumeNames.SSGI)]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#if UNITY_6000_3_OR_NEWER
    [DisplayInfo(name = HoVolumeNames.SSGI)]
#endif
    public sealed class HoSSGIVolume : VolumeComponent, IPostProcessComponent
    {
        public HoSSGIVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = HoVolumeNames.SSGI;
#endif
        }

        [InspectorName("启用")]
        [Tooltip("读取不透明物体光照后的相机颜色，在后处理之前合成；需要 Ho GeometryBuffer 的几何输入。运行参数在此 Volume 调整。")]
        public BoolParameter enable = new BoolParameter(true);

        [InspectorName("射线数量")]
        public ClampedIntParameter rayCount = new ClampedIntParameter(8, 1, 128);

        [InspectorName("步数")]
        public ClampedIntParameter stepCount = new ClampedIntParameter(24, 4, 256);

        [InspectorName("最大射线长度")]
        public ClampedFloatParameter rayLength = new ClampedFloatParameter(4.0f, 0.01f, 32.0f);

        [InspectorName("厚度")]
        public ClampedFloatParameter thickness = new ClampedFloatParameter(0.08f, 0.0f, 4.0f);

        [InspectorName("时域混合")]
        public ClampedFloatParameter temporalBlend = new ClampedFloatParameter(0.9f, 0.0f, 1.0f);

        [InspectorName("空间半径")]
        public ClampedFloatParameter spatialRadius = new ClampedFloatParameter(2.0f, 0.5f, 8.0f);

        [InspectorName("时域 Reservoir 重用")]
        public BoolParameter temporalReservoirReuse = new BoolParameter(true);

        [InspectorName("空间 Reservoir 重用")]
        public BoolParameter spatialReservoirReuse = new BoolParameter(true);

        [InspectorName("时域射线验证")]
        public BoolParameter temporalReservoirValidation = new BoolParameter(true);

        [InspectorName("空间射线验证")]
        public BoolParameter spatialReservoirValidation = new BoolParameter(true);

        [InspectorName("Firefly 抑制")]
        public BoolParameter fireflySuppression = new BoolParameter(true);

        [InspectorName("GI 强度")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(1.0f, 0.0f, 8.0f);

        [InspectorName("Source 饱和度")]
        public ClampedFloatParameter sourceSaturation = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [InspectorName("调试模式")]
        public HoSSGIDebugModeParameter debugMode = new HoSSGIDebugModeParameter(HoSSGIDebugMode.Off);

        public bool IsActive() => enable.value;
        public bool IsTileCompatible() => false;
    }
}
