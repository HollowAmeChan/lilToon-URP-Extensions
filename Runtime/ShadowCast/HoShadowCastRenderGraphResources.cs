using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.ShadowCast
{
    internal sealed class HoShadowCastRenderGraphResources : ContextItem
    {
        public TextureHandle atlasTexture;

        public override void Reset()
        {
            atlasTexture = TextureHandle.nullHandle;
        }
    }
}
