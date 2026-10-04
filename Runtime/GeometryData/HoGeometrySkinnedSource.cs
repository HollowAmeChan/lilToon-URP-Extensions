using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.GeometryData
{
    /// <summary>
    /// Borrowed native skin output for geometry producers. Acquisition must happen after native skinning.
    /// Validated baseline: Unity 6000.3 / D3D11, float32 position first in the skin output.
    /// </summary>
    public sealed class HoGeometrySkinnedSample : IDisposable
    {
        public GraphicsBuffer Buffer { get; private set; }
        public int VertexCount { get; }
        public int PositionStride { get; }
        public int PositionOffset => 0;
        public Matrix4x4 ToRendererLocal { get; }
        internal HoGeometrySkinnedSample(SkinnedMeshRenderer renderer, GraphicsBuffer buffer)
        {
            Buffer = buffer;
            VertexCount = renderer.sharedMesh.vertexCount;
            PositionStride = buffer.stride;
            // Native positions include scale but use the root bone's position/rotation frame.
            // Applying rootBone.localToWorldMatrix would apply its scale twice.
            Transform root = renderer.rootBone;
            ToRendererLocal = renderer.transform.worldToLocalMatrix
                * Matrix4x4.TRS(root.position, root.rotation, Vector3.one);
        }
        public void Dispose() { Buffer?.Dispose(); Buffer = null; }
    }

    public static class HoGeometrySkinnedSource
    {
        public static void Prepare(SkinnedMeshRenderer renderer)
        {
            if (renderer != null) renderer.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
        }

        public static bool TryAcquire(SkinnedMeshRenderer renderer,
            out HoGeometrySkinnedSample sample, out string reason)
        {
            sample = null;
            if (renderer == null || renderer.sharedMesh == null)
            { reason = "未提供蒙皮来源。"; return false; }
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
            { reason = "当前蒙皮来源布局只验证了 D3D11。"; return false; }
            Mesh mesh = renderer.sharedMesh;
            if (renderer.rootBone == null)
            { reason = "当前来源需要指定 rootBone。"; return false; }
            if (!mesh.HasVertexAttribute(VertexAttribute.Position)
                || mesh.GetVertexAttributeFormat(VertexAttribute.Position) != VertexAttributeFormat.Float32
                || mesh.GetVertexAttributeDimension(VertexAttribute.Position) != 3
                || mesh.GetVertexAttributeOffset(VertexAttribute.Position) != 0
                || mesh.GetVertexAttributeStream(VertexAttribute.Position) != 0)
            { reason = "当前来源需要 stream 0 / offset 0 的 float32 三维位置。"; return false; }
            if ((renderer.vertexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
            { reason = "请在蒙皮更新前调用 Prepare，请求 Raw 访问。"; return false; }
            GraphicsBuffer buffer = renderer.GetVertexBuffer();
            if (buffer == null || !buffer.IsValid())
            { buffer?.Dispose(); reason = "当前蒙皮 GPU 缓冲尚未就绪。"; return false; }
            if (buffer.stride < 12 || (buffer.stride & 3) != 0)
            { buffer.Dispose(); reason = "蒙皮输出步长不在已支持范围内。"; return false; }
            sample = new HoGeometrySkinnedSample(renderer, buffer);
            reason = null; return true;
        }
    }
}
