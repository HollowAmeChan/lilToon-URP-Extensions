using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal readonly struct ImageProcessPassContext
    {
        public readonly RenderGraph RenderGraph;
        public readonly TextureHandle Read;
        public readonly TextureHandle Write;
        public readonly int LayerIndex;

        public ImageProcessPassContext(
            RenderGraph renderGraph,
            TextureHandle read,
            TextureHandle write,
            int layerIndex)
        {
            RenderGraph = renderGraph;
            Read = read;
            Write = write;
            LayerIndex = layerIndex;
        }
    }
}
