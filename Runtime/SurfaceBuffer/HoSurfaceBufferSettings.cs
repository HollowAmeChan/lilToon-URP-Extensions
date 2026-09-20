using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    /// <summary>SB 调试视图：与 debug shader 的 mode 一一对应，只能往后加。</summary>
    public enum HoSurfaceBufferDebugMode
    {
        [InspectorName("Off")]
        Off = 0,
        [InspectorName("Color")]
        Color,
        [InspectorName("Normal（octa 还原）")]
        Normal,
        [InspectorName("Material（roughness / metallic / thickness）")]
        Material,
        [InspectorName("Reflection（reflectance / plrStrength）")]
        Reflection,
        [InspectorName("Classification（profile / curvature / transmittance / class）")]
        Classification,
        [InspectorName("Owner（前表面是不是 OB 层 0）")]
        Owner
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
