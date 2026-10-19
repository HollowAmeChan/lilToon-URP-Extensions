// Isolated diagnostic fixture matching Tests~/RTGI/Reference/HoReferenceGIProbe.cs.
// Synchronous capture is for signal comparison, never a GPU timing benchmark.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    public static class HoRTGIMatchedReferenceValidation
    {
        const int Size=128,Count=256;
        static readonly List<UnityEngine.Object> owned=new();
        static readonly List<Window> windows=new();
        static Camera camera;
        static HoRTGIRendererFeature feature;
        static RenderTexture target;
        static Texture2D image;
        static RenderPipelineAsset oldDefault,oldQuality;
        static bool noReuse,compatibility,commonDiffuse,oldAsync,finished;
        static string failure,output;
        static int frame,windowStart=1,windowCount;
        static double[] sums,means,m2;
        static Vector3 total;
        static Report report;
        static T Keep<T>(T obj) where T:UnityEngine.Object {owned.Add(obj);return obj;}
        public static void Run(){Setup();}
        public static void RunNoReuse(){noReuse=true;Setup();}
        public static void RunCompatibility(){compatibility=true;Setup();}
        public static void RunCommonDiffuse(){commonDiffuse=true;Setup();}
        public static void RunCommonDiffuseNoReuse(){commonDiffuse=true;noReuse=true;Setup();}
        static void Setup()
        {
            try
            {
                if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"))throw new Exception("Dedicated RTGIValidation project required; do not use the parallel DI project");
                oldDefault=GraphicsSettings.defaultRenderPipeline;oldQuality=QualitySettings.renderPipeline;
                oldAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
                output=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-gi-"+(commonDiffuse?"common-diffuse-":"v01-matched-")+(compatibility?"compat":noReuse?"no-reuse":"rg")));Directory.CreateDirectory(output);
                report=new Report{unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),colorSpace=QualitySettings.activeColorSpace.ToString(),noReuse=noReuse,compatibility=compatibility};
                if(commonDiffuse)report.protocol="Step002 common diffuse receiver: linear F0=.04 via gamma-tagged Reflectance property, metallic=0, roughness=1; same geometry/light as step001, reference final evaluator also selects diffuse only. Secondary F0 quantization differs (.0392 Ho vs about .03995 reference). 128x128, 256 requests; not performance/motion/Player acceptance.";
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                var gb=Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());gb.Settings.maxRenderQueue=(int)RenderQueue.GeometryLast;
                var sb=Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());sb.Settings.enableSemanticLanes=sb.Settings.enableCorrelatedSemantics=false;
                feature=Keep(ScriptableObject.CreateInstance<HoRTGIRendererFeature>());
                feature.Settings.temporal=feature.Settings.spatial=!noReuse;feature.Settings.secondaryLightSamples=1;feature.Settings.useEnvironment=false;
                feature.Settings.maxHistory=20;feature.Settings.spatialSamples=4;feature.Settings.spatialRadius=16;
                renderer.rendererFeatures.Add(gb);renderer.rendererFeatures.Add(sb);renderer.rendererFeatures.Add(feature);
                var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #else
                if(compatibility)throw new Exception("Compatibility symbol missing");
                #endif
                camera=Keep(new GameObject("Ho matched GI camera")).AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=40;camera.aspect=1;
                camera.transform.position=new Vector3(0,0,-3);camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.allowHDR=true;
                target=Keep(new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();
                image=Keep(new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true));
                Quad("White receiver",Vector3.zero,Vector3.zero,new Vector2(2,2),Color.white);
                Quad("Red offscreen bounce",new Vector3(1.25f,0,-1),new Vector3(0,90,0),new Vector2(2,3),new Color(.8f,.1f,.05f));
                var light=Keep(new GameObject("Matched point")).AddComponent<Light>();light.type=LightType.Point;light.color=Color.white;light.intensity=4;light.range=100;light.transform.position=new Vector3(.25f,0,-1);
                sums=new double[32*32*3];means=new double[sums.Length];m2=new double[sums.Length];
                Application.logMessageReceived+=OnLog;EditorApplication.update+=Tick;
                Debug.Log("[HoGIMatched] START "+report.api+" noReuse="+noReuse+" compatibility="+compatibility);
            }
            catch(Exception error){Finish(error);}
        }
        static void Quad(string name,Vector3 position,Vector3 rotation,Vector2 size,Color color)
        {
            var obj=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));obj.name=name;obj.transform.position=position;obj.transform.eulerAngles=rotation;obj.transform.localScale=new Vector3(size.x,size.y,1);
            var material=Keep(new Material(Shader.Find("lilToon")));material.EnableKeyword("UNITY_UI_ALPHACLIP");
            material.SetFloat("_Cull",0);material.SetColor("_Color",color);material.SetTexture("_MainTex",Texture2D.whiteTexture);
            material.SetFloat("_Metallic",0);material.SetFloat("_Reflectance",commonDiffuse?Mathf.LinearToGammaSpace(.04f):0);material.SetFloat("_Smoothness",0);material.SetFloat("_UseEmission",0);material.SetFloat("_UseEmission2nd",0);
            obj.GetComponent<Renderer>().sharedMaterial=material;
        }
        static void OnLog(string text,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)failure=text;}
        static Color[] Read()
        {
            var previous=RenderTexture.active;
            try {RenderTexture.active=feature.LastContext.diffuse.rt;image.ReadPixels(new Rect(0,0,Size,Size),0,0);image.Apply();return image.GetPixels();}
            finally {RenderTexture.active=previous;}
        }
        static void Tick()
        {
            try
            {
                if(failure!=null)throw new Exception("Actual render diagnostic: "+failure);
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});frame++;
                if(feature.LastContext==null)throw new Exception("GI did not allocate: "+feature.Status);
                var pixels=Read();int k=0;windowCount++;Vector3 sum=Vector3.zero;
                for(int y=48;y<80;y++)for(int x=48;x<80;x++)
                {
                    Color value=pixels[y*Size+x];if(value.a<.99f)throw new Exception("Matched receiver is invalid");sum+=new Vector3(value.r,value.g,value.b);
                    foreach(float component in new[]{value.r,value.g,value.b})
                    {
                        if(float.IsNaN(component)||float.IsInfinity(component))throw new Exception("Nonfinite GI output");
                        double delta=component-means[k];means[k]+=delta/windowCount;m2[k]+=delta*(component-means[k]);sums[k]+=component;k++;
                    }
                }
                total+=sum/1024;
                if(frame==1||frame==4||frame==8||frame==16||frame==32||frame==64||frame==128||frame==Count)
                {
                    var result=new Window{first=windowStart,last=frame,mean=new Vector3((float)(sums.Where((v,i)=>i%3==0).Sum()/windowCount/1024),(float)(sums.Where((v,i)=>i%3==1).Sum()/windowCount/1024),(float)(sums.Where((v,i)=>i%3==2).Sum()/windowCount/1024)),temporalVariance=m2.Sum()/Math.Max(1,windowCount-1)/m2.Length};
                    windows.Add(result);Debug.Log("[HoGIMatched] WINDOW "+JsonUtility.ToJson(result));
                    File.WriteAllBytes(Path.Combine(output,"indirect-raw-"+frame+".exr"),image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                    File.WriteAllBytes(Path.Combine(output,"indirect-raw-"+frame+".png"),image.EncodeToPNG());
                    Array.Clear(sums,0,sums.Length);Array.Clear(means,0,means.Length);Array.Clear(m2,0,m2.Length);windowCount=0;windowStart=frame+1;
                }
                if(frame<Count)return;
                report.rawIndirectMean=total/Count;
                var packets=new SecondaryPacket[feature.LastContext.secondary.count];feature.LastContext.secondary.GetData(packets);
                var wall=packets.FirstOrDefault(p=>Math.Abs(p.position.x-1.25f)<.005f&&p.flags==1&&p.pdf>0);
                if(wall.flags!=1||wall.diffuse==0)throw new Exception("No evaluated red-wall secondary material packet captured");
                if(commonDiffuse&&(wall.specular&0x00ffffff)!=0x000a0a0a)throw new Exception("Common diffuse fixture did not produce the expected quantized linear F0=.04");
                report.secondaryMaterialSample="diffuse=0x"+wall.diffuse.ToString("X8")+", specularRoughness=0x"+wall.specular.ToString("X8")+", position="+wall.position;
                Debug.Log("[HoGIMatched] SECONDARY_MATERIAL "+report.secondaryMaterialSample);
                if(report.rawIndirectMean.x<1e-5f||report.rawIndirectMean.x<report.rawIndirectMean.y*1.5f)throw new Exception("Red bounce contribution missing");
                var compute=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTGI/Shaders/HoRTGI.compute");
                var messages=ShaderUtil.GetComputeShaderMessages(compute);
                if(messages.Length>0)throw new Exception("GI compute diagnostic: "+string.Join("; ",messages.Select(m=>m.message)));
                Finish(null);
            }
            catch(Exception error){Finish(error);}
        }
        static void Finish(Exception error)
        {
            if(finished)return;finished=true;EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;
            if(report!=null){report.pass=error==null;report.failure=error?.ToString();report.frames=frame;report.windows=windows.ToArray();File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));}
            if(error!=null)Debug.LogException(error);
            GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;ShaderUtil.allowAsyncCompilation=oldAsync;
            foreach(var obj in owned.AsEnumerable().Reverse())if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);
            Debug.Log("[HoGIMatched] END pass="+(error==null));EditorApplication.Exit(error==null?0:1);
        }
        [Serializable] sealed class Window {public int first,last;public Vector3 mean;public double temporalVariance;}
        [StructLayout(LayoutKind.Sequential)] struct SecondaryPacket
        {public Vector3 position;public uint normal,diffuse,specular;public Vector3 radiance;public float pdf;public uint geometricNormal,flags;}
        [Serializable] sealed class Report
        {
            public bool pass,diagnosticOnly=true,noReuse,compatibility;public string unity,gpu,api,colorSpace,failure,secondaryMaterialSample;public int frames;
            public Vector3 rawIndirectMean;public Window[] windows;
            public string protocol="128x128, 256 manual render requests, one point light, reflectance=0/metallic=0/roughness=1, no environment/emission, dedicated raw GI; diagnostic signal comparison, not performance or automatic-frame motion validation";
        }
    }
}
