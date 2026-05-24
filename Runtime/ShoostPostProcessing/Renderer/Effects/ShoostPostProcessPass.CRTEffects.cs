using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
#pragma warning disable CS0618, CS0672

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed partial class ShoostPostProcessPass
    {
        private static void ApplyCRTEffectsLayer(
            CommandBuffer cmd,
            RTHandle source,
            RTHandle destination,
            ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ApplySinglePassLayer(cmd, source, destination, runtimeLayer);
        }

        private TextureHandle RecordCRTEffectsLayer(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            ShoostPostProcessRuntimeLayer runtimeLayer,
            int layerIndex)
        {
            return RecordSinglePassLayer(renderGraph, source, destination, runtimeLayer, layerIndex);
        }
    }
}

