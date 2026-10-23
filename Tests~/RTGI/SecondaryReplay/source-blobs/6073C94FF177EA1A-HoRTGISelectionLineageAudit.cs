using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.RTGI;
namespace lilToon.URP.Extensions.Editor.RTGI
{
    internal sealed class HoRTGISelectionLineageAudit:IDisposable
    {
        [Serializable,StructLayout(LayoutKind.Sequential)]internal struct U4{public uint x,y,z,w;public bool Same(U4 b)=>x==b.x&&y==b.y&&z==b.z&&w==b.w;}
        [Serializable]internal sealed class Frame{public int mode,seed,shaderFrame,comparedPackets,sourceChecks,temporalPrevious,spatialOther,generation,initialInvalid,initialBlack;public int exactReplayMismatches,packedViewMismatches,primaryVisibilityMismatches;public bool expectedEmpty;public int[] eventCounts;public string controlSha,traceSha,rawControlSha,rawTraceSha,filteredControlSha,filteredTraceSha;}
        [Serializable]internal sealed class Report{public int failedPacketIndex=-1,failedShaderFrame;public HoRTGIHistoryFeedbackAudit.Packet failedControl,failedTrace;public bool pass,restored,branchCoverage,secondaryReplay;public Vector4[] analyticLights;public int contextGenerations;public int width,height,framesPerMode;public long logicalBufferBytes;public Frame[] frames;public string stream,streamSha256;}
        readonly HoRTGIRendererFeature control,trace;readonly Camera camera;readonly ComputeShader compute,capture;readonly RayTracingShader ray;readonly int captureKernel;
        readonly List<Frame> frames=new();readonly Dictionary<ulong,(HoRTGIHistoryFeedbackAudit.Packet packet,U4 meta)> ledger=new();
        GraphicsBuffer records,packets,snapMetadata,snapOrigin;HoRTGIContext attached;bool disposed;int writes;int mode;readonly bool branches,replay;int? pendingSeed;internal bool ExpectEmptyCurrent;internal static bool ReplayDispatchEnabled=true;
        BinaryWriter stream;readonly Report report=new();
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;const string DirectoryPath="Assets/HoRTGISelectionLineage/";
        internal HoRTGISelectionLineageAudit(HoRTGIRendererFeature control,HoRTGIRendererFeature trace,Camera camera,bool branches=false,bool secondaryReplay=false)
        {
            this.control=control;this.trace=trace;this.camera=camera;this.branches=branches;replay=secondaryReplay;report.branchCoverage=branches;report.secondaryReplay=replay;AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            compute=AssetDatabase.LoadAssetAtPath<ComputeShader>(DirectoryPath+"Lineage.compute");ray=AssetDatabase.LoadAssetAtPath<RayTracingShader>(DirectoryPath+"Lineage.raytrace");if(compute==null||ray==null)throw new Exception("Generate lineage shaders before launch");
            capture=AssetDatabase.LoadAssetAtPath<ComputeShader>(replay?DirectoryPath+"ReplayCapture.compute":"Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTGI/Shaders/HoRTGISelectionCapture.compute");captureKernel=capture.FindKernel("Capture");
            typeof(HoRTGIRendererFeature).GetField("shader",Flags).SetValue(trace,compute);typeof(HoRTGIRendererFeature).GetField("rayShader",Flags).SetValue(trace,ray);
        }
        internal BindingFeature MakeBinding(){var b=ScriptableObject.CreateInstance<BindingFeature>();b.owner=this;return b;}
        internal sealed class BindingFeature:ScriptableRendererFeature
        {
            internal HoRTGISelectionLineageAudit owner;public override void Create(){}
            public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data){if(owner!=null&&!owner.disposed&&data.cameraData.camera==owner.camera)owner.Attach();}
        }
        void Attach()
        {
            var c=trace.LastContext;if(c==null)return;if(attached!=null){if(attached!=c)throw new Exception("Unexpected trace context replacement");SetTimes();return;}
            if(c.width!=64||c.height!=64||c.ValidationGpuMarker!=null)throw new Exception("Small exclusive lineage scope required");attached=c;int n=c.width*c.height;report.width=c.width;report.height=c.height;
            records=new GraphicsBuffer(GraphicsBuffer.Target.Structured,replay?18*c.arrayPitch+16*n:6*c.arrayPitch+5*n,16);
            packets=new GraphicsBuffer(GraphicsBuffer.Target.Structured,n*5,32);snapMetadata=new GraphicsBuffer(GraphicsBuffer.Target.Structured,n*5,16);snapOrigin=new GraphicsBuffer(GraphicsBuffer.Target.Structured,n*5,16);
            report.logicalBufferBytes=(long)records.count*16+(long)n*64*5;c.ValidationGpuMarker=Mark;report.contextGenerations++;if(pendingSeed.HasValue){foreach(var next in new[]{control.LastContext,trace.LastContext}){next.frame=pendingSeed.Value;next.historyValid=false;next.nrd.historyValid=false;}pendingSeed=null;}SetTimes();
        }
        void SetTimes(){foreach(var c in new[]{control.LastContext,trace.LastContext})if(c?.nrd!=null)c.nrd.ValidationTimeSeconds=(c.frame+1)/60.0;}
        internal void PrepareRebuild(int seed){ReleaseBuffers();ledger.Clear();pendingSeed=seed;}
        void ReleaseBuffers(){if(attached!=null)attached.ValidationGpuMarker=null;foreach(var b in new[]{records,packets,snapMetadata,snapOrigin})b?.Dispose();records=packets=snapMetadata=snapOrigin=null;attached=null;}
        void Mark(CommandBuffer cmd,string name,bool begin)
        {
            var c=attached;
            if(name=="GI.Total"&&begin){writes=0;foreach(string k in new[]{"ShadeSecondary","Temporal","Spatial","Final","FinalNRD"}){
                int kernel=compute.FindKernel(k);cmd.SetComputeBufferParam(compute,kernel,"_HoLRecords",records);
            }cmd.SetRayTracingBufferParam(ray,"_HoLRecords",records);}

            if(begin)return;
            if(replay&&ReplayDispatchEnabled&&name=="GI.FinalVisibility")cmd.DispatchRays(ray,"ReplaySelected",(uint)c.width,(uint)c.height,1);
            if(name=="GI.ShadeSecondary")Capture(cmd,0,1,false);if(name=="GI.Temporal")Capture(cmd,1,2,false);if(name=="GI.Spatial")Capture(cmd,2,1,false);if(name=="GI.Final"){Capture(cmd,3,0,false);Capture(cmd,4,0,true);}
        }
        void Capture(CommandBuffer cmd,int stage,int pool,bool linear)
        {
            var c=attached;cmd.SetComputeIntParam(capture,"_Width",c.width);cmd.SetComputeIntParam(capture,"_Height",c.height);cmd.SetComputeIntParam(capture,"_RowPitch",c.rowPitch);cmd.SetComputeIntParam(capture,"_ArrayPitch",c.arrayPitch);cmd.SetComputeIntParam(capture,"_Pool",pool);cmd.SetComputeIntParam(capture,"_Stage",stage);cmd.SetComputeIntParam(capture,"_Linear",linear?1:0);
            cmd.SetComputeBufferParam(capture,captureKernel,"_SourcePacket",c.reservoirs);cmd.SetComputeBufferParam(capture,captureKernel,"_Records",records);cmd.SetComputeBufferParam(capture,captureKernel,"_Packet",packets);cmd.SetComputeBufferParam(capture,captureKernel,"_Metadata",snapMetadata);cmd.SetComputeBufferParam(capture,captureKernel,"_Origin",snapOrigin);cmd.DispatchCompute(capture,captureKernel,c.width/8,c.height/8,1);writes++;
        }
        internal void Begin(string output,int framesPerMode)
        {report.framesPerMode=framesPerMode;report.stream="lineage.bin";stream=new BinaryWriter(File.Create(Path.Combine(output,report.stream)));stream.Write(new byte[]{72,71,(byte)(replay?82:branches?69:76),49});stream.Write(64);stream.Write(64);stream.Write(framesPerMode);}
        internal void ResetMode(int value){mode=value;ledger.Clear();}
        internal void Observe(int seed,Func<RenderTexture,Color[]> read)
        {
            var a=control.LastContext;var b=trace.LastContext;if(a==null||b==null||a.frame!=b.frame||writes!=5)throw new Exception("Real dual GI sequence or stage capture missing");int n=64*64;
            var ca=new HoRTGIHistoryFeedbackAudit.Packet[a.reservoirs.count];var cb=new HoRTGIHistoryFeedbackAudit.Packet[b.reservoirs.count];a.reservoirs.GetData(ca);b.reservoirs.GetData(cb);
            var ps=new HoRTGIHistoryFeedbackAudit.Packet[n*5];var ms=new U4[n*5];var os=new U4[n*5];packets.GetData(ps);snapMetadata.GetData(ms);snapOrigin.GetData(os);
            foreach(var shader in new[]{compute,capture})foreach(var m in ShaderUtil.GetComputeShaderMessages(shader))if(m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(m.message);
            var f=new Frame{mode=mode,seed=seed,shaderFrame=a.frame-1,comparedPackets=ca.Length,generation=report.contextGenerations,expectedEmpty=ExpectEmptyCurrent};
            for(int i=0;i<ca.Length;i++)if(!HoRTGIHistoryFeedbackAudit.Equal(ca[i],cb[i])){report.failedPacketIndex=i;report.failedShaderFrame=f.shaderFrame;report.failedControl=ca[i];report.failedTrace=cb[i];throw new Exception("Lineage changed original packed GI output at "+i+" frame="+f.shaderFrame);}
            for(int i=0;i<n;i++){var o=os[i];if(o.z!=0){if(o.x!=f.shaderFrame||o.y!=i)throw new Exception("Initial origin stamp differs");ledger[((ulong)o.x<<32)|o.y]=(ps[i],ms[i]);}}
            for(int s=1;s<5;s++)for(int i=0;i<n;i++){
                int at=s*n+i;var p=ps[at];var o=os[at];if((p.misc&255)==0){if(!ms[at].Same(default)||!o.Same(default))throw new Exception("Empty sample retained metadata");continue;}
                ulong key=((ulong)o.x<<32)|o.y;if(o.z==0||!ledger.TryGetValue(key,out var item)||!item.meta.Same(ms[at]))throw new Exception("Selected metadata has no immutable real initial source");
                if(p.position!=item.packet.position)throw new Exception("Selected origin position identity mismatch");f.sourceChecks++;if(s==1&&o.x<f.shaderFrame)f.temporalPrevious++;if(s==2&&(o.x!=os[n+i].x||o.y!=os[n+i].y))f.spatialOther++;
            }
            if(f.sourceChecks==0&&!ExpectEmptyCurrent&&!branches)throw new Exception("No valid real lineage source observed");
            f.initialInvalid=Enumerable.Range(0,n).Count(i=>os[i].z==0);f.initialBlack=Enumerable.Range(0,n).Count(i=>os[i].z==3);
            if(ExpectEmptyCurrent&&ps.Any(p=>(p.misc&255)!=0))throw new Exception("Expected invalid primary retained a GI sample");
            U4[] eventData=null;if(branches){var all=new U4[records.count];records.GetData(all);eventData=all.Skip(6*b.arrayPitch+4*n).Take(n).ToArray();f.eventCounts=Enumerable.Range(0,17).Select(bit=>eventData.Count(v=>(v.x&(1u<<bit))!=0)).ToArray();}
            for(int i=0;i<n;i++)foreach(int s in new[]{3,4}){int expected=(s==3&&(mode&2)!=0)?n+i:2*n+i,at=s*n+i;if(!HoRTGIHistoryFeedbackAudit.Equal(ps[at],ps[expected])||!ms[at].Same(ms[expected])||!os[at].Same(os[expected]))throw new Exception("Final history/actual Shade source mismatch");}
            U4[] replayMaterial=null,replayData=null;
            if(replay){var all=new U4[records.count];records.GetData(all);replayMaterial=new U4[n*4];replayData=all.Skip(18*b.arrayPitch+8*n).Take(n*8).ToArray();
                for(int i=0;i<n;i++){int x=i%b.width,y=i/b.width,address=(y/16)*b.rowPitch+(x/16)*256+(y%16)*16+x%16+b.arrayPitch;Array.Copy(all,address*6+2,replayMaterial,i*4,4);var v=replayData[i*8];if(v.x!=v.y)f.exactReplayMismatches++;if(v.x!=v.z)f.packedViewMismatches++;var visibility=replayData[i*8+6];if(visibility.x!=visibility.y)f.primaryVisibilityMismatches++;}
            }
            var rawA=read(a.diffuse.rt);var rawB=read(b.diffuse.rt);var filterA=read(a.PublishedDiffuse.rt);var filterB=read(b.PublishedDiffuse.rt);Compare(rawA,rawB);Compare(filterA,filterB);
            f.controlSha=Hash(Bytes(ca));f.traceSha=Hash(Bytes(cb));f.rawControlSha=Hash(Bytes(rawA));f.rawTraceSha=Hash(Bytes(rawB));f.filteredControlSha=Hash(Bytes(filterA));f.filteredTraceSha=Hash(Bytes(filterB));frames.Add(f);
            stream.Write(mode);stream.Write(f.shaderFrame);stream.Write(Time.frameCount);if(branches)stream.Write(f.generation);stream.Write(Bytes(ps));stream.Write(Bytes(ms));stream.Write(Bytes(os));stream.Write(Bytes(ca));if(branches)stream.Write(Bytes(eventData));if(replay){stream.Write(Bytes(replayMaterial));stream.Write(Bytes(replayData));}
        }
        static void Compare(Color[] a,Color[] b){for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)if(!float.IsFinite(a[i][c])||BitConverter.SingleToInt32Bits(a[i][c])!=BitConverter.SingleToInt32Bits(b[i][c]))throw new Exception("Lineage changed physical raw/NRD publication");}
        internal Report Finish(string output){stream.Dispose();stream=null;report.streamSha256=Hash(File.ReadAllBytes(Path.Combine(output,report.stream)));report.frames=frames.ToArray();if(replay){var pass=typeof(HoRTGIRendererFeature).GetField("pass",Flags).GetValue(control);int count=(int)pass.GetType().GetField("lightCount",Flags).GetValue(pass);var lights=new lilToon.URP.Extensions.RTDI.HoRTDIRendererFeature.AnalyticLight[control.LastContext.lights.count];control.LastContext.lights.GetData(lights);report.analyticLights=lights.Take(count).SelectMany(v=>new[]{v.positionRange,v.radianceType,v.directionOuter,v.innerShadow}).ToArray();}report.pass=frames.Count==(branches?5:4)*report.framesPerMode&&frames.Any(f=>f.temporalPrevious>0)&&frames.Any(f=>f.spatialOther>0);report.pass&=!replay||!ReplayDispatchEnabled||frames.All(f=>f.exactReplayMismatches==0&&f.primaryVisibilityMismatches==0);if(!report.pass)throw new Exception("Actual provenance/replay qualification failed");return report;}
        internal Report SnapshotReport(){report.frames=frames.ToArray();return report;}
        public void Dispose(){if(disposed)return;disposed=true;stream?.Dispose();ReleaseBuffers();trace.SetActive(false);report.restored=true;}
        static string Hash(byte[] b){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(b)).Replace("-","");}
        static byte[] Bytes<T>(T[] values)where T:struct{int size=Marshal.SizeOf<T>();byte[] data=new byte[size*values.Length];var handle=GCHandle.Alloc(values,GCHandleType.Pinned);try{Marshal.Copy(handle.AddrOfPinnedObject(),data,0,data.Length);return data;}finally{handle.Free();}}
    }
}
