using lilToon.URP.Extensions.OIT;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Shadows
{
    internal enum HoLayeredShadowScope
    {
        Transparent,
        WeightedOit,
        OpaqueQueue,
    }

    internal sealed class HoTransparentShadowSurfaceData : ContextItem
    {
        internal TextureHandle First { get; set; }
        internal TextureHandle Second { get; set; }
        internal int CameraInstanceId { get; set; }
        internal HoLayeredShadowScope Scope { get; set; }

        public override void Reset()
        {
            First = TextureHandle.nullHandle;
            Second = TextureHandle.nullHandle;
            CameraInstanceId = 0;
            Scope = default;
        }
    }

    internal sealed class HoTransparentShadowSurfacePass : ScriptableRenderPass
    {
        private static readonly ShaderTagId ReceiverTag = new("HoEyeShadowReceiver");
        private static readonly ShaderTagId StandardReceiverTag = new("HoTransparentShadowReceiver");
        private static readonly ShaderTagId EarlyReceiverTag = new("HoEarlyTransparentShadowReceiver");
        private static readonly int OpaqueDepthId = Shader.PropertyToID("_HoShadowReceiverOpaqueDepth");
        private static readonly int PreviousLayerId = Shader.PropertyToID("_HoShadowReceiverPreviousLayer");
        private static readonly int PeelId = Shader.PropertyToID("_HoShadowReceiverPeel");
        private const GraphicsFormat CandidateFormat = GraphicsFormat.R32G32_UInt;
        private readonly HoLayeredShadowScope scope;

        private sealed class PassData
        {
            internal TextureHandle opaqueDepth;
            internal TextureHandle previous;
            internal RendererListHandle renderers;
            internal bool weightedOit;
            internal float weight, alphaClip;
            internal Vector4 queueRange;
        }

        internal HoTransparentShadowSurfacePass(HoLayeredShadowScope scope = HoLayeredShadowScope.Transparent)
        {
            this.scope = scope;
            renderPassEvent = scope == HoLayeredShadowScope.OpaqueQueue
                ? RenderPassEvent.BeforeRenderingOpaques : RenderPassEvent.BeforeRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            TextureHandle opaque = resources.cameraDepthTexture;
            if (!opaque.IsValid())
                return;

            var camera = frameData.Get<UniversalCameraData>();
            ShaderTagId tag = ReceiverTag;
            var filtering = new FilteringSettings(RenderQueueRange.transparent, camera.camera.cullingMask);
            if (scope == HoLayeredShadowScope.OpaqueQueue)
            {
                if (!(camera.renderer is UniversalRenderer renderer)
                    || !renderer.TryGetEarlyTransparentShadowLayerMask(camera, out int layers))
                    return;
                filtering = new FilteringSettings(RenderQueueRange.opaque, layers);
                tag = EarlyReceiverTag;
            }
            var product = frameData.GetOrCreate<HoTransparentShadowSurfaceData>();
            RecordSurfaces(graph, frameData, opaque, tag, filtering, out var first, out var second, scope);
            product.First = first;
            product.Second = second;
            product.CameraInstanceId = camera.camera.GetInstanceID();
            product.Scope = scope;
        }

        internal static void RecordSurfaces(RenderGraph graph, ContextContainer frameData, TextureHandle opaque,
            ShaderTagId tag, FilteringSettings filtering, out TextureHandle first, out TextureHandle second,
            HoLayeredShadowScope scope, float weight = 0, float alphaClip = 0, Vector4 queueRange = default)
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
            if (scope == HoLayeredShadowScope.Transparent)
                drawing.SetShaderPassName(1, StandardReceiverTag);
            var renderers = new RendererListParams(rendering.cullResults, drawing, filtering);
            first = RecordLayer(graph, raster, opaque, graph.CreateRendererList(renderers), TextureHandle.nullHandle,
                scope, weight, alphaClip, queueRange);
            second = RecordLayer(graph, raster, opaque, graph.CreateRendererList(renderers), first,
                scope, weight, alphaClip, queueRange);
        }

        private static TextureHandle RecordLayer(RenderGraph graph, TextureDesc raster, TextureHandle opaque,
            RendererListHandle renderers, TextureHandle previous, HoLayeredShadowScope scope, float weight, float alphaClip, Vector4 queueRange)
        {
            bool peel = previous.IsValid();
            string prefix = scope switch
            {
                HoLayeredShadowScope.OpaqueQueue => "Ho Early Transparent Shadow Receiver Layer ",
                HoLayeredShadowScope.WeightedOit => "Ho OIT Shadow Receiver Layer ",
                _ => "Ho Eye Shadow Receiver Layer ",
            };
            string name = prefix + (peel ? 2 : 1);
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
            pass.weightedOit = scope == HoLayeredShadowScope.WeightedOit;
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
