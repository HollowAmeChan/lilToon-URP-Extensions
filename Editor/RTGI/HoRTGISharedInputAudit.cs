using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using lilToon.URP.Extensions.RTDI.NRD;
using lilToon.URP.Extensions.RTGI.NRD;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    internal sealed class HoRTGISharedInputAudit:IDisposable
    {
        [Serializable]internal sealed class Frame
        {
            public int shaderFrame,applicationFrame,comparedComponents,mismatches0,mismatches1;public bool baselineReset,isolatedReset1;
            public float maxError0,maxError1;public string baselineSha256,branch0Sha256,branch1Sha256,commonSha256,settingsSha256;
        }
        [Serializable]internal sealed class Report
        {
            public bool restoredHook,released,independentPools,sharedInputBindings;public int recorded,width,height;
            public Frame[] frames;public HoRTGIAtrousTrace.Report noop;public HoRTGIPairedStageAudit.Report paired;public long diagnosticLogicalBytes;
        }
        readonly HoRTGINrdContext baseline;readonly HoRTDINrdContext[] branches=new HoRTDINrdContext[2];
        readonly RenderTexture[] outputs=new RenderTexture[2],validations=new RenderTexture[2];
        readonly HoRTGIAtrousTrace noop;internal readonly HoRTGIPairedStageAudit paired;readonly List<Frame> frames=new();readonly Report report;
        readonly Action<CommandBuffer,HoRTDINrdNative.CommonSettings,HoRTDINrdNative.RelaxSettings,IReadOnlyDictionary<string,RenderTargetIdentifier>> previous;
        Frame current;bool disposed;internal bool isolateBranch1;
        internal HoRTGISharedInputAudit(HoRTGINrdContext baseline,int width,int height,bool pairedStages=false)
        {
            this.baseline=baseline;if(baseline.ValidationRecordInputs!=null)throw new Exception("Shared-input hook already owned");previous=baseline.ValidationRecordInputs;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var shaders=(ComputeShader[])typeof(HoRTDINrdContext).GetField("shaders",flags).GetValue(baseline.core);
            report=new Report{width=width,height=height,independentPools=true,sharedInputBindings=true};
            for(int i=0;i<2;i++)
            {
                branches[i]=new HoRTDINrdContext(shaders,HoRTGINrdManifest.DenoiserType,HoRTGINrdManifest.Identifiers,HoRTGINrdManifest.Resources,highPrecisionColorPool:true);branches[i].Prepare(width,height);
                outputs[i]=new RenderTexture(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){enableRandomWrite=true};validations[i]=new RenderTexture(width,height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear){enableRandomWrite=true};
                if(!outputs[i].Create()||!validations[i].Create())throw new Exception("Shared output allocation failed");report.diagnosticLogicalBytes+=(long)width*height*24;
            }
            foreach(string name in new[]{"permanent","transient","constants"})
            {
                var a=(Array)typeof(HoRTDINrdContext).GetField(name,flags).GetValue(branches[0]);var b=(Array)typeof(HoRTDINrdContext).GetField(name,flags).GetValue(branches[1]);var c=(Array)typeof(HoRTDINrdContext).GetField(name,flags).GetValue(baseline.core);
                for(int i=0;i<a.Length;i++)if(ReferenceEquals(a.GetValue(i),b.GetValue(i))||ReferenceEquals(a.GetValue(i),c.GetValue(i)))throw new Exception("NRD branches share writable resources");
            }
            if(pairedStages){paired=new HoRTGIPairedStageAudit(branches,width,height);report.paired=paired.report;}else{noop=new HoRTGIAtrousTrace(branches[1],true,true);report.noop=noop.report;}baseline.ValidationRecordInputs=Record;
        }
        void Record(CommandBuffer cmd,HoRTDINrdNative.CommonSettings common,HoRTDINrdNative.RelaxSettings settings,IReadOnlyDictionary<string,RenderTargetIdentifier> source)
        {
            paired?.BeginFrame();current=new Frame{shaderFrame=(int)common.frameIndex,applicationFrame=Time.frameCount,baselineReset=common.accumulationMode==2,isolatedReset1=isolateBranch1,commonSha256=Hash(StructBytes(common)),settingsSha256=Hash(StructBytes(settings))};report.recorded++;
            for(int i=0;i<2;i++)
            {
                var resources=source.ToDictionary(p=>p.Key,p=>p.Value);resources["OUT_DIFF_RADIANCE_HITDIST"]=outputs[i];resources["OUT_VALIDATION"]=validations[i];
                foreach(var p in source.Where(p=>p.Key.StartsWith("IN_",StringComparison.Ordinal)))if(!resources[p.Key].Equals(p.Value))throw new Exception("Shared input identifier changed");
                var copy=common;if(i==1&&isolateBranch1)copy.accumulationMode=2;
                branches[i].Record(cmd,copy,resources,settings);
            }
        }
        internal void Observe()
        {
            if(current==null)throw new Exception("No actual NRD input callback");
            var a=Read(baseline.output.rt);var b=Read(outputs[0]);var c=Read(outputs[1]);current.comparedComponents=a.Length*4;
            for(int i=0;i<a.Length;i++)for(int k=0;k<4;k++)
            {
                if(!float.IsFinite(a[i][k])||!float.IsFinite(b[i][k])||!float.IsFinite(c[i][k]))throw new Exception("Nonfinite shared NRD output");
                if(BitConverter.SingleToInt32Bits(a[i][k])!=BitConverter.SingleToInt32Bits(b[i][k]))current.mismatches0++;
                if(BitConverter.SingleToInt32Bits(a[i][k])!=BitConverter.SingleToInt32Bits(c[i][k]))current.mismatches1++;
                current.maxError0=Mathf.Max(current.maxError0,Mathf.Abs(a[i][k]-b[i][k]));current.maxError1=Mathf.Max(current.maxError1,Mathf.Abs(a[i][k]-c[i][k]));
            }
            current.baselineSha256=Hash(ColorBytes(a));current.branch0Sha256=Hash(ColorBytes(b));current.branch1Sha256=Hash(ColorBytes(c));frames.Add(current);current=null;
        }
        internal Report Snapshot(){report.frames=frames.ToArray();if(paired!=null)report.paired=paired.Snapshot();return report;}
        internal void ObservePaired(lilToon.URP.Extensions.RTGI.HoRTGIContext context)=>paired?.Observe(context,outputs[0],outputs[1]);
        public void Dispose()
        {
            if(disposed)return;disposed=true;baseline.ValidationRecordInputs=previous;report.restoredHook=baseline.ValidationRecordInputs==previous;noop?.Dispose();paired?.Dispose();
            for(int i=0;i<2;i++){branches[i].Dispose();outputs[i].Release();validations[i].Release();UnityEngine.Object.DestroyImmediate(outputs[i]);UnityEngine.Object.DestroyImmediate(validations[i]);}report.released=true;
        }
        internal static Color[] Read(RenderTexture texture)
        {
            var image=new Texture2D(texture.width,texture.height,TextureFormat.RGBAFloat,false,true);var old=RenderTexture.active;
            try{RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();return image.GetPixels();}
            finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
        }
        static byte[] ColorBytes(Color[] values){using var m=new MemoryStream();using var w=new BinaryWriter(m);foreach(var v in values){w.Write(v.r);w.Write(v.g);w.Write(v.b);w.Write(v.a);}return m.ToArray();}
        static byte[] StructBytes<T>(T value)where T:struct{int n=Marshal.SizeOf<T>();byte[] data=new byte[n];IntPtr p=Marshal.AllocHGlobal(n);try{Marshal.Copy(data,0,p,n);Marshal.StructureToPtr(value,p,false);Marshal.Copy(p,data,0,n);return data;}finally{Marshal.FreeHGlobal(p);}}
        static string Hash(byte[] bytes){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","");}
    }
    public static partial class HoRTGICastleValidation
    {
        public static async void RunSharedInputSmall()
        {
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-gi-shared-small/report.json"));Directory.CreateDirectory(Path.GetDirectoryName(output));var oldDefault=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;UniversalRenderPipelineAsset pipeline=null;UniversalRendererData renderer=null;GameObject cameraObject=null;RenderTexture warmup=null;HoRTGINrdContext nrd=null;HoRTGISharedInputAudit twin=null;Texture2D upload=null;bool pass=false;string error=null;
            try
            {
                if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"))throw new Exception("Dedicated project required");EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                renderer=ScriptableObject.CreateInstance<UniversalRendererData>();pipeline=UniversalRenderPipelineAsset.Create(renderer);GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                cameraObject=new GameObject("Empty shared-input warmup");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=0;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                warmup=new RenderTexture(64,64,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);warmup.Create();
                for(int i=0;i<3;i++){await NextEditorTick();RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=warmup});}
                const string root="Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTGI/Shaders/NRD/";
                var shaders=Enumerable.Range(0,HoRTGINrdManifest.Identifiers.Length).Select(i=>AssetDatabase.LoadAssetAtPath<ComputeShader>(root+"HoRTGINrd"+i.ToString("D2")+".compute")).ToArray();
                nrd=new HoRTGINrdContext(shaders);nrd.Prepare(64,64);twin=new HoRTGISharedInputAudit(nrd,64,64);upload=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
                for(int frame=0;frame<64;frame++)
                {
                    await NextEditorTick();var pixels=new Color[4096];
                    for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(frame<16?.01f:0,.002f,frame<16?0:.01f,1);
                    upload.SetPixels(pixels);upload.Apply();Graphics.Blit(upload,nrd.signal.rt);
                    using var cmd=new CommandBuffer{name="Shared-input small qualification"};cmd.SetRenderTarget(nrd.normal.nameID);cmd.ClearRenderTarget(false,true,new Color(.5f,.5f,1,0));cmd.SetRenderTarget(nrd.viewZ.nameID);cmd.ClearRenderTarget(false,true,new Color(3,0,0,0));Graphics.ExecuteCommandBuffer(cmd);
                    twin.isolateBranch1=frame==24;nrd.ValidationTimeSeconds=(frame+1)/60.0;
                    using var dispatch=new CommandBuffer{name="Shared exact inputs, three independent instances"};nrd.Record(dispatch,Matrix4x4.identity,Matrix4x4.Perspective(45,1,.1f,100),frame,frame==0||frame==48,(frame+1)/60.0,false);Graphics.ExecuteCommandBuffer(dispatch);twin.Observe();
                }
                var r=twin.Snapshot();if(r.frames.Any(f=>f.mismatches0!=0)||r.frames.Take(24).Any(f=>f.mismatches1!=0)||!r.frames.Skip(24).Take(24).Any(f=>f.mismatches1>0)||r.frames.Skip(48).Any(f=>f.mismatches1!=0))throw new Exception("Shared equality/isolation/reset qualification failed");pass=true;
            }
            catch(Exception e){error=e.ToString();Debug.LogException(e);}
            finally{twin?.Dispose();File.WriteAllText(output,JsonUtility.ToJson(new SharedSmallResult{pass=pass,error=error,shared=twin?.Snapshot()},true));nrd?.Dispose();GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;if(warmup!=null){warmup.Release();UnityEngine.Object.DestroyImmediate(warmup);}if(cameraObject!=null)UnityEngine.Object.DestroyImmediate(cameraObject);if(pipeline!=null)UnityEngine.Object.DestroyImmediate(pipeline);if(renderer!=null)UnityEngine.Object.DestroyImmediate(renderer);if(upload!=null)UnityEngine.Object.DestroyImmediate(upload);}
            Debug.Log("[HoGISharedSmall] END pass="+pass);EditorApplication.Exit(pass?0:1);
        }
        [Serializable]sealed class SharedSmallResult{public bool pass;public string error;public HoRTGISharedInputAudit.Report shared;}
    }
}
