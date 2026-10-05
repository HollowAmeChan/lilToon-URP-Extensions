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
    [Serializable, VolumeComponentMenu("Post-processing/Ho-GeometryData/几何数据")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class HoGeometryDataVolume : VolumeComponent, IPostProcessComponent
    {
        [InspectorName("启用")] public BoolParameter enable = new BoolParameter(true);
        [InspectorName("调试模式")] public HoGeometryDataDebugModeParameter debugMode = new HoGeometryDataDebugModeParameter(HoGeometryDataDebugMode.Off);
        [InspectorName("Scene 视图")] public BoolParameter debugInSceneView = new BoolParameter(true);
        [InspectorName("Game 视图")] public BoolParameter debugInGameView = new BoolParameter(true);
        [InspectorName("预览 Layer Mask")] public HoGeometryDataLayerMaskParameter debugLayerMask = new HoGeometryDataLayerMaskParameter(-1);
        [InspectorName("热图满量程")] public MinFloatParameter debugMaxValue = new MinFloatParameter(0.2f, 0.0001f);
        [InspectorName("预览绘制时机")] public HoGeometryDataPassEventParameter debugPassEvent = new HoGeometryDataPassEventParameter(RenderPassEvent.AfterRenderingPostProcessing);
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
