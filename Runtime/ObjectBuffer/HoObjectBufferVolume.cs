using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    [Serializable]
    public sealed class HoObjectBufferDebugModeParameter : VolumeParameter<HoObjectBufferDebugMode>
    {
        public HoObjectBufferDebugModeParameter(HoObjectBufferDebugMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoObjectBufferDebugMode from, HoObjectBufferDebugMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    [VolumeComponentMenu("Post-processing/Ho-ObjectBuffer/逐物体通道")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class HoObjectBufferVolume : VolumeComponent, IPostProcessComponent
    {
        [InspectorName("启用")]
        public BoolParameter enable = new BoolParameter(true);

        [InspectorName("调试模式")]
        public HoObjectBufferDebugModeParameter debugMode = new HoObjectBufferDebugModeParameter(HoObjectBufferDebugMode.Off);

        [InspectorName("Debug In Scene View")]
        public BoolParameter debugInSceneView = new BoolParameter(true);

        [InspectorName("Debug In Game View")]
        public BoolParameter debugInGameView = new BoolParameter(false);

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
