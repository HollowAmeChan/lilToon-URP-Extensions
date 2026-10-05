using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GeometryData
{
    public enum HoGeometryDataDebugMode
    {
        [InspectorName("关闭")] Off,
        [InspectorName("GD 绑定槽")] RendererSlot,
        [InspectorName("描边方向")] OutlineDirection,
        [InspectorName("描边厚度")] OutlineThickness,
        [InspectorName("Tesion 拉伸")] TensionStretch,
        [InspectorName("Tesion 挤压")] TensionCompression,
        [InspectorName("Tesion 角变化")] TensionAngular,
        [InspectorName("Tesion 有效性")] TensionValidity
    }
    [Serializable]
    public sealed class HoGeometryDataSettings
    {
        [InspectorName("运行")] public bool enabled = true;
    }
}
