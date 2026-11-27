using System.Collections.Generic;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTShadow;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Shadows
{
    internal sealed class HoRayTracedScreenShadowFrameData : ContextItem
    {
        private readonly List<(int lightIndex, HoRTShadowProduct source)> sources = new();

        internal TextureHandle Visibility { get; set; }
        internal TextureHandle SurfaceDepth { get; set; }
        internal int CameraInstanceId { get; set; }
        internal int Count => sources.Count;
        internal int GetLightIndex(int slot) => sources[slot].lightIndex;
        internal HoRTShadowProduct GetSource(int slot) => sources[slot].source;
        internal void Add(int lightIndex, HoRTShadowProduct source) => sources.Add((lightIndex, source));

        public override void Reset()
        {
            sources.Clear();
            Visibility = TextureHandle.nullHandle;
            SurfaceDepth = TextureHandle.nullHandle;
            CameraInstanceId = 0;
        }
    }

    internal sealed class HoRayTracedScreenShadowPass : ScriptableRenderPass
    {
        private static readonly int RawId = Shader.PropertyToID("_HoRayTracedShadowRaw");
        private static readonly int VisibilityId = Shader.PropertyToID("_HoRayTracedShadowVisibility");
        private static readonly int SourceDepthId = Shader.PropertyToID("_HoRayTracedShadowSourceDepth");
        private static readonly int CameraDepthId = Shader.PropertyToID("_HoRayTracedShadowCameraDepth");
        private static readonly int LightStrengthId = Shader.PropertyToID("_HoRayTracedShadowLightStrength");
        private const GraphicsFormat OutputFormat = GraphicsFormat.R16G16B16A16_SFloat;
        internal const int MainLightIndex = -1;

        private readonly Material material;

        private sealed class PassData
        {
            internal Material material;
            internal HoRTShadowProduct source;
            internal TextureHandle depth;
        }

        internal HoRayTracedScreenShadowPass(Material material)
        {
            this.material = material;
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            if (!frameData.Contains<HoRTShadowRenderGraphResources>() || !SystemInfo.supports2DArrayTextures
                || !SystemInfo.IsFormatSupported(OutputFormat, GraphicsFormatUsage.Render | GraphicsFormatUsage.Sample))
                return;

            var resources = frameData.Get<HoRTShadowRenderGraphResources>();
            var camera = frameData.Get<UniversalCameraData>().camera;
            TextureHandle depth = frameData.Get<UniversalResourceData>().cameraDepthTexture;
            if (resources.Camera != camera || !depth.IsValid())
                return;

            var desc = depth.GetDescriptor(graph);
            var lights = frameData.Get<UniversalLightData>();
            var product = frameData.GetOrCreate<HoRayTracedScreenShadowFrameData>();
            if (lights.mainLightIndex >= 0 && lights.mainLightIndex < lights.visibleLights.Length)
                AddCandidate(product, resources, MainLightIndex, lights.visibleLights[lights.mainLightIndex].light, desc);

            if (frameData.Contains<HoAdditionalShadowFrameData>())
            {
                var additional = frameData.Get<HoAdditionalShadowFrameData>();
                for (int slot = 0; slot < additional.Count && product.Count < SystemInfo.maxTextureArraySlices; slot++)
                {
                    int sourceIndex = additional.GetShaderLightIndex(slot);
                    var source = lights.visibleLights[additional.GetVisibleLightIndex(slot)].light;
                    AddCandidate(product, resources, sourceIndex, source, desc);
                }
            }
            if (product.Count == 0)
                return;

            desc.name = "Ho Ray Traced Screen Shadow Candidates";
            desc.depthBufferBits = DepthBits.None;
            desc.colorFormat = OutputFormat;
            desc.dimension = TextureDimension.Tex2DArray;
            desc.slices = product.Count;
            desc.msaaSamples = MSAASamples.None;
            desc.bindTextureMS = false;
            desc.filterMode = FilterMode.Point;
            desc.wrapMode = TextureWrapMode.Clamp;
            desc.clearBuffer = false;

            TextureHandle output = graph.CreateTexture(desc);
            for (int slot = 0; slot < product.Count; slot++)
            {
                using var builder = graph.AddRasterRenderPass<PassData>("Ho Ray Traced Screen Shadow Resolve", out var pass);
                pass.material = material;
                pass.source = product.GetSource(slot);
                pass.depth = depth;
                builder.UseTexture(pass.source.Raw, AccessFlags.Read);
                if (pass.source.Visibility != pass.source.Raw)
                    builder.UseTexture(pass.source.Visibility, AccessFlags.Read);
                builder.UseTexture(pass.source.SourceDepth, AccessFlags.Read);
                builder.UseTexture(depth, AccessFlags.Read);
                builder.SetRenderAttachment(output, 0, AccessFlags.Write, 0, slot);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    var props = context.renderGraphPool.GetTempMaterialPropertyBlock();
                    props.SetTexture(RawId, (Texture)data.source.Raw);
                    props.SetTexture(VisibilityId, (Texture)data.source.Visibility);
                    props.SetTexture(SourceDepthId, (Texture)data.source.SourceDepth);
                    props.SetTexture(CameraDepthId, (Texture)data.depth);
                    props.SetFloat(LightStrengthId, data.source.LightShadowStrength);
                    CoreUtils.DrawFullScreen(context.cmd, data.material, props);
                });
            }

            product.Visibility = output;
            product.SurfaceDepth = depth;
            product.CameraInstanceId = camera.GetInstanceID();
        }

        private static void AddCandidate(HoRayTracedScreenShadowFrameData product,
            HoRTShadowRenderGraphResources resources, int lightIndex, Light light, TextureDesc depth)
        {
            if (!resources.TryGet(HoRTLightIdentity.Get(light), out var source) || source.Light != light
                || source.Status != HoRTShadowProductStatus.Available || source.Width != depth.width || source.Height != depth.height
                || !source.SourceDepth.IsValid() || !source.Raw.IsValid() || !source.Visibility.IsValid())
                return;
            product.Add(lightIndex, source);
        }
    }
}
