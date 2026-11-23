using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GeometryData
{
    [Serializable]
    public sealed class HoGeometryDataDebugModeParameter : VolumeParameter<HoGeometryDataDebugMode>
    {
        public HoGeometryDataDebugModeParameter(HoGeometryDataDebugMode value, bool overrideState = false) : base(value, overrideState) { }
        public override void Interp(HoGeometryDataDebugMode from, HoGeometryDataDebugMode to, float t) => value = t > 0 ? to : from;
    }
    [Serializable]
    public sealed class HoGeometryDataLayerMaskParameter : VolumeParameter<LayerMask>
    {
        public HoGeometryDataLayerMaskParameter(LayerMask value, bool overrideState = false) : base(value, overrideState) { }
        public override void Interp(LayerMask from, LayerMask to, float t) => value = t > 0 ? to : from;
    }
    [Serializable]
    public sealed class HoGeometryDataPassEventParameter : VolumeParameter<RenderPassEvent>
    {
        public HoGeometryDataPassEventParameter(RenderPassEvent value, bool overrideState = false) : base(value, overrideState) { }
        public override void Interp(RenderPassEvent from, RenderPassEvent to, float t) => value = t > 0 ? to : from;
    }
    [Serializable, VolumeComponentMenu(HoVolumeNames.GeometryData)]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#if UNITY_6000_3_OR_NEWER
    [DisplayInfo(name = HoVolumeNames.GeometryData)]
#endif
    public sealed class HoGeometryDataVolume : VolumeComponent, IPostProcessComponent
    {
        public HoGeometryDataVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = HoVolumeNames.GeometryData;
#endif
        }

        [Tooltip("逐相机覆盖运行开关；未覆盖时使用 Feature 兜底值。调试不要求开启材质 Tension。")]
        [InspectorName("启用")] public BoolParameter enable = new BoolParameter(true);
        [InspectorName("调试模式")] public HoGeometryDataDebugModeParameter debugMode = new HoGeometryDataDebugModeParameter(HoGeometryDataDebugMode.Off);
        [InspectorName("场景视图")] public BoolParameter debugInSceneView = new BoolParameter(true);
        [InspectorName("游戏视图")] public BoolParameter debugInGameView = new BoolParameter(true);
        [Tooltip("仅筛选预览几何，不改变实际数据生产或材质；预览跳过材质裁剪与描边位移，在最终后处理之后显示。")]
        [InspectorName("预览 Layer Mask")] public HoGeometryDataLayerMaskParameter debugLayerMask = new HoGeometryDataLayerMaskParameter(-1);
        [InspectorName("热图满量程")] public MinFloatParameter debugMaxValue = new MinFloatParameter(0.2f, 0.0001f);
        [HideInInspector] public HoGeometryDataPassEventParameter debugPassEvent = new HoGeometryDataPassEventParameter(RenderPassEvent.AfterRenderingPostProcessing);
        public bool IsActive() => enable.value;
        public bool IsTileCompatible() => false;
    }
    internal sealed class HoGeometryDataDebugSettings
    {
        internal HoGeometryDataDebugMode debugMode;
        internal bool debugInSceneView, debugInGameView;
        internal LayerMask debugLayerMask = -1;
        internal float debugMaxValue = 0.2f;
        internal RenderPassEvent debugPassEvent;
    }
}
