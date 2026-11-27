using lilToon.URP.Extensions.CharacterShadow;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Shadows
{
    internal sealed class HoCharacterScreenShadowPass : ScriptableRenderPass
    {
        private static readonly int AtlasId = Shader.PropertyToID("_HoCSAtlas");
        private static readonly int CameraDepthId = Shader.PropertyToID("_HoCharacterScreenCameraDepth");
        private static readonly int SurfaceDepthId = Shader.PropertyToID("_HoCharacterScreenSurfaceDepth");
        private static readonly int OwnerId = Shader.PropertyToID("_HoCharacterScreenOwner");
        private static readonly int RasterSizeId = Shader.PropertyToID("_HoCharacterScreenRasterSize");
        private static readonly int SoftId = Shader.PropertyToID("_HoCharacterScreenSoft");
        private const GraphicsFormat OutputFormat = GraphicsFormat.R16G16B16A16_SFloat;

        private readonly Material material;

        private sealed class PassData
        {
            internal Material material;
            internal TextureHandle atlas, cameraDepth, surfaceDepth, owner;
            internal Vector4 rasterSize;
            internal bool soft;
        }

        internal HoCharacterScreenShadowPass(Material material)
        {
            this.material = material;
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            if (!frameData.Contains<HoNativeMainShadowFrameData>()
                || !frameData.Contains<HoCharacterShadowResources>()
                || !frameData.Contains<HoSurfaceBufferRenderGraphResources>()
                || !SystemInfo.IsFormatSupported(OutputFormat, GraphicsFormatUsage.Render | GraphicsFormatUsage.Sample))
                return;

            var product = frameData.Get<HoNativeMainShadowFrameData>();
            var surfaces = frameData.Get<HoSurfaceBufferRenderGraphResources>();
            TextureHandle atlas = frameData.Get<HoCharacterShadowResources>().atlas;
            if (!product.IsValid || !atlas.IsValid() || !surfaces.depthTexture.IsValid() || !surfaces.ownerTexture.IsValid())
                return;

            var desc = product.Visibility.GetDescriptor(graph);
            if (!MatchesRaster(desc, surfaces.depthTexture.GetDescriptor(graph))
                || !MatchesRaster(desc, surfaces.ownerTexture.GetDescriptor(graph)))
                return;

            var lights = frameData.Get<UniversalLightData>();
            var main = lights.visibleLights[lights.mainLightIndex].light;
            desc.name = "Ho Character Screen Shadow Visibility";
            desc.colorFormat = OutputFormat;
            TextureHandle output = graph.CreateTexture(desc);

            using var builder = graph.AddRasterRenderPass<PassData>("Ho Character Screen Shadow Resolve", out var pass);
            pass.material = material;
            pass.atlas = atlas;
            pass.cameraDepth = product.SurfaceDepth;
            pass.surfaceDepth = surfaces.depthTexture;
            pass.owner = surfaces.ownerTexture;
            pass.rasterSize = new Vector4(desc.width, desc.height, 1f / desc.width, 1f / desc.height);
            pass.soft = main.shadows == LightShadows.Soft && frameData.Get<UniversalShadowData>().supportsSoftShadows;
            builder.UseTexture(pass.atlas, AccessFlags.Read);
            builder.UseTexture(pass.cameraDepth, AccessFlags.Read);
            builder.UseTexture(pass.surfaceDepth, AccessFlags.Read);
            builder.UseTexture(pass.owner, AccessFlags.Read);
            builder.SetRenderAttachment(output, 0, AccessFlags.WriteAll);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                var properties = context.renderGraphPool.GetTempMaterialPropertyBlock();
                properties.SetTexture(AtlasId, (Texture)data.atlas);
                properties.SetTexture(CameraDepthId, (Texture)data.cameraDepth);
                properties.SetTexture(SurfaceDepthId, (Texture)data.surfaceDepth);
                properties.SetTexture(OwnerId, (Texture)data.owner);
                properties.SetVector(RasterSizeId, data.rasterSize);
                properties.SetFloat(SoftId, data.soft ? 1 : 0);
                CoreUtils.DrawFullScreen(context.cmd, data.material, properties);
            });

            product.CharacterVisibility = output;
        }

        private static bool MatchesRaster(TextureDesc target, TextureDesc source) =>
            target.width > 0 && target.height > 0 && target.width == source.width && target.height == source.height
            && target.dimension == source.dimension && target.slices == source.slices && source.msaaSamples == MSAASamples.None;
    }
}
