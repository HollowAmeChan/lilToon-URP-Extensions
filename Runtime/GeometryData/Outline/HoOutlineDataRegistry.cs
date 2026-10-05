using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace lilToon.URP.Extensions.GeometryData
{
    public static class HoOutlineDataRegistry
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Entry { public uint offset, count, valid, reserved; }
        private static readonly List<HoGeometryDataOutlineCorrection> Producers = new List<HoGeometryDataOutlineCorrection>();
        private static readonly HashSet<HoGeometryDataOutlineCorrection> Bound = new HashSet<HoGeometryDataOutlineCorrection>();
        private static readonly HashSet<Renderer> WarnedAmbiguous = new HashSet<Renderer>();
        private static readonly Dictionary<HoGeometryDataOutlineCorrection, (Renderer renderer, Vector4[] data)> Published
            = new Dictionary<HoGeometryDataOutlineCorrection, (Renderer, Vector4[])>();
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
        public static bool IsPublished(HoGeometryDataOutlineCorrection producer) => producer != null && producer.isActiveAndEnabled
            && producer.HasData && !dirty && Bound.Contains(producer) && entries != null && entries.IsValid();

        internal static void Register(HoGeometryDataOutlineCorrection producer)
        {
            if(!Producers.Contains(producer))Producers.Add(producer);
            // Unity snapshots draw metadata during camera culling, before AddRenderPasses/RecordRenderGraph.
            if(producer.isActiveAndEnabled && producer.HasData && HoGeometryRendererBinding.Acquire(producer.targetRenderer,producer)!=0)Bound.Add(producer);
            else { HoGeometryRendererBinding.Release(producer);Bound.Remove(producer); }
            dirty=true;
        }
        internal static void Unregister(HoGeometryDataOutlineCorrection producer)
        {
            Producers.Remove(producer);
            HoGeometryRendererBinding.Release(producer);
            Bound.Remove(producer);
            dirty=true;
        }
        public static void MarkDirty() => dirty = true;

        public static void Capture(out GraphicsBuffer entryBuffer, out GraphicsBuffer valueBuffer)
        {
            Producers.RemoveAll(p=>p==null);
            for(int i=0; !dirty && i<Producers.Count; i++)
            {
                var p=Producers[i];
                if(Published.TryGetValue(p,out var published))
                    dirty=p.targetRenderer!=published.renderer || p.Data!=published.data;
                else if(p.isActiveAndEnabled && p.HasData)dirty=true;
            }
            if(bindingRevision!=HoGeometryRendererBinding.Revision)dirty=true;
            if(dirty || entries==null || !entries.IsValid() || values==null || !values.IsValid())Rebuild();
            entryBuffer=entries;valueBuffer=values;
        }
        private static void Rebuild()
        {
            Published.Clear();
            var multiplicity=new Dictionary<Renderer,int>();
            foreach(var p in Producers)
                if(p!=null && p.isActiveAndEnabled && p.targetRenderer!=null)
                    multiplicity[p.targetRenderer]=multiplicity.TryGetValue(p.targetRenderer,out int n) ? n+1 : 1;
            var accepted=new HashSet<HoGeometryDataOutlineCorrection>();
            foreach(var p in Producers)
                if(p!=null && p.isActiveAndEnabled && p.targetRenderer!=null && p.HasData && multiplicity[p.targetRenderer]==1)
                    accepted.Add(p);
            foreach(var p in Bound)if(!accepted.Contains(p))HoGeometryRendererBinding.Release(p);
            Bound.Clear();
            var records=new Dictionary<uint,Entry>();var data=new List<Vector4>{Vector4.zero};
            foreach(var p in Producers)
            {
                if(p!=null)Published[p]=(p.targetRenderer,p.Data);
                if(p==null || !p.isActiveAndEnabled || p.targetRenderer==null || !p.HasData)continue;
                // A draw has one dedicated outline source. Ambiguous ownership is invalid, never last-writer-wins.
                if(multiplicity[p.targetRenderer]!=1)
                {
                    if(WarnedAmbiguous.Add(p.targetRenderer))Debug.LogWarning("[Ho-GD] 同一 Renderer 有多个描边修正组件，未发布修正来源。",p.targetRenderer);
                    continue;
                }
                WarnedAmbiguous.Remove(p.targetRenderer);
                Vector4[] source=p.Data;
                uint slot=HoGeometryRendererBinding.Acquire(p.targetRenderer,p);
                if(slot==0)continue;
                records.Add(slot,new Entry{offset=(uint)data.Count,count=(uint)source.Length,valid=1});
                data.AddRange(source);Bound.Add(p);
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
