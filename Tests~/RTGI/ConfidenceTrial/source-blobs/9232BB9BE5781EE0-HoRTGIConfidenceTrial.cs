using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.RTGI.NRD;
using lilToon.URP.Extensions.RTDI.NRD;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace lilToon.URP.Extensions.Editor.RTGI
{
    // Isolated constant-plane/static-primary experiment; never a product policy.
    internal sealed class HoRTGIConfidenceTrial:IDisposable
    {
        [Serializable]internal sealed class Frame{public int phase,index;public bool baselineReset,hardReset,confidenceEnabled;public int unchangedBranch0,unchangedBranch1;public string rawSha,baselineSha,keepSha,confidenceSha,confidenceInputSha;}
        [Serializable]internal sealed class Report{public bool restored,released,independentPools;public long externalBytes;public string stream,streamSha256;public Frame[] frames;public string scope="Static primary/constant material controlled trial only; no quality or production acceptance";}
        readonly HoRTGIContext context;readonly HoRTGISelectionLineageAudit lineage;readonly HoRTGINrdContext source;readonly HoRTDINrdContext[] branches=new HoRTDINrdContext[2];
        readonly RenderTexture[] outputs=new RenderTexture[2],validation=new RenderTexture[2];readonly RenderTexture confidence,previousPrimary;readonly ComputeShader shader;readonly int kernel;
        readonly Action<CommandBuffer,HoRTDINrdNative.CommonSettings,HoRTDINrdNative.RelaxSettings,IReadOnlyDictionary<string,RenderTargetIdentifier>> previous;
        readonly List<Frame> frames=new();readonly string output;readonly BinaryWriter stream;readonly Report report=new();Frame current;bool disposed;internal int Phase;internal bool HardReset;
        internal HoRTGIConfidenceTrial(HoRTGIContext context,HoRTGISelectionLineageAudit lineage,string output)
        {
            this.context=context;this.lineage=lineage;this.output=output;source=context.nrd;if(source.ValidationRecordInputs!=null)throw new Exception("NRD hook owned");previous=source.ValidationRecordInputs;
            if(context.width!=64||context.height!=64)throw new Exception("Small fixed primary scope only");
            shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/HoRTGISelectionLineage/EndpointConfidence.compute");if(shader==null)throw new Exception("Generate confidence shader first");kernel=shader.FindKernel("Confidence");
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;var shaders=(ComputeShader[])typeof(HoRTDINrdContext).GetField("shaders",flags).GetValue(source.core);
            for(int i=0;i<2;i++){branches[i]=new HoRTDINrdContext(shaders,HoRTGINrdManifest.DenoiserType,HoRTGINrdManifest.Identifiers,HoRTGINrdManifest.Resources,highPrecisionColorPool:true);branches[i].Prepare(64,64);outputs[i]=Texture(RenderTextureFormat.ARGBFloat);validation[i]=Texture(RenderTextureFormat.ARGBHalf);}
            foreach(string name in new[]{"permanent","transient","constants"}){var arrays=new[]{source.core,branches[0],branches[1]}.Select(v=>(Array)typeof(HoRTDINrdContext).GetField(name,flags).GetValue(v)).ToArray();for(int i=0;i<arrays[0].Length;i++)if(ReferenceEquals(arrays[0].GetValue(i),arrays[1].GetValue(i))||ReferenceEquals(arrays[1].GetValue(i),arrays[2].GetValue(i)))throw new Exception("Writable pools shared");}report.independentPools=true;
            confidence=Texture(RenderTextureFormat.RFloat);previousPrimary=Texture(RenderTextureFormat.ARGBFloat);report.externalBytes=64L*64*(2*24+4+16);
            report.stream="confidence-images.bin";stream=new BinaryWriter(File.Create(Path.Combine(output,report.stream)));stream.Write(new byte[]{72,71,67,49});stream.Write(64);stream.Write(64);stream.Write(5);source.ValidationRecordInputs=Record;
        }
        static RenderTexture Texture(RenderTextureFormat format){var t=new RenderTexture(64,64,0,format,RenderTextureReadWrite.Linear){enableRandomWrite=true};if(!t.Create())throw new Exception("Trial texture allocation failed");return t;}
        void Record(CommandBuffer cmd,HoRTDINrdNative.CommonSettings common,HoRTDINrdNative.RelaxSettings settings,IReadOnlyDictionary<string,RenderTargetIdentifier> inputs)
        {
            current=new Frame{phase=Phase,index=frames.Count,baselineReset=common.accumulationMode==2,hardReset=HardReset,confidenceEnabled=Phase!=0};
            if(frames.Count==0&&!HardReset)throw new Exception("Trial initial reset required");
            cmd.SetComputeBufferParam(shader,kernel,"_Records",lineage.EndpointRecords);cmd.SetComputeIntParam(shader,"_QueryBase",18*context.arrayPitch+8*4096);cmd.SetComputeIntParam(shader,"_FirstFrame",frames.Count==0?1:0);
            cmd.SetComputeTextureParam(shader,kernel,"_Confidence",confidence);cmd.SetComputeTextureParam(shader,kernel,"_PreviousPrimary",previousPrimary);cmd.DispatchCompute(shader,kernel,8,8,1);
            for(int i=0;i<2;i++){var resources=inputs.ToDictionary(p=>p.Key,p=>p.Value);resources["OUT_DIFF_RADIANCE_HITDIST"]=outputs[i];resources["OUT_VALIDATION"]=validation[i];var copy=common;
                if(Phase!=0)copy.accumulationMode=(byte)(HardReset?2:0);
                if(i==1&&Phase!=0){copy.confidence=1;resources["IN_DIFF_CONFIDENCE"]=confidence;}
                foreach(var p in inputs.Where(p=>p.Key.StartsWith("IN_",StringComparison.Ordinal)&&p.Key!="IN_DIFF_CONFIDENCE"))if(!resources[p.Key].Equals(p.Value))throw new Exception("Trial changed shared input");
                branches[i].Record(cmd,copy,resources,settings);
            }
        }
        internal void Observe()
        {
            if(current==null)throw new Exception("NRD trial did not record");foreach(var m in ShaderUtil.GetComputeShaderMessages(shader))if(m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(m.message);
            var images=new[]{HoRTGISharedInputAudit.Read(context.diffuse.rt),HoRTGISharedInputAudit.Read(source.output.rt),HoRTGISharedInputAudit.Read(outputs[0]),HoRTGISharedInputAudit.Read(outputs[1]),HoRTGISharedInputAudit.Read(confidence)};
            var hashes=new string[5];for(int j=0;j<images.Length;j++){using var ms=new MemoryStream();using var writer=new BinaryWriter(ms);foreach(var value in images[j])for(int k=0;k<4;k++){if(!float.IsFinite(value[k]))throw new Exception("Trial nonfinite output");writer.Write(value[k]);}var bytes=ms.ToArray();hashes[j]=Hash(bytes);stream.Write(bytes);}
            current.rawSha=hashes[0];current.baselineSha=hashes[1];current.keepSha=hashes[2];current.confidenceSha=hashes[3];current.confidenceInputSha=hashes[4];
            if(Phase==0&&(hashes[1]!=hashes[2]||hashes[1]!=hashes[3]))throw new Exception("Static shared-input NRD parity failed");frames.Add(current);current=null;
        }
        internal void Save(){stream.Dispose();report.frames=frames.ToArray();report.streamSha256=Hash(File.ReadAllBytes(Path.Combine(output,report.stream)));File.WriteAllText(Path.Combine(output,"confidence-trial.json"),JsonUtility.ToJson(report,true));Debug.Log("[HoGIConfidenceTrial] COMPLETE frames="+frames.Count);}
        static string Hash(byte[] b){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(b)).Replace("-","");}
        public void Dispose(){if(disposed)return;disposed=true;source.ValidationRecordInputs=previous;report.restored=source.ValidationRecordInputs==previous;stream.Dispose();foreach(var branch in branches)branch.Dispose();foreach(var t in outputs.Concat(validation).Concat(new[]{confidence,previousPrimary})){t.Release();UnityEngine.Object.DestroyImmediate(t);}report.released=true;report.frames=frames.ToArray();File.WriteAllText(Path.Combine(output,"confidence-trial.json"),JsonUtility.ToJson(report,true));}
    }
    public static partial class HoRTGICastleValidation
    {
        public static void RunConfidenceTrialQualification()=>Run(false,64,denseScheduled:true,lineageAudit:true,lineageFrames:16,secondaryReplay:true,endpointAudit:true,endpointConfidence:true,validationTag:"confidence-trial-qualification");
        public static void RunConfidenceTrial()=>Run(false,64,denseScheduled:true,lineageAudit:true,lineageFrames:128,secondaryReplay:true,endpointAudit:true,endpointConfidence:true,validationTag:"confidence-trial");
        public static void RunConfidenceTrialCompatibility()=>Run(true,64,denseScheduled:true,lineageAudit:true,lineageFrames:128,secondaryReplay:true,endpointAudit:true,endpointConfidence:true,validationTag:"confidence-trial");
    }
}
