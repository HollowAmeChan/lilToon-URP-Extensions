using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GeometryData
{
    internal sealed class HoGeometryTensionPass : ScriptableRenderPass
    {
        private readonly List<HoGeometrySkinnedSample> borrowed = new List<HoGeometrySkinnedSample>();
        private ComputeShader compute;
        private int extractKernel, triangleKernel, gatherKernel;
        private sealed class ComputeData
        {
            internal BufferHandle source, rest, restMetrics, triangles, neighborOffsets, neighbors, cornerOffsets, corners, positions, triangleMetrics, output;
            internal ComputeShader shader;
            internal int extract, triangle, gather, vertices, faces, stride, offset, frame;
            internal float lengthEpsilon, areaEpsilon;
            internal Matrix4x4 matrix;
            internal Vector3 weights;
            internal HoGeometryDataTension producer;
        }
        private sealed class PublishData { internal BufferHandle entries, values; internal int count; }
        internal HoGeometryTensionPass() { renderPassEvent = RenderPassEvent.BeforeRendering; }
        internal void ReleaseBorrowed() { foreach (var sample in borrowed) sample.Dispose(); borrowed.Clear(); }
        private bool LoadCompute()
        {
            if (compute != null) return true;
            if (!SystemInfo.supportsComputeShaders) return false;
            compute = Resources.Load<ComputeShader>("HoGeometryDataTension");
            if (compute == null) return false;
            extractKernel = compute.FindKernel("ExtractPositions"); triangleKernel = compute.FindKernel("TriangleMetrics"); gatherKernel = compute.FindKernel("VertexGather");
            return true;
        }
        private bool Acquire(HoTensionDataRegistry.Item item, out HoGeometrySkinnedSample sample)
        {
            sample = null; var p = item.producer;
            if (p.targetRenderer == null || !p.targetRenderer.enabled || !p.targetRenderer.gameObject.activeInHierarchy)
            { p.SetStatus("目标 Renderer 未启用。"); HoTensionDataRegistry.SetValid(item, false); return false; }
            if (!LoadCompute()) { p.SetStatus("张力 Compute Shader 不可用。"); HoTensionDataRegistry.SetValid(item, false); return false; }
            if (p.ProducedFrame == Time.frameCount && p.ProducedWeights == p.Weights)
            { HoTensionDataRegistry.SetValid(item, true); return false; }
            HoGeometrySkinnedSource.Prepare(p.targetRenderer);
            if (!HoGeometrySkinnedSource.TryAcquire(p.targetRenderer, out sample, out string reason))
            { p.SetStatus(reason); HoTensionDataRegistry.SetValid(item, false); return false; }
            borrowed.Add(sample); HoTensionDataRegistry.SetValid(item, true); return true;
        }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var items = HoTensionDataRegistry.Capture(out var entries, out var values);
            foreach (var item in items)
            {
                if (!Acquire(item, out var sample)) continue;
                var p = item.producer; var r = p.EnsureResources();
                using (var builder = graph.AddComputePass<ComputeData>("Ho-GD Tension Produce", out var d))
                {
                    d.source = graph.ImportBuffer(sample.Buffer); d.rest = graph.ImportBuffer(r.rest); d.restMetrics = graph.ImportBuffer(r.metrics);
                    d.triangles = graph.ImportBuffer(r.triangles); d.neighborOffsets = graph.ImportBuffer(r.neighborOffsets); d.neighbors = graph.ImportBuffer(r.neighbors);
                    d.cornerOffsets = graph.ImportBuffer(r.cornerOffsets); d.corners = graph.ImportBuffer(r.corners);
                    d.positions = graph.ImportBuffer(r.positions); d.triangleMetrics = graph.ImportBuffer(r.triangleMetrics); d.output = graph.ImportBuffer(values);
                    d.shader = compute; d.extract = extractKernel; d.triangle = triangleKernel; d.gather = gatherKernel;
                    d.vertices = p.VertexCount; d.faces = p.TriangleCount; d.stride = sample.PositionStride; d.offset = item.offset;
                    d.matrix = sample.ToRendererLocal; d.weights = p.Weights; d.lengthEpsilon = r.lengthEpsilon; d.areaEpsilon = r.areaEpsilon;
                    d.producer = p; d.frame = Time.frameCount;
                    builder.UseBuffer(d.source, AccessFlags.Read); builder.UseBuffer(d.rest, AccessFlags.Read); builder.UseBuffer(d.restMetrics, AccessFlags.Read);
                    builder.UseBuffer(d.triangles, AccessFlags.Read); builder.UseBuffer(d.neighborOffsets, AccessFlags.Read); builder.UseBuffer(d.neighbors, AccessFlags.Read);
                    builder.UseBuffer(d.cornerOffsets, AccessFlags.Read); builder.UseBuffer(d.corners, AccessFlags.Read);
                    builder.UseBuffer(d.positions, AccessFlags.ReadWrite); builder.UseBuffer(d.triangleMetrics, AccessFlags.ReadWrite); builder.UseBuffer(d.output, AccessFlags.Write);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (ComputeData data, ComputeGraphContext context) =>
                    {
                        var cmd = context.cmd; var s = data.shader;
                        cmd.SetComputeIntParam(s, "_VertexCount", data.vertices); cmd.SetComputeIntParam(s, "_TriangleCount", data.faces);
                        cmd.SetComputeIntParam(s, "_Stride", data.stride); cmd.SetComputeIntParam(s, "_OutputOffset", data.offset);
                        cmd.SetComputeFloatParam(s, "_LengthEpsilon", data.lengthEpsilon); cmd.SetComputeFloatParam(s, "_AreaEpsilon", data.areaEpsilon);
                        cmd.SetComputeVectorParam(s, "_Weights", data.weights); cmd.SetComputeMatrixParam(s, "_ToRendererLocal", data.matrix);
                        cmd.SetComputeBufferParam(s, data.extract, "_Source", data.source); cmd.SetComputeBufferParam(s, data.extract, "_Positions", data.positions);
                        cmd.DispatchCompute(s, data.extract, (data.vertices + 63) / 64, 1, 1);
                        cmd.SetComputeBufferParam(s, data.triangle, "_Positions", data.positions); cmd.SetComputeBufferParam(s, data.triangle, "_Triangles", data.triangles);
                        cmd.SetComputeBufferParam(s, data.triangle, "_RestMetrics", data.restMetrics); cmd.SetComputeBufferParam(s, data.triangle, "_TriangleMetrics", data.triangleMetrics);
                        cmd.DispatchCompute(s, data.triangle, (data.faces + 63) / 64, 1, 1);
                        cmd.SetComputeBufferParam(s, data.gather, "_Positions", data.positions); cmd.SetComputeBufferParam(s, data.gather, "_RestPositions", data.rest);
                        cmd.SetComputeBufferParam(s, data.gather, "_RestMetrics", data.restMetrics); cmd.SetComputeBufferParam(s, data.gather, "_TriangleMetrics", data.triangleMetrics);
                        cmd.SetComputeBufferParam(s, data.gather, "_NeighborOffsets", data.neighborOffsets); cmd.SetComputeBufferParam(s, data.gather, "_Neighbors", data.neighbors);
                        cmd.SetComputeBufferParam(s, data.gather, "_CornerOffsets", data.cornerOffsets); cmd.SetComputeBufferParam(s, data.gather, "_Corners", data.corners);
                        cmd.SetComputeBufferParam(s, data.gather, "_Tension", data.output); cmd.DispatchCompute(s, data.gather, (data.vertices + 63) / 64, 1, 1);
                        data.producer.ProducedFrame = data.frame; data.producer.ProducedWeights = data.weights; data.producer.ProductionCount++;
                        data.producer.SetStatus($"有效 GPU 数据；参考版本 {data.producer.ReferenceVersion}，帧 {data.frame}");
                    });
                }
            }
            HoTensionDataRegistry.UploadEntries();
            using (var builder = graph.AddRasterRenderPass<PublishData>("Ho-GD Tension Publish", out var d))
            {
                d.entries = graph.ImportBuffer(entries); d.values = graph.ImportBuffer(values); d.count = HoTensionDataRegistry.EntryCount;
                builder.UseBuffer(d.entries, AccessFlags.Read); builder.UseBuffer(d.values, AccessFlags.Read); builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PublishData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalBuffer(HoTensionDataRegistry.EntriesId, data.entries); context.cmd.SetGlobalBuffer(HoTensionDataRegistry.ValuesId, data.values);
                    context.cmd.SetGlobalInt(HoTensionDataRegistry.CountId, data.count); context.cmd.SetGlobalFloat(HoTensionDataRegistry.AvailableId, 1);
                });
            }
        }
#pragma warning disable CS0672, CS0618
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var items = HoTensionDataRegistry.Capture(out var entries, out var values);
            var cmd = CommandBufferPool.Get("Ho-GD Tension");
            foreach (var item in items)
            {
                if (!Acquire(item, out var sample)) continue;
                var p = item.producer; var r = p.EnsureResources();
                cmd.SetComputeIntParam(compute, "_VertexCount", p.VertexCount); cmd.SetComputeIntParam(compute, "_TriangleCount", p.TriangleCount);
                cmd.SetComputeIntParam(compute, "_Stride", sample.PositionStride); cmd.SetComputeIntParam(compute, "_OutputOffset", item.offset);
                cmd.SetComputeFloatParam(compute, "_LengthEpsilon", r.lengthEpsilon); cmd.SetComputeFloatParam(compute, "_AreaEpsilon", r.areaEpsilon);
                cmd.SetComputeVectorParam(compute, "_Weights", p.Weights); cmd.SetComputeMatrixParam(compute, "_ToRendererLocal", sample.ToRendererLocal);
                cmd.SetComputeBufferParam(compute, extractKernel, "_Source", sample.Buffer); cmd.SetComputeBufferParam(compute, extractKernel, "_Positions", r.positions);
                cmd.DispatchCompute(compute, extractKernel, (p.VertexCount + 63) / 64, 1, 1);
                cmd.SetComputeBufferParam(compute, triangleKernel, "_Positions", r.positions); cmd.SetComputeBufferParam(compute, triangleKernel, "_Triangles", r.triangles);
                cmd.SetComputeBufferParam(compute, triangleKernel, "_RestMetrics", r.metrics); cmd.SetComputeBufferParam(compute, triangleKernel, "_TriangleMetrics", r.triangleMetrics);
                cmd.DispatchCompute(compute, triangleKernel, (p.TriangleCount + 63) / 64, 1, 1);
                cmd.SetComputeBufferParam(compute, gatherKernel, "_Positions", r.positions); cmd.SetComputeBufferParam(compute, gatherKernel, "_RestPositions", r.rest);
                cmd.SetComputeBufferParam(compute, gatherKernel, "_RestMetrics", r.metrics); cmd.SetComputeBufferParam(compute, gatherKernel, "_TriangleMetrics", r.triangleMetrics);
                cmd.SetComputeBufferParam(compute, gatherKernel, "_NeighborOffsets", r.neighborOffsets); cmd.SetComputeBufferParam(compute, gatherKernel, "_Neighbors", r.neighbors);
                cmd.SetComputeBufferParam(compute, gatherKernel, "_CornerOffsets", r.cornerOffsets); cmd.SetComputeBufferParam(compute, gatherKernel, "_Corners", r.corners);
                cmd.SetComputeBufferParam(compute, gatherKernel, "_Tension", values); cmd.DispatchCompute(compute, gatherKernel, (p.VertexCount + 63) / 64, 1, 1);
                p.ProducedFrame = Time.frameCount; p.ProducedWeights = p.Weights; p.ProductionCount++;
            }
            HoTensionDataRegistry.UploadEntries();
            cmd.SetGlobalBuffer(HoTensionDataRegistry.EntriesId, entries); cmd.SetGlobalBuffer(HoTensionDataRegistry.ValuesId, values);
            cmd.SetGlobalInt(HoTensionDataRegistry.CountId, HoTensionDataRegistry.EntryCount); cmd.SetGlobalFloat(HoTensionDataRegistry.AvailableId, 1);
            context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
#pragma warning restore CS0672, CS0618
    }
}
