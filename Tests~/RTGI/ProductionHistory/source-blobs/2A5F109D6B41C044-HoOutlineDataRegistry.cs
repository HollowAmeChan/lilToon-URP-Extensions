using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace lilToon.URP.Extensions.GeometryData
{
    public static class HoOutlineDataRegistry
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Entry { public uint offset, count, valid, reserved; }
        // One owner token per Renderer: a component may publish several draws.
        private sealed class Source
        {
            public HoGeometryDataOutlineCorrection producer;
            public Renderer renderer;
            public Vector4[] data;
        }
        private static readonly Dictionary<HoGeometryDataOutlineCorrection, Dictionary<Renderer, Source>> Producers
            = new Dictionary<HoGeometryDataOutlineCorrection, Dictionary<Renderer, Source>>();
        private static readonly HashSet<Source> Bound = new HashSet<Source>();
        private static readonly HashSet<Renderer> WarnedAmbiguous = new HashSet<Renderer>();
        private static GraphicsBuffer entries, values;
        private static bool dirty = true;
        private static int bindingRevision = -1;
        public static readonly int EntriesId = Shader.PropertyToID("_HoGDOutlineEntries");
        public static readonly int ValuesId = Shader.PropertyToID("_HoGDOutlineValues");
        public static readonly int CountId = Shader.PropertyToID("_HoGDOutlineEntryCount");
        public static readonly int AvailableId = Shader.PropertyToID("_HoGDOutlineAvailable");
        public static int EntryCount { get; private set; }
        public static int ProducerCount => Producers.Count;
        public static int PublishedSourceCount => !dirty && entries != null && entries.IsValid() ? Bound.Count : 0;
        public static int PublishedVertexCount => values != null && values.IsValid() ? values.count - 1 : 0;
        public static bool TryGetBuffers(out GraphicsBuffer entryBuffer, out GraphicsBuffer valueBuffer)
        {
            entryBuffer = entries; valueBuffer = values;
            return entries != null && entries.IsValid() && values != null && values.IsValid();
        }
        public static bool IsPublished(HoGeometryDataOutlineCorrection producer) => GetPublishedRendererCount(producer) > 0;

        public static int GetPublishedRendererCount(HoGeometryDataOutlineCorrection producer)
        {
            if (producer == null || !producer.isActiveAndEnabled || dirty || entries == null || !entries.IsValid()) return 0;
            int count = 0;
            foreach (var source in Bound)
                if (source.producer == producer && producer.GetPreparedData(source.renderer) == source.data) count++;
            return count;
        }

        internal static void Register(HoGeometryDataOutlineCorrection producer)
        {
            bool changed = false;
            if (!Producers.ContainsKey(producer))
            {
                Producers.Add(producer, new Dictionary<Renderer, Source>());
                changed = true;
            }
            changed |= SynchronizeSources(producer, Producers[producer]);
            // Unity snapshots draw metadata during camera culling, before AddRenderPasses/RecordRenderGraph.
            if (changed) UpdateBindings();
            dirty |= changed;
        }
        internal static void Unregister(HoGeometryDataOutlineCorrection producer)
        {
            if (!Producers.TryGetValue(producer, out var sources)) return;
            foreach (var source in sources.Values) Release(source);
            Producers.Remove(producer);
            UpdateBindings();
            dirty = true;
        }
        public static void MarkDirty() => dirty = true;

        private static bool SynchronizeSources(HoGeometryDataOutlineCorrection producer, Dictionary<Renderer, Source> sources)
        {
            var targets = new HashSet<Renderer>();
            bool changed = false;
            if (producer != null && producer.isActiveAndEnabled)
            {
                foreach (var renderer in producer.GetTargetRenderers())
                {
                    targets.Add(renderer);
                    var data = producer.GetPreparedData(renderer);
                    if (!sources.TryGetValue(renderer, out var source))
                    {
                        sources.Add(renderer, new Source { producer = producer, renderer = renderer, data = data });
                        changed = true;
                    }
                    else if (source.data != data)
                    {
                        source.data = data;
                        changed = true;
                    }
                }
            }
            var removed = new List<Renderer>();
            foreach (var pair in sources) if (!targets.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (var renderer in removed)
            {
                Release(sources[renderer]);
                sources.Remove(renderer);
                changed = true;
            }
            return changed;
        }

        private static void Release(Source source)
        {
            HoGeometryRendererBinding.Release(source);
            Bound.Remove(source);
        }

        private static void UpdateBindings()
        {
            var multiplicity = new Dictionary<Renderer, int>();
            foreach (var sources in Producers.Values)
                foreach (var source in sources.Values)
                    multiplicity[source.renderer] = multiplicity.TryGetValue(source.renderer, out int count) ? count + 1 : 1;
            foreach (var sources in Producers.Values)
                foreach (var source in sources.Values)
                {
                    if (multiplicity[source.renderer] != 1)
                    {
                        Release(source);
                        if (WarnedAmbiguous.Add(source.renderer))
                            Debug.LogWarning("[Ho-GD] 同一 Renderer 有多个描边修正组件，未发布修正来源。", source.renderer);
                        continue;
                    }
                    WarnedAmbiguous.Remove(source.renderer);
                    if (source.data == null) Release(source);
                    else if (!Bound.Contains(source) && HoGeometryRendererBinding.Acquire(source.renderer, source) != 0)
                        Bound.Add(source);
                }
            WarnedAmbiguous.RemoveWhere(renderer => !multiplicity.ContainsKey(renderer));
        }

        public static void Capture(out GraphicsBuffer entryBuffer, out GraphicsBuffer valueBuffer)
        {
            bool changed = false;
            foreach (var pair in Producers) changed |= SynchronizeSources(pair.Key, pair.Value);
            if (changed) UpdateBindings();
            dirty |= changed || bindingRevision != HoGeometryRendererBinding.Revision;
            if (dirty || entries == null || !entries.IsValid() || values == null || !values.IsValid()) Rebuild();
            entryBuffer = entries; valueBuffer = values;
        }

        private static void Rebuild()
        {
            var records = new Dictionary<uint, Entry>();
            var offsets = new Dictionary<Vector4[], uint>();
            var data = new List<Vector4> { Vector4.zero };
            foreach (var source in Bound)
            {
                uint slot = HoGeometryRendererBinding.GetManagedValue(source.renderer) >> 16;
                if (!offsets.TryGetValue(source.data, out uint offset))
                {
                    offset = (uint)data.Count;
                    offsets.Add(source.data, offset);
                    data.AddRange(source.data);
                }
                records.Add(slot, new Entry { offset = offset, count = (uint)source.data.Length, valid = 1 });
            }
            var rows=new Entry[HoGeometryRendererBinding.SlotCapacity];
            foreach(var record in records)rows[record.Key]=record.Value;
            entries?.Dispose();values?.Dispose();
            entries=new GraphicsBuffer(GraphicsBuffer.Target.Structured,rows.Length,16){name="Ho-GD Outline Entries"};
            values=new GraphicsBuffer(GraphicsBuffer.Target.Structured,data.Count,16){name="Ho-GD Outline Values"};
            entries.SetData(rows);values.SetData(data);EntryCount=rows.Length;dirty=false;
            bindingRevision=HoGeometryRendererBinding.Revision;
        }
        public static void ReleaseResources()
        {
            entries?.Dispose();values?.Dispose();entries=null;values=null;EntryCount=0;dirty=true;
            Shader.SetGlobalFloat(AvailableId,0);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => ReleaseResources();
    }
}
