using lilToon.URP.Extensions.OIT;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Shadows
{
    internal sealed class HoTransparentShadowSurfaceData : ContextItem
    {
        internal TextureHandle First { get; set; }
        internal TextureHandle Second { get; set; }
        internal int CameraInstanceId { get; set; }

        public override void Reset()
        {
            First = TextureHandle.nullHandle;
            Second = TextureHandle.nullHandle;
            CameraInstanceId = 0;
        }
    }

    internal sealed class HoTransparentShadowSurfacePass : ScriptableRenderPass
    {
        private static readonly ShaderTagId ReceiverTag = new("HoEyeShadowReceiver");
        private static readonly int OpaqueDepthId = Shader.PropertyToID("_HoShadowReceiverOpaqueDepth");
        private static readonly int PreviousLayerId = Shader.PropertyToID("_HoShadowReceiverPreviousLayer");
        private static readonly int PeelId = Shader.PropertyToID("_HoShadowReceiverPeel");
        private const GraphicsFormat CandidateFormat = GraphicsFormat.R32G32_UInt;

        private sealed class PassData
        {
            internal TextureHandle opaqueDepth;
            internal TextureHandle previous;
            internal RendererListHandle renderers;
            internal bool weightedOit;
            internal float weight, alphaClip;
            internal Vector4 queueRange;
        }

        internal HoTransparentShadowSurfacePass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            if (!SystemInfo.IsFormatSupported(CandidateFormat, GraphicsFormatUsage.Render))
                return;
            var resources = frameData.Get<UniversalResourceData>();
            TextureHandle opaque = resources.cameraDepthTexture;
            if (!opaque.IsValid())
                return;

            var camera = frameData.Get<UniversalCameraData>();
            var filtering = new FilteringSettings(RenderQueueRange.transparent, camera.camera.cullingMask);
            var product = frameData.GetOrCreate<HoTransparentShadowSurfaceData>();
            RecordSurfaces(graph, frameData, opaque, ReceiverTag, filtering, out var first, out var second);
            product.First = first;
            product.Second = second;
            product.CameraInstanceId = camera.camera.GetInstanceID();
        }

        internal static void RecordSurfaces(RenderGraph graph, ContextContainer frameData, TextureHandle opaque,
            ShaderTagId tag, FilteringSettings filtering, out TextureHandle first, out TextureHandle second,
            bool weightedOit = false, float weight = 0, float alphaClip = 0, Vector4 queueRange = default)
        {
            first = second = TextureHandle.nullHandle;
            if (!opaque.IsValid() || !SystemInfo.IsFormatSupported(CandidateFormat, GraphicsFormatUsage.Render))
                return;

            var raster = opaque.GetDescriptor(graph);
            if (raster.msaaSamples != MSAASamples.None || raster.dimension != TextureDimension.Tex2D)
                return;

            var camera = frameData.Get<UniversalCameraData>();
            var rendering = frameData.Get<UniversalRenderingData>();
            var lights = frameData.Get<UniversalLightData>();
            var drawing = RenderingUtils.CreateDrawingSettings(tag, rendering, camera, lights, SortingCriteria.CommonOpaque);
            var renderers = new RendererListParams(rendering.cullResults, drawing, filtering);
            first = RecordLayer(graph, raster, opaque, graph.CreateRendererList(renderers), TextureHandle.nullHandle,
                weightedOit, weight, alphaClip, queueRange);
            second = RecordLayer(graph, raster, opaque, graph.CreateRendererList(renderers), first,
                weightedOit, weight, alphaClip, queueRange);
        }

        private static TextureHandle RecordLayer(RenderGraph graph, TextureDesc raster, TextureHandle opaque,
            RendererListHandle renderers, TextureHandle previous, bool weightedOit, float weight, float alphaClip, Vector4 queueRange)
        {
            bool peel = previous.IsValid();
            string name = (weightedOit ? "Ho OIT Shadow Receiver Layer " : "Ho Eye Shadow Receiver Layer ") + (peel ? 2 : 1);
            var format = new TextureDesc(raster.width, raster.height)
            {
                name = name,
                colorFormat = CandidateFormat,
                clearBuffer = true,
                clearColor = Color.clear,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var depth = new TextureDesc(raster.width, raster.height)
            {
                name = name + " Depth",
                depthBufferBits = DepthBits.Depth32,
                clearBuffer = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            TextureHandle output = graph.CreateTexture(format);
            TextureHandle privateDepth = graph.CreateTexture(depth);
            using var builder = graph.AddRasterRenderPass<PassData>(name, out var pass);
            pass.opaqueDepth = opaque;
            pass.previous = previous;
            pass.renderers = renderers;
            pass.weightedOit = weightedOit;
            pass.weight = weight;
            pass.alphaClip = alphaClip;
            pass.queueRange = queueRange;
            builder.UseTexture(opaque, AccessFlags.Read);
            if (peel) builder.UseTexture(previous, AccessFlags.Read);
            builder.UseRendererList(renderers);
            builder.SetRenderAttachment(output, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(privateDepth, AccessFlags.Write);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                var commands = context.cmd;
                if (data.weightedOit)
                    WeightedOITShaderConstants.SetAccumulationGlobals(commands, true, data.weight, data.alphaClip, data.queueRange);
                commands.SetGlobalTexture(OpaqueDepthId, data.opaqueDepth);
                commands.SetGlobalInt(PeelId, data.previous.IsValid() ? 1 : 0);
                if (data.previous.IsValid()) commands.SetGlobalTexture(PreviousLayerId, data.previous);
                commands.DrawRendererList(data.renderers);
            });
            return output;
        }
    }
}
