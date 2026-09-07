#pragma warning disable CS0618, CS0672

using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.GTAO
{
    // Frame-local publication of the public AO semantic. Consumers such as
    // DebugTile must depend on this handle, rather than guessing a global
    // texture binding after RenderGraph compilation.
    internal sealed class HoGTAORenderGraphResources : ContextItem
    {
        public TextureHandle aoTexture = TextureHandle.nullHandle;

        public bool HasAO => aoTexture.IsValid();

        public override void Reset()
        {
            aoTexture = TextureHandle.nullHandle;
        }
    }
}
