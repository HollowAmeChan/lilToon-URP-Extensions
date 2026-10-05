using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace lilToon.URP.Extensions.GeometryData
{
    public static class HoTensionDataRegistry
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Entry { public uint offset, count, valid, reserved; }
        internal sealed class Item
        {
            internal HoGeometryDataTension producer;
            internal uint slot;
            internal int offset, version;
            internal SkinnedMeshRenderer renderer;
        }
        private static readonly List<HoGeometryDataTension> Producers = new List<HoGeometryDataTension>();
        private static readonly List<Item> Items = new List<Item>();
        private static readonly HashSet<HoGeometryDataTension> Bound = new HashSet<HoGeometryDataTension>();
        private static readonly Dictionary<HoGeometryDataTension, (SkinnedMeshRenderer renderer, Mesh mesh, bool valid, int version)> Snapshot
            = new Dictionary<HoGeometryDataTension, (SkinnedMeshRenderer, Mesh, bool, int)>();
        private static GraphicsBuffer entries, values;
        private static Entry[] rows;
        private static bool dirty = true;
        private static int bindingRevision = -1;
        public static readonly int EntriesId = Shader.PropertyToID("_HoGDTensionEntries");
        public static readonly int ValuesId = Shader.PropertyToID("_HoGDTensionValues");
        public static readonly int CountId = Shader.PropertyToID("_HoGDTensionEntryCount");
        public static readonly int AvailableId = Shader.PropertyToID("_HoGDTensionAvailable");
        public static int EntryCount => rows?.Length ?? 0;
        public static int ProducerCount => Producers.Count;
        public static int BoundSourceCount => entries != null && entries.IsValid() ? Items.Count : 0;
        public static int PublishedVertexCount => values != null && values.IsValid() ? values.count - 1 : 0;
        public static int ValidSourceCount
        {
            get
            {
                if (dirty || entries == null || !entries.IsValid() || rows == null) return 0;
                int count = 0;
                foreach (var item in Items)
                    if (item.slot < rows.Length && rows[item.slot].valid != 0 && item.producer != null && item.producer.isActiveAndEnabled
                        && item.producer.HasData && item.producer.ProducedFrame >= 0) count++;
                return count;
            }
        }
        public static bool TryGetBuffers(out GraphicsBuffer entryBuffer, out GraphicsBuffer valueBuffer)
        {
            entryBuffer = entries; valueBuffer = values;
            return entries != null && entries.IsValid() && values != null && values.IsValid();
        }
        public static bool IsValid(HoGeometryDataTension producer)
        {
            if (producer == null || !producer.isActiveAndEnabled || !producer.HasData || dirty || entries == null || !entries.IsValid()) return false;
            foreach (var item in Items)
                if (item.producer == producer && item.renderer == producer.targetRenderer && item.version == producer.ReferenceVersion)
                    return rows != null && item.slot < rows.Length && rows[item.slot].valid != 0 && producer.ProducedFrame >= 0;
            return false;
        }
        internal static void Register(HoGeometryDataTension producer)
        {
            if (!Producers.Contains(producer)) Producers.Add(producer);
            // The GD slot must exist when Unity culls/captures this Renderer, not only when passes are recorded.
            if (producer.isActiveAndEnabled && producer.HasData && HoGeometryRendererBinding.Acquire(producer.targetRenderer, producer) != 0) Bound.Add(producer);
            else { HoGeometryRendererBinding.Release(producer); Bound.Remove(producer); }
            dirty = true;
        }
        internal static void Unregister(HoGeometryDataTension producer)
        {
            Producers.Remove(producer); Bound.Remove(producer); HoGeometryRendererBinding.Release(producer); dirty = true;
        }
        public static void MarkDirty() => dirty = true;
        internal static IReadOnlyList<Item> Capture(out GraphicsBuffer entryBuffer, out GraphicsBuffer valueBuffer)
        {
            Producers.RemoveAll(p => p == null);
            foreach (var p in Producers)
                if (!Snapshot.TryGetValue(p, out var state) || p.targetRenderer != state.renderer || p.SourceMesh != state.mesh
                    || p.HasData != state.valid || p.ReferenceVersion != state.version) dirty = true;
            foreach (var item in Items)
                if (item.producer == null || !item.producer.isActiveAndEnabled || !item.producer.HasData
                    || item.producer.ReferenceVersion != item.version || item.producer.targetRenderer != item.renderer) dirty = true;
            if (dirty || entries == null || !entries.IsValid() || values == null || !values.IsValid()) Rebuild();
            if (bindingRevision != HoGeometryRendererBinding.Revision) RebuildEntries();
            entryBuffer = entries; valueBuffer = values; return Items;
        }
        private static void Rebuild()
        {
            var multiplicity = new Dictionary<SkinnedMeshRenderer, int>();
            Snapshot.Clear();
            foreach (var p in Producers)
            {
                if (p != null) Snapshot[p] = (p.targetRenderer, p.SourceMesh, p.HasData, p.ReferenceVersion);
                if (p != null && p.isActiveAndEnabled && p.targetRenderer != null)
                    multiplicity[p.targetRenderer] = multiplicity.TryGetValue(p.targetRenderer, out int count) ? count + 1 : 1;
            }
            var accepted = new HashSet<HoGeometryDataTension>();
            foreach (var p in Producers)
            {
                if (p == null || !p.isActiveAndEnabled || p.targetRenderer == null || !p.HasData) continue;
                if (multiplicity[p.targetRenderer] != 1) { p.SetStatus("同一 Renderer 有多个张力组件，无法发布专用来源。"); continue; }
                accepted.Add(p);
            }
            foreach (var p in Bound) if (!accepted.Contains(p)) HoGeometryRendererBinding.Release(p);
            Bound.Clear(); Items.Clear();
            int countTotal = 1;
            foreach (var p in Producers)
            {
                if (!accepted.Contains(p)) continue;
                uint slot = HoGeometryRendererBinding.Acquire(p.targetRenderer, p);
                if (slot == 0) { p.SetStatus("无法分配 GeometryData Renderer 槽。"); continue; }
                Bound.Add(p); p.ProducedFrame = -1;
                Items.Add(new Item { producer = p, renderer = p.targetRenderer, slot = slot, offset = countTotal, version = p.ReferenceVersion });
                countTotal = checked(countTotal + p.VertexCount);
            }
            values?.Dispose(); values = new GraphicsBuffer(GraphicsBuffer.Target.Structured, countTotal, 16) { name = "Ho-GD Tension Values" };
            // Initialize every row, including invalid/unproduced ranges, for deterministic missing-source behavior.
            values.SetData(new Vector4[countTotal]); RebuildEntries(); dirty = false;
        }
        private static void RebuildEntries()
        {
            var previous = rows;
            rows = new Entry[HoGeometryRendererBinding.SlotCapacity];
            foreach (var item in Items)
                rows[item.slot] = new Entry { offset = (uint)item.offset, count = (uint)item.producer.VertexCount,
                    valid = !dirty && previous != null && item.slot < previous.Length ? previous[item.slot].valid : 0 };
            entries?.Dispose(); entries = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rows.Length, 16) { name = "Ho-GD Tension Entries" };
            UploadEntries(); bindingRevision = HoGeometryRendererBinding.Revision;
        }
        internal static void SetValid(Item item, bool valid) => rows[item.slot].valid = valid ? 1u : 0u;
        internal static void UploadEntries() => entries.SetData(rows);
        public static void ReleaseResources()
        {
            entries?.Dispose(); values?.Dispose(); entries = null; values = null; rows = null; dirty = true;
            foreach (var p in Producers) if (p != null) p.ReleaseGpu();
            Shader.SetGlobalFloat(AvailableId, 0);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => ReleaseResources();
    }
}
