using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    /// <summary>SB 调试视图：与 debug shader 的 mode 一一对应，只能往后加。</summary>
    /// <remarks>
    /// 约定：**每个视图都把"没人写"的像素（owner = 0）画成暗红** —— "整屏暗红"= SB 没产出，
    /// "黑"= 有值但值是 0，两者必须能一眼分开（否则 0 和"没跑"长得一模一样）。
    /// 两个 byte ID（profile / materialClass）按缩放显示（÷8、÷32）：字节原值直接铺到 0..1 基本是黑的。
    /// **显示名只写视图名**：名字里不放说明（Unity 的下拉把 "/" 当分组分隔符，`[InspectorName("")]` 变分隔线
    /// 是同一套规则），每个模式的说明在 Volume 面板里跟着调试模式**单独画一行**
    /// （`Editor/SurfaceBuffer/HoSurfaceBufferVolumeEditor.cs` 的 `DescribeDebugMode`）。
    /// </remarks>
    public enum HoSurfaceBufferDebugMode
    {
        [InspectorName("Off")]
        Off = 0,
        [InspectorName("Color")]
        Color,
        [InspectorName("Normal")]
        Normal,
        [InspectorName("Material")]
        Material,
        [InspectorName("Reflection")]
        Reflection,
        [InspectorName("Classification")]
        Classification,
        [InspectorName("Owner")]
        Owner,
        [InspectorName("Class Id")]
        ClassId
    }

    [Serializable]
    public sealed class HoSurfaceBufferSettings
    {
        public bool enabled = true;

        public LayerMask layerMask = -1;

        public int minRenderQueue;

        /// <summary>
        /// 上限固定在上不透明段的末尾（`GeometryLast`）：**透明表面本轮不生产 SB 数值**。
        /// 多层透明的 roughness / classification 加不出唯一的前表面真值（规划 §0.5），
        /// 先明确"不生产"，等策略定下来再放开。
        /// </summary>
        public int maxRenderQueue = (int)RenderQueue.GeometryLast;

        /// <summary>必须排在 OB 之后（AC 要用 OB 层 0 的身份校验 owner）。</summary>
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingOpaques;

        public RenderPassEvent debugPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [NonSerialized]
        public HoSurfaceBufferDebugMode debugMode = HoSurfaceBufferDebugMode.Off;

        [NonSerialized]
        public bool debugInSceneView = true;

        [NonSerialized]
        public bool debugInGameView = true;

        public void CopyFrom(HoSurfaceBufferSettings source)
        {
            if (source == null)
            {
                return;
            }

            enabled = source.enabled;
            layerMask = source.layerMask;
            minRenderQueue = source.minRenderQueue;
            maxRenderQueue = source.maxRenderQueue;
            passEvent = source.passEvent;
            debugPassEvent = source.debugPassEvent;
            debugMode = source.debugMode;
            debugInSceneView = source.debugInSceneView;
            debugInGameView = source.debugInGameView;
        }
    }
}
