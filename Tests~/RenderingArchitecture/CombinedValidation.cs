#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.SurfaceBuffer;
using lilToon.URP.Extensions.RTDI;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.NRD;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace lilToon.URP.Extensions.Editor.NrdArchitecture
{
    public static class CombinedValidation
    {
        static bool compatibility;
        static readonly List<UnityEngine.Object> owned=new();
        static readonly List<string> checks=new();
        [Serializable]sealed class Report{public bool pass,compatibility;public int frames;public string error;public string[] checks;public long diInstance,giInstance;}
        static T Keep<T>(T v)where T:UnityEngine.Object{owned.Add(v);return v;}
        static void Require(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
        static IntPtr Instance(HoNrdContext c)=>(IntPtr)typeof(HoNrdContext).GetField("instance",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c);
        static float Mean(RenderTexture t)
        {
            var old=RenderTexture.active;var image=new Texture2D(t.width,t.height,TextureFormat.RGBAFloat,false,true);
            try{RenderTexture.active=t;image.ReadPixels(new Rect(0,0,t.width,t.height),0,0);image.Apply();float sum=0;foreach(var p in image.GetPixels()){if(!float.IsFinite(p.r)||!float.IsFinite(p.g)||!float.IsFinite(p.b))throw new Exception("Nonfinite output");sum+=p.r+p.g+p.b;}return sum/(t.width*t.height);}
            finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
        }
        public static void RunCompatibility(){compatibility=true;Run();}
        public static void Run()
        {
            var report=new Report{compatibility=compatibility};var oldDefault=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;int exit=1;
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../nrd-architecture-combined-"+(compatibility?"compat":"rg")));Directory.CreateDirectory(output);
            try
            {
                Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"),"Dedicated project");ShaderUtil.allowAsyncCompilation=false;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());var gb=Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());gb.Settings.maxRenderQueue=(int)RenderQueue.GeometryLast;
                var sb=Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());sb.Settings.enableSemanticLanes=sb.Settings.enableCorrelatedSemantics=false;
                var sky=Keep(ScriptableObject.CreateInstance<HoAtmosphereRendererFeature>());sky.Settings.source=HoAtmosphereSource.Constant;sky.Settings.constantColor=new Color(.3f,.2f,.1f,1);
                var di=Keep(ScriptableObject.CreateInstance<HoRTDIRendererFeature>());di.Settings.denoise=true;di.Settings.useEnvironment=false;var gi=Keep(ScriptableObject.CreateInstance<HoRTGIRendererFeature>());gi.Settings.denoise=true;gi.Settings.useEnvironment=false;
                renderer.rendererFeatures.Add(gb);renderer.rendererFeatures.Add(sb);renderer.rendererFeatures.Add(sky);renderer.rendererFeatures.Add(di);renderer.rendererFeatures.Add(gi);
                var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #endif
                var camera=Keep(new GameObject("Shared NRD channels")).AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;camera.cullingMask=1;camera.transform.position=new Vector3(0,0,-3);camera.nearClipPlane=.1f;camera.farClipPlane=20;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.GetUniversalAdditionalCameraData().volumeLayerMask=-1;
                var target=Keep(new RenderTexture(64,64,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();
                var holder=Keep(new GameObject("Architecture volume")).AddComponent<Volume>();holder.isGlobal=true;holder.sharedProfile=Keep(ScriptableObject.CreateInstance<VolumeProfile>());holder.sharedProfile.Add<HoRTDIVolume>().enable.Override(true);holder.sharedProfile.Add<HoRTGIVolume>().enable.Override(true);
                Material Material(bool cutout=false){var m=Keep(new Material(Shader.Find(cutout?"Hidden/lilToonCutout":"lilToon")));m.SetFloat("_Cull",0);m.SetFloat("_AsUnlit",1);m.SetFloat("_Reflectance",0);m.SetFloat("_Metallic",0);m.SetFloat("_Smoothness",0);m.SetTexture("_MainTex",Texture2D.whiteTexture);m.SetColor("_Color",cutout?new Color(0,0,0,0):Color.white);if(cutout){m.SetOverrideTag("RenderType","TransparentCutout");m.renderQueue=2450;m.SetFloat("_Cutoff",.5f);}return m;}
                GameObject Plane(string name,Vector3 pos,int layer,Material material,bool flip=false){var p=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));p.name=name;p.transform.position=pos;p.transform.localScale=new Vector3(4,4,1);if(flip)p.transform.rotation=Quaternion.Euler(0,180,0);p.layer=layer;p.GetComponent<Renderer>().sharedMaterial=material;return p;}
                Plane("Primary",Vector3.zero,0,Material());var secondary=Plane("Secondary",new Vector3(0,0,-1),1,Material(),true);var alphaMaterial=Material(true);var shutter=Plane("Existing alpha instance",new Vector3(10,0,-.5f),1,alphaMaterial);
                foreach(float x in new[]{-.6f,.6f}){var o=Keep(new GameObject("Point light"));var light=o.AddComponent<Light>();light.type=LightType.Point;light.range=6;light.intensity=3;light.color=x<0?Color.red:Color.blue;o.transform.position=new Vector3(x,.3f,-.2f);}
                void Render(){if(di.LastContext?.nrd!=null)di.LastContext.nrd.ValidationTimeSeconds=report.frames/60.0;if(gi.LastContext?.nrd!=null)gi.LastContext.nrd.ValidationTimeSeconds=report.frames/60.0;VolumeManager.instance.Update(camera.transform,camera.GetUniversalAdditionalCameraData().volumeLayerMask);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});report.frames++;}
                for(int i=0;i<32;i++)Render();Require(di.LastContext?.nrd!=null&&gi.LastContext?.nrd!=null,"Both denoisers active");Require(Mean(di.LastContext.PublishedDiffuse.rt)>1e-5f&&Mean(gi.LastContext.PublishedDiffuse.rt)>1e-5f,"Both physical diffuse outputs positive");
                var dc=di.LastContext.nrd;var gc=gi.LastContext.nrd.core;report.diInstance=Instance(dc).ToInt64();report.giInstance=Instance(gc).ToInt64();Require(report.diInstance!=report.giInstance,"Separate native histories");
                secondary.transform.position=new Vector3(0,0,-1.1f);Render();Require(gi.LastContext.nrd.lastReset,"GI world reset retained");
                shutter.transform.position=new Vector3(0,0,-.5f);alphaMaterial.SetColor("_Color",Color.black);Render();alphaMaterial.SetColor("_Color",new Color(0,0,0,0));Render();Require(float.IsFinite(Mean(di.LastContext.PublishedDiffuse.rt))&&float.IsFinite(Mean(gi.LastContext.PublishedDiffuse.rt)),"Shared cutout contract exercised");
                gi.Settings.enabled=false;Render();Require(Shader.GetGlobalFloat("_HoRTGIActive")==0&&Shader.GetGlobalFloat("_HoRTDIActive")>0,"DI works with GI off");gi.Settings.enabled=true;di.Settings.enabled=false;Render();Require(Shader.GetGlobalFloat("_HoRTGIActive")>0&&Shader.GetGlobalFloat("_HoRTDIActive")==0,"GI works with DI off");di.Settings.enabled=true;for(int i=0;i<4;i++)Render();
                target.Release();target.width=target.height=32;target.Create();Render();Require(gi.LastContext.nrd.lastReset&&gi.LastContext.width==32&&di.LastContext.width==32,"Both resize independently");
                di.Dispose();gi.Dispose();Require(Instance(dc)==IntPtr.Zero&&Instance(gc)==IntPtr.Zero,"Both native owners released");report.pass=true;exit=0;Debug.Log("[NrdArchitectureCombined] PASS frames="+report.frames);
            }
            catch(Exception e){report.error=e.ToString();Debug.LogException(e);}
            finally{GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;foreach(var o in owned){if(o is RenderTexture t)t.Release();UnityEngine.Object.DestroyImmediate(o);}report.checks=checks.ToArray();File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));EditorApplication.Exit(exit);}
        }
    }
}
