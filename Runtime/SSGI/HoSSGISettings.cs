using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SSGI
{
    public enum HoSSGIDebugMode
    {
        Off = 0,
        Source = 1,
        SourceValidity = 2,
        Geometry = 3,
        RawGI = 4,
        Confidence = 5,
        SurfaceColor = 6
    }

    [Serializable]
    public sealed class HoSSGISettings
    {
        public bool enabled = true;
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingOpaques;
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
