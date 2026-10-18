using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using PathTracing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public static class HoReferenceConvergenceProbe
{
    static Camera camera;
    static RenderTexture target;
    static ScriptableRendererFeature feature;
    static RenderPipelineAsset previousDefault,previousQuality;
    static readonly List<UnityEngine.Object> owned=new();
    static bool native;
    static bool skipEmptyEmitters;
    static bool disableRR;
    static bool matchedPoint;
    static HoRTDIConvergenceStatistics statistics;
    static HoRTDIConvergenceStatistics reconstructedStatistics;
    static int firstFrame=1;
    static readonly List<HoRTDIConvergenceStatistics.Result> measurements=new();
    static int frame;
    static Texture2D image;
    static string failure;
    static T Keep<T>(T obj) where T:UnityEngine.Object {owned.Add(obj);return obj;}
    public static void RunNative(){native=true;Setup();}
    public static void RunUnity(){native=false;Setup();}
    public static void RunUnityAnalytic(){skipEmptyEmitters=true;RunUnity();}
    public static void RunUnityAnalyticRaw(){disableRR=true;RunUnityAnalytic();}
    public static void RunMatchedRaw(){matchedPoint=true;disableRR=true;RunUnity();}
    public static void RunMatchedRR(){matchedPoint=true;RunUnity();}
    public static void RunMatchedNative(){matchedPoint=true;RunNative();}
    static void Setup()
    {
        try
        {
            if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/"))throw new Exception("Isolated reference project required");
            previousDefault=GraphicsSettings.defaultRenderPipeline;previousQuality=QualitySettings.renderPipeline;
            ShaderUtil.allowAsyncCompilation=false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
            if(native)
            {
                var shaderSettings=NativeRender.NativeShaderProjectSettings.instance;
                shaderSettings.globalDefines=new[]{"WITH_NRD=1","NRD_NORMAL_ENCODING=2","NRD_ROUGHNESS_ENCODING=1"};
                shaderSettings.globalIncludePaths=new[]{Path.GetFullPath(Path.Combine(Application.dataPath,"../../UnityPathTracing/RenderingPlugin/External/NRD/Shaders"))};
                shaderSettings.Save();
                foreach(string shaderPath in new[]{"Assets/RTXDI/Shaders/LightingPasses/DI/ShadeSamples.computeshader","Assets/RTXDI/Shaders/CompositingPass.computeshader"})
                    AssetDatabase.ImportAsset(shaderPath,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                var source=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/RTXDI/NativeRtxdiRenderer.asset").rendererFeatures.OfType<NativeRtxdiFeature>().Single();
                var f=Keep(UnityEngine.Object.Instantiate(source));feature=f;
                f.setting.indirectLightingMode=IndirectLightingMode.None;f.setting.directLightingMode=DirectLightingMode.ReStir;
                f.setting.cameraJitter=false;f.setting.environmentMap=null;f.setting.enableEnv=false;f.setting.SR=false;
                f.setting.denoiserMode=RtxDiDenoiserType.DENOISER_MODE_RELAX;f.setting.enableGradients=false;f.setting.enableToneMapping=false;
                if(matchedPoint)
                {
                    f.setting.diResamplingMode=Rtxdi.DI.ReSTIRDI_ResamplingMode.TemporalAndSpatial;
                    f.setting.initialSamplingParams.localLightSamplingMode=Rtxdi.DI.ReSTIRDI_LocalLightSamplingMode.Uniform;
                    f.setting.initialSamplingParams.enableInitialVisibility=0;
                    f.setting.initialSamplingParams.numLocalLightSamples=8;f.setting.initialSamplingParams.numBrdfSamples=0;f.setting.initialSamplingParams.numEnvironmentSamples=0;
                    f.setting.temporalResamplingParams.maxHistoryLength=4;f.setting.temporalResamplingParams.depthThreshold=.05f;f.setting.temporalResamplingParams.normalThreshold=.9f;
                    f.setting.temporalResamplingParams.biasCorrectionMode=Rtxdi.DI.ReSTIRDI_TemporalBiasCorrectionMode.Raytraced;
                    f.setting.spatialResamplingParams.numSamples=4;f.setting.spatialResamplingParams.numDisocclusionBoostSamples=4;f.setting.spatialResamplingParams.samplingRadius=16;
                    f.setting.spatialResamplingParams.depthThreshold=.05f;f.setting.spatialResamplingParams.normalThreshold=.9f;
                    f.setting.spatialResamplingParams.biasCorrectionMode=Rtxdi.DI.ReSTIRDI_SpatialBiasCorrectionMode.Raytraced;
                }
            }
            else
            {
                var source=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/RTXDI/UnityRtxdiRenderer.asset").rendererFeatures.OfType<UnityRtxdiFeature>().Single();
                var f=Keep(UnityEngine.Object.Instantiate(source));feature=f;
                f.setting.indirectLightingMode=IndirectLightingMode.None;f.setting.directLightingMode=DirectLightingMode.ReStir;
                f.setting.cameraJitter=false;f.setting.enableEnv=false;f.setting.tmpDisableRR=disableRR;
                if(matchedPoint)
                {
                    f.setting.diResamplingMode=Rtxdi.DI.ReSTIRDI_ResamplingMode.TemporalAndSpatial;
                    f.setting.initialSamplingParams.localLightSamplingMode=Rtxdi.DI.ReSTIRDI_LocalLightSamplingMode.Uniform;
                    f.setting.initialSamplingParams.enableInitialVisibility=0;
                    f.setting.initialSamplingParams.numLocalLightSamples=8;f.setting.initialSamplingParams.numBrdfSamples=0;f.setting.initialSamplingParams.numEnvironmentSamples=0;
                    f.setting.temporalResamplingParams.maxHistoryLength=4;f.setting.temporalResamplingParams.depthThreshold=.05f;f.setting.temporalResamplingParams.normalThreshold=.9f;
                    f.setting.temporalResamplingParams.biasCorrectionMode=Rtxdi.DI.ReSTIRDI_TemporalBiasCorrectionMode.Raytraced;
                    f.setting.spatialResamplingParams.numSamples=4;f.setting.spatialResamplingParams.numDisocclusionBoostSamples=4;f.setting.spatialResamplingParams.samplingRadius=16;
                    f.setting.spatialResamplingParams.depthThreshold=.05f;f.setting.spatialResamplingParams.normalThreshold=.9f;
                    f.setting.spatialResamplingParams.biasCorrectionMode=Rtxdi.DI.ReSTIRDI_SpatialBiasCorrectionMode.Raytraced;
                }
                if(skipEmptyEmitters)
                {
                    f.Create();
                    typeof(UnityRtxdiFeature).GetField("_prepareLightPass",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(f,new EmptyEmitterPreparePass(f));
                }
            }
            renderer.rendererFeatures.Add(feature);
            var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            camera=Keep(new GameObject("Ho reference DI camera")).AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1;
            camera.transform.position=new Vector3(0,0,-3);camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.aspect=1;camera.allowHDR=true;
            target=Keep(new RenderTexture(128,128,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();
            image=Keep(new Texture2D(128,128,TextureFormat.RGBAFloat,false,true));
            var quad=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));quad.transform.localScale=new Vector3(2,2,1);
            var shader=Shader.Find("RayTracing/Lit");if(shader==null)throw new Exception("Reference receiver shader missing");
            var material=Keep(new Material(shader));material.SetColor("_BaseColor",Color.white);material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",.5f);material.SetFloat("_IOR",1.5f);
            quad.GetComponent<Renderer>().sharedMaterial=material;
            if(native)quad.AddComponent<NativeRender.NativeRayTracingTarget>();
            for(int i=0;i<2;i++){var light=Keep(new GameObject("Ho reference directional "+i)).AddComponent<Light>();light.type=LightType.Directional;light.color=i==0?Color.red:Color.blue;light.intensity=1;light.transform.rotation=Quaternion.Euler(0,i==0?25:-25,0);}
            if(matchedPoint)
            {
                camera.orthographic=false;camera.fieldOfView=40;
                foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)){light.type=LightType.Point;light.transform.position=new Vector3(light.color.r>.5f?1.5f:-1.5f,0,-1);light.intensity=4;light.range=100;}
                statistics=new HoRTDIConvergenceStatistics(32*32*3);
                reconstructedStatistics=new HoRTDIConvergenceStatistics(32*32*3);
            }
            Application.logMessageReceived+=OnLog;
            Debug.Log("[HoReference] RENDER_START native="+native);
            EditorApplication.update+=Tick;
        }
        catch(Exception error){Finish(error);}
    }
    static void OnLog(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)failure=text;}
    sealed class EmptyEmitterPreparePass:PrepareLightPass
    {
        readonly UnityRtxdiFeature owner;
        internal EmptyEmitterPreparePass(UnityRtxdiFeature owner){this.owner=owner;}
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
        {
            var scene=(RTXDI.LightScene)typeof(UnityRtxdiFeature).GetField("_lightScene",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
            if(scene.emissiveTriangleCount!=0)throw new Exception("Empty-emitter bypass used with mesh lights");
            // Analytic lights are already uploaded by LightScene.SetData.
            // Skip only the zero-work native emissive-triangle preparation.
        }
    }
    static void Tick()
    {
        try
        {
            if(failure!=null)throw new Exception("Reference render diagnostic: "+failure);
            var request=new UniversalRenderPipeline.SingleCameraRequest{destination=target};RenderPipeline.SubmitRenderRequest(camera,request);frame++;
            if(frame<4&&!matchedPoint)return;
            var pools=(System.Collections.IDictionary)feature.GetType().GetField("_resourcePools",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(feature);
            if(pools.Count!=1)throw new Exception("Reference camera pool count: "+pools.Count);
            var pool=pools.Values.Cast<object>().Single();
            if(matchedPoint)
            {
                var resource=pool.GetType().GetField(native?"DiffuseLighting":"DirectLighting").GetValue(pool);
                var rt=(RenderTexture)resource.GetType().GetField("rt").GetValue(resource);
                var previous=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();RenderTexture.active=previous;
                var values=new double[32*32*3];int index=0;
                for(int y=48;y<80;y++)for(int x=48;x<80;x++){var color=image.GetPixel(x,y);values[index++]=color.r;values[index++]=color.g;values[index++]=color.b;}
                if(native)
                {
                    var specular=pool.GetType().GetField("SpecularLighting").GetValue(pool);
                    var specularRt=(RenderTexture)specular.GetType().GetField("rt").GetValue(specular);
                    previous=RenderTexture.active;RenderTexture.active=specularRt;image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();RenderTexture.active=previous;
                    index=0;for(int y=48;y<80;y++)for(int x=48;x<80;x++){var color=image.GetPixel(x,y);values[index++]+=color.r*.04;values[index++]+=color.g*.04;values[index++]+=color.b*.04;}
                }
                statistics.Add(values);
                if(native||!disableRR)
                {
                    var reconstructed=pool.GetType().GetField(native?"HdrColor":"DlssOutput").GetValue(pool);
                    var output=(RenderTexture)reconstructed.GetType().GetField("rt").GetValue(reconstructed);
                    previous=RenderTexture.active;RenderTexture.active=output;image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();RenderTexture.active=previous;
                    index=0;for(int y=48;y<80;y++)for(int x=48;x<80;x++){var color=image.GetPixel(x,y);values[index++]=color.r;values[index++]=color.g;values[index++]=color.b;}
                    reconstructedStatistics.Add(values);
                }
                if(frame==8||frame==32||frame==128||frame==256||frame==512)
                {
                    var result=statistics.Summarize("rawCombined",firstFrame,frame);measurements.Add(result);Debug.Log("[HoReference] MEASUREMENT "+JsonUtility.ToJson(result));
                    if(native||!disableRR){var reconstructed=reconstructedStatistics.Summarize(native?"relaxCombined":"rrCombined",firstFrame,frame);measurements.Add(reconstructed);Debug.Log("[HoReference] MEASUREMENT "+JsonUtility.ToJson(reconstructed));reconstructedStatistics=new HoRTDIConvergenceStatistics(values.Length);}
                    statistics=new HoRTDIConvergenceStatistics(values.Length);firstFrame=frame+1;
                }
                if(frame<512)return;
                if(measurements.Any(r=>r.mean<=1e-6))throw new Exception("Matched reference output black");
                File.WriteAllText(Path.Combine(Application.dataPath,"../ho-reference-convergence108-matched-point-"+(native?"native-relax":disableRR?"raw":"rr")+".json"),JsonUtility.ToJson(new MeasurementReport{rrEnabled=!native&&!disableRR,emptyEmitterGuard=skipEmptyEmitters,results=measurements.ToArray()},true));
                Finish(null);return;
            }
            foreach(string name in native?new[]{"DiffuseLighting","SpecularLighting","DenoisedDiffuseLighting","DenoisedSpecularLighting","HdrColor"}:disableRR?new[]{"RtxdiDiffuseLighting","RtxdiSpecularLighting","DirectLighting"}:new[]{"RtxdiDiffuseLighting","RtxdiSpecularLighting","DirectLighting","DlssOutput"})
            {
                var resource=pool.GetType().GetField(name).GetValue(pool);
                var rt=(RenderTexture)resource.GetType().GetField("rt").GetValue(resource);
                if(rt==null||!rt.IsCreated())throw new Exception("Reference output missing: "+name);
                var previous=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();RenderTexture.active=previous;
                double sum=0;for(int y=48;y<80;y++)for(int x=48;x<80;x++){var color=image.GetPixel(x,y);sum+=color.r+color.g+color.b;}
                Debug.Log($"[HoReference] OUTPUT native={native} frame={frame} name={name} roiMean={sum/1024:R}");
                if(name==(native?"HdrColor":"DirectLighting")&&sum<=.001)throw new Exception("Reference DI output is black");
            }
            Finish(null);
        }
        catch(Exception error){Finish(error);}
    }
    [Serializable] sealed class MeasurementReport{public bool diagnosticOnly=true;public bool rrEnabled=false;public bool emptyEmitterGuard=true;public HoRTDIConvergenceStatistics.Result[] results;}
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;
        if(error!=null)Debug.LogException(error);
        GraphicsSettings.defaultRenderPipeline=previousDefault;QualitySettings.renderPipeline=previousQuality;
        foreach(var obj in owned.AsEnumerable().Reverse())if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);
        Debug.Log("[HoReference] RENDER_END pass="+(error==null));EditorApplication.Exit(error==null?0:1);
    }
    public static void Inventory()
    {
        try
        {
            Debug.Log($"[HoReference] INVENTORY unity={Application.unityVersion} api={SystemInfo.graphicsDeviceType} gpu={SystemInfo.graphicsDeviceName} rayTracing={SystemInfo.supportsRayTracing}");
            foreach (string path in new[]{"Assets/RTXDI/UnityRtxdiRenderer.asset","Assets/RTXDI/NativeRtxdiRenderer.asset"})
            {
                var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if(renderer==null)throw new Exception("Missing renderer: "+path);
                foreach(var feature in renderer.rendererFeatures)
                    Debug.Log($"[HoReference] FEATURE {path} {feature?.GetType().FullName}");
            }
            var unity=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/RTXDI/UnityRtxdiRenderer.asset").rendererFeatures.OfType<UnityRtxdiFeature>().Single();
            foreach(var field in typeof(UnityRtxdiFeature).GetFields())
            {
                if(field.FieldType!=typeof(ComputeShader))continue;
                var shader=(ComputeShader)field.GetValue(unity);
                if(shader==null)throw new Exception("Missing compute: "+field.Name);
                var errors=ShaderUtil.GetComputeShaderMessages(shader).Where(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
                Debug.Log($"[HoReference] COMPUTE {field.Name} errors={errors.Length}");
                foreach(var error in errors)Debug.LogError(error.message);
                if(errors.Length>0)throw new Exception("Reference compute compilation failed");
            }
            var defaults=Nrd.RelaxSettings._default;
            Debug.Log($"[HoReference] RELAX_DEFAULTS diffuse={defaults.diffuseMaxAccumulatedFrameNum}/{defaults.diffuseMaxFastAccumulatedFrameNum} specular={defaults.specularMaxAccumulatedFrameNum}/{defaults.specularMaxFastAccumulatedFrameNum}");
            File.WriteAllText(Path.Combine(Application.dataPath,"../ho-reference-inventory.json"),"{\"pass\":true,\"api\":\""+SystemInfo.graphicsDeviceType+"\"}");
            Debug.Log("[HoReference] INVENTORY_PASS");EditorApplication.Exit(0);
        }
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
