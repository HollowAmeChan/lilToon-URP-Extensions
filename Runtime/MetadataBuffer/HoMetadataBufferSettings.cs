using System;
using lilToon.URP.Extensions.AOV;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.MetadataBuffer
{
    [Serializable]
    public sealed class HoMetadataBufferSettings
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

        [InspectorName("Debug Pass Event")]
        public RenderPassEvent debugPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [InspectorName("Render Scale")]
        public HoMetadataBufferRenderScale renderScale = HoMetadataBufferRenderScale.Full;

        [InspectorName("Metadata Channels")]
        public HoAovChannelMask systemChannels = HoAovChannelMask.Default;

        [InspectorName("Custom Channel Count")]
        [Range(0, HoAovCustomChannels.MaxSupportedCount)]
        public int customChannelCount = HoAovCustomChannels.DefaultCount;

        [InspectorName("Use Fallback Material")]
        public bool useFallbackMaterial = true;

        [InspectorName("Fallback Shader")]
        public Shader fallbackShader;

        [InspectorName("Debug Shader")]
        public Shader debugShader;

        [InspectorName("Debug Mode")]
        public HoMetadataBufferDebugMode debugMode = HoMetadataBufferDebugMode.Off;

        [InspectorName("Debug In Scene View")]
        public bool debugInSceneView = true;

        [InspectorName("Debug In Game View")]
        public bool debugInGameView;

        [InspectorName("Debug Depth Near")]
        [Min(0.0f)]
        public float debugDepthNear = 0.0f;

        [InspectorName("Debug Depth Far")]
        [Min(0.0001f)]
        public float debugDepthFar = 25.0f;

        [InspectorName("Custom Channel Names")]
        public string[] customChannelNames = new string[HoAovCustomChannels.DefaultCount];

        [InspectorName("Custom Channel Colors")]
        public Color[] customChannelColors = new Color[HoAovCustomChannels.DefaultCount];

        public int ClampedCustomChannelCount => Mathf.Clamp(customChannelCount, 0, HoAovCustomChannels.MaxSupportedCount);

        public void ClampCustomChannels()
        {
            customChannelCount = ClampedCustomChannelCount;
            ResizeArray(ref customChannelNames);
            ResizeArray(ref customChannelColors);
        }

        private static void ResizeArray<T>(ref T[] values)
        {
            if (values == null)
            {
                values = new T[HoAovCustomChannels.DefaultCount];
                return;
            }

            if (values.Length != HoAovCustomChannels.DefaultCount)
            {
                Array.Resize(ref values, HoAovCustomChannels.DefaultCount);
            }
        }
    }

    public enum HoMetadataBufferRenderScale
    {
        [InspectorName("Full")]
        Full = 1,
        [InspectorName("Half")]
        Half = 2,
        [InspectorName("Quarter")]
        Quarter = 4
    }
}
