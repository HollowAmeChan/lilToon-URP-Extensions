using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GeometryBuffer
{
    [Serializable]
    public sealed class HoGeometryBufferSettings
    {
        [InspectorName("Enabled")]
        public bool enabled = true;

        [InspectorName("Layer Mask")]
        public LayerMask layerMask = -1;

        [InspectorName("Min Render Queue")]
        public int minRenderQueue = 0;

        [InspectorName("Max Render Queue")]
        public int maxRenderQueue = (int)RenderQueue.Overlay - 1;

        [InspectorName("Pass Event")]
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingOpaques;

        [InspectorName("Render Scale")]
        public HoGeometryBufferRenderScale renderScale = HoGeometryBufferRenderScale.Full;

        [InspectorName("Fallback Shader")]
        public Shader fallbackShader;
    }

    public enum HoGeometryBufferRenderScale
    {
        [InspectorName("Full")]
        Full = 1,
        [InspectorName("Half")]
        Half = 2,
        [InspectorName("Quarter")]
        Quarter = 4
    }
}
