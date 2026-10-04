using System;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEngine;

namespace lilToon.URP.Extensions.GeometryData
{
    /// <summary>Identity-to-frame adapter. ReferenceFrame itself has no OB dependency.</summary>
    public static class HoObjectReferenceFrameBinding
    {
        public static HoGeometryReferenceFrame Find(HoObjectBufferGroup group)
        {
            if (group == null) return null;
            return group.geometryReferenceFrame != null ? group.geometryReferenceFrame
                : group.GetComponent<HoGeometryReferenceFrame>();
        }

        public static bool TryGetFrame(HoObjectBufferGroup group, string partName, out HoGeometryFrameData data)
        {
            data = default;
            if (group == null || !group.isActiveAndEnabled) return false;
            HoGeometryReferenceFrame frame = Find(group);
            return frame != null && frame.TryGetFrame(partName, out data);
        }
    }

    /// <summary>Shared GPU publication of dynamic frames; no per-camera angle results.</summary>
    public sealed class HoGeometryObjectFrameBuffer : IDisposable
    {
        public const int Capacity = 256;
        public const int Stride = 64;
        public static readonly int BufferId = Shader.PropertyToID("_HoGDObjectFrames");
        public static readonly int ViewPositionId = Shader.PropertyToID("_HoGDObserverPosition");
        private static readonly HoGeometryFrameData[] Rows = new HoGeometryFrameData[Capacity];
        private static readonly HoGeometryFrameData[] Pending = new HoGeometryFrameData[Capacity];
        private static GraphicsBuffer buffer;
        private static int leases;
        private bool disposed;
        public static int UploadRevision { get; private set; }

        public HoGeometryObjectFrameBuffer() { leases++; }
        public GraphicsBuffer Capture()
        {
            if (disposed) throw new ObjectDisposedException(nameof(HoGeometryObjectFrameBuffer));
            HoObjectBufferRegistry.EnsureBuilt();
            Array.Clear(Pending, 0, Capacity);
            var groups = HoObjectBufferGroup.GetActiveGroups();
            for (int i = 0; i < groups.Count; i++)
            {
                HoObjectBufferGroup group = groups[i];
                if (group != null && group.groupId > 0 && group.groupId < Capacity
                    && HoObjectReferenceFrameBinding.TryGetFrame(group, null, out HoGeometryFrameData data))
                    Pending[group.groupId] = data;
            }
            bool changed = buffer == null || !buffer.IsValid();
            for (int i = 0; !changed && i < Capacity; i++) changed = !Pending[i].Equals(Rows[i]);
            if (buffer == null || !buffer.IsValid())
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Capacity, Stride) { name = "Ho-GD Object Frames" };
            if (changed)
            {
                Array.Copy(Pending, Rows, Capacity);
                buffer.SetData(Rows);
                UploadRevision++;
            }
            return buffer;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (--leases == 0)
            {
                ReleaseResources();
            }
        }

        // Releasing allocations does not invalidate live consumer leases.
        public static void ReleaseResources()
        {
            buffer?.Dispose(); buffer = null;
            Array.Clear(Rows, 0, Capacity);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources() => ReleaseResources();
    }
}
