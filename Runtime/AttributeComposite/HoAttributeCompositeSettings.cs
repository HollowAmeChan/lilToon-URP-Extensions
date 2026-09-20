using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.AttributeComposite
{
    /// <summary>AC 调试视图：与 debug shader 的 mode 数值一一对应，只能往后加。</summary>
    public enum HoAttributeCompositeDebugMode
    {
        [InspectorName("Off")]
        Off = 0,
        [InspectorName("Lane 覆盖率（4 条一组）")]
        LaneCoverage,
        [InspectorName("Lane SemanticId（4 条一组）")]
        LaneSemanticId,
        [InspectorName("候选 lane 的 object 位掩码")]
        LaneObjectMask
    }

    [Serializable]
    public sealed class HoAttributeCompositeSettings
    {
        public bool enabled = true;

        /// <summary>
        /// AC 必须排在 {OB, SB} 之后。默认与 OB 同事件（`BeforeRenderingOpaques`），
        /// 靠 Renderer Feature 列表顺序 + RenderGraph 的读依赖保证在后。
        /// </summary>
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingOpaques;

        public RenderPassEvent debugPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [NonSerialized]
        public HoAttributeCompositeDebugMode debugMode = HoAttributeCompositeDebugMode.Off;

        [NonSerialized]
        public bool debugInSceneView = true;

        [NonSerialized]
        public bool debugInGameView = true;

        /// <summary>把资产上的高级设置复制进运行时载体（Volume 只覆盖调试项）。</summary>
        public void CopyFrom(HoAttributeCompositeSettings source)
        {
            if (source == null)
            {
                return;
            }

            enabled = source.enabled;
            passEvent = source.passEvent;
            debugPassEvent = source.debugPassEvent;
            debugMode = source.debugMode;
            debugInSceneView = source.debugInSceneView;
            debugInGameView = source.debugInGameView;
        }
    }
}
