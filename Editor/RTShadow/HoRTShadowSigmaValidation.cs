using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using lilToon.URP.Extensions.NRD;
using lilToon.URP.Extensions.RTShadow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace lilToon.URP.Extensions.Editor.RTShadow
{
    // Standalone NRD GPU qualification: no material, camera render or RT scene
    // can hide a frontend encoding/descriptor/decoder error in this fixture.
    public static class HoRTShadowSigmaValidation
    {
        private static readonly List<RTHandle> textures=new();
        private static readonly List<Object> objects=new();
        private static RenderPipelineAsset previousDefault,previousQuality;
        private static SigmaContext context;private static ComputeShader probe;private static ComputeShader[] shaders;
        private sealed class SigmaContext:HoNrdContext
        {
            internal SigmaContext(ComputeShader[] shaders):base(shaders,16,HoRTShadowSigmaManifest.Identifiers,HoRTShadowSigmaManifest.Resources){}
            internal bool HasTemporalStabilization(){for(int i=0;i<LastDispatchCount;i++)if(passes[i].pipeline==5)return true;return false;}
        }
        private static RTHandle normal,motion,viewZ,penumbra,encoded,decoded,reference;
        [Serializable] private sealed class Report
        {public bool pass;public string error;public int dispatches,levels;public float rawMse,filteredMse,decodeError;public List<string> checks=new();}
        private static Report report;
        public static void Run()
        {
            try
            {
                if(!Application.isBatchMode||!Application.dataPath.Replace('\\','/').Contains("/.codex-research/"))throw new Exception("Independent hidden batch project required");
                if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Direct3D12)throw new Exception("D3D12 fixture required");
                report=new Report();Check(Marshal.SizeOf<HoNrdNative.SigmaSettings>()==20,"SigmaSettings exact pinned ABI is 20 bytes");
                previousDefault=GraphicsSettings.defaultRenderPipeline;previousQuality=QualitySettings.renderPipeline;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                var camera=Keep(new GameObject("SIGMA allocator bootstrap")).AddComponent<Camera>();camera.enabled=false;
                var target=Keep(new RenderTexture(16,16,0));target.Create();
                // The existing URP allocator pool is created by its pipeline,
                // even though the actual SIGMA fixture uses direct commands.
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                shaders=new ComputeShader[HoRTShadowSigmaManifest.Identifiers.Length];
                for(int i=0;i<shaders.Length;i++){shaders[i]=AssetDatabase.LoadAssetAtPath<ComputeShader>($"Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTShadow/Shaders/NRD/HoRTShadowSigma{i:00}.compute");Check(shaders[i]!=null,"SIGMA pipeline "+i);}
                probe=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTShadow/HoRTShadowSigmaValidation.compute");
                Check(probe!=null,"Official frontend/backend probe present");
                context=new SigmaContext(shaders);context.Prepare(128,64);
                bool rejected=false;using(var commands=new CommandBuffer())try{context.Record(commands,default,new Dictionary<string,RenderTargetIdentifier>(),default(HoNrdNative.RelaxSettings));}catch(InvalidOperationException){rejected=true;}
                Check(rejected,"Wrong settings family is rejected before entering native ABI");
                normal=Texture(GraphicsFormat.R16G16B16A16_UNorm);motion=Texture(GraphicsFormat.R16G16B16A16_SFloat);
                viewZ=Texture(GraphicsFormat.R32_SFloat);penumbra=Texture(GraphicsFormat.R16_SFloat);encoded=Texture(GraphicsFormat.R16_SFloat);
                decoded=Texture(GraphicsFormat.R32G32B32A32_SFloat);reference=Texture(GraphicsFormat.R32G32B32A32_SFloat);
                EditorApplication.delayCall+=Validate;
            }
            catch(Exception error){Finish(error);}
        }
        private static RTHandle Texture(GraphicsFormat format)
        {var descriptor=new RenderTextureDescriptor(128,64){graphicsFormat=format,depthBufferBits=0,enableRandomWrite=true,msaaSamples=1};var texture=RTHandles.Alloc(descriptor,FilterMode.Point,TextureWrapMode.Clamp,name:"SIGMA fixture "+format);textures.Add(texture);return texture;}
        private static Color[] Render(int mode,int frame,uint history,bool reset)
        {
            var cmd=new CommandBuffer{name="SIGMA standalone GPU qualification"};
            try
            {
                int fill=probe.FindKernel("Fill"),decode=probe.FindKernel("Decode");
                cmd.SetComputeIntParam(probe,"_Mode",mode);cmd.SetComputeIntParam(probe,"_Frame",frame);
                cmd.SetComputeTextureParam(probe,fill,"_Normal",normal.nameID);cmd.SetComputeTextureParam(probe,fill,"_Motion",motion.nameID);
                cmd.SetComputeTextureParam(probe,fill,"_ViewZ",viewZ.nameID);cmd.SetComputeTextureParam(probe,fill,"_Penumbra",penumbra.nameID);cmd.SetComputeTextureParam(probe,fill,"_Reference",reference.nameID);
                cmd.DispatchCompute(probe,fill,16,8,1);
                var settings=HoNrdNative.CommonSettings.Create(128,64,Matrix4x4.Scale(new Vector3(1,1,-1)),Matrix4x4.Ortho(-2,2,-1,1,.1f,100),Matrix4x4.Scale(new Vector3(1,1,-1)),Matrix4x4.Ortho(-2,2,-1,1,.1f,100),frame,reset);
                settings.timeDelta=16.666667f;
                context.Record(cmd,settings,new Dictionary<string,RenderTargetIdentifier>{{"IN_NORMAL_ROUGHNESS",normal.nameID},{"IN_MV",motion.nameID},{"IN_VIEWZ",viewZ.nameID},{"IN_PENUMBRA",penumbra.nameID},{"OUT_SHADOW_TRANSLUCENCY",encoded.nameID}},new HoNrdNative.SigmaSettings{lightDirection=Vector3.back,planeDistanceSensitivity=.02f,maxStabilizedFrameNum=history});
                Check(context.HasTemporalStabilization()==(history>0),"Actual dispatch list includes stabilization exactly when requested");
                cmd.SetComputeTextureParam(probe,decode,"_Encoded",encoded.nameID);cmd.SetComputeTextureParam(probe,decode,"_Decoded",decoded.nameID);cmd.DispatchCompute(probe,decode,16,8,1);
                Graphics.ExecuteCommandBuffer(cmd);report.dispatches=context.LastDispatchCount;
                Check(report.dispatches>0,"Actual native SIGMA dispatch list executed");
                return Read(decoded.rt);
            }
            finally{cmd.Dispose();}
        }
        private static void Validate()
        {
            try
            {
                var lit=Render(1,0,0,true);Check(Mathf.Abs(lit[32*128+64].r-1)<.005f,"Official miss sentinel reconstructs fully lit");
                var shadow=Render(2,0,0,true);Check(shadow[32*128+64].r<.005f,"Fully occluded reconstructs zero");
                var mixed=Render(0,0,0,true);var raw=Read(reference.rt);var levels=new HashSet<int>();double noisy=0,filtered=0;
                for(int y=8;y<56;y++)for(int x=40;x<88;x++)
                {int i=y*128+x;var p=mixed[i];Check(!float.IsNaN(p.r)&&p.r>=0&&p.r<=1,"Finite decoded visibility");report.decodeError=Mathf.Max(report.decodeError,Mathf.Abs(p.r-p.b));if(p.r>0&&p.r<1)levels.Add(Mathf.RoundToInt(p.r*65535));float n=raw[i].g-raw[i].r,f=p.r-raw[i].r;noisy+=n*n;filtered+=f*f;}
                report.rawMse=(float)(noisy/(48*48));report.filteredMse=(float)(filtered/(48*48));report.levels=levels.Count;
                Check(report.decodeError<.000001f,"Official backend squares encoded shadow");Check(levels.Count>8,"SIGMA produces continuous reconstructed visibility");Check(filtered<noisy,"Decoded SIGMA improves over independent noisy frontend reference");
                var repeated=Render(0,0,0,true);bool same=true;for(int i=0;i<mixed.Length;i++)same&=mixed[i]==repeated[i];Check(same,"Reset no-history same seed is repeatable");
                for(int frame=0;frame<8;frame++)Render(0,frame,5,frame==0);
                Check(context.AppliedSigmaSettings.maxStabilizedFrameNum==5,"Preview uses SIGMA-specific settings without RELAX conversion");
                Render(0,0,0,true);Check(context.AppliedSigmaSettings.maxStabilizedFrameNum==0,"Single-frame mode disables stabilization explicitly");
                foreach(var shader in shaders)foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))Check(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
                Debug.Log("[HoRTShadowSigmaValidation] PASS independent official SIGMA / decode / no-history / preview; levels="+report.levels+" MSE="+report.rawMse+" -> "+report.filteredMse);
                Finish(null);
            }
            catch(Exception error){Finish(error);}
        }
        private static Color[] Read(RenderTexture source)
        {var old=RenderTexture.active;var image=new Texture2D(128,64,TextureFormat.RGBAFloat,false,true);try{RenderTexture.active=source;image.ReadPixels(new Rect(0,0,128,64),0,0);image.Apply();return image.GetPixels();}finally{RenderTexture.active=old;Object.DestroyImmediate(image);}}
        private static void Check(bool condition,string text){if(!condition)throw new Exception(text);if(text!="Finite decoded visibility")report.checks.Add(text);}
        private static T Keep<T>(T item)where T:Object{objects.Add(item);return item;}
        private static void Finish(Exception error)
        {if(report!=null){report.pass=error==null;report.error=error?.ToString();var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-rt-sigma"));Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"report.json"),JsonUtility.ToJson(report,true));}context?.Dispose();foreach(var texture in textures)texture.Release();textures.Clear();GraphicsSettings.defaultRenderPipeline=previousDefault;QualitySettings.renderPipeline=previousQuality;for(int i=objects.Count-1;i>=0;i--)if(objects[i]!=null)Object.DestroyImmediate(objects[i]);objects.Clear();if(error!=null)Debug.LogException(error);EditorApplication.Exit(error==null?0:1);}
    }
}
