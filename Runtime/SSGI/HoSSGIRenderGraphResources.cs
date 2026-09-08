#pragma warning disable CS0618, CS0672

using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.SSGI
{
    internal sealed class HoSSGIRenderGraphResources : ContextItem
    {
        public TextureHandle giTexture = TextureHandle.nullHandle;
        public TextureHandle rawGiTexture = TextureHandle.nullHandle;
        public TextureHandle sourceTexture = TextureHandle.nullHandle;

        public bool HasGI => giTexture.IsValid();

        public override void Reset()
        {
            giTexture = TextureHandle.nullHandle;
            rawGiTexture = TextureHandle.nullHandle;
            sourceTexture = TextureHandle.nullHandle;
        }
    }
}
