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
        ClassId,
        /// <summary>语义 lane 的 owner（与数值面同一个身份）：绿 = 与 OB 层 0 一致 / 橙 = 不一致 / 红 = 没人写 / 洋红 = OB 没产出。</summary>
        [InspectorName("Semantic Owner")]
        SemanticOwner,
        /// <summary>8 条语义 lane 铺成 4×2 网格：每格一个 lane，通道 = (SemanticId÷255, value, 写了没有)；未写画暗红。</summary>
        [InspectorName("Semantic Lanes")]
        SemanticLanes
    }

    [Serializable]
    public sealed class HoSurfaceBufferSettings
    {
        public bool enabled = true;

        /// <summary>
        /// 语义 lane pass（**逐像素**的 `(SemanticId, value)` + owner）。**这是 AC 的 surface 来源**：
        /// 关掉它，AC 的语义合成就只剩物体位（回落到 OB）。代价是每帧多一趟角色几何 + 5 张附件。
        /// <para>
        /// 逐 sample 的细分（同一材质内部的眼白 / 虹膜）等真有消费者要时再上，形态是
        /// "SB 自己 resolve 出单采样 lane 再发布"：读端永远只读单采样。
        /// </para>
        /// </summary>
        public bool enableSemanticLanes = true;

        public LayerMask layerMask = -1;

        public int minRenderQueue;

        /// <summary>
        /// 上限固定在上不透明段的末尾（`GeometryLast`）：**透明表面本轮不生产 SB 数值**。
        /// 多层透明的 roughness / classification 加不出唯一的前表面真值（SB 架构 §0.5），
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
            enableSemanticLanes = source.enableSemanticLanes;
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
