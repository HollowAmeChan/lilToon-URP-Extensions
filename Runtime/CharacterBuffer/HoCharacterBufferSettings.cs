using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterBuffer
{
    [Serializable]
    public sealed class HoCharacterBufferSettings
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
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingOpaques;

        [InspectorName("Debug Pass Event")]
        public RenderPassEvent debugPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [InspectorName("MSAA Samples")]
        [Tooltip("ID pass 自建的 MSAA 采样数。**与相机的 MSAA 设置无关**：覆盖率是本 feature 的产品功能，" +
                 "相机把 AA 关掉时它也必须照常产出（规划决策 7）。")]
        public HoCharacterBufferSampleCount sampleCount = HoCharacterBufferSampleCount.Four;

        [InspectorName("Selection Layers")]
        [Tooltip("每像素的选择层数。2 = 一张 RGBA8（Cryptomatte 成对布局）；4 = 两张（P1 暂按 2 跑并告警）。")]
        public HoCharacterBufferSelectionLayers selectionLayers = HoCharacterBufferSelectionLayers.Two;

        [InspectorName("Use Fallback Material")]
        public bool useFallbackMaterial = true;

        [InspectorName("Fallback Shader")]
        public Shader fallbackShader;

        [InspectorName("Debug Shader")]
        public Shader debugShader;

        [InspectorName("Debug Mode")]
        public HoCharacterBufferDebugMode debugMode = HoCharacterBufferDebugMode.Off;

        [InspectorName("Debug In Scene View")]
        public bool debugInSceneView = true;

        [InspectorName("Debug In Game View")]
        public bool debugInGameView;

        public int RequestedSampleCount => Mathf.Max(2, (int)sampleCount);

        public int RequestedSelectionLayerCount => Mathf.Clamp((int)selectionLayers, 0, 4);
    }
}
