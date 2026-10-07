#pragma warning disable CS0618, CS0672
using System.Collections.Generic;
using lilToon.URP.Extensions.Debugging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GeometryData
{
    internal sealed class HoGeometryDataDebugPass : HoDebugRenderPass
    {
        private static readonly List<ShaderTagId> Tags = new List<ShaderTagId>
        {
            new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly")
        };
        private static readonly int ModeId = Shader.PropertyToID("_HoGDDebugMode");
        private static readonly int RangeId = Shader.PropertyToID("_HoGDDebugMaxValue");
        private HoGeometryDataDebugSettings settings;
        private Material material;
        private Material[] allMaterials;
        private RTHandle cameraColor, depth;
        private sealed class Data
        {
            public RendererListHandle[] renderers;
            public Vector2Int size;
            public bool all;
            public BufferHandle outlineEntries, outlineValues, tensionEntries, tensionValues;
            public Material material;
            public int mode;
            public int outlineCount, tensionCount;
            public float range;
        }
        internal void Setup(HoGeometryDataDebugSettings configuration, Material debugMaterial)
        {
            settings = configuration; material = debugMaterial; renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }
        internal void SetupCompatibility(RTHandle color) => cameraColor = color;
        internal void Dispose()
        {
            ReleaseDebugResources(); depth?.Release(); depth = null; cameraColor = null;
            if (allMaterials != null) foreach (Material value in allMaterials) CoreUtils.Destroy(value);
            allMaterials = null;
        }
        private void PrepareMaterials()
        {
            if (allMaterials == null || allMaterials[0].shader != material.shader)
            {
                if (allMaterials != null) foreach (Material value in allMaterials) CoreUtils.Destroy(value);
                allMaterials = new Material[HoGeometryDataDebugViewInfo.Views.Length];
                for (int i = 0; i < allMaterials.Length; i++) allMaterials[i] = CoreUtils.CreateEngineMaterial(material.shader);
            }
            for (int i = 0; i < allMaterials.Length; i++)
            {
                allMaterials[i].SetFloat(ModeId, HoGeometryDataDebugViewInfo.Views[i].ModeValue);
                allMaterials[i].SetFloat(RangeId, Range);
            }
        }
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
            color = CreateDebugTarget(graph, frameData);
            bool all = settings.debugMode == HoGeometryDataDebugMode.All;
            if (all) PrepareMaterials();
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
                d.all = all; d.size = DebugSize(graph, color);
                d.renderers = new RendererListHandle[all ? allMaterials.Length : 1];
                for (int i = 0; i < d.renderers.Length; i++)
                {
                    drawing.overrideMaterial = all ? allMaterials[i] : material;
                    d.renderers[i] = graph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, filtering));
                    builder.UseRendererList(d.renderers[i]);
                }
                d.outlineEntries = graph.ImportBuffer(outlineEntries); d.outlineValues = graph.ImportBuffer(outlineValues);
                d.tensionEntries = graph.ImportBuffer(tensionEntries); d.tensionValues = graph.ImportBuffer(tensionValues);
                d.material = material; d.mode = (int)settings.debugMode; d.range = Range;
                d.outlineCount = HoOutlineDataRegistry.EntryCount; d.tensionCount = HoTensionDataRegistry.EntryCount;
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
                    for (int i = 0; i < data.renderers.Length; i++)
                    {
                        Vector4 rect = HoDebugGrid.Rect(i, data.renderers.Length);
                        context.cmd.SetViewport(new Rect(rect.x * data.size.x, (1 - rect.y - rect.w) * data.size.y, rect.z * data.size.x, rect.w * data.size.y));
                        context.cmd.DrawRendererList(data.renderers[i]);
                        if (data.all) HoDebugGrid.DrawLabel(context.cmd, HoGeometryDataDebugViewInfo.Views[i].ShortName, data.renderers.Length);
                    }
                    context.cmd.SetViewport(new Rect(0, 0, data.size.x, data.size.y));
                });
            }
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            base.OnCameraSetup(cmd, ref renderingData);
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.bindMS = false;
            desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
            desc.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.D32_SFloat;
            RenderingUtils.ReAllocateIfNeeded(ref depth, desc, name: "Ho-GD Debug Depth");
            ConfigureTarget(DebugTarget, depth); ConfigureClear(ClearFlag.All, Color.black);
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            material.SetFloat(ModeId, (int)settings.debugMode); material.SetFloat(RangeId, Range);
            var drawing = CreateDrawingSettings(Tags, ref renderingData, SortingCriteria.CommonOpaque);
            drawing.enableDynamicBatching = false;
            drawing.overrideMaterial = material; drawing.overrideMaterialPassIndex = 0;
            var filtering = new FilteringSettings(RenderQueueRange.all, settings.debugLayerMask);
            bool all = settings.debugMode == HoGeometryDataDebugMode.All;
            if (all) PrepareMaterials();
            int count = all ? allMaterials.Length : 1;
            var cmd = CommandBufferPool.Get("Ho-GD Data Preview");
            for (int i = 0; i < count; i++)
            {
                Vector4 rect = HoDebugGrid.Rect(i, count);
                cmd.SetViewport(new Rect(rect.x * DebugTarget.rt.width, (1 - rect.y - rect.w) * DebugTarget.rt.height, rect.z * DebugTarget.rt.width, rect.w * DebugTarget.rt.height));
                context.ExecuteCommandBuffer(cmd); cmd.Clear();
                drawing.overrideMaterial = all ? allMaterials[i] : material;
                context.DrawRenderers(renderingData.cullResults, ref drawing, ref filtering);
                if (all) HoDebugGrid.DrawLabel(CommandBufferHelpers.GetRasterCommandBuffer(cmd), HoGeometryDataDebugViewInfo.Views[i].ShortName, count);
                context.ExecuteCommandBuffer(cmd); cmd.Clear();
            }
            cmd.SetViewport(new Rect(0, 0, DebugTarget.rt.width, DebugTarget.rt.height));
            context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            PublishCompatibility(renderingData.cameraData.camera);
        }
    }
}
