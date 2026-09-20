#pragma warning disable CS0618, CS0672

using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.MetadataBuffer
{
    internal sealed class HoMetadataBufferRenderGraphResources : ContextItem
    {
        public TextureHandle maskIdTexture = TextureHandle.nullHandle;
        public TextureHandle custom0Texture = TextureHandle.nullHandle;
        public TextureHandle objectCustom0Texture = TextureHandle.nullHandle;
        public TextureHandle objectCustom1Texture = TextureHandle.nullHandle;

        public bool HasRequiredTextures => maskIdTexture.IsValid()
            && custom0Texture.IsValid()
            && objectCustom0Texture.IsValid()
            && objectCustom1Texture.IsValid();

        public override void Reset()
        {
            maskIdTexture = TextureHandle.nullHandle;
            custom0Texture = TextureHandle.nullHandle;
            objectCustom0Texture = TextureHandle.nullHandle;
            objectCustom1Texture = TextureHandle.nullHandle;
        }
    }
}
