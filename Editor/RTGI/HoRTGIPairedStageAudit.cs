using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using lilToon.URP.Extensions.NRD;
using lilToon.URP.Extensions.RTGI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // One coherent, fixed DI diffuse policy. Only the owned experimental core changes.
    internal sealed class HoRTGIPairedStageAudit:IDisposable
    {
        [Serializable]internal sealed class ShaderSource{public int pipeline;public string bodyPath,wrapperPath,bodySha256,wrapperSha256;}
        [Serializable]internal sealed class Segment{public int seed,frames;public string map,mapSha256,stream,streamSha256;public HoRTGISpatialAudit.Report spatial;}
        [Serializable]internal sealed class Report
        {
            public bool restored,released;public int calibratedFrames,calibrationMismatches,firstWrites,laterWrites;public float maxCalibrationError;
            public ShaderSource[] sources;public Segment[] segments;public bool gatherBitExact;public int gatherComparedComponents;public StageSegment[] stageSegments;public HoRTGIDenseReferenceAudit.Point addedReference;public float maxAddedGuideError;
        }
        const string DirectoryPath="Assets/HoRTGIPairedStages/";
        readonly ComputeShader[] shaders,original=new ComputeShader[2];readonly int[] kernels,originalKernels=new int[2];
        readonly ComputeShader resolve;readonly int resolveKernel;readonly RenderTexture[] physical=new RenderTexture[2];
        readonly List<Segment> segments=new();internal readonly Report report=new Report();
        readonly HoNrdContext core;readonly HoNrdContext[] cores;HoRTGISpatialAudit spatial;BinaryWriter stream;Segment current;int observed;bool disposed;
        internal HoRTGIPairedStageAudit(HoNrdContext[] cores,int width,int height)
        {
            if(width!=256||height!=256)throw new Exception("Paired stage domain must be256");
            this.cores=cores;this.core=cores[1];var core=this.core;const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            if(cores.Any(c=>c.ValidationDiffuseStage!=null))throw new Exception("Stage hook already owned");
            shaders=(ComputeShader[])typeof(HoNrdContext).GetField("shaders",flags).GetValue(core);kernels=(int[])typeof(HoNrdContext).GetField("kernels",flags).GetValue(core);
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));var sources=new List<ShaderSource>();
            string project=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
            if(!project.Replace('\\','/').EndsWith("/.codex-research/RTGIValidation"))throw new Exception("Dedicated project required");
            System.IO.Directory.CreateDirectory(Path.Combine(project,DirectoryPath));
            var inputs=JsonUtility.FromJson<PolicyInputs>(File.ReadAllText(Path.Combine(root,"Tests~/RTGI/PairedStages/policy-inputs.json")));
            foreach(var item in inputs.entries){var bytes=File.ReadAllBytes(Path.Combine(root,item.path));if(Hash(bytes)!=item.sha256)throw new Exception("Frozen policy source drift");File.WriteAllBytes(Path.Combine(project,DirectoryPath,item.name),bytes);}
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var replacements=new ComputeShader[2];var replacementKernels=new int[2];
            for(int i=0;i<2;i++)
            {
                string name="HoRTGIDiffusePolicy"+(i==0?"AtrousSmem":"Atrous");string wrapper=DirectoryPath+name+".compute",body=DirectoryPath+name+".hlsl";
                replacements[i]=AssetDatabase.LoadAssetAtPath<ComputeShader>(wrapper);if(replacements[i]==null)throw new Exception("Policy import failed: "+wrapper);
                replacementKernels[i]=replacements[i].FindKernel("main");Check(replacements[i]);
                sources.Add(new ShaderSource{pipeline=9+i,bodyPath=body,wrapperPath=wrapper,bodySha256=Hash(File.ReadAllBytes(Path.Combine(project,body))),wrapperSha256=Hash(File.ReadAllBytes(Path.Combine(project,wrapper)))});
            }
            resolve=AssetDatabase.LoadAssetAtPath<ComputeShader>(DirectoryPath+"HoRTGISharedResolve.compute");resolveKernel=resolve.FindKernel("main");Check(resolve);
            for(int i=0;i<2;i++){physical[i]=new RenderTexture(width,height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear){enableRandomWrite=true};if(!physical[i].Create())throw new Exception("Physical output allocation failed");}
            report.sources=sources.ToArray();
            for(int i=0;i<2;i++){original[i]=shaders[9+i];originalKernels[i]=kernels[9+i];shaders[9+i]=replacements[i];kernels[9+i]=replacementKernels[i];}
            if(core.ValidationDiffuseStage!=null)throw new Exception("Experimental core observer already owned");
            try{
                InitializeGather(root);
                cores[0].ValidationDiffuseStage=(cmd,id,source)=>Capture(cmd,0,id,source);
                cores[1].ValidationDiffuseStage=(cmd,id,source)=>Capture(cmd,1,id,source);
            }catch{Dispose();throw;}
        }
        static void Check(ComputeShader shader){foreach(var m in ShaderUtil.GetComputeShaderMessages(shader))if(m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(m.message);}

        internal void Begin(int seed,string output,int width,int height)
        {
            if(current!=null)throw new Exception("Unfinished candidate segment");observed=0;spatial=new HoRTGISpatialAudit(width,height);current=new Segment{seed=seed,map="candidate-publication-"+seed+".bin",stream="candidate-local-"+seed+".bin"};
            stageCurrent=new StageSegment{seed=seed,stream="paired-stages-"+seed+".bin"};stageStream=new BinaryWriter(File.Create(Path.Combine(output,stageCurrent.stream)));
            stageStream.Write(new byte[]{(byte)'H',(byte)'G',(byte)'B',(byte)'1'});foreach(int v in new[]{width,height,seed,2,StageIds.Length,Pixels.Length})stageStream.Write(v);foreach(int v in StageIds)stageStream.Write(v);foreach(var v in Pixels){stageStream.Write(v.x);stageStream.Write(v.y);}
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
            ObserveStages(context);
            spatial.AddPublication(HoRTGISharedInputAudit.Read(context.diffuse.rt),candidate,observed);
            stream.Write(context.frame-1);stream.Write(Time.frameCount);
            foreach(var p in HoRTGIAtrousTrace.Pixels){var v=candidate[p.y*context.width+p.x];stream.Write(v.r);stream.Write(v.g);stream.Write(v.b);}observed++;
        }
        internal void End(string output)
        {
            stageStream.Dispose();stageStream=null;stageCurrent.frames=observed;stageCurrent.streamSha256=Hash(File.ReadAllBytes(Path.Combine(output,stageCurrent.stream)));stageSegments.Add(stageCurrent);stageCurrent=null;
            stream.Dispose();stream=null;current.frames=observed;current.spatial=spatial.Finish();spatial.WritePublicationMeans(Path.Combine(output,current.map));
            current.mapSha256=Hash(File.ReadAllBytes(Path.Combine(output,current.map)));current.streamSha256=Hash(File.ReadAllBytes(Path.Combine(output,current.stream)));segments.Add(current);current=null;spatial=null;
        }
        internal Report Snapshot(){report.segments=segments.ToArray();report.stageSegments=stageSegments.ToArray();return report;}
        public void Dispose()
        {
            if(disposed)return;disposed=true;stream?.Dispose();stageStream?.Dispose();foreach(var c in cores)c.ValidationDiffuseStage=null;gatherPixels?.Dispose();gatherValues?.Dispose();
            report.restored=true;for(int i=0;i<2;i++){shaders[9+i]=original[i];kernels[9+i]=originalKernels[i];report.restored&=shaders[9+i]==original[i]&&kernels[9+i]==originalKernels[i];physical[i].Release();UnityEngine.Object.DestroyImmediate(physical[i]);}report.released=true;
        }
        internal static readonly Vector2Int[] Pixels=HoRTGIAtrousTrace.Pixels.Concat(new[]{new Vector2Int(196,141)}).ToArray();
        internal static readonly int[] StageIds={3,4,5,6,9,101,102,103,104};
        [Serializable]internal sealed class PolicyInput{public string path,name,sha256;}
        [Serializable]internal sealed class PolicyInputs{public PolicyInput[] entries;}
        [Serializable]internal sealed class StageSegment{public int seed,frames;public string stream,streamSha256;public Guide firstGuide,lastGuide;}
        [Serializable]internal sealed class Guide{public int x,y;public Vector4 originalWorld,rabWorld,normalDepth,albedo,errors0,errors1,rayWorld,rayRabWorld,rayNormalDepth,rayAlbedo;public float maxError;}
        [Serializable]sealed class ReferenceFile{public HoRTGIDenseReferenceAudit.Report denseReference;}
        readonly List<StageSegment> stageSegments=new();StageSegment stageCurrent;BinaryWriter stageStream;
        ComputeShader gather;int gatherKernel;GraphicsBuffer gatherPixels,gatherValues;readonly int[,] stageWrites=new int[2,6];
        void InitializeGather(string root)
        {
            report.addedReference=JsonUtility.FromJson<ReferenceFile>(File.ReadAllText(Path.Combine(root,"Tests~/RTGI/DenseReference/results/fixed-reference.json"))).denseReference.points.Single(p=>p.x==196&&p.y==141);
            if(!report.addedReference.converged)throw new Exception("Added reference must be qualified");
            gather=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTGI/Shaders/HoRTGIPairedGather.compute");gatherKernel=gather.FindKernel("Gather");Check(gather);
            gatherPixels=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Pixels.Length,8);gatherPixels.SetData(Pixels);
            gatherValues=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2*StageIds.Length*Pixels.Length,16);
            var image=new Texture2D(256,256,TextureFormat.RGBAFloat,false,true);var values=new Color[65536];
            for(int i=0;i<values.Length;i++)values[i]=new Color((i+1)*1e-12f,-(i+1)*1e-9f,(i+1)*.00001f,(i+1)*123.125f);
            try
            {
                image.SetPixels(values);image.Apply();using var cmd=new CommandBuffer{name="F32 sparse stage copy tiny-value qualification"};
                BindGather(cmd,image,0);Graphics.ExecuteCommandBuffer(cmd);var actual=new Vector4[gatherValues.count];gatherValues.GetData(actual);
                for(int i=0;i<Pixels.Length;i++)for(int c=0;c<4;c++)if(BitConverter.SingleToInt32Bits(actual[i][c])!=BitConverter.SingleToInt32Bits(values[Pixels[i].y*256+Pixels[i].x][c]))throw new Exception("Gather F32 precision loss");
                report.gatherComparedComponents=Pixels.Length*4;report.gatherBitExact=true;
            }
            finally{UnityEngine.Object.DestroyImmediate(image);}
        }
        void BindGather(CommandBuffer cmd,RenderTargetIdentifier source,int offset)
        {
            cmd.SetComputeIntParam(gather,"_Count",Pixels.Length);cmd.SetComputeIntParam(gather,"_Offset",offset);
            cmd.SetComputeBufferParam(gather,gatherKernel,"_Pixels",gatherPixels);cmd.SetComputeBufferParam(gather,gatherKernel,"_Values",gatherValues);cmd.SetComputeTextureParam(gather,gatherKernel,"_Source",source);cmd.DispatchCompute(gather,gatherKernel,(Pixels.Length+31)/32,1,1);
        }
        internal void BeginFrame(){Array.Clear(stageWrites,0,stageWrites.Length);}
        void Capture(CommandBuffer cmd,int branch,int pipeline,RenderTargetIdentifier source)
        {
            int p=Array.IndexOf(HoRTGILocalStageAudit.CaptureIds,pipeline);if(p<0)throw new Exception("Unexpected paired stage writer");int ordinal=++stageWrites[branch,p];
            int id=pipeline==10?100+ordinal:pipeline;int stage=Array.IndexOf(StageIds,id);if(stage<0)throw new Exception("Unexpected paired stage ordinal");
            BindGather(cmd,source,(branch*StageIds.Length+stage)*Pixels.Length);
            if(branch==1){if(pipeline==9)report.firstWrites++;if(pipeline==10)report.laterWrites++;}
        }
        void ObserveStages(HoRTGIContext context)
        {
            stageStream.Write(context.frame-1);stageStream.Write(Time.frameCount);
            for(int b=0;b<2;b++)for(int p=0;p<6;p++){int n=stageWrites[b,p];if(n!=(p==5?4:1))throw new Exception("Missing paired stage dispatch");stageStream.Write(n);}
            var data=new Vector4[gatherValues.count];gatherValues.GetData(data);
            foreach(var v in data){for(int c=0;c<4;c++)if(!float.IsFinite(v[c]))throw new Exception("Nonfinite stage capture");stageStream.Write(v.x);stageStream.Write(v.y);stageStream.Write(v.z);stageStream.Write(v.w);}
        }
        internal void ValidateAddedGuide(HoRTGIDenseReferenceFeature feature,bool first)
        {
            if(feature.pointCount!=Pixels.Length)throw new Exception("Added primary probe not scheduled");var probes=new HoRTGIDenseReferenceFeature.PrimaryProbe[Pixels.Length];feature.probe.GetData(probes);var rays=new Vector4[Pixels.Length*4];feature.rayProbe.GetData(rays);int i=Pixels.Length-1;var p=probes[i];var r=report.addedReference;
            float error=Mathf.Max(Vector3.Distance(p.rabWorld,r.primaryWorld),Vector3.Distance(p.normalDepth,r.normal),Vector3.Distance(p.albedo,r.albedo),Mathf.Abs(p.normalDepth.w-r.viewDepth));
            if(p.originalWorld.w<.5f||p.rabWorld.w<.5f)throw new Exception("Invalid added primary");var v=new[]{p.originalWorld,p.rabWorld,p.normalDepth,p.albedo};
            for(int c=0;c<4;c++){if(!float.IsFinite(p.errors0[c])||!float.IsFinite(p.errors1[c]))throw new Exception("Invalid added primary errors");error=Mathf.Max(error,p.errors0[c],p.errors1[c]);for(int k=0;k<4;k++)error=Mathf.Max(error,Mathf.Abs(v[k][c]-rays[i*4+k][c]));}
            if(!float.IsFinite(error)||error>.0005f)throw new Exception("Added full/RAB/history/ray/reference guide mismatch");report.maxAddedGuideError=Mathf.Max(report.maxAddedGuideError,error);
            var g=new Guide{x=196,y=141,originalWorld=p.originalWorld,rabWorld=p.rabWorld,normalDepth=p.normalDepth,albedo=p.albedo,errors0=p.errors0,errors1=p.errors1,rayWorld=rays[i*4],rayRabWorld=rays[i*4+1],rayNormalDepth=rays[i*4+2],rayAlbedo=rays[i*4+3],maxError=error};if(first)stageCurrent.firstGuide=g;else stageCurrent.lastGuide=g;
        }
        static string Hash(byte[] b){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(b)).Replace("-","");}
    }
}
