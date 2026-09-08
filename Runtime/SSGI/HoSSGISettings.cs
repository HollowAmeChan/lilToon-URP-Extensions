using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SSGI
{
    public enum HoSSGIDebugMode
    {
        [InspectorName("关闭")]
        Off = 0,
        [InspectorName("Clean Base Source")]
        Source = 1,
        [InspectorName("Source Validity")]
        SourceValidity = 2,
        [InspectorName("Geometry")]
        Geometry = 3,
        [InspectorName("Raw GI")]
        RawGI = 4,
        [InspectorName("Confidence")]
        Confidence = 5,
        [InspectorName("Direct Source")]
        DirectSource = 6
    }

    [Serializable]
    public sealed class HoSSGISettings
    {
        public bool enabled = true;
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingOpaques;
        [Range(1, 32)] public int rayCount = 8;
        [Range(4, 64)] public int stepCount = 24;
        [Min(0.01f)] public float rayLength = 4.0f;
        [Range(0.0f, 1.0f)] public float thickness = 0.08f;
        [Range(0.0f, 4.0f)] public float intensity = 1.0f;
        [Range(0.0f, 1.0f)] public float sourceSaturation = 1.0f;
        public HoSSGIDebugMode debugMode = HoSSGIDebugMode.Off;
        public bool debugInSceneView = true;
        public bool debugInGameView;
        public Shader shader;
        public Shader debugShader;
    }
}
