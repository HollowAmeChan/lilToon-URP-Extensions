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
        public TextureHandle cameraSourceTexture = TextureHandle.nullHandle;
        public TextureHandle reservoirColorTexture = TextureHandle.nullHandle;
        public TextureHandle reservoirAuxTexture = TextureHandle.nullHandle;
        public TextureHandle spatialGuidanceTexture = TextureHandle.nullHandle;
        public TextureHandle sampleCountTexture = TextureHandle.nullHandle;
        public TextureHandle invalidityTexture = TextureHandle.nullHandle;
        public TextureHandle temporalTexture = TextureHandle.nullHandle;
        public TextureHandle spatialResolveTexture = TextureHandle.nullHandle;
        public TextureHandle temporalDenoisedTexture = TextureHandle.nullHandle;
        public TextureHandle bilateralFirstTexture = TextureHandle.nullHandle;

        public bool HasGI => giTexture.IsValid();

        public override void Reset()
        {
            giTexture = TextureHandle.nullHandle;
            rawGiTexture = TextureHandle.nullHandle;
            sourceTexture = TextureHandle.nullHandle;
            cameraSourceTexture = TextureHandle.nullHandle;
            reservoirColorTexture = TextureHandle.nullHandle;
            reservoirAuxTexture = TextureHandle.nullHandle;
            spatialGuidanceTexture = TextureHandle.nullHandle;
            sampleCountTexture = TextureHandle.nullHandle;
            invalidityTexture = TextureHandle.nullHandle;
            temporalTexture = TextureHandle.nullHandle;
            spatialResolveTexture = TextureHandle.nullHandle;
            temporalDenoisedTexture = TextureHandle.nullHandle;
            bilateralFirstTexture = TextureHandle.nullHandle;
        }
    }
}
