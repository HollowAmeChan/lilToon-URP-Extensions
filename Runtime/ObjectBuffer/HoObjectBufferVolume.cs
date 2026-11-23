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
    [VolumeComponentMenu(HoVolumeNames.ObjectBuffer)]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#if UNITY_6000_3_OR_NEWER
    [DisplayInfo(name = HoVolumeNames.ObjectBuffer)]
#endif
    public sealed class HoObjectBufferVolume : VolumeComponent, IPostProcessComponent
    {
        public HoObjectBufferVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = HoVolumeNames.ObjectBuffer;
#endif
        }

        [InspectorName("启用")]
        public BoolParameter enable = new BoolParameter(true);

        [InspectorName("调试模式")]
        public HoObjectBufferDebugModeParameter debugMode = new HoObjectBufferDebugModeParameter(HoObjectBufferDebugMode.Off);

        [InspectorName("场景视图")]
        public BoolParameter debugInSceneView = new BoolParameter(true);

        [InspectorName("游戏视图")]
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
