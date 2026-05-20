using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
#pragma warning disable CS0618, CS0672

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed partial class ShoostPostProcessPass
    {
        private static void ApplyLogoOverlayLayer(
            CommandBuffer cmd,
            RTHandle source,
            RTHandle destination,
            ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ApplySinglePassLayer(cmd, source, destination, runtimeLayer);
        }

        private TextureHandle RecordLogoOverlayLayer(
            RenderGraph renderGraph,
            TextureHandle source,
            ShoostPostProcessRuntimeLayer runtimeLayer,
            int layerIndex)
        {
            return RecordSinglePassLayer(renderGraph, source, runtimeLayer, layerIndex);
        }

        private static void ApplyLogoOverlayPropertyDefaults(ref LayerPropertyBlock properties)
        {
            if (Mathf.Approximately(properties.Params12.x, LogoOverlayInitMarker))
            {
                return;
            }

            properties.Params0 = new Vector4(0.5f, 0.5f, 0.2f, 1.0f);
            properties.Params1 = new Vector4(0.5f, 0.5f, 0.2f, 1.0f);
            properties.Params2 = new Vector4(0.5f, 0.5f, 0.2f, 1.0f);
            properties.Params3 = new Vector4(0.5f, 0.5f, 0.2f, 1.0f);
            properties.Params4 = new Vector4(0.5f, 0.5f, 0.2f, 1.0f);
            properties.Params5 = new Vector4(0.5f, 0.5f, 0.2f, 1.0f);
            properties.Params6 = new Vector4(0.5f, 0.5f, 0.2f, 1.0f);
            properties.Params7 = new Vector4(0.5f, 0.5f, 0.2f, 1.0f);
            properties.Params8 = new Vector4(0.0f, 1.0f, 2.0f, 3.0f);
            properties.Params9 = new Vector4(4.0f, 5.0f, 6.0f, 7.0f);
            properties.Params10 = Vector4.one;
            properties.Params11 = Vector4.one;
            properties.Params12 = new Vector4(LogoOverlayInitMarker, 0.0f, 0.0f, 0.0f);
        }
    }
}
