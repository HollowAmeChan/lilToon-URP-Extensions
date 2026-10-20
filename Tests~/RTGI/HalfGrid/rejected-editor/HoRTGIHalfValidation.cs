#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.RayTracing;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static class HoRTGIHalfValidation
    {
        [Serializable]sealed class Row
        {
            public string name;public bool halfRequested,firstTemporal,firstNrdReset,stableTemporal;public int inputWidth,inputHeight,giWidth,giHeight,scale,valid,changedPixels;
            public double rawError,filteredError,thinRawMean,thinFilteredMean,thinRawMin,thinFilteredMin;
            public HoRTGIResourceAudit.Report resources;
        }
        [Serializable]sealed class Report{public bool pass,functionalPass,qualityPass,compatibility;public string error,gpu;public int requests;public Row[] rows;}
        public static void Run()=>Run(false);
        public static void RunCompatibility()=>Run(true);
        static void Run(bool compatibility)
        {
            var owned=new List<UnityEngine.Object>();var rows=new List<Row>();var report=new Report{compatibility=compatibility,gpu=SystemInfo.graphicsDeviceName};
            var oldDefault=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;bool oldAsync=ShaderUtil.allowAsyncCompilation;HoRTGIRendererFeature gi=null;int exit=1;string output=null;
            try
            {
                if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"))throw new Exception("Dedicated GI project required");
                output=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-gi-half-"+(compatibility?"compat":"rg")));Directory.CreateDirectory(output);
                ShaderUtil.allowAsyncCompilation=false;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);RenderSettings.fog=false;
                var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                var gb=Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());gb.Settings.maxRenderQueue=(int)RenderQueue.GeometryLast;
                var sb=Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());sb.Settings.enableSemanticLanes=sb.Settings.enableCorrelatedSemantics=false;
                var sky=Keep(ScriptableObject.CreateInstance<HoAtmosphereRendererFeature>());sky.Settings.source=HoAtmosphereSource.Constant;sky.Settings.constantColor=Color.white;
                gi=Keep(ScriptableObject.CreateInstance<HoRTGIRendererFeature>());gi.Settings.denoise=true;gi.Settings.includeDirectEnvironment=true;gi.Settings.secondaryEnvironmentSamples=0;gi.Settings.secondaryLightSamples=1;
                // Isolate the mapping/reconstruction analytical signal. A single
                // spatial-ReSTIR realization is not a per-pixel energy oracle (F23).
                gi.Settings.spatial=false;
                renderer.rendererFeatures.Add(gb);renderer.rendererFeatures.Add(sb);renderer.rendererFeatures.Add(sky);renderer.rendererFeatures.Add(gi);
                var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #endif
                var camera=Keep(new GameObject("Half GI real pipeline")).AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;camera.nearClipPlane=.1f;camera.farClipPlane=30;camera.transform.position=new Vector3(0,0,-3);camera.GetUniversalAdditionalCameraData().volumeLayerMask=0;
                Quad("Red receiver",new Vector3(-.4885f,0,0),new Vector3(1.023f,2,1),Color.red);
                Quad("Blue receiver",new Vector3(.5115f,0,0),new Vector3(.977f,2,1),Color.blue);
                var thin=Quad("Unrepresented white foreground",new Vector3(-.734375f,0,-.5f),new Vector3(.015625f,1,1),Color.white);thin.SetActive(false);
                RenderTexture target=null;int clock=0;
                SetTarget(64,64);
                Flat("full-flat",false);Flat("half-flat",true);
                SetTarget(65,63);Flat("odd-full-fallback",true);
                if(gi.LastContext.grid.scale!=1||gi.LastContext.fullRaw!=null||gi.LastContext.fullFiltered!=null)throw new Exception("Odd input did not release half outputs");
                SetTarget(64,64);thin.SetActive(true);
                var full=Thin("full-thin",false);var half=Thin("half-thin",true);
                thin.SetActive(false);Render();if(gi.LastContext.lastTemporal||!gi.LastContext.nrd.lastReset)throw new Exception("Geometry removal did not reset raw/NRD history");
                ScoreFlat(new Row{name="restored-flat"});
                report.functionalPass=true;report.qualityPass=Math.Abs(full.thinRawMean-1)<.05&&Math.Abs(full.thinFilteredMean-1)<.05&&Math.Abs(half.thinRawMean-1)<.05&&Math.Abs(half.thinFilteredMean-1)<.05&&half.thinRawMin>.95&&half.thinFilteredMin>.95;
                report.pass=report.functionalPass&&report.qualityPass;exit=report.pass?0:1;
                if(!report.qualityPass)report.error="Half GI thin foreground failed original 5% energy/minimum gate; no compatible representative cannot be reconstructed from background.";
                Debug.Log("[HoGIHalf] END functional="+report.functionalPass+" quality="+report.qualityPass);

                void SetTarget(int w,int h){target=Keep(new RenderTexture(w,h,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();}
                void Render(){if(gi.LastContext?.nrd!=null)gi.LastContext.nrd.ValidationTimeSeconds=++clock/60.0;VolumeManager.instance.Update(camera.transform,0);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});report.requests++;}
                Row Settle(string name,bool halfRequested)
                {
                    gi.ValidationHalfResolution=halfRequested;gi.Settings.composite=false;Render();var c=gi.LastContext;
                    var row=new Row{name=name,halfRequested=halfRequested,firstTemporal=c.lastTemporal,firstNrdReset=c.nrd.lastReset,inputWidth=target.width,inputHeight=target.height,giWidth=c.width,giHeight=c.height,scale=c.grid.scale};rows.Add(row);
                    if(row.firstTemporal||!row.firstNrdReset)throw new Exception("Lattice transition retained history: "+name);
                    for(int i=0;i<32;i++)Render();row.stableTemporal=c.lastTemporal;if(!row.stableTemporal)throw new Exception("Stable half/full history never reused");
                    var scene=(HoRTScene)typeof(HoRTGIRendererFeature).GetField("scene",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(gi);row.resources=HoRTGIResourceAudit.Capture(c,scene);
                    if(c.RawDiffuse.rt.width!=target.width||c.PublishedDiffuse.rt.width!=target.width||c.RawDiffuse.rt.height!=target.height||c.PublishedDiffuse.rt.height!=target.height)throw new Exception("Publication changed full receiver domain");return row;
                }
                void Flat(string name,bool halfRequested)
                {
                    var row=Settle(name,halfRequested);ScoreFlat(row);var before=Read(target);gi.Settings.composite=true;gi.Settings.intensity=.25f;Render();var after=Read(target);
                    for(int i=0;i<before.Length;i++)if(Math.Max(Math.Abs(before[i].r-after[i].r),Math.Abs(before[i].b-after[i].b))>1e-4)row.changedPixels++;
                    if(row.changedPixels==0)throw new Exception("No real full-domain composition");gi.Settings.composite=false;
                }
                void ScoreFlat(Row row)
                {
                    var raw=Read(gi.LastContext.RawDiffuse.rt);var filtered=Read(gi.LastContext.PublishedDiffuse.rt);
                    for(int y=0;y<target.height;y++)for(int x=0;x<target.width;x++)
                    {
                        int at=y*target.width+x;var ray=camera.ViewportPointToRay(new Vector3((x+.5f)/target.width,(y+.5f)/target.height,0));var point=ray.origin+ray.direction*(-ray.origin.z/ray.direction.z);Color expected=point.x<.023f?Color.red:Color.blue;
                        if(raw[at].a<.5f||filtered[at].a<.5f)throw new Exception("Lost full receiver validity");row.valid++;
                        row.rawError=Math.Max(row.rawError,Error(raw[at],expected));row.filteredError=Math.Max(row.filteredError,Error(filtered[at],expected));
                    }
                    if(row.rawError>.05||row.filteredError>.05)throw new Exception("Full-domain albedo/flat energy gate "+row.name+" raw="+row.rawError+" filtered="+row.filteredError);
                }
                Row Thin(string name,bool halfRequested)
                {
                    var row=Settle(name,halfRequested);var raw=Read(gi.LastContext.RawDiffuse.rt);var filtered=Read(gi.LastContext.PublishedDiffuse.rt);row.thinRawMin=row.thinFilteredMin=double.PositiveInfinity;
                    for(int y=17;y<47;y++)
                    {
                        int at=y*64+8;if(raw[at].a<.5f||filtered[at].a<.5f)throw new Exception("Thin receiver validity lost");
                        double a=Math.Min(raw[at].r,Math.Min(raw[at].g,raw[at].b)),b=Math.Min(filtered[at].r,Math.Min(filtered[at].g,filtered[at].b));row.thinRawMean+=a/30;row.thinFilteredMean+=b/30;row.thinRawMin=Math.Min(row.thinRawMin,a);row.thinFilteredMin=Math.Min(row.thinFilteredMin,b);
                    }
                    Save(gi.LastContext.RawDiffuse.rt,name+"-raw");Save(gi.LastContext.PublishedDiffuse.rt,name+"-filtered");return row;
                }
                Color[] Read(RenderTexture texture)
                {
                    var old=RenderTexture.active;var image=Keep(new Texture2D(texture.width,texture.height,TextureFormat.RGBAFloat,false,true));try{RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();return image.GetPixels();}finally{RenderTexture.active=old;}
                }
                void Save(RenderTexture texture,string name){var pixels=Read(texture);var image=Keep(new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false,true));image.SetPixels(pixels);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());}
                T Keep<T>(T value)where T:UnityEngine.Object{owned.Add(value);return value;}
                GameObject Quad(string name,Vector3 position,Vector3 scale,Color color)
                {
                    var obj=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));obj.name=name;obj.transform.position=position;obj.transform.localScale=scale;
                    var m=Keep(new Material(Shader.Find("lilToon")));m.EnableKeyword("UNITY_UI_ALPHACLIP");m.SetFloat("_Cull",0);m.SetFloat("_Metallic",0);m.SetFloat("_Reflectance",0);m.SetColor("_Color",color);m.SetTexture("_MainTex",Texture2D.whiteTexture);m.SetFloat("_UseEmission",0);m.SetFloat("_UseEmission2nd",0);obj.GetComponent<Renderer>().sharedMaterial=m;return obj;
                }
            }
            catch(Exception e){report.error=e.ToString();Debug.LogException(e);}
            finally
            {
                report.rows=rows.ToArray();if(output!=null)File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));gi?.Dispose();GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;ShaderUtil.allowAsyncCompilation=oldAsync;
                foreach(var obj in owned.AsEnumerable().Reverse())if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);
            }
            EditorApplication.Exit(exit);
        }
        static double Error(Color a,Color b)=>Math.Max(Math.Abs(a.r-b.r),Math.Max(Math.Abs(a.g-b.g),Math.Abs(a.b-b.b)));
    }
}
