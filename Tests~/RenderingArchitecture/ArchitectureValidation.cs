using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using lilToon.URP.Extensions.NRD;
using lilToon.URP.Extensions.RTDI.NRD;
using lilToon.URP.Extensions.RTGI.NRD;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Legacy=lilToon.URP.Extensions.Editor.NrdArchitecture.Legacy;

namespace lilToon.URP.Extensions.Editor.NrdArchitecture
{
    public static class ArchitectureValidation
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        [Serializable] public sealed class Row
        {public string channel,pool,format;public int index,width,height,factor;public long bytes;}
        [Serializable] public sealed class Result
        {
            public bool pass,independentHistories,isolatedResetObserved;
            public int gpuFrames,gpuComponents,diDispatchCases;
            public string[] gpuHashes,diDispatchHashes;
            public Row[] pools;
            public long diPoolBytes,giPoolBytes,matchingTransientBytes64,matchingTransientLayoutBytes1080;
            public string memoryScope="Actual64x64 texture payload. 1080 is descriptor arithmetic only; pools remain separately owned. No memory saving claimed for extraction.";
            public string error;
        }
        static readonly List<UnityEngine.Object> owned=new();
        static string Hash(byte[] b){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(b)).Replace("-","");}
        static byte[] Struct<T>(T v) where T:struct
        {
            int n=Marshal.SizeOf<T>();var b=new byte[n];IntPtr p=Marshal.AllocHGlobal(n);
            try{Marshal.Copy(b,0,p,n);Marshal.StructureToPtr(v,p,false);Marshal.Copy(p,b,0,n);return b;}
            finally{Marshal.FreeHGlobal(p);}
        }
        static T ConvertStruct<T,U>(U v) where T:struct where U:struct
        {
            var b=Struct(v);Require(b.Length==Marshal.SizeOf<T>(),"ABI size changed");IntPtr p=Marshal.AllocHGlobal(b.Length);
            try{Marshal.Copy(b,0,p,b.Length);return Marshal.PtrToStructure<T>(p);}finally{Marshal.FreeHGlobal(p);}
        }
        static object Field(object v,string name)=>v.GetType().GetField(name,Flags)?.GetValue(v)??throw new Exception("Missing field "+name);
        static ComputeShader[] Library(string channel,int count)=>Enumerable.Range(0,count).Select(i=>AssetDatabase.LoadAssetAtPath<ComputeShader>($"Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/{channel}/Shaders/NRD/Ho{channel}Nrd{i:00}.compute")).ToArray();
        static RenderTexture Texture(RenderTextureFormat f)
        {var t=new RenderTexture(64,64,0,f,RenderTextureReadWrite.Linear){enableRandomWrite=true};Require(t.Create(),"Texture allocation");owned.Add(t);return t;}
        static byte[] Pixels(RenderTexture t)
        {
            var old=RenderTexture.active;var image=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
            try{RenderTexture.active=t;image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();using var m=new MemoryStream();using var w=new BinaryWriter(m);foreach(var c in image.GetPixels())for(int k=0;k<4;k++){Require(float.IsFinite(c[k]),"Nonfinite NRD output");w.Write(c[k]);}return m.ToArray();}
            finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
        }
        static HoNrdNative.CommonSettings Common(int frame,bool reset)=>HoNrdNative.CommonSettings.Create(64,64,Matrix4x4.identity,Matrix4x4.Perspective(60,1,.1f,50),Matrix4x4.identity,Matrix4x4.Perspective(60,1,.1f,50),frame,reset);
        static void Require(bool b,string message){if(!b)throw new Exception(message);}
        public static void Run()
        {
            var r=new Result();string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../nrd-architecture"));Directory.CreateDirectory(output);int code=1;
            var oldDefault=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;
            try
            {
                Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"),"Dedicated project required");ShaderUtil.allowAsyncCompilation=false;
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
                var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();owned.Add(renderer);var pipeline=UniversalRenderPipelineAsset.Create(renderer);owned.Add(pipeline);GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                var holder=new GameObject("NRD architecture pipeline initialization");owned.Add(holder);var camera=holder.AddComponent<Camera>();camera.enabled=false;var target=Texture(RenderTextureFormat.ARGBHalf);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                var giShaders=Library("RTGI",HoRTGINrdManifest.Identifiers.Length);var diShaders=Library("RTDI",HoRTDINrdManifest.Identifiers.Length);
                using var oldGi=new Legacy.HoLegacyNrdContext(giShaders,HoRTGINrdManifest.DenoiserType,HoRTGINrdManifest.Identifiers,HoRTGINrdManifest.Resources,true);
                using var newGi=new HoNrdContext(giShaders,HoRTGINrdManifest.DenoiserType,HoRTGINrdManifest.Identifiers,HoRTGINrdManifest.Resources,true);
                using var oldDi=new Legacy.HoLegacyNrdContext(diShaders);
                using var newDi=new HoRTDINrdContext(diShaders);
                oldGi.Prepare(64,64);newGi.Prepare(64,64);oldDi.Prepare(64,64);newDi.Prepare(64,64);
                r.pools=Rows(newDi,"DI").Concat(Rows(newGi,"GI")).ToArray();
                ComparePoolLayouts(Rows(oldDi,"DI"),Rows(newDi,"DI"));ComparePoolLayouts(Rows(oldGi,"GI"),Rows(newGi,"GI"));
                r.diPoolBytes=r.pools.Where(x=>x.channel=="DI").Sum(x=>x.bytes);r.giPoolBytes=r.pools.Where(x=>x.channel=="GI").Sum(x=>x.bytes);
                var diPermanent=(RTHandle[])Field(newDi,"permanent");var giPermanent=(RTHandle[])Field(newGi,"permanent");r.independentHistories=!diPermanent.Any(a=>giPermanent.Any(b=>a.rt.GetInstanceID()==b.rt.GetInstanceID()));Require(r.independentHistories,"DI/GI histories alias");
                var matches=r.pools.Where(x=>x.pool=="transient").GroupBy(x=>x.format+"/"+x.factor);
                foreach(var group in matches){int count=Math.Min(group.Count(x=>x.channel=="DI"),group.Count(x=>x.channel=="GI"));var first=group.First();r.matchingTransientBytes64+=count*first.bytes;r.matchingTransientLayoutBytes1080+=count*Payload((1920+first.factor-1)/first.factor,(1080+first.factor-1)/first.factor,(GraphicsFormat)Enum.Parse(typeof(GraphicsFormat),first.format));}
                var signal=Texture(RenderTextureFormat.ARGBHalf);var normal=Texture(RenderTextureFormat.ARGBHalf);var z=Texture(RenderTextureFormat.RFloat);var confidence=Texture(RenderTextureFormat.RFloat);var a=Texture(RenderTextureFormat.ARGBFloat);var b=Texture(RenderTextureFormat.ARGBFloat);var validation=Texture(RenderTextureFormat.ARGBHalf);
                var inputs=new Dictionary<string,RenderTargetIdentifier>{{"IN_MV",Texture2D.blackTexture},{"IN_NORMAL_ROUGHNESS",normal},{"IN_VIEWZ",z},{"IN_DIFF_RADIANCE_HITDIST",signal},{"IN_DIFF_CONFIDENCE",confidence},{"IN_DISOCCLUSION_THRESHOLD_MIX",Texture2D.blackTexture},{"OUT_DIFF_RADIANCE_HITDIST",a},{"OUT_VALIDATION",validation}};
                var fill=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/HoNrdArchitecture/ArchitectureFill.compute");int kernel=fill.FindKernel("Fill");var hashes=new List<string>();
                using var cmd=new CommandBuffer();
                for(int frame=0;frame<34;frame++)
                {
                    cmd.Clear();cmd.SetComputeIntParam(fill,"_Frame",frame);cmd.SetComputeTextureParam(fill,kernel,"_Signal",signal);cmd.SetComputeTextureParam(fill,kernel,"_Normal",normal);cmd.SetComputeTextureParam(fill,kernel,"_ViewZ",z);cmd.SetComputeTextureParam(fill,kernel,"_Confidence",confidence);cmd.DispatchCompute(fill,kernel,8,8,1);
                    bool reset=frame==0||frame==16||frame==33;var common=Common(frame,reset);common.confidence=1;var settings=HoRTGINrdContext.ReferenceSettings(1f/60);
                    inputs["OUT_DIFF_RADIANCE_HITDIST"]=a;oldGi.Record(cmd,ConvertStruct<Legacy.HoLegacyNrdNative.CommonSettings,HoNrdNative.CommonSettings>(common),inputs,ConvertStruct<Legacy.HoLegacyNrdNative.RelaxSettings,HoNrdNative.RelaxSettings>(settings));
                    if(frame==32)common.accumulationMode=2;inputs["OUT_DIFF_RADIANCE_HITDIST"]=b;newGi.Record(cmd,common,inputs,settings);Graphics.ExecuteCommandBuffer(cmd);
                    var pa=Pixels(a);var pb=Pixels(b);
                    if(frame==32){r.isolatedResetObserved=!pa.SequenceEqual(pb);Require(r.isolatedResetObserved,"Single-instance reset did not isolate history");}
                    else{Require(pa.SequenceEqual(pb),"GPU parity failed frame "+frame);r.gpuFrames++;r.gpuComponents+=64*64*4;hashes.Add(Hash(pa));}
                }
                r.gpuHashes=hashes.ToArray();Require(hashes.Distinct().Count()>3,"GPU test did not exercise changing input");
                // DI default/cutout/stability/SceneView/supplied profiles: compare
                // the actual native dispatches, resources and uploaded constants.
                var traceHashes=new List<string>();var external=HoNrdResources.Types.ToDictionary(x=>x,x=>new RenderTargetIdentifier(Texture2D.blackTexture));
                for(int frame=0;frame<64;frame++)
                {
                    oldDi.SceneViewPreview=newDi.SceneViewPreview=frame>=48;oldDi.HasCutoutVisibility=newDi.HasCutoutVisibility=frame>=8&&frame<24;
                    double time=frame<48?frame/60.0:frame-47;bool oldReset,newReset;float od=oldDi.SetRenderTime(time,out oldReset),nd=newDi.SetRenderTime(time,out newReset);Require(od==nd&&oldReset==newReset,"DI clock changed");
                    bool changed=frame==0||frame==40;Require(oldDi.ObserveLightingStability(changed)==newDi.ObserveLightingStability(changed)&&oldDi.StableDiffuseHistory==newDi.StableDiffuseHistory,"DI stability changed");
                    var c=Common(frame,frame==0||oldReset);c.timeDelta=nd;var lc=ConvertStruct<Legacy.HoLegacyNrdNative.CommonSettings,HoNrdNative.CommonSettings>(c);cmd.Clear();
                    if(frame<56){oldDi.Record(cmd,lc,external);newDi.Record(cmd,c,external);}else{var s=HoRTGINrdContext.ReferenceSettings(1f/60);oldDi.Record(cmd,lc,external,ConvertStruct<Legacy.HoLegacyNrdNative.RelaxSettings,HoNrdNative.RelaxSettings>(s));newDi.Record(cmd,c,external,s);}
                    Require(Struct(oldDi.AppliedSettings).SequenceEqual(Struct(newDi.AppliedSettings)),"DI applied settings changed");Require(oldDi.SceneViewAccumulatedFrames==newDi.SceneViewAccumulatedFrames,"DI preview budget changed");
                    byte[] ta=Trace(oldDi),tb=Trace(newDi);Require(ta.SequenceEqual(tb),"DI native dispatch changed frame "+frame);traceHashes.Add(Hash(ta));r.diDispatchCases++;
                }
                cmd.Clear();r.diDispatchHashes=traceHashes.ToArray();foreach(var shader in giShaders.Concat(diShaders).Append(fill))foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
                r.pass=true;code=0;Debug.Log("[NrdArchitecture] PASS GPU frames="+r.gpuFrames+" DI cases="+r.diDispatchCases);
            }
            catch(Exception error){r.error=error.ToString();Debug.LogException(error);}
            finally{GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;foreach(var o in owned){if(o is RenderTexture t)t.Release();UnityEngine.Object.DestroyImmediate(o);}File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(r,true));EditorApplication.Exit(code);}
        }
        static long Payload(int w,int h,GraphicsFormat f)=>(long)((w+GraphicsFormatUtility.GetBlockWidth(f)-1)/GraphicsFormatUtility.GetBlockWidth(f))*((h+GraphicsFormatUtility.GetBlockHeight(f)-1)/GraphicsFormatUtility.GetBlockHeight(f))*GraphicsFormatUtility.GetBlockSize(f);
        static Row[] Rows(object context,string channel)
        {
            var rows=new List<Row>();var desc=ConvertDescription(Field(context,"description"));
            foreach(string pool in new[]{"permanent","transient"})
            {var handles=(RTHandle[])Field(context,pool);IntPtr pointer=pool=="permanent"?desc.permanent:desc.transient;for(int i=0;i<handles.Length;i++){var t=handles[i].rt;var d=HoNrdNative.Read<HoNrdNative.TextureDesc>(pointer,i);rows.Add(new Row{channel=channel,pool=pool,index=i,width=t.width,height=t.height,factor=Math.Max(1,(int)d.downsample),format=t.graphicsFormat.ToString(),bytes=Payload(t.width,t.height,t.graphicsFormat)});}}
            return rows.ToArray();
        }
        static HoNrdNative.InstanceDesc ConvertDescription(object d)
        {if(d is HoNrdNative.InstanceDesc n)return n;return ConvertStruct<HoNrdNative.InstanceDesc,Legacy.HoLegacyNrdNative.InstanceDesc>((Legacy.HoLegacyNrdNative.InstanceDesc)d);}
        static void ComparePoolLayouts(Row[] a,Row[] b)
        {Require(a.Length==b.Length,"Pool count changed");for(int i=0;i<a.Length;i++)Require(a[i].pool==b[i].pool&&a[i].format==b[i].format&&a[i].width==b[i].width&&a[i].height==b[i].height&&a[i].bytes==b[i].bytes,"Pool layout changed");}
        static byte[] Trace(object context)
        {
            int count=(int)context.GetType().GetProperty("LastDispatchCount",Flags).GetValue(context);int stride=(int)Field(context,"constantStride");var words=(int[])Field(context,"constantWords");var passes=(Array)Field(context,"passes");var resources=(Array)Field(context,"resources");
            using var ms=new MemoryStream();using var w=new BinaryWriter(ms);w.Write(count);w.Write(stride);for(int i=0;i<count*stride/4;i++)w.Write(words[i]);
            for(int i=0;i<count;i++){var pass=passes.GetValue(i);foreach(string name in new[]{"pipeline","gridWidth","gridHeight","constantSize","resourceCount"})w.Write(Convert.ToUInt32(Field(pass,name)));int n=Convert.ToInt32(Field(pass,"resourceCount"));for(int j=0;j<n;j++){var r=resources.GetValue(i,j);w.Write(Convert.ToUInt32(Field(r,"descriptorType")));w.Write(Convert.ToUInt32(Field(r,"type")));w.Write(Convert.ToUInt32(Field(r,"poolIndex")));}}
            return ms.ToArray();
        }
    }
}
