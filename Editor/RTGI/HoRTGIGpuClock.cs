using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // Editor benchmark sidecar only. Resolve is deliberately outside raster
    // render passes; timestamps themselves are recorded at the actual markers.
    internal sealed class HoRTGIGpuClock:IDisposable
    {
        [DllImport("HoGIGpuProbe",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr HoGpu_Event();
        [DllImport("HoGIGpuProbe",CallingConvention=CallingConvention.Cdecl)]static extern int HoGpu_Base();
        [DllImport("HoGIGpuProbe",CallingConvention=CallingConvention.Cdecl)]static extern int HoGpu_Status();
        [DllImport("HoGIGpuProbe",CallingConvention=CallingConvention.Cdecl)]static extern int HoGpu_Allocate();
        [DllImport("HoGIGpuProbe",CallingConvention=CallingConvention.Cdecl)]static extern int HoGpu_Poll(int index,out NativeResult result);
        [StructLayout(LayoutKind.Sequential)]struct NativeResult{public ulong begin,end,frequency,targetFence,completedFence;public int error,ready;}
        [Serializable]internal sealed class Sample{public string name;public int frame,slot;public ulong begin,end,frequency,targetFence,completedFence;public double milliseconds;}
        [Serializable]internal sealed class Summary{public string name;public int count;public double mean,median,p95;}
        sealed class Pending{internal Sample sample;internal IntPtr packet;internal bool resolved;}
        readonly Dictionary<string,Pending> active=new();
        readonly List<Pending> pending=new();
        internal readonly List<Sample> samples=new();
        readonly RenderTexture sync;
        readonly Texture2D pixel;
        readonly IntPtr callback;
        readonly int eventBase;
        internal int Frame;
        internal bool TotalOnly;
        internal HoRTGIGpuClock()
        {
            sync=new RenderTexture(1,1,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);sync.Create();pixel=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
            callback=HoGpu_Event();eventBase=HoGpu_Base();if(callback==IntPtr.Zero||eventBase<0)throw new Exception("GPU timestamp bridge unavailable");
            using(var cmd=new CommandBuffer{name="GI timestamp queue initialization"}){cmd.IssuePluginEventAndData(callback,eventBase,IntPtr.Zero);Graphics.ExecuteCommandBuffer(cmd);}Flush();
            if(HoGpu_Status()!=1)throw new Exception("GPU clock init not ready: "+HoGpu_Status());
        }
        void Flush(){var old=RenderTexture.active;try{RenderTexture.active=sync;pixel.ReadPixels(new Rect(0,0,1,1),0,0);pixel.Apply();}finally{RenderTexture.active=old;}}
        internal void Mark(CommandBuffer cmd,string name,bool begin)=>Emit((id,data)=>cmd.IssuePluginEventAndData(callback,id,data),name,begin);
        internal void MarkRaster(RasterCommandBuffer cmd,string name,bool begin)=>Emit((id,data)=>cmd.IssuePluginEventAndData(callback,id,data),name,begin);
        void Emit(Action<int,IntPtr> emit,string name,bool begin)
        {
            if(TotalOnly&&name!="GI.Total"&&name!="GI.Composite")return;
            if(begin)
            {
                if(active.ContainsKey(name))throw new Exception("Nested duplicate GPU marker "+name);
                int slot=HoGpu_Allocate();if(slot<0)throw new Exception("GPU query capacity exhausted");
                var packet=Marshal.AllocHGlobal(4);Marshal.WriteInt32(packet,slot);var entry=new Pending{packet=packet,sample=new Sample{name=name,slot=slot,frame=Frame}};active.Add(name,entry);emit(eventBase+1,packet);
            }
            else
            {
                if(!active.TryGetValue(name,out var entry))throw new Exception("GPU marker end without begin: "+name);
                emit(eventBase+2,entry.packet);pending.Add(entry);active.Remove(name);
            }
        }
        internal void Collect()
        {
            if(active.Count!=0)throw new Exception("GPU marker stack is incomplete");
            using(var cmd=new CommandBuffer{name="GI timestamp query resolve after camera"})
            {
                foreach(var entry in pending)if(!entry.resolved){cmd.IssuePluginEventAndData(callback,eventBase+3,entry.packet);entry.resolved=true;}
                Graphics.ExecuteCommandBuffer(cmd);
            }
            Flush();
            for(int i=pending.Count-1;i>=0;i--)
            {
                var entry=pending[i];int state=HoGpu_Poll(entry.sample.slot,out var data);
                if(state<0)throw new Exception("GPU query failed "+state+" marker="+entry.sample.name+" ticks="+data.begin+"/"+data.end+" fence="+data.completedFence+"/"+data.targetFence);
                if(state==0)continue;
                var sample=entry.sample;sample.begin=data.begin;sample.end=data.end;sample.frequency=data.frequency;sample.targetFence=data.targetFence;sample.completedFence=data.completedFence;sample.milliseconds=1000.0*(data.end-data.begin)/data.frequency;samples.Add(sample);Marshal.FreeHGlobal(entry.packet);pending.RemoveAt(i);
            }
        }
        internal Summary[] Summarize(int minimumFrames)
        {
            Collect();if(pending.Count!=0)throw new Exception("GPU queries still lack completed fence");
            var groups=samples.GroupBy(x=>x.name).Select(g=>
            {
                var values=g.Select(x=>x.milliseconds).OrderBy(x=>x).ToArray();if(values.Length<(g.Key=="GI.BuildEnvironmentPdf"?1:minimumFrames))throw new Exception("Insufficient GPU samples for "+g.Key+": "+values.Length);
                return new Summary{name=g.Key,count=values.Length,mean=values.Average(),median=values[values.Length/2],p95=values[Math.Min(values.Length-1,(int)(values.Length*.95))]};
            }).ToArray();
            foreach(string required in new[]{"GI.Total","GI.Composite"})if(!groups.Any(x=>x.name==required&&x.median>0))throw new Exception("No positive completed GPU interval for "+required);
            return groups;
        }
        public void Dispose()
        {
            // Pending event packets must survive until callbacks complete. Any
            // failure deliberately leaves them to this isolated process exit.
            sync.Release();UnityEngine.Object.DestroyImmediate(sync);UnityEngine.Object.DestroyImmediate(pixel);
        }
    }
}
