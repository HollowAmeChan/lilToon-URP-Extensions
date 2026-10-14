#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.SurfaceBuffer;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static class HoRTDICastleFrameValidation
    {
        [Serializable] private sealed class Report
        {
            public bool pass,compatibility,skyShellExcluded,environmentActive;public string camera,gpu,status,skybox;public int width,height,acceptedLights,acceptedInstances,validPixels,litPixels,invalidPixels,warmupFrames;
            public double peak,meanValid;public long auxiliaryBytes;public string[] rejections;
        }
        public static void Run()=>Run(false);
        public static void RunCompatibility()=>Run(true);
        private static void Run(bool compatibility)
        {
            if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/"))throw new InvalidOperationException("Castle frame audit requires isolated copied project");
            var oldDefault=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;bool oldAsync=ShaderUtil.allowAsyncCompilation;var owned=new List<UnityEngine.Object>();HoRTDIRendererFeature di=null;int exit=0;
            #if URP_COMPATIBILITY_MODE
            bool oldCompatibility=GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode;
            #endif
            try
            {
                ShaderUtil.allowAsyncCompilation=false;EditorSceneManager.OpenScene("Assets/mmd场景测试/血月古堡/血月古堡.unity",OpenSceneMode.Single);
                var source=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.InstanceID).FirstOrDefault(c=>c.CompareTag("MainCamera"));if(source==null)throw new Exception("Copied scene MainCamera missing");
                var renderers=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.InstanceID);var skyMaterials=renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.name=="天空").Distinct().ToArray();if(skyMaterials.Length==0)throw new Exception("Known fixture sky material missing");
                var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();owned.Add(renderer);
                var gb=ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>();owned.Add(gb);gb.Settings.maxRenderQueue=(int)RenderQueue.GeometryLast;
                var sb=ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>();owned.Add(sb);sb.Settings.enableSemanticLanes=sb.Settings.enableCorrelatedSemantics=false;
                var atmosphere=ScriptableObject.CreateInstance<HoAtmosphereRendererFeature>();owned.Add(atmosphere);atmosphere.Settings.source=HoAtmosphereSource.SceneSkybox;
                di=ScriptableObject.CreateInstance<HoRTDIRendererFeature>();owned.Add(di);di.Settings.excludedRayMaterials=skyMaterials;di.Settings.finalSampling=HoRTDIFinalSampling.TripleAnalytic;di.Settings.localSampling=HoRTDILocalSampling.Power;di.Settings.denoise=true;
                renderer.rendererFeatures.Add(atmosphere);renderer.rendererFeatures.Add(gb);renderer.rendererFeatures.Add(sb);renderer.rendererFeatures.Add(di);
                var pipeline=UniversalRenderPipelineAsset.Create(renderer);owned.Add(pipeline);pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #else
                if(compatibility)throw new NotSupportedException("Compatibility build symbol missing");
                #endif
                var holder=new GameObject("Isolated castle frame camera");owned.Add(holder);var camera=holder.AddComponent<Camera>();camera.CopyFrom(source);camera.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);camera.enabled=false;camera.targetTexture=null;camera.aspect=1;
                camera.GetUniversalAdditionalCameraData().volumeLayerMask=0;
                var target=new RenderTexture(512,512,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);owned.Add(target);target.Create();
                for(int frame=0;frame<16;frame++){VolumeManager.instance.Update(camera.transform,0);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});}
                var c=di.LastContext;if(c==null||c.nrd==null||c.nrd.LastDispatchCount==0)throw new Exception("Actual castle DI/NRD context missing: "+di.Status);
                var image=new Texture2D(512,512,TextureFormat.RGBAFloat,false,true);owned.Add(image);var previous=RenderTexture.active;try{RenderTexture.active=c.filteredDiffuse.rt;image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();}finally{RenderTexture.active=previous;}
                var pixels=image.GetPixels();var report=new Report{compatibility=compatibility,skyShellExcluded=true,camera=source.name,gpu=SystemInfo.graphicsDeviceName,status=di.Status,width=512,height=512,warmupFrames=16,environmentActive=c.environmentSampling,skybox=RenderSettings.skybox!=null?RenderSettings.skybox.name:null,acceptedLights=c.previousLightKeys.Length,auxiliaryBytes=(c.auxiliarySecondary?.count??0)*8L};
                var scene=(lilToon.URP.Extensions.RayTracing.HoRTScene)di.GetType().GetField("scene",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(di);report.acceptedInstances=scene.InstanceCount;report.rejections=scene.Rejections.ToArray();
                foreach(var pixel in pixels)
                {
                    if(!float.IsFinite(pixel.r)||!float.IsFinite(pixel.g)||!float.IsFinite(pixel.b)||!float.IsFinite(pixel.a)||pixel.r<0||pixel.g<0||pixel.b<0)throw new Exception("Castle output contains invalid radiance");
                    if(pixel.a>.5f){report.validPixels++;double peak=Math.Max(pixel.r,Math.Max(pixel.g,pixel.b));report.peak=Math.Max(report.peak,peak);report.meanValid+=peak;if(peak>1e-5)report.litPixels++;}else report.invalidPixels++;
                }
                report.meanValid/=Math.Max(1,report.validPixels);report.pass=report.validPixels>0&&report.litPixels>0&&report.acceptedLights>0;
                string prefix=Path.Combine(Application.dataPath,"../rtdi-castleframe86-"+(compatibility?"compat":"rg"));File.WriteAllText(prefix+".json",JsonUtility.ToJson(report,true));
                var preview=new Texture2D(512,512,TextureFormat.RGBA32,false,true);owned.Add(preview);for(int i=0;i<pixels.Length;i++){var p=pixels[i];pixels[i]=p.a>.5f?new Color(Mathf.Pow(p.r/(1+p.r),1/2.2f),Mathf.Pow(p.g/(1+p.g),1/2.2f),Mathf.Pow(p.b/(1+p.b),1/2.2f),1):Color.magenta;}preview.SetPixels(pixels);preview.Apply();File.WriteAllBytes(prefix+".png",preview.EncodeToPNG());
                if(!report.pass)throw new Exception("Castle frame has no valid/lit primary output");Debug.Log($"[HoRTDIValidation] CASTLE_FRAME pass=true valid={report.validPixels} lit={report.litPixels} lights={report.acceptedLights} instances={report.acceptedInstances}; support audit, no artistic/energy acceptance");
            }
            catch(Exception error){Debug.LogException(error);exit=1;}
            finally
            {
                di?.Dispose();GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;ShaderUtil.allowAsyncCompilation=oldAsync;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=oldCompatibility;
                #endif
                for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)UnityEngine.Object.DestroyImmediate(owned[i]);
            }
            EditorApplication.Exit(exit);
        }
    }
}
