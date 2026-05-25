using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
#pragma warning disable CS0618, CS0672

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed partial class ImageProcessPass
    {
        private static void ApplyRetroLookProOldFilm2CustomLayer(
            CommandBuffer cmd,
            RTHandle source,
            RTHandle destination,
            ImageProcessRuntimeLayer runtimeLayer)
        {
            ApplySinglePassLayer(cmd, source, destination, runtimeLayer);
        }

        private TextureHandle RecordRetroLookProOldFilm2CustomLayer(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            ImageProcessRuntimeLayer runtimeLayer,
            int layerIndex)
        {
            return RecordSinglePassLayer(renderGraph, source, destination, runtimeLayer, layerIndex);
        }
    }
}

