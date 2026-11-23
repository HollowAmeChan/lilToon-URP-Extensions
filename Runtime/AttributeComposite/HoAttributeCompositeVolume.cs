using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.AttributeComposite
{
    [Serializable]
    public sealed class HoACDebugCategoryParameter : VolumeParameter<HoACDebugCategory>
    {
        public HoACDebugCategoryParameter(HoACDebugCategory value, bool overrideState = false) : base(value, overrideState) { }
        public override void Interp(HoACDebugCategory from, HoACDebugCategory to, float t) { value = t > 0f ? to : from; }
    }

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
    [VolumeComponentMenu(HoVolumeNames.AttributeComposite)]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#if UNITY_6000_3_OR_NEWER
    [DisplayInfo(name = HoVolumeNames.AttributeComposite)]
#endif
    public sealed class HoAttributeCompositeVolume : VolumeComponent, IPostProcessComponent
    {
        public HoAttributeCompositeVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = HoVolumeNames.AttributeComposite;
#endif
        }

        [InspectorName("启用")]
        [Tooltip("未覆盖时沿用 Ho AttributeComposite Feature 的运行兜底值。")]
        public BoolParameter enable = new BoolParameter(true);

        [InspectorName("调试模式")]
        public HoAttributeCompositeDebugModeParameter debugMode =
            new HoAttributeCompositeDebugModeParameter(HoAttributeCompositeDebugMode.Off);

        [Tooltip("选择当前已登记的分类；选择组在 Ho ObjectBuffer 组件的选区中登记。\n\n选择查看的登记项；彩色总览中点击名称可突出显示，再次点击取消。重叠选择组或语义会混色。")]
        public HoACDebugCategoryParameter debugCategory = new HoACDebugCategoryParameter(HoACDebugCategory.Parts);
        public HoACSemanticNameParameter debugTarget = new HoACSemanticNameParameter(string.Empty);

        [InspectorName("调试语义"), Tooltip("单语义视图与六格对照使用的 schema 稳定名称。")]
        public HoACSemanticNameParameter debugSemanticName = new HoACSemanticNameParameter("Face");

        [InspectorName("调试选择组")]
        [Tooltip("在 Ho ObjectBuffer 组件的选区中创建选择组并添加对象后，可在这里选择。")]
        public HoACSemanticNameParameter debugSelectionName = new HoACSemanticNameParameter("Selection");

        [InspectorName("调试身份组 ID")]
        public NoInterpClampedIntParameter debugGroupId = new NoInterpClampedIntParameter(1, 1, 255);

        [InspectorName("场景视图")]
        public BoolParameter debugInSceneView = new BoolParameter(true);

        [InspectorName("游戏视图")]
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
