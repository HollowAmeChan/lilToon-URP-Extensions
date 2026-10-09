using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
namespace lilToon.URP.Extensions.RTDI
{
    // RGB includes the reference receiver BRDF. A is receiver validity, not visibility.
    public sealed class HoRTDIRenderGraphResources : ContextItem
    {
        public TextureHandle diffuse=TextureHandle.nullHandle,specular=TextureHandle.nullHandle;
        public TextureHandle rawDiffuse=TextureHandle.nullHandle,rawSpecular=TextureHandle.nullHandle;
        public override void Reset() { diffuse=specular=rawDiffuse=rawSpecular=TextureHandle.nullHandle; }
    }
}
