// Real GPU checks for secondary sky lighting, ownership and frame snapshots.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static class HoRTGIEnvironmentValidation
    {
        const int Size=64;
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static readonly List<UnityEngine.Object> owned=new();
        static readonly List<string> checks=new();
        static Camera camera;
        static RenderTexture target;
        static Texture2D image;
        static HoRTGIRendererFeature gi;
        static HoAtmosphereRendererFeature atmosphere;
        static GameObject wall,roof,farBlocker;
        static Light analyticLight;
        static RenderPipelineAsset oldDefault,oldQuality;
        static bool compatibility,oldAsync,finished;
        static string failure,output;
        static int frames,warmupFrames;
        static Color bounced,doubled,analytic,mixed;
        static T Keep<T>(T obj) where T:UnityEngine.Object{owned.Add(obj);return obj;}
        public static void RunCompatibility(){compatibility=true;Run();}
        public static void Run()
        {
            try
            {
                if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"))throw new Exception("Dedicated isolated GI project required");
                oldDefault=GraphicsSettings.defaultRenderPipeline;oldQuality=QualitySettings.renderPipeline;oldAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
                output=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-gi-secondary-environment-"+(compatibility?"compat":"rg")));Directory.CreateDirectory(output);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                var gb=Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());gb.Settings.maxRenderQueue=(int)RenderQueue.GeometryLast;
                var sb=Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());sb.Settings.enableSemanticLanes=sb.Settings.enableCorrelatedSemantics=false;
                atmosphere=Keep(ScriptableObject.CreateInstance<HoAtmosphereRendererFeature>());atmosphere.Settings.source=HoAtmosphereSource.Constant;atmosphere.Settings.constantColor=new Color(.5f,.25f,.125f,1);
                gi=Keep(ScriptableObject.CreateInstance<HoRTGIRendererFeature>());gi.Settings.temporal=gi.Settings.spatial=false;gi.Settings.secondaryLightSamples=1;gi.Settings.secondaryEnvironmentSamples=4;
                renderer.rendererFeatures.Add(gb);renderer.rendererFeatures.Add(sb);renderer.rendererFeatures.Add(atmosphere);renderer.rendererFeatures.Add(gi);
                var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #else
                Require(!compatibility,"Compatibility symbol");
                #endif
                camera=Keep(new GameObject("Secondary environment camera")).AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;camera.cullingMask=1;
                camera.transform.position=new Vector3(0,0,-3);camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.allowHDR=true;
                target=Keep(new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();image=Keep(new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true));
                Quad("Receiver",0,2,0);
                wall=Quad("World only bounce",-1,4000,1);wall.transform.rotation=Quaternion.Euler(0,180,0);
                roof=Quad("Sky blocker behind receiver",.002f,4000,1);roof.SetActive(false);
                farBlocker=Quad("Sky blocker beyond bounce range",-1500,4000000,1);farBlocker.SetActive(false);
                analyticLight=Keep(new GameObject("Mixed secondary point")).AddComponent<Light>();analyticLight.type=LightType.Point;analyticLight.color=Color.white;analyticLight.intensity=1;analyticLight.range=100;analyticLight.transform.position=new Vector3(0,0,-.5f);analyticLight.enabled=false;
                Application.logMessageReceived+=OnLog;EditorApplication.update+=Tick;
            }
            catch(Exception error){Finish(error);}
        }
        static GameObject Quad(string name,float z,float size,int layer)
        {
            var obj=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));obj.name=name;obj.layer=layer;obj.transform.position=new Vector3(0,0,z);obj.transform.localScale=new Vector3(size,size,1);
            var material=Keep(new Material(Shader.Find("lilToon")));material.EnableKeyword("UNITY_UI_ALPHACLIP");material.SetFloat("_Cull",0);material.SetColor("_Color",Color.white);material.SetTexture("_MainTex",Texture2D.whiteTexture);
            material.SetFloat("_Metallic",0);material.SetFloat("_Reflectance",0);material.SetFloat("_Smoothness",0);material.SetFloat("_UseEmission",0);material.SetFloat("_UseEmission2nd",0);
            obj.GetComponent<Renderer>().sharedMaterial=material;return obj;
        }
        static void OnLog(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)failure=message;}
        static Color Render()
        {
            if(failure!=null)throw new Exception("Render diagnostic: "+failure);
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});frames++;
            if(gi.LastContext==null)throw new Exception("GI missing: "+gi.Status);
            var previous=RenderTexture.active;
            try{RenderTexture.active=gi.LastContext.diffuse.rt;image.ReadPixels(new Rect(0,0,Size,Size),0,0);image.Apply();}
            finally{RenderTexture.active=previous;}
            Color sum=Color.clear;for(int y=16;y<48;y++)for(int x=16;x<48;x++){var c=image.GetPixel(x,y);if(c.a<.99f||!Finite(c.r)||!Finite(c.g)||!Finite(c.b))throw new Exception("Invalid receiver/signal at "+x+","+y+" frame="+frames+" color="+c+" status="+gi.Status);sum+=c;}
            return sum/1024;
        }
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        static Color Mean(int count=24){Color sum=Color.clear;for(int i=0;i<count;i++)sum+=Render();return sum/count;}
        static void Require(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);Debug.Log("[HoGIEnvironment] CHECK "+name);}
        static void Near(Color value,Color expected,string name,float tolerance=.025f)
        {Require(Mathf.Abs(value.r-expected.r)<=tolerance&&Mathf.Abs(value.g-expected.g)<=tolerance&&Mathf.Abs(value.b-expected.b)<=tolerance,name+" actual="+value+" expected="+expected);}
        static void Save(string name)
        {File.WriteAllBytes(Path.Combine(output,name+".exr"),image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());}
        static void Tick()
        {
            // GB/SB demand and material bindings need bounded editor warmup after
            // switching project color space, as in the existing GI validation.
            if(warmupFrames<3)
            {
                try {RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});frames++;warmupFrames++;}
                catch(Exception error){Finish(error);}
                return;
            }
            EditorApplication.update-=Tick;
            try
            {
                Require(!gi.Settings.includeDirectEnvironment,"Default GI excludes DI-owned direct sky");
                gi.Settings.useEnvironment=false;Near(Mean(2),Color.black,"No analytic light / environment is black",.0001f);
                Require(gi.LastContext.environmentPdf==null&&gi.LastContext.environmentRis.count==1,"Disabled environment has no PDF/full RIS allocation");
                gi.Settings.useEnvironment=true;bounced=Mean();Save("secondary-sky");
                Require(bounced.r>.03f&&bounced.r<.55f,"Zero analytic lights still receive finite secondary sky");
                Require(Mathf.Abs(bounced.g/bounced.r-.5f)<.03f&&Mathf.Abs(bounced.b/bounced.r-.25f)<.03f,"Secondary sky spectral ratios");
                Require(gi.LastContext.environmentPdfReady&&gi.LastContext.environmentRis.count==16384&&gi.LastContext.environmentPdf.rt.width==128,"Private environment PDF and RIS produced");
                uint revision=gi.LastContext.environmentPdfRevision;
                analyticLight.enabled=true;gi.Settings.useEnvironment=false;analytic=Mean();
                gi.Settings.useEnvironment=true;mixed=Mean();var expectedMixed=analytic+bounced;
                Require(Mathf.Abs(mixed.r/expectedMixed.r-1)<.05f&&Mathf.Abs(mixed.g/expectedMixed.g-1)<.05f&&Mathf.Abs(mixed.b/expectedMixed.b-1)<.05f,"Analytic plus environment MIS preserves both contributions within 5% actual="+mixed+" expected="+expectedMixed);
                analyticLight.enabled=false;
                gi.Settings.secondaryEnvironmentSamples=0;Near(Mean(2),Color.black,"Secondary candidate budget zero removes only bounce sky",.0001f);
                wall.SetActive(false);Near(Mean(2),Color.black,"Direct sky remains excluded with no bounce geometry",.0001f);
                gi.Settings.includeDirectEnvironment=true;Near(Mean(4),atmosphere.Settings.constantColor,"Explicit GI-only direct sky integral",.01f);
                farBlocker.SetActive(true);Near(Mean(8),Color.black,"Geometry beyond finite bounce range still blocks direct sky",.002f);farBlocker.SetActive(false);
                gi.Settings.includeDirectEnvironment=false;Near(Mean(2),Color.black,"Direct sky ownership switch removes duplicate",.0001f);
                wall.SetActive(true);gi.Settings.secondaryEnvironmentSamples=4;Mean(2);
                roof.SetActive(true);Near(Mean(8),Color.black,"World-only blocker occludes secondary sky",.001f);roof.SetActive(false);
                atmosphere.Settings.constantColor=new Color(1,.5f,.25f,1);doubled=Mean();
                Near(doubled,bounced*2,"Sky radiance doubling preserves bounce energy",.035f);
                Require(gi.LastContext.environmentPdfRevision!=revision,"Sky revision rebuilds private PDF");
                gi.Settings.temporal=gi.Settings.spatial=true;Mean(4);Require(gi.LastContext.lastTemporal,"Stable secondary sky can reuse GI history");
                gi.Settings.useEnvironment=false;Near(Render(),Color.black,"Disable sky rejects old lighting immediately",.0001f);Require(!gi.LastContext.lastTemporal,"Settings change resets GI reservoir history");
                gi.Settings.useEnvironment=true;atmosphere.SetActive(false);Near(Mean(2),Color.black,"Absent atmosphere does not inherit pooled bindings",.0001f);atmosphere.SetActive(true);
                SnapshotContract();
                var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTGI/Shaders/HoRTGI.compute");
                Require(ShaderUtil.GetComputeShaderMessages(shader).Length==0,"No GI compute warnings/errors");
                Finish(null);
            }
            catch(Exception error){Finish(error);}
        }
        static object Field(object value,string name)=>value.GetType().GetField(name,Instance).GetValue(value);
        static void SnapshotContract()
        {
            wall.SetActive(false);gi.Settings.temporal=gi.Settings.spatial=false;gi.Settings.includeDirectEnvironment=true;gi.Settings.secondaryEnvironmentSamples=0;Render();
            object pass=Field(gi,"pass");var method=pass.GetType().GetMethod("Snapshot",Instance);
            object[] arguments={camera,camera.worldToCameraMatrix,camera.projectionMatrix};
            object before=method.Invoke(pass,arguments);var old=Field(before,"settings");
            gi.Settings.useEnvironment=false;gi.Settings.includeDirectEnvironment=false;gi.Settings.secondaryEnvironmentSamples=17;
            object after=method.Invoke(pass,arguments);var retained=Field(after,"settings");
            Require((bool)Field(old,"useEnvironment")&&(bool)Field(retained,"useEnvironment")&&(bool)Field(retained,"includeDirectEnvironment")&&(int)Field(retained,"secondaryEnvironmentSamples")==0,"Actual pass snapshots retain captured numeric settings until next Setup");
            Near(Render(),Color.black,"Next GPU frame consumes changed environment settings",.0001f);
            var next=Field(method.Invoke(pass,arguments),"settings");Require(!(bool)Field(next,"useEnvironment")&&(int)Field(next,"secondaryEnvironmentSamples")==17,"Next Setup publishes new numeric snapshot");
        }
        static void Finish(Exception error)
        {
            if(finished)return;finished=true;EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;
            if(error!=null)Debug.LogException(error);
            if(output!=null)File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(new Report{pass=error==null,failure=error?.ToString(),frames=frames,warmupFrames=warmupFrames,compatibility=compatibility,colorSpace=QualitySettings.activeColorSpace.ToString(),bounce=bounced,doubled=doubled,analytic=analytic,mixed=mixed,checks=checks.ToArray()},true));
            GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;ShaderUtil.allowAsyncCompilation=oldAsync;
            foreach(var obj in owned.AsEnumerable().Reverse())if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);
            Debug.Log("[HoGIEnvironment] END pass="+(error==null));EditorApplication.Exit(error==null?0:1);
        }
        [Serializable] sealed class Report {public bool pass,compatibility;public string failure,colorSpace;public int frames,warmupFrames;public Color bounce,doubled,analytic,mixed;public string[] checks;}
    }
}
