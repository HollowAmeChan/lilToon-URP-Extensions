using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Debugging
{
    [Serializable]
    public sealed class HoDebugTileViewIdParameter : VolumeParameter<string>
    {
        public HoDebugTileViewIdParameter(string value, bool overrideState = false) : base(value, overrideState) { }
        public override void Interp(string from, string to, float t) { value = t > 0.0f ? to : from; }
    }

    [VolumeComponentMenu("Debug/Ho-DebugPerformance/性能 HUD")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class HoDebugTileVolume : VolumeComponent, IPostProcessComponent
    {
        [InspectorName("启用")]
        public BoolParameter enable = new BoolParameter(true);

        // Retained for old VolumeProfiles; the performance HUD no longer dispatches tile views.
        [HideInInspector]
        public HoDebugTileViewIdParameter debugView = new HoDebugTileViewIdParameter(HoDebugTileRendererFeature.NoneViewId);

        [InspectorName("性能信息")]
        public HoDebugTilePerformanceModeParameter performance =
            new HoDebugTilePerformanceModeParameter(HoDebugTilePerformanceMode.Off);

        [InspectorName("场景视图")]
        public BoolParameter debugInSceneView = new BoolParameter(true);

        [InspectorName("游戏视图")]
        public BoolParameter debugInGameView = new BoolParameter(true);

        public bool IsActive() => enable.value;
        public bool IsTileCompatible() => false;
    }
}
