using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    [Serializable]
    public sealed class HoSurfaceBufferDebugModeParameter : VolumeParameter<HoSurfaceBufferDebugMode>
    {
        public HoSurfaceBufferDebugModeParameter(HoSurfaceBufferDebugMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoSurfaceBufferDebugMode from, HoSurfaceBufferDebugMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    /// <summary>SB 的**调试入口**（SB 架构 §14：调试在 Volume，feature 只放高级设置 + 兜底默认值）。</summary>
    [Serializable]
    [VolumeComponentMenu("Post-processing/Ho-SurfaceBuffer/表面数值")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class HoSurfaceBufferVolume : VolumeComponent, IPostProcessComponent
    {
        [InspectorName("启用")]
        public BoolParameter enable = new BoolParameter(true);

        [InspectorName("调试模式")]
        public HoSurfaceBufferDebugModeParameter debugMode =
            new HoSurfaceBufferDebugModeParameter(HoSurfaceBufferDebugMode.Off);

        [InspectorName("Debug In Scene View")]
        public BoolParameter debugInSceneView = new BoolParameter(true);

        [InspectorName("Debug In Game View")]
        public BoolParameter debugInGameView = new BoolParameter(true);

        public bool IsActive()
        {
            return enable.value;
        }

        public bool IsTileCompatible()
        {
            return false;
        }
    }
}
