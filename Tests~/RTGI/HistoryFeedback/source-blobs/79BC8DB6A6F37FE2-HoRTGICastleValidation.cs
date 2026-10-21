#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
    public static partial class HoRTGICastleValidation
    {
        static readonly List<UnityEngine.Object> owned=new();
        static T Keep<T>(T value)where T:UnityEngine.Object{owned.Add(value);return value;}
        public static void Run()=>Run(false,128);
        public static void RunCompatibility()=>Run(true,128);
        public static void Run256()=>Run(false,256);
        public static void Run256Compatibility()=>Run(true,256);
        public static void RunStability()=>Run(false,256,true);
        public static void RunStabilityCompatibility()=>Run(true,256,true);
        public static void RunNrdStages()=>Run(false,256,true,true);
        public static void RunLongStability()=>Run(false,256,true,false,256,256);
        public static void RunLongWideClamp()=>Run(false,256,true,false,256,256,true);
        public static void RunLongWideClampSix()=>Run(false,256,true,false,256,256,true,6);
        public static void RunGpu256()=>Run(false,256,gpu:true);
        public static void RunGpu256Compatibility()=>Run(true,256,gpu:true);
        public static void RunGpu512()=>Run(false,512,gpu:true);
        public static void RunGpu512Compatibility()=>Run(true,512,gpu:true);
        public static void RunGpu256TotalOnly()=>Run(false,256,gpu:true,totalOnly:true);
        public static void RunGpu256Rebuild()=>Run(false,256,gpu:true,rebuild:true);
        public static void RunFixedASControls()=>Run(false,256,true,false,256,256,denseReference:true,denseSamples:512,denseBatches:16384,denseScheduled:true,denseControls:true,validationTag:"fixed-as");
        public static void RunFixedASReference()=>Run(false,256,true,false,256,256,denseReference:true,denseSamples:512,denseBatches:2048,denseScheduled:true,validationTag:"fixed-as");
        public static void RunFixedASSpatial()=>Run(false,256,true,true,256,256,spatialAudit:true,denseScheduled:true,validationTag:"fixed-as");
        public static void RunDenseScheduledQualification()=>Run(false,256,denseReference:true,denseSamples:512,denseBatches:2,denseScheduled:true);
        public static void RunSpatialAudit()=>Run(false,256,true,true,256,256,spatialAudit:true);
        public static void RunSpatialAuditNormalized()=>Run(false,256,true,true,256,256,spatialAudit:true,momentScale:.125f);
        public static void RunMomentBindings()=>Run(false,64,bindings:true);
        public static void RunMomentAuditOne()=>Run(false,256,true,true,256,256,moments:true,momentScale:1);
        public static void RunMomentAuditEighth()=>Run(false,256,true,true,256,256,moments:true,momentScale:.125f);
        public static void RunMomentAudit()=>Run(false,256,true,true,256,256,moments:true);
        public static void RunResources256()=>Run(false,256,gpu:true,resources:true);
        public static void RunResources512()=>Run(false,512,gpu:true,resources:true);
        public static void RunResources1080()=>Run(false,1920,gpu:true,resources:true,renderHeight:1080);
        public static void RunResources1080Compatibility()=>Run(true,1920,gpu:true,resources:true,renderHeight:1080);
        static async void Run(bool compatibility,int size,bool stability=false,bool stages=false,int settle=32,int measured=64,bool wideClamp=false,float historySigma=4,bool gpu=false,bool totalOnly=false,bool rebuild=false,bool resources=false,int renderHeight=0,bool moments=false,float momentScale=64,bool bindings=false,bool spatialAudit=false,bool denseReference=false,int denseSamples=512,int denseBatches=2,bool denseScheduled=false,bool denseControls=false,string validationTag="",bool seedAudit=false,int seedCount=8,int seedWarmup=64,int seedMeasured=256,bool feedbackAudit=false,bool feedbackQualification=false)
        {
            int width=size,height=renderHeight>0?renderHeight:size;
            var oldDefault=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;bool oldAsync=ShaderUtil.allowAsyncCompilation;HoRTGIRendererFeature gi=null;int exit=1;
            HoRTGIGpuClock gpuClock=null;HoRTGIDenseReferenceFeature denseFeature=null;HoRTGIDenseReferenceAudit denseAudit=null;
            var report=new Report{compatibility=compatibility,width=width,height=height,gpu=SystemInfo.graphicsDeviceName};string output=null;
            try
            {
                if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"))throw new Exception("Dedicated GI project required");
                output=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-gi-castle-"+width+(height==width?"":"x"+height)+"-"+(compatibility?"compat":"rg")+(stability?"-stability":"")+(validationTag.Length>0?"-"+validationTag:"")+(stages?"-stages":"")+(measured>64?"-long":"")+(wideClamp?"-wide-clamp"+(historySigma==4?"":"-"+historySigma):"")+(denseReference?"-dense-"+denseSamples+"x"+denseBatches+(denseScheduled?"-scheduled":"")+(denseControls?"-controls":""):"")+(spatialAudit?"-spatial-audit-scale-"+momentScale:"")+(bindings?"-bindings":"")+(moments?"-moments"+(momentScale==64?"":"-scale-"+momentScale):"")+(resources?"-resources":"")+(gpu?"-gpu"+(totalOnly?"-total-only":"")+(rebuild?"-rebuild":""):"")));Directory.CreateDirectory(output);
                ShaderUtil.allowAsyncCompilation=false;EditorSceneManager.OpenScene("Assets/mmd场景测试/血月古堡/血月古堡.unity",OpenSceneMode.Single);
                var source=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.InstanceID).FirstOrDefault(c=>c.CompareTag("MainCamera"));if(source==null)throw new Exception("Castle MainCamera missing");report.camera=source.name;
                var geometry=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.InstanceID);
                var sky=geometry.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.name=="天空").Distinct().ToArray();if(sky.Length==0)throw new Exception("Known copied sky shell material missing");
                report.materials=geometry.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().Select(m=>new MaterialRow{name=m.name,shader=m.shader!=null?m.shader.name:"null",queue=m.renderQueue,excluded=sky.Contains(m),supported=HoRTGISecondaryMaterials.IsSupported(m),cutout=HoRTGISecondaryMaterials.SupportsCutout(m),main2=m.HasProperty("_UseMain2ndTex")&&m.GetFloat("_UseMain2ndTex")!=0,main3=m.HasProperty("_UseMain3rdTex")&&m.GetFloat("_UseMain3rdTex")!=0}).ToArray();
                var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                var gb=Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());gb.Settings.maxRenderQueue=(int)RenderQueue.GeometryLast;
                var sb=Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());sb.Settings.enableSemanticLanes=sb.Settings.enableCorrelatedSemantics=false;
                var atmosphere=Keep(ScriptableObject.CreateInstance<HoAtmosphereRendererFeature>());atmosphere.Settings.source=HoAtmosphereSource.SceneSkybox;
                gi=Keep(ScriptableObject.CreateInstance<HoRTGIRendererFeature>());gi.Settings.excludedRayMaterials=sky;gi.Settings.denoise=true;gi.Settings.composite=false;
                renderer.rendererFeatures.Add(atmosphere);renderer.rendererFeatures.Add(gb);renderer.rendererFeatures.Add(sb);renderer.rendererFeatures.Add(gi);
                if(denseReference||seedAudit||feedbackAudit){denseFeature=Keep(ScriptableObject.CreateInstance<HoRTGIDenseReferenceFeature>());denseFeature.source=gi;renderer.rendererFeatures.Add(denseFeature);}
                var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #endif
                var camera=Keep(new GameObject("GI copied castle view")).AddComponent<Camera>();camera.CopyFrom(source);camera.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);camera.enabled=false;camera.targetTexture=null;camera.aspect=(float)width/height;camera.GetUniversalAdditionalCameraData().volumeLayerMask=0;
                var target=Keep(new RenderTexture(width,height,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();var image=Keep(new Texture2D(width,height,TextureFormat.RGBAFloat,false,true));
                int clock=0;for(int i=0;i<32;i++){if(denseScheduled)await NextEditorTick();Render();}
                if(denseReference||seedAudit||feedbackAudit){denseFeature.targetCamera=camera;var receivers=denseControls?HoRTGIDenseReferenceAudit.ControlPixels:HoRTGIDenseReferenceAudit.Pixels;denseFeature.Initialize(seedAudit||feedbackAudit?1:denseSamples,receivers);denseFeature.scheduled=denseScheduled;denseAudit=new HoRTGIDenseReferenceAudit(denseFeature,receivers);}
                var context=gi.LastContext;if(context?.nrd==null)throw new Exception("Actual castle GI/NRD missing: "+gi.Status);
                report.status=gi.Status;var registry=(HoRTScene)typeof(HoRTGIRendererFeature).GetField("scene",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(gi);
                report.instances=registry.InstanceCount;report.cutouts=registry.CutoutCount;report.skinnedSnapshots=registry.SkinnedMeshCount;report.rejections=registry.Rejections.ToArray();report.bakes=registry.SkinnedBakeCount;
                if(gpu)
                {
                    gpuClock=new HoRTGIGpuClock{TotalOnly=totalOnly};context.ValidationGpuMarker=gpuClock.Mark;context.ValidationGpuRasterMarker=gpuClock.MarkRaster;gi.Settings.composite=true;gi.Settings.intensity=.25f;
                    if(rebuild){registry.MarkGeometryDirty();context.environmentPdfReady=false;}
                    for(int frame=0;frame<40;frame++){gpuClock.Frame=frame;Render();gpuClock.Collect();}
                    report.gpuSummary=gpuClock.Summarize(32);report.gpuSamples=gpuClock.samples.OrderBy(x=>x.slot).ToArray();report.gpuQualified=true;report.gpuTotalOnly=totalOnly;report.gpuForcedRebuild=rebuild;
                    context.ValidationGpuMarker=null;context.ValidationGpuRasterMarker=null;gi.Settings.composite=false;
                }
                if(bindings)report.momentBindings=HoRTGIMomentAudit.CaptureBindings(context.nrd);
                if(resources)report.resourceAudit=HoRTGIResourceAudit.Capture(context,registry);
                if(feedbackAudit)report.feedbackAudit=await AuditHistoryFeedback(gi,context,denseFeature,Render,Read,feedbackQualification,output);
                if(seedAudit)report.seedAudit=await AuditSeeds(gi,context,denseFeature,Render,Read,seedCount,seedWarmup,seedMeasured,output);
                if(stability)
                {
                    if(moments||spatialAudit)context.nrd.ValidationSignalScale=momentScale;
                    if(wideClamp){context.nrd.core.ValidationSettings=s=>{s.fastSigma=historySigma;return s;};context.nrd.historyValid=false;}
                    for(int i=0;i<settle;i++){if(denseScheduled)await NextEditorTick();Render();}int count=measured;int components=((width+3)/4)*((height+3)/4)*3;var means=new double[2,components];var m2=new double[2,components];
                    var spatial=spatialAudit?new HoRTGISpatialAudit(width,height):null;
                    var momentAudit=moments?new HoRTGIMomentAudit():null;
                    var captured=new Dictionary<int,RenderTexture>();var stageSums=new Dictionary<int,Vector3>();var frameMeans=new List<FrameMean>();
                    if(stages)context.nrd.core.ValidationDiffuseStage=(cmd,index,texture)=>{if(!captured.TryGetValue(index,out var copy)){copy=Keep(new RenderTexture(width,height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));copy.Create();captured.Add(index,copy);}cmd.CopyTexture(texture,copy);};
                    for(int frame=0;frame<count;frame++)
                    {
                        if(denseScheduled)await NextEditorTick();
                        Render();var a=Read(context.diffuse.rt);var b=Read(context.PublishedDiffuse.rt);int index=0;spatial?.AddPublication(a,b,frame);denseAudit?.AddMain(a,b,width);
                        Vector3 frameA=Vector3.zero,frameB=Vector3.zero;
                        for(int y=0;y<height;y+=4)for(int x=0;x<width;x+=4)for(int channel=0;channel<3;channel++,index++)
                        {
                            if(a[y*width+x].a<.5f||b[y*width+x].a<.5f)throw new Exception("Static scene receiver validity changed");
                            for(int signal=0;signal<2;signal++){double value=signal==0?a[y*width+x][channel]:b[y*width+x][channel];if(!double.IsFinite(value)||value<0)throw new Exception("Invalid stability sample");double delta=value-means[signal,index];means[signal,index]+=delta/(frame+1);m2[signal,index]+=delta*(value-means[signal,index]);}
                            frameA[channel]+=a[y*width+x][channel]/(components/3);frameB[channel]+=b[y*width+x][channel]/(components/3);
                        }
                        frameMeans.Add(new FrameMean{index=frame,raw=frameA,filtered=frameB});
                        if(stages)
                        {
                            var guides=new Primary[width*height];context.history.GetData(guides);
                            Color[] inputPixels=null;
                            Accumulate(-1,context.nrd.signal.rt);
                            foreach(var pair in captured)Accumulate(pair.Key,pair.Value);
                            void Accumulate(int key,RenderTexture texture)
                            {
                                var pixels=Read(texture);Vector3 sum=Vector3.zero;
                                if(key==-1)inputPixels=pixels;
                                spatial?.AddStage(key,pixels,guides,context.nrd.AppliedSignalScale);
                                if(momentAudit!=null)momentAudit.Add(key,pixels,inputPixels,a,b,width,height,frame,context.nrd.AppliedSignalScale);
                                for(int y=0;y<height;y+=4)for(int x=0;x<width;x+=4){int at=y*width+x;uint albedo=guides[at].diffuse;var p=pixels[at];sum+=new Vector3(p.r*(albedo&255),p.g*((albedo>>8)&255),p.b*((albedo>>16)&255))*(context.nrd.AppliedSignalScale/(255*(components/3)*count));}
                                stageSums.TryGetValue(key,out var previous);stageSums[key]=previous+sum;
                            }
                        }
                    }
                    context.nrd.core.ValidationDiffuseStage=null;report.frameMeans=frameMeans.ToArray();if(stages)report.stages=stageSums.OrderBy(x=>x.Key).Select(x=>new StageMean{pipeline=x.Key,remodulatedMean=x.Value}).ToArray();
                    if(spatial!=null)report.spatialAudit=spatial.Finish();
                    if(momentAudit!=null){report.moments=momentAudit.Finish();report.momentAuditComplete=report.moments.Length==2;}
                    var rgb=new double[2,3];for(int signal=0;signal<2;signal++)for(int i=0;i<components;i++){rgb[signal,i%3]+=means[signal,i]/(components/3);if(signal==0)report.rawVariance+=m2[signal,i]/((count-1)*components);else report.filteredVariance+=m2[signal,i]/((count-1)*components);}
                    report.staticRaw=new Vector3((float)rgb[0,0],(float)rgb[0,1],(float)rgb[0,2]);report.staticFiltered=new Vector3((float)rgb[1,0],(float)rgb[1,1],(float)rgb[1,2]);report.observedFrames=count;report.energyGatePass=true;
                    for(int channel=0;channel<3;channel++)report.energyGatePass&=Math.Abs(rgb[1,channel]-rgb[0,channel])<Math.Max(.000005,Math.Abs(rgb[0,channel])*.05);
                }
                if(denseReference){denseFeature.collect=true;for(int batch=0;batch<denseBatches;batch++){if(denseScheduled)await NextEditorTick();denseFeature.batch=batch;Render();denseAudit.AddBatch(batch);if(denseScheduled&&(batch+1)%512==0)File.WriteAllText(Path.Combine(output,"dense-progress.json"),JsonUtility.ToJson(new DenseProgress{terminal=false,completedBatches=batch+1,audit=denseAudit.Finish()},false));}denseFeature.collect=false;report.denseReference=denseAudit.Finish();}
                Save(target,"scene-before",false);Save(context.diffuse.rt,"raw-gi",true);Save(context.PublishedDiffuse.rt,"filtered-gi",true);Save(context.PublishedDiffuse.rt,"filtered-gi-preview-x100",true,100);
                var raw=Read(context.diffuse.rt);var filtered=Read(context.PublishedDiffuse.rt);var before=Read(target);
                for(int i=0;i<filtered.Length;i++)
                {
                    var pixel=filtered[i];if(!Finite(pixel)||!Finite(raw[i]))throw new Exception("Invalid castle GI radiance");
                    if(pixel.a>.5f){report.valid++;report.filteredMean+=Max(pixel);report.rawMean+=Max(raw[i]);report.peak=Math.Max(report.peak,Max(pixel));if(Max(pixel)>1e-5)report.lit++;}else report.invalid++;
                }
                report.rawMean/=Math.Max(1,report.valid);report.filteredMean/=Math.Max(1,report.valid);
                gi.Settings.composite=true;gi.Settings.intensity=.25f;Render();Save(target,"scene-added-quarter",false);var added=Read(target);
                for(int i=0;i<added.Length;i++){if(!Finite(added[i]))throw new Exception("Invalid composed camera color");double delta=Math.Max(Math.Abs(added[i].r-before[i].r),Math.Max(Math.Abs(added[i].g-before[i].g),Math.Abs(added[i].b-before[i].b)));report.cameraMeanChange+=delta;if(delta>1e-4)report.changedPixels++;}report.cameraMeanChange/=added.Length;
                gi.Settings.intensity=1;Render();Save(target,"scene-added-one",false);
                report.stableBakes=registry.SkinnedBakeCount-report.bakes;report.historyReused=context.lastTemporal;report.environment=context.environmentSampling;report.nativeCutout=context.alphaVisibility;report.skybox=RenderSettings.skybox!=null?RenderSettings.skybox.name:null;
                report.pass=report.instances>0&&report.cutouts>0&&report.valid>0&&report.lit>0&&report.changedPixels>0&&report.stableBakes==0;
                if(gpu)report.pass=report.gpuQualified&&report.instances>0&&report.valid>0&&report.changedPixels>0;
                if(stability)report.pass&=report.energyGatePass;
                if(stability&&!report.energyGatePass)throw new Exception("Castle filtered RGB time-mean failed original 5% energy gate");
                if(!report.pass)throw new Exception("Castle scene lacks actual supported/lit/composed GI or stable pose cache");
                Debug.Log("[HoGICastle] PASS actual scene instances="+report.instances+" cutout="+report.cutouts+" valid="+report.valid+" lit="+report.lit+" changed="+report.changedPixels);exit=0;

                void Render(){if(gi.LastContext?.nrd!=null)gi.LastContext.nrd.ValidationTimeSeconds=++clock/60.0;VolumeManager.instance.Update(camera.transform,0);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});report.frames++;}
                Color[] Read(RenderTexture texture){var old=RenderTexture.active;try{RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();return image.GetPixels();}finally{RenderTexture.active=old;}}
                void Save(RenderTexture texture,string name,bool showValidity,float previewExposure=1){var pixels=Read(texture);File.WriteAllBytes(Path.Combine(output,name+".exr"),image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));var preview=Keep(new Texture2D(width,height,TextureFormat.RGBA32,false,true));for(int i=0;i<pixels.Length;i++){var p=pixels[i];pixels[i]=showValidity&&p.a<.5f?Color.magenta:new Color(Map(p.r*previewExposure),Map(p.g*previewExposure),Map(p.b*previewExposure),1);}preview.SetPixels(pixels);preview.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),preview.EncodeToPNG());}
            }
            catch(Exception error){report.error=error.ToString();Debug.LogException(error);}
            finally
            {
                if(output!=null)File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,!denseReference));
                if(gi?.LastContext!=null){gi.LastContext.ValidationGpuMarker=null;gi.LastContext.ValidationGpuRasterMarker=null;}gpuClock?.Dispose();denseFeature?.Dispose();gi?.Dispose();GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;ShaderUtil.allowAsyncCompilation=oldAsync;
                foreach(var obj in owned.AsEnumerable().Reverse())if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);
            }
            EditorApplication.Exit(exit);
        }
        [Serializable]sealed class DenseProgress{public bool terminal;public int completedBatches;public HoRTGIDenseReferenceAudit.Report audit;}
        static Task NextEditorTick()
        {
            var completion=new TaskCompletionSource<bool>();
            void Wake(){EditorApplication.update-=Wake;completion.SetResult(true);}
            EditorApplication.update+=Wake;EditorApplication.QueuePlayerLoopUpdate();return completion.Task;
        }
        static float Map(float x)=>Mathf.Pow(Mathf.Max(0,x)/(1+Mathf.Max(0,x)),1/2.2f);
        static bool Finite(Color c)=>float.IsFinite(c.r)&&float.IsFinite(c.g)&&float.IsFinite(c.b)&&float.IsFinite(c.a)&&c.r>=0&&c.g>=0&&c.b>=0;
        static double Max(Color c)=>Math.Max(c.r,Math.Max(c.g,c.b));
        [Serializable]sealed class MaterialRow{public string name,shader;public int queue;public bool excluded,supported,cutout,main2,main3;}
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]internal struct Primary{public uint depth,normal,geometric,diffuse,specular;}
        [Serializable]sealed class StageMean{public int pipeline;public Vector3 remodulatedMean;}
        [Serializable]sealed class FrameMean{public int index;public Vector3 raw,filtered;}
        [Serializable]sealed class Report{public bool pass,compatibility,historyReused,environment,nativeCutout,energyGatePass,gpuQualified,gpuTotalOnly,gpuForcedRebuild;public string error,camera,gpu,status,skybox;public int width,height,frames,observedFrames,instances,cutouts,skinnedSnapshots,valid,invalid,lit,changedPixels;public uint bakes,stableBakes;public double rawMean,filteredMean,peak,cameraMeanChange,rawVariance,filteredVariance;public Vector3 staticRaw,staticFiltered;public MaterialRow[] materials;public string[] rejections;public StageMean[] stages;public FrameMean[] frameMeans;public HoRTGIDenseReferenceAudit.Report denseReference;public HoRTGISpatialAudit.Report spatialAudit;public HoRTGIMomentAudit.Binding[] momentBindings;public HoRTGISeedAudit.Report seedAudit;public HoRTGIHistoryFeedbackAudit.Report feedbackAudit;public bool momentAuditComplete;public HoRTGIMomentAudit.Stage[] moments;public HoRTGIResourceAudit.Report resourceAudit;public HoRTGIGpuClock.Sample[] gpuSamples;public HoRTGIGpuClock.Summary[] gpuSummary;}
    }
}
