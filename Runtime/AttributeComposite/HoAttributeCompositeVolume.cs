using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.AttributeComposite
{
    [Serializable]
    public sealed class HoAttributeCompositeDebugModeParameter : VolumeParameter<HoAttributeCompositeDebugMode>
    {
        public HoAttributeCompositeDebugModeParameter(HoAttributeCompositeDebugMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoAttributeCompositeDebugMode from, HoAttributeCompositeDebugMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    /// <summary>
    /// AC 的**调试入口**（AC 架构 §9.13：调试在 Volume，feature 只放高级设置 + 兜底默认值 + 消费者登记表）。
    /// </summary>
    [Serializable]
    [VolumeComponentMenu("Post-processing/Ho-AttributeComposite/属性合成")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class HoAttributeCompositeVolume : VolumeComponent, IPostProcessComponent
    {
        [InspectorName("启用")]
        public BoolParameter enable = new BoolParameter(true);

        [InspectorName("调试模式")]
        public HoAttributeCompositeDebugModeParameter debugMode =
            new HoAttributeCompositeDebugModeParameter(HoAttributeCompositeDebugMode.Off);

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
