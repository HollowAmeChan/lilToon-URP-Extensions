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
        private static readonly List<HoOutlineCorrection> Producers = new List<HoOutlineCorrection>();
        private static readonly HashSet<Renderer> Bound = new HashSet<Renderer>();
        private static readonly HashSet<Renderer> WarnedAmbiguous = new HashSet<Renderer>();
        private static readonly Dictionary<HoOutlineCorrection, (Renderer renderer, Vector4[] data)> Published
            = new Dictionary<HoOutlineCorrection, (Renderer, Vector4[])>();
        private static GraphicsBuffer entries, values;
        private static bool dirty = true;
        public static readonly int EntriesId = Shader.PropertyToID("_HoGDOutlineEntries");
        public static readonly int ValuesId = Shader.PropertyToID("_HoGDOutlineValues");
        public static readonly int CountId = Shader.PropertyToID("_HoGDOutlineEntryCount");
        public static readonly int AvailableId = Shader.PropertyToID("_HoGDOutlineAvailable");
        public static int EntryCount { get; private set; }

        internal static void Register(HoOutlineCorrection producer)
        { if(!Producers.Contains(producer))Producers.Add(producer);dirty=true; }
        internal static void Unregister(HoOutlineCorrection producer)
        {
            Producers.Remove(producer);
            if(producer.targetRenderer!=null)HoGeometryRendererBinding.SetOutlineSlot(producer.targetRenderer,0);
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
            if(dirty || entries==null || !entries.IsValid() || values==null || !values.IsValid())Rebuild();
            entryBuffer=entries;valueBuffer=values;
        }
        private static void Rebuild()
        {
            foreach(var renderer in Bound) if(renderer!=null)HoGeometryRendererBinding.SetOutlineSlot(renderer,0);
            Bound.Clear();
            Published.Clear();
            var multiplicity=new Dictionary<Renderer,int>();
            foreach(var p in Producers)
                if(p!=null && p.isActiveAndEnabled && p.targetRenderer!=null)
                    multiplicity[p.targetRenderer]=multiplicity.TryGetValue(p.targetRenderer,out int n) ? n+1 : 1;
            var rows=new List<Entry>{default};var data=new List<Vector4>{Vector4.zero};
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
                if(rows.Count>=65536)break;
                Vector4[] source=p.Data;
                if(!HoGeometryRendererBinding.SetOutlineSlot(p.targetRenderer,(uint)rows.Count))continue;
                rows.Add(new Entry{offset=(uint)data.Count,count=(uint)source.Length,valid=1});
                data.AddRange(source);Bound.Add(p.targetRenderer);
            }
            entries?.Dispose();values?.Dispose();
            entries=new GraphicsBuffer(GraphicsBuffer.Target.Structured,rows.Count,16){name="Ho-GD Outline Entries"};
            values=new GraphicsBuffer(GraphicsBuffer.Target.Structured,data.Count,16){name="Ho-GD Outline Values"};
            entries.SetData(rows);values.SetData(data);EntryCount=rows.Count;dirty=false;
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
