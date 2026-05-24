using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
#pragma warning disable CS0618, CS0672

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed partial class ShoostPostProcessPass
    {
        private static void ApplySharpenBeforeLayer(
            CommandBuffer cmd,
            RTHandle source,
            RTHandle destination,
            ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ApplySinglePassLayer(cmd, source, destination, runtimeLayer);
        }

        private TextureHandle RecordSharpenBeforeLayer(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            ShoostPostProcessRuntimeLayer runtimeLayer,
            int layerIndex)
        {
            return RecordSinglePassLayer(renderGraph, source, destination, runtimeLayer, layerIndex);
        }

        private static void ApplySharpenPropertyDefaults(ref LayerPropertyBlock properties)
        {
            if (properties.Sharpness <= 0.0f)
            {
                properties.Sharpness = 0.2f;
            }
        }
    }
}
