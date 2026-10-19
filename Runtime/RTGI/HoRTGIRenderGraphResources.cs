using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.RTGI
{
    // Linear HDR, including the reference diffuse receiver BRDF. A = receiver valid.
    public sealed class HoRTGIRenderGraphResources : ContextItem
    {
        public TextureHandle diffuse = TextureHandle.nullHandle;
        public TextureHandle rawDiffuse = TextureHandle.nullHandle;
        public override void Reset() {diffuse=rawDiffuse=TextureHandle.nullHandle;}
    }
}
