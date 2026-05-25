#pragma warning disable CS0618, CS0672

using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.GeometryBuffer
{
    internal sealed class HoGeometryBufferRenderGraphResources : ContextItem
    {
        public TextureHandle normalDepthTexture = TextureHandle.nullHandle;
        public TextureHandle depthTexture = TextureHandle.nullHandle;

        public bool HasRequiredTextures => normalDepthTexture.IsValid();

        public override void Reset()
        {
            normalDepthTexture = TextureHandle.nullHandle;
            depthTexture = TextureHandle.nullHandle;
        }
    }
}
