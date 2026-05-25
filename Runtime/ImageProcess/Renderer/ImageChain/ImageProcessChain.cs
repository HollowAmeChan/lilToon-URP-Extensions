using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed class ImageProcessChain
    {
        private TextureHandle current;
        private TextureHandle workA;
        private TextureHandle workB;
        private bool writeToA;

        public TextureHandle Current => current;

        public void Begin(RenderGraph renderGraph, TextureHandle source)
        {
            current = source;
            writeToA = true;

            TextureDesc workDesc = renderGraph.GetTextureDesc(source);
            workDesc.name = "_lilImageProcessWorkA";
            workDesc.clearBuffer = false;
            workDesc.depthBufferBits = 0;
            ImageProcessPass.EnsureImageProcessHdrTextureDesc(ref workDesc);
            workA = renderGraph.CreateTexture(workDesc);

            workDesc.name = "_lilImageProcessWorkB";
            workB = renderGraph.CreateTexture(workDesc);
        }

        public ImageProcessPassContext NextPass(RenderGraph renderGraph, int layerIndex)
        {
            TextureHandle write = writeToA ? workA : workB;
            return new ImageProcessPassContext(renderGraph, current, write, layerIndex);
        }

        public void Commit(TextureHandle result)
        {
            current = result;
            writeToA = !writeToA;
        }
    }
}
