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

    [VolumeComponentMenu(HoVolumeNames.DebugHUD)]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#if UNITY_6000_3_OR_NEWER
    [DisplayInfo(name = HoVolumeNames.DebugHUD)]
#endif
    public sealed class HoDebugTileVolume : VolumeComponent, IPostProcessComponent
    {
        public HoDebugTileVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = HoVolumeNames.DebugHUD;
#endif
        }

        [InspectorName("启用")]
        public BoolParameter enable = new BoolParameter(true);

        // Retained for old VolumeProfiles; the performance HUD no longer dispatches tile views.
        [HideInInspector]
        public HoDebugTileViewIdParameter debugView = new HoDebugTileViewIdParameter(HoDebugTileRendererFeature.NoneViewId);

        [InspectorName("性能信息")]
        [Tooltip("在最终画面叠加 Ho 体系的轻量性能 HUD，显示 FPS、帧时间、活动 Feature 数量及 CPU/GPU marker 耗时。各 Feature 自己持有图像调试入口。")]
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
