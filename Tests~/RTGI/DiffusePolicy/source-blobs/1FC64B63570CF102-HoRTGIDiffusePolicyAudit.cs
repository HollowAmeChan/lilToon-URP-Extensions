using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using lilToon.URP.Extensions.RTDI.NRD;
using lilToon.URP.Extensions.RTGI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // One coherent, fixed DI diffuse policy. Only the owned experimental core changes.
    internal sealed class HoRTGIDiffusePolicyAudit:IDisposable
    {
        [Serializable]internal sealed class ShaderSource{public int pipeline;public string bodyPath,wrapperPath,bodySha256,wrapperSha256;}
        [Serializable]internal sealed class Segment{public int seed,frames;public string map,mapSha256,stream,streamSha256;public HoRTGISpatialAudit.Report spatial;}
        [Serializable]internal sealed class Report
        {
            public bool restored,released;public int calibratedFrames,calibrationMismatches,firstWrites,laterWrites;public float maxCalibrationError;
            public ShaderSource[] sources;public Segment[] segments;
        }
        const string DirectoryPath="Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTGI/Shaders/";
        readonly ComputeShader[] shaders,original=new ComputeShader[2];readonly int[] kernels,originalKernels=new int[2];
        readonly ComputeShader resolve;readonly int resolveKernel;readonly RenderTexture[] physical=new RenderTexture[2];
        readonly List<Segment> segments=new();internal readonly Report report=new Report();
        readonly HoRTDINrdContext core;HoRTGISpatialAudit spatial;BinaryWriter stream;Segment current;int observed;bool disposed;
        internal HoRTGIDiffusePolicyAudit(HoRTDINrdContext core,int width,int height)
        {
            this.core=core;const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            shaders=(ComputeShader[])typeof(HoRTDINrdContext).GetField("shaders",flags).GetValue(core);kernels=(int[])typeof(HoRTDINrdContext).GetField("kernels",flags).GetValue(core);
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));var sources=new List<ShaderSource>();
            var replacements=new ComputeShader[2];var replacementKernels=new int[2];
            for(int i=0;i<2;i++)
            {
                string name="HoRTGIDiffusePolicy"+(i==0?"AtrousSmem":"Atrous");string wrapper=DirectoryPath+name+".compute",body=DirectoryPath+name+".hlsl";
                replacements[i]=AssetDatabase.LoadAssetAtPath<ComputeShader>(wrapper);if(replacements[i]==null)throw new Exception("Policy import failed: "+wrapper);
                replacementKernels[i]=replacements[i].FindKernel("main");Check(replacements[i]);
                sources.Add(new ShaderSource{pipeline=9+i,bodyPath=body,wrapperPath=wrapper,bodySha256=Hash(File.ReadAllBytes(Path.Combine(root,"Editor/RTGI/Shaders/"+name+".hlsl"))),wrapperSha256=Hash(File.ReadAllBytes(Path.Combine(root,"Editor/RTGI/Shaders/"+name+".compute")))});
            }
            resolve=AssetDatabase.LoadAssetAtPath<ComputeShader>(DirectoryPath+"HoRTGISharedResolve.compute");resolveKernel=resolve.FindKernel("main");Check(resolve);
            for(int i=0;i<2;i++){physical[i]=new RenderTexture(width,height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear){enableRandomWrite=true};if(!physical[i].Create())throw new Exception("Physical output allocation failed");}
            report.sources=sources.ToArray();
            for(int i=0;i<2;i++){original[i]=shaders[9+i];originalKernels[i]=kernels[9+i];shaders[9+i]=replacements[i];kernels[9+i]=replacementKernels[i];}
            if(core.ValidationDiffuseStage!=null)throw new Exception("Experimental core observer already owned");
            core.ValidationDiffuseStage=Count;
        }
        static void Check(ComputeShader shader){foreach(var m in ShaderUtil.GetComputeShaderMessages(shader))if(m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(m.message);}
        void Count(CommandBuffer cmd,int pipeline,RenderTargetIdentifier source){if(pipeline==9)report.firstWrites++;if(pipeline==10)report.laterWrites++;}
        internal void Begin(int seed,string output,int width,int height)
        {
            if(current!=null)throw new Exception("Unfinished candidate segment");observed=0;spatial=new HoRTGISpatialAudit(width,height);current=new Segment{seed=seed,map="candidate-publication-"+seed+".bin",stream="candidate-local-"+seed+".bin"};
            stream=new BinaryWriter(File.Create(Path.Combine(output,current.stream)));stream.Write(new byte[]{(byte)'H',(byte)'G',(byte)'C',(byte)'1'});stream.Write(width);stream.Write(height);stream.Write(seed);stream.Write(HoRTGIAtrousTrace.Pixels.Length);
            foreach(var p in HoRTGIAtrousTrace.Pixels){stream.Write(p.x);stream.Write(p.y);}
        }
        internal void Observe(HoRTGIContext context,RenderTexture baselineBranch,RenderTexture candidateBranch)
        {
            using(var cmd=new CommandBuffer{name="Shared-input physical publication calibration"})
            {
                cmd.SetComputeIntParam(resolve,"_Width",context.width);cmd.SetComputeIntParam(resolve,"_Height",context.height);cmd.SetComputeFloatParam(resolve,"_Scale",context.nrd.AppliedSignalScale);
                cmd.SetComputeBufferParam(resolve,resolveKernel,"_History",context.history);cmd.SetComputeTextureParam(resolve,resolveKernel,"_Raw",context.diffuse.nameID);
                for(int i=0;i<2;i++){cmd.SetComputeTextureParam(resolve,resolveKernel,"_Source",i==0?baselineBranch:candidateBranch);cmd.SetComputeTextureParam(resolve,resolveKernel,"_Output",physical[i]);cmd.DispatchCompute(resolve,resolveKernel,(context.width+7)/8,(context.height+7)/8,1);}
                Graphics.ExecuteCommandBuffer(cmd);
            }
            var a=HoRTGISharedInputAudit.Read(context.PublishedDiffuse.rt);var b=HoRTGISharedInputAudit.Read(physical[0]);var candidate=HoRTGISharedInputAudit.Read(physical[1]);int differences=0;
            for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)
            {
                if(!float.IsFinite(b[i][c])||!float.IsFinite(candidate[i][c]))throw new Exception("Nonfinite physical publication");
                if(BitConverter.SingleToInt32Bits(a[i][c])!=BitConverter.SingleToInt32Bits(b[i][c]))differences++;
                report.maxCalibrationError=Mathf.Max(report.maxCalibrationError,Mathf.Abs(a[i][c]-b[i][c]));
            }
            report.calibratedFrames++;report.calibrationMismatches+=differences;
            if(differences!=0)throw new Exception("Physical baseline calibration mismatch: "+differences);
            if(current==null)throw new Exception("Candidate segment not started");
            spatial.AddPublication(HoRTGISharedInputAudit.Read(context.diffuse.rt),candidate,observed);
            stream.Write(context.frame-1);stream.Write(Time.frameCount);
            foreach(var p in HoRTGIAtrousTrace.Pixels){var v=candidate[p.y*context.width+p.x];stream.Write(v.r);stream.Write(v.g);stream.Write(v.b);}observed++;
        }
        internal void End(string output)
        {
            stream.Dispose();stream=null;current.frames=observed;current.spatial=spatial.Finish();spatial.WritePublicationMeans(Path.Combine(output,current.map));
            current.mapSha256=Hash(File.ReadAllBytes(Path.Combine(output,current.map)));current.streamSha256=Hash(File.ReadAllBytes(Path.Combine(output,current.stream)));segments.Add(current);current=null;spatial=null;
        }
        internal Report Snapshot(){report.segments=segments.ToArray();return report;}
        public void Dispose()
        {
            if(disposed)return;disposed=true;stream?.Dispose();core.ValidationDiffuseStage=null;
            report.restored=true;for(int i=0;i<2;i++){shaders[9+i]=original[i];kernels[9+i]=originalKernels[i];report.restored&=shaders[9+i]==original[i]&&kernels[9+i]==originalKernels[i];physical[i].Release();UnityEngine.Object.DestroyImmediate(physical[i]);}report.released=true;
        }
        static string Hash(byte[] b){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(b)).Replace("-","");}
    }
}
