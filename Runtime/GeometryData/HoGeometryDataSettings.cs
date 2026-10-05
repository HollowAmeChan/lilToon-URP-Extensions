using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GeometryData
{
    public enum HoGeometryDataDebugMode
    {
        [InspectorName("关闭")] Off,
        [InspectorName("Renderer 绑定 / GD 槽")] RendererSlot,
        [InspectorName("描边方向（世界空间 RGB）")] OutlineDirection,
        [InspectorName("描边厚度（原始 A）")] OutlineThickness,
        [InspectorName("Tesion 拉伸")] TensionStretch,
        [InspectorName("Tesion 挤压")] TensionCompression,
        [InspectorName("Tesion 角变化")] TensionAngular,
        [InspectorName("Tesion 有效性")] TensionValidity
    }
    [Serializable]
    public sealed class HoGeometryDataSettings
    {
        [InspectorName("运行")] public bool enabled = true;
        [InspectorName("数据预览")] public HoGeometryDataDebugMode debugMode;
        [InspectorName("Scene 视图")] public bool debugInSceneView = true;
        [InspectorName("Game 视图")] public bool debugInGameView;
        [InspectorName("预览 Layer Mask")] public LayerMask debugLayerMask = -1;
        [Min(0.0001f), InspectorName("热图满量程")]
        [Tooltip("仅调整热图显示。数值达到满量程显示红色，不改变几何测量或材质响应。")]
        public float debugMaxValue = 0.2f;
        [InspectorName("预览绘制时机")] public RenderPassEvent debugPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }
}
