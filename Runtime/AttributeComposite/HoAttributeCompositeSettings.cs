using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.AttributeComposite
{
    /// <summary>AC 调试视图：与 debug shader 的 mode 数值一一对应，只能往后加。</summary>
    /// <remarks>显示名只写视图名；每个模式的说明在 Volume 面板里跟着调试模式单独画一行
    /// （`Editor/AttributeComposite/HoAttributeCompositeVolumeEditor.cs` 的 `DescribeDebugMode`）。</remarks>
    public enum HoAttributeCompositeDebugMode
    {
        [InspectorName("Off")]
        Off = 0,
        [InspectorName("Lane Coverage")]
        LaneCoverage,
        [InspectorName("Lane SemanticId")]
        LaneSemanticId,
        [InspectorName("Lane Object Mask")]
        LaneObjectMask,
        [InspectorName("Geometry Coverage")]
        GeometryCoverage,
        [InspectorName("Outline Coverage")]
        OutlineCoverage,
        [InspectorName("Input Availability")]
        InputAvailability,
        [InspectorName("Object Coverage")]
        ObjectCoverage,
        [InspectorName("Surface Written")]
        SurfaceWritten,
        [InspectorName("Surface Value")]
        SurfaceValue,
        [InspectorName("Semantic Owner Match")]
        SemanticOwnerMatch,
        [InspectorName("Final Coverage")]
        FinalCoverage,
        [InspectorName("Semantic Compare")]
        SemanticCompare,
        [InspectorName("Written Coverage")]
        WrittenCoverage,
        [InspectorName("Weighted Coverage")]
        WeightedCoverage,
        [InspectorName("Semantic Precision")]
        SemanticPrecision,
        [InspectorName("Outline Owner")]
        OutlineOwner
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

        [NonSerialized]
        public string debugSemanticName = "Face";

        internal static bool IsSemanticDebug(HoAttributeCompositeDebugMode mode) =>
            mode >= HoAttributeCompositeDebugMode.ObjectCoverage && mode <= HoAttributeCompositeDebugMode.SemanticPrecision;

        internal static bool TryResolveDebugLane(string name, out Vector4 lane)
        {
            lane = Vector4.zero;
            if (!HoSemanticSchema.TryGetByName(name, out HoSemanticEntry entry) || entry.laneIndex < 0 ||
                entry.laneIndex >= HoSemanticSchema.ResolvedLaneCount || entry.semanticId < 1 || entry.semanticId > 255)
                return false;
            lane = new Vector4(entry.laneIndex, entry.semanticId, entry.objectTagBit, (int)entry.sourceMode);
            return true;
        }

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
            debugSemanticName = source.debugSemanticName;
        }
    }
}
