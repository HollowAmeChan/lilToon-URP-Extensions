#pragma warning disable CS0618, CS0672
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GeometryData
{
    internal sealed class HoGeometryDataDebugPass : ScriptableRenderPass
    {
        private static readonly List<ShaderTagId> Tags = new List<ShaderTagId>
        {
            new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly")
        };
        private static readonly int ModeId = Shader.PropertyToID("_HoGDDebugMode");
        private static readonly int RangeId = Shader.PropertyToID("_HoGDDebugMaxValue");
        private HoGeometryDataSettings settings;
        private Material material;
        private RTHandle cameraColor, depth;
        private sealed class Data
        {
            public RendererListHandle renderers;
            public BufferHandle outlineEntries, outlineValues, tensionEntries, tensionValues;
            public Material material;
            public int mode;
            public int outlineCount, tensionCount;
            public float range;
        }
        internal void Setup(HoGeometryDataSettings configuration, Material debugMaterial)
        {
            settings = configuration; material = debugMaterial; renderPassEvent = settings.debugPassEvent;
        }
        internal void SetupCompatibility(RTHandle color) => cameraColor = color;
        internal void Dispose() { depth?.Release(); depth = null; cameraColor = null; }
        private float Range => float.IsNaN(settings.debugMaxValue) || float.IsInfinity(settings.debugMaxValue)
            ? 0.2f : Mathf.Max(0.0001f, settings.debugMaxValue);
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var lightData = frameData.Get<UniversalLightData>();
            var color = resourceData.activeColorTexture;
            if (!color.IsValid() || !HoOutlineDataRegistry.TryGetBuffers(out var outlineEntries, out var outlineValues)
                || !HoTensionDataRegistry.TryGetBuffers(out var tensionEntries, out var tensionValues)) return;
            // Debug geometry has its own depth so previewing the data does not depend on the material's ZWrite/alpha/outline.
            var desc = graph.GetTextureDesc(color); desc.name = "Ho-GD Debug Depth";
            desc.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
            desc.depthBufferBits = DepthBits.Depth32; desc.clearBuffer = true;
            var debugDepth = graph.CreateTexture(desc);
            var drawing = RenderingUtils.CreateDrawingSettings(Tags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
            drawing.enableDynamicBatching = false;
            drawing.overrideMaterial = material; drawing.overrideMaterialPassIndex = 0;
            var filtering = new FilteringSettings(RenderQueueRange.all, settings.debugLayerMask);
            using (var builder = graph.AddRasterRenderPass<Data>("Ho-GD Data Preview", out var d))
            {
                d.renderers = graph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, filtering));
                d.outlineEntries = graph.ImportBuffer(outlineEntries); d.outlineValues = graph.ImportBuffer(outlineValues);
                d.tensionEntries = graph.ImportBuffer(tensionEntries); d.tensionValues = graph.ImportBuffer(tensionValues);
                d.material = material; d.mode = (int)settings.debugMode; d.range = Range;
                d.outlineCount = HoOutlineDataRegistry.EntryCount; d.tensionCount = HoTensionDataRegistry.EntryCount;
                builder.UseRendererList(d.renderers);
                builder.UseBuffer(d.outlineEntries, AccessFlags.Read); builder.UseBuffer(d.outlineValues, AccessFlags.Read);
                builder.UseBuffer(d.tensionEntries, AccessFlags.Read); builder.UseBuffer(d.tensionValues, AccessFlags.Read);
                builder.SetRenderAttachment(color, 0, AccessFlags.WriteAll); builder.SetRenderAttachmentDepth(debugDepth, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (Data data, RasterGraphContext context) =>
                {
                    data.material.SetFloat(ModeId, data.mode); data.material.SetFloat(RangeId, data.range);
                    context.cmd.SetGlobalBuffer(HoOutlineDataRegistry.EntriesId, data.outlineEntries);
                    context.cmd.SetGlobalBuffer(HoOutlineDataRegistry.ValuesId, data.outlineValues);
                    context.cmd.SetGlobalBuffer(HoTensionDataRegistry.EntriesId, data.tensionEntries);
                    context.cmd.SetGlobalBuffer(HoTensionDataRegistry.ValuesId, data.tensionValues);
                    context.cmd.SetGlobalInt(HoOutlineDataRegistry.CountId, data.outlineCount);
                    context.cmd.SetGlobalInt(HoTensionDataRegistry.CountId, data.tensionCount);
                    context.cmd.SetGlobalFloat(HoOutlineDataRegistry.AvailableId, 1);
                    context.cmd.SetGlobalFloat(HoTensionDataRegistry.AvailableId, 1);
                    context.cmd.ClearRenderTarget(RTClearFlags.ColorDepth, Color.black, 1, 0);
                    context.cmd.DrawRendererList(data.renderers);
                });
            }
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
            desc.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.D32_SFloat;
            RenderingUtils.ReAllocateIfNeeded(ref depth, desc, name: "Ho-GD Debug Depth");
            ConfigureTarget(cameraColor, depth); ConfigureClear(ClearFlag.All, Color.black);
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            material.SetFloat(ModeId, (int)settings.debugMode); material.SetFloat(RangeId, Range);
            var drawing = CreateDrawingSettings(Tags, ref renderingData, SortingCriteria.CommonOpaque);
            drawing.enableDynamicBatching = false;
            drawing.overrideMaterial = material; drawing.overrideMaterialPassIndex = 0;
            var filtering = new FilteringSettings(RenderQueueRange.all, settings.debugLayerMask);
            context.DrawRenderers(renderingData.cullResults, ref drawing, ref filtering);
        }
    }
}
