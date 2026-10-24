using lilToon.URP.Extensions.RTGI.NRD;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using lilToon.URP.Extensions.NRD;
using lilToon.URP.Extensions.RTGI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Legacy=lilToon.URP.Extensions.Editor.NrdReuse.Legacy;
namespace lilToon.URP.Extensions.Editor.NrdReuse
{
    internal sealed class ReuseAudit:IDisposable
    {
        [Serializable]sealed class Frame{public int width,height;public bool snapshot;public long savedDefaultPayload;public string outputSha,publishedSha,inputSha;}
        [Serializable]sealed class Report{public bool pass,restored,zeroAlbedoInputWitness,curvedGuideWitness;public int comparedComponents;public float maxPhysicalInput,minPositiveSignal=float.MaxValue;public long observedDefaultSaving64,observedDefaultSaving32;public Frame[] frames;}
        readonly HoRTGIContext context;readonly HoRTGINrdContext source;readonly Legacy.HoLegacyGINrdContext legacy;readonly ComputeShader shader;readonly int resolve;readonly string directory;
        readonly List<Frame> frames=new();readonly Report report=new();bool recorded,resolved,disposed;int width,height;internal bool CaptureInput;
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        internal ReuseAudit(HoRTGIRendererFeature feature,string directory)
        {
            this.directory=directory;context=feature.LastContext;source=context.nrd;if(source.ValidationRecordInputs!=null||context.ValidationGpuMarker!=null)throw new Exception("Exclusive input/marker audit required");
            shader=(ComputeShader)typeof(HoRTGIRendererFeature).GetField("shader",Flags).GetValue(feature);resolve=shader.FindKernel("ResolveNRD");
            var shaders=(ComputeShader[])typeof(HoNrdContext).GetField("shaders",Flags).GetValue(source.core);legacy=new Legacy.HoLegacyGINrdContext(shaders);
            source.historyValid=false;source.ValidationRecordInputs=Record;context.ValidationGpuMarker=Mark;
        }
        internal void BeforeRender(int w,int h){width=w;height=h;legacy.Prepare(w,h);source.ValidationCaptureSignal=CaptureInput;recorded=resolved=false;}
        void Record(CommandBuffer cmd,HoNrdNative.CommonSettings common,HoNrdNative.RelaxSettings settings,IReadOnlyDictionary<string,RenderTargetIdentifier> input)
        {
            if(context.width!=width||context.height!=height)throw new Exception("Audit target mismatch");
            cmd.CopyTexture(source.signal.nameID,legacy.signal.nameID);cmd.CopyTexture(source.normal.nameID,legacy.normal.nameID);cmd.CopyTexture(source.viewZ.nameID,legacy.viewZ.nameID);
            var bindings=input.ToDictionary(p=>p.Key,p=>p.Value);bindings["IN_DIFF_RADIANCE_HITDIST"]=legacy.signal.nameID;bindings["IN_NORMAL_ROUGHNESS"]=legacy.normal.nameID;bindings["IN_VIEWZ"]=legacy.viewZ.nameID;bindings["OUT_DIFF_RADIANCE_HITDIST"]=legacy.output.nameID;bindings["OUT_VALIDATION"]=legacy.filtered.nameID;
            legacy.core.Record(cmd,common,bindings,settings);recorded=true;
        }
        void Mark(CommandBuffer cmd,string name,bool begin)
        {
            if(name!="GI.Resolve"||begin)return;
            cmd.SetComputeTextureParam(shader,resolve,"_HoRTGINrdOutput",legacy.output.nameID);cmd.SetComputeTextureParam(shader,resolve,"_HoRTGIFiltered",legacy.filtered.nameID);cmd.DispatchCompute(shader,resolve,(width+7)/8,(height+7)/8,1);
            cmd.SetComputeTextureParam(shader,resolve,"_HoRTGINrdOutput",source.output.nameID);cmd.SetComputeTextureParam(shader,resolve,"_HoRTGIFiltered",source.filtered.nameID);resolved=true;
        }
        static byte[] Pixels(RenderTexture t)
        {
            var previous=RenderTexture.active;var image=new Texture2D(t.width,t.height,TextureFormat.RGBAFloat,false,true);
            try{RenderTexture.active=t;image.ReadPixels(new Rect(0,0,t.width,t.height),0,0);image.Apply();using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);foreach(var pixel in image.GetPixels())for(int k=0;k<4;k++){if(!float.IsFinite(pixel[k]))throw new Exception("Nonfinite result");writer.Write(pixel[k]);}return stream.ToArray();}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
        }
        static string Hash(byte[] b){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(b)).Replace("-","");}
        internal void Observe()
        {
            if(!recorded||!resolved)throw new Exception("Actual NRD and Resolve were not observed");
            if(!ReferenceEquals(source.signal,source.filtered)||ReferenceEquals(legacy.signal,legacy.filtered))throw new Exception("Old/new resource lifetime contract changed");
            var a=Pixels(source.output.rt);var b=Pixels(legacy.output.rt);if(!a.SequenceEqual(b))throw new Exception("Demodulated NRD output mismatch");
            var p=Pixels(source.filtered.rt);var q=Pixels(legacy.filtered.rt);if(!p.SequenceEqual(q))throw new Exception("Physical publication mismatch");
            var input=Pixels(legacy.signal.rt);if(CaptureInput&&!input.SequenceEqual(Pixels(source.ValidationSignal.rt)))throw new Exception("Pre-NRD input snapshot mismatch");
            for(int offset=0;offset<input.Length;offset+=16)
            {float red=BitConverter.ToSingle(input,offset);for(int k=0;k<3;k++){float value=BitConverter.ToSingle(input,offset+k*4);report.maxPhysicalInput=Math.Max(report.maxPhysicalInput,value*source.AppliedSignalScale);if(value>0)report.minPositiveSignal=Math.Min(report.minPositiveSignal,value);}if(CaptureInput&&red>1e-6f&&BitConverter.ToSingle(p,offset)==0&&BitConverter.ToSingle(p,offset+12)>.5f)report.zeroAlbedoInputWitness=true;}
            var normals=Pixels(source.normal.rt);report.curvedGuideWitness|=Enumerable.Range(0,width*height).Select(i=>BitConverter.ToInt64(normals,i*16)).Distinct().Take(3).Count()>2;
            // Actual handles, not declared aliases: default four targets vs old five.
            int distinct=new[]{source.signal,source.normal,source.viewZ,source.output,source.filtered}.Select(x=>x.rt.GetInstanceID()).Distinct().Count();if(distinct!=4)throw new Exception("No default allocation saving");
            var snapshot=typeof(HoRTGINrdContext).GetField("validationSignal",Flags).GetValue(source);if(!CaptureInput&&snapshot!=null)throw new Exception("Snapshot retained without an audit request");
            long Payload(IEnumerable<RTHandle> handles)=>handles.GroupBy(x=>x.rt.GetInstanceID()).Sum(g=>{var t=g.First().rt;return (long)t.width*t.height*GraphicsFormatUtility.GetBlockSize(t.graphicsFormat);});
            var targets=new[]{source.signal,source.normal,source.viewZ,source.output,source.filtered};if(snapshot is RTHandle captured)targets=targets.Concat(new[]{captured}).ToArray();
            long saving=Payload(new[]{legacy.signal,legacy.normal,legacy.viewZ,legacy.output,legacy.filtered})-Payload(targets);if(saving!=(CaptureInput?0:(long)width*height*8))throw new Exception("Actual texture allocation accounting differs");if(!CaptureInput){if(width==64)report.observedDefaultSaving64=saving;if(width==32)report.observedDefaultSaving32=saving;}
            report.comparedComponents+=width*height*8;frames.Add(new Frame{width=width,height=height,snapshot=CaptureInput,savedDefaultPayload=CaptureInput?0:saving,outputSha=Hash(a),publishedSha=Hash(p),inputSha=Hash(input)});
        }
        internal void Save(){report.pass=frames.Count>=40&&frames.Any(f=>f.snapshot)&&report.observedDefaultSaving64==32768&&report.observedDefaultSaving32==8192&&report.zeroAlbedoInputWitness&&report.curvedGuideWitness&&report.maxPhysicalInput>1&&report.minPositiveSignal<1e-5f;if(!report.pass)throw new Exception("Missing reuse/snapshot/resize/curve/zero-albedo/HDR/weak-signal witnesses");Write();}
        void Write(){report.frames=frames.ToArray();File.WriteAllText(Path.Combine(directory,"reuse-audit.json"),JsonUtility.ToJson(report,true));}
        public void Dispose(){if(disposed)return;disposed=true;source.ValidationRecordInputs=null;context.ValidationGpuMarker=null;source.ValidationCaptureSignal=false;legacy.Dispose();report.restored=true;Write();}
    }
}
