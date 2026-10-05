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
    /// 调试语义名称的离散 Volume 参数；不在名字之间插值。
    /// </summary>
    [Serializable]
    public sealed class HoACSemanticNameParameter : VolumeParameter<string>
    {
        public HoACSemanticNameParameter(string value, bool overrideState = false) : base(value, overrideState) { }
        public override void Interp(string from, string to, float t) { value = t > 0f ? to : from; }
    }

    /// <summary>AC 调试入口与逐相机启用；声明仍由 feature/schema 管理。</summary>
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

        [InspectorName("调试语义"), Tooltip("单语义视图与六格对照使用的 schema 稳定名称。")]
        public HoACSemanticNameParameter debugSemanticName = new HoACSemanticNameParameter("Face");

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
