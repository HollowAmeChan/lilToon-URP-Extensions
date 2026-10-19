// Runs only in the isolated local UnityPathTracing reference project.
// Capture real reference outputs without replacing GI sampling shaders.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using PathTracing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class HoReferenceGIProbe
{
    const int Size=128, Count=256, Warmup=8;
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly List<UnityEngine.Object> owned=new();
    static readonly List<Window> windows=new();
    static ScriptableRendererFeature feature;
    static object setting;
    static Camera camera;
    static RenderTexture target;
    static Texture2D image;
    static RenderPipelineAsset oldDefault,oldQuality;
    static bool native,rr,noReuse,oldAsync,finished;
    static int tick,windowStart=1,windowCount;
    static string failure,output;
    static Color[] baseline;
    static double[] sums,means,m2,filteredSums,filteredMeans,filteredM2;
    static Vector3 totalRaw,totalFiltered;
    static double maximumBaselineDifference;
    static Report report;

    public static void RunUnityRaw(){Setup();}
    public static void RunUnityRR(){rr=true;Setup();}
    public static void RunUnityNoReuse(){noReuse=true;Setup();}
    public static void RunNativeRelax(){native=true;Setup();}
    static T Keep<T>(T obj) where T:UnityEngine.Object {owned.Add(obj);return obj;}
    static object Get(object obj,string name)
    {
        var field=obj.GetType().GetField(name,Flags);
        if(field!=null)return field.GetValue(obj);
        var property=obj.GetType().GetProperty(name,Flags);
        if(property!=null)return property.GetValue(obj);
        throw new MissingMemberException(obj.GetType().Name,name);
    }
    // Propagate nested value types back into the owning settings object.
    static object SetPath(object obj,string path,object value)
    {
        int dot=path.IndexOf('.');string name=dot<0?path:path.Substring(0,dot);
        var field=obj.GetType().GetField(name,Flags);
        if(field==null)throw new MissingFieldException(obj.GetType().Name,name);
        if(dot>=0)field.SetValue(obj,SetPath(field.GetValue(obj),path.Substring(dot+1),value));
        else field.SetValue(obj,field.FieldType.IsEnum?Enum.ToObject(field.FieldType,value):value==null?null:Convert.ChangeType(value,field.FieldType));
        return obj;
    }
    static void Set(string path,object value)=>SetPath(setting,path,value);
    static object SingleResource(string dictionary)
    {
        var map=(IDictionary)Get(feature,dictionary);
        if(map.Count!=1)throw new Exception(dictionary+" camera count="+map.Count);
        return map.Values.Cast<object>().Single();
    }
    static RenderTexture Texture(string name)
    {
        var texture=(RenderTexture)Get(Get(SingleResource("_resourcePools"),name),"rt");
        if(texture==null||!texture.IsCreated())throw new Exception("Missing actual output: "+name);
        return texture;
    }
    static Color[] Read(RenderTexture texture)
    {
        var previous=RenderTexture.active;
        try {RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,Size,Size),0,0);image.Apply();return image.GetPixels();}
        finally {RenderTexture.active=previous;}
    }
    static void Setup()
    {
        try
        {
            if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/"))throw new Exception("Isolated reference project required");
            oldDefault=GraphicsSettings.defaultRenderPipeline;oldQuality=QualitySettings.renderPipeline;
            oldAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            output=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-reference-gi-v01-"+(native?"native-relax":rr?"unity-rr":noReuse?"unity-no-reuse":"unity-raw")));
            Directory.CreateDirectory(output);
            report=new Report{mode=native?"native-relax":rr?"unity-rr":noReuse?"unity-no-reuse":"unity-raw",unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),colorSpace=QualitySettings.activeColorSpace.ToString()};
            if(native)report.protocol="Native diagnostic enables the existing outputDebugIndirectLighting constant, captures the actual IndirectLightingRaw texture (demodulated diffuse+specular, not a matched diffuse-only reference), plus actual reconstructed combined HdrColor. No subtraction of DI inputs that may be repacked/modified by NRD. Manual requests/readback are not GPU timing or motion validation.";
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
            if(native)
            {
                // Reuse the already repaired local native integration from the DI benchmark.
                var settings=NativeRender.NativeShaderProjectSettings.instance;
                if(!settings.globalDefines.Contains("WITH_NRD=1"))throw new Exception("Existing native WITH_NRD reference setup required");
                feature=Keep(UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/RTXDI/NativeRtxdiRenderer.asset").rendererFeatures.OfType<NativeRtxdiFeature>().Single()));
            }
            else feature=Keep(UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/RTXDI/UnityRtxdiRenderer.asset").rendererFeatures.OfType<UnityRtxdiFeature>().Single()));
            setting=Get(feature,"setting");
            Set("indirectLightingMode",(int)IndirectLightingMode.None);Set("directLightingMode",(int)DirectLightingMode.ReStir);
            Set("cameraJitter",false);Set("enableEnv",false);
            if(native){Set("environmentMap",null);Set("SR",false);Set("enableGradients",false);Set("enableToneMapping",false);Set("denoiserMode",(int)RtxDiDenoiserType.DENOISER_MODE_RELAX);}
            else {Set("tmpDisableRR",!rr);Set("exposureEv",0f);}
            Set("diResamplingMode",0);Set("initialSamplingParams.localLightSamplingMode",0);
            Set("initialSamplingParams.numLocalLightSamples",1u);Set("initialSamplingParams.numInfiniteLightSamples",0u);Set("initialSamplingParams.numEnvironmentSamples",0u);Set("initialSamplingParams.numBrdfSamples",0u);
            Set("initialSamplingParams.enableInitialVisibility",1u);
            Set("giResamplingMode",noReuse?0:3);
            Set("giTemporalResamplingParams.depthThreshold",.1f);Set("giTemporalResamplingParams.normalThreshold",.9f);
            Set("giTemporalResamplingParams.maxHistoryLength",20u);Set("giTemporalResamplingParams.maxReservoirAge",30u);
            Set("giSpatialResamplingParams.depthThreshold",.1f);Set("giSpatialResamplingParams.normalThreshold",.9f);
            Set("giSpatialResamplingParams.numSamples",4u);Set("giSpatialResamplingParams.samplingRadius",16f);
            Set("giFinalShadingParams.enableFinalVisibility",1u);Set("giFinalShadingParams.enableFinalMIS",0u);
            Set("brdfptParams.enableSecondaryResampling",0u);
            Set("brdfptParams.secondarySurfaceReSTIRDIParams.initialSamplingParams.localLightSamplingMode",0);
            Set("brdfptParams.secondarySurfaceReSTIRDIParams.initialSamplingParams.numLocalLightSamples",1u);
            Set("brdfptParams.secondarySurfaceReSTIRDIParams.initialSamplingParams.numInfiniteLightSamples",0u);
            Set("brdfptParams.secondarySurfaceReSTIRDIParams.initialSamplingParams.numEnvironmentSamples",0u);
            Set("brdfptParams.secondarySurfaceReSTIRDIParams.initialSamplingParams.numBrdfSamples",0u);
            renderer.rendererFeatures.Add(feature);
            if(native){var diagnostic=Keep(ScriptableObject.CreateInstance<NativeIndirectDebugFeature>());diagnostic.owner=feature;renderer.rendererFeatures.Add(diagnostic);}
            var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            camera=Keep(new GameObject("Reference GI camera")).AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=40;camera.aspect=1;
            camera.transform.position=new Vector3(0,0,-3);camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.allowHDR=true;
            target=Keep(new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();
            image=Keep(new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true));
            Quad("White receiver",Vector3.zero,Vector3.zero,new Vector2(2,2),Color.white);
            Quad("Red offscreen bounce",new Vector3(1.25f,0,-1),new Vector3(0,90,0),new Vector2(2,3),new Color(.8f,.1f,.05f));
            var light=Keep(new GameObject("Reference point")).AddComponent<Light>();light.type=LightType.Point;light.color=Color.white;light.intensity=4;light.range=100;light.transform.position=new Vector3(.25f,0,-1);
            sums=new double[32*32*3];means=new double[sums.Length];m2=new double[sums.Length];
            filteredSums=new double[sums.Length];filteredMeans=new double[sums.Length];filteredM2=new double[sums.Length];
            Application.logMessageReceived+=OnLog;EditorApplication.update+=Tick;
            Debug.Log("[HoReferenceGI] START "+report.mode+" "+report.api+" "+report.gpu);
        }
        catch(Exception error){Finish(error);}
    }
    static void Quad(string name,Vector3 position,Vector3 rotation,Vector2 size,Color color)
    {
        var obj=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));obj.name=name;obj.transform.position=position;obj.transform.eulerAngles=rotation;obj.transform.localScale=new Vector3(size.x,size.y,1);
        var material=Keep(new Material(Shader.Find("RayTracing/Lit")));material.SetColor("_BaseColor",color);material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",0);material.SetFloat("_IOR",1);material.SetFloat("_Cull",0);
        obj.GetComponent<Renderer>().sharedMaterial=material;
        if(native)obj.AddComponent<NativeRender.NativeRayTracingTarget>();
    }
    static void OnLog(string text,string stack,LogType type)
    {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)failure=text;}
    static void Save(string name,RenderTexture texture)
    {
        Read(texture);File.WriteAllBytes(Path.Combine(output,name+".exr"),image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
        File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
    }
    static void SaveIndirect(string name,RenderTexture texture)
    {
        var pixels=Read(texture);
        if(!native)for(int i=0;i<pixels.Length;i++){pixels[i]-=baseline[i];pixels[i].a=1;}
        image.SetPixels(pixels);image.Apply();
        File.WriteAllBytes(Path.Combine(output,name+".exr"),image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
        File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
    }
    public sealed class NativeIndirectDebugFeature:ScriptableRendererFeature
    {
        public ScriptableRendererFeature owner;
        public override void Create(){}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {
            // Owner has already prepared this frame's constants. Enable an existing
            // shader diagnostic without replacing or recompiling its GI algorithm.
            var values=(NativeResamplingConstants[])Get(owner,"_resamplingConstantsArray");
            values[0].debug.outputDebugIndirectLighting=1;
            ((GraphicsBuffer)Get(owner,"_resamplingConstantBuffer")).SetData(values);
        }
    }
    static void Tick()
    {
        try
        {
            if(failure!=null)throw new Exception("Reference diagnostic: "+failure);
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});tick++;
            var raw=Texture(native&&tick>Warmup?"IndirectLightingRaw":native?"DiffuseLighting":"DirectLighting");
            if(tick<=Warmup)
            {
                var current=Read(raw);
                if(tick==Warmup-1)baseline=current;
                if(tick==Warmup)
                {
                    for(int y=48;y<80;y++)for(int x=48;x<80;x++){int i=y*Size+x;var difference=current[i]-baseline[i];maximumBaselineDifference=Math.Max(maximumBaselineDifference,Math.Max(Math.Abs(difference.r),Math.Max(Math.Abs(difference.g),Math.Abs(difference.b))));}
                    report.baselineDelta=maximumBaselineDifference;
                    if(!native&&maximumBaselineDifference>1e-5)throw new Exception("Direct-only raw baseline is not deterministic; GI subtraction is invalid: "+maximumBaselineDifference);
                    baseline=current;Save("direct-only",raw);Set("indirectLightingMode",(int)IndirectLightingMode.ReStirGI);
                    Debug.Log("[HoReferenceGI] GI_ENABLED baselineDelta="+maximumBaselineDifference);
                }
                return;
            }
            int giFrame=tick-Warmup;var pixels=Read(raw);windowCount++;int k=0;Vector3 mean=Vector3.zero;
            for(int y=48;y<80;y++)for(int x=48;x<80;x++)
            {
                int i=y*Size+x;Color value=native?pixels[i]:pixels[i]-baseline[i];mean+=new Vector3(value.r,value.g,value.b);
                foreach(float component in new[]{value.r,value.g,value.b})
                {
                    if(float.IsNaN(component)||float.IsInfinity(component))throw new Exception("Nonfinite actual GI output");
                    double delta=component-means[k];means[k]+=delta/windowCount;m2[k]+=delta*(component-means[k]);sums[k]+=component;k++;
                }
            }
            totalRaw+=mean/1024;
            if(native||rr)
            {
                var filtered=Read(Texture(native?"HdrColor":"DlssOutput"));Vector3 sum=Vector3.zero;
                int filteredIndex=0;
                for(int y=48;y<80;y++)for(int x=48;x<80;x++)
                {
                    var c=filtered[y*Size+x];sum+=new Vector3(c.r,c.g,c.b);
                    foreach(float component in new[]{c.r,c.g,c.b})
                    {
                        if(float.IsNaN(component)||float.IsInfinity(component))throw new Exception("Nonfinite reconstructed output");
                        double delta=component-filteredMeans[filteredIndex];filteredMeans[filteredIndex]+=delta/windowCount;
                        filteredM2[filteredIndex]+=delta*(component-filteredMeans[filteredIndex]);filteredSums[filteredIndex]+=component;filteredIndex++;
                    }
                }
                totalFiltered+=sum/1024;
            }
            if(giFrame==1||giFrame==4||giFrame==8||giFrame==16||giFrame==32||giFrame==64||giFrame==128||giFrame==Count)
            {
                var result=new Window{first=windowStart,last=giFrame,mean=new Vector3((float)(sums.Where((v,i)=>i%3==0).Sum()/windowCount/1024),(float)(sums.Where((v,i)=>i%3==1).Sum()/windowCount/1024),(float)(sums.Where((v,i)=>i%3==2).Sum()/windowCount/1024)),temporalVariance=m2.Sum()/Math.Max(1,windowCount-1)/m2.Length};
                if(native||rr)
                {
                    result.reconstructedCombinedMean=new Vector3((float)(filteredSums.Where((v,i)=>i%3==0).Sum()/windowCount/1024),(float)(filteredSums.Where((v,i)=>i%3==1).Sum()/windowCount/1024),(float)(filteredSums.Where((v,i)=>i%3==2).Sum()/windowCount/1024));
                    result.reconstructedTemporalVariance=filteredM2.Sum()/Math.Max(1,windowCount-1)/filteredM2.Length;
                }
                windows.Add(result);Debug.Log("[HoReferenceGI] WINDOW "+JsonUtility.ToJson(result));
                Save("combined-raw-"+giFrame,raw);SaveIndirect("indirect-raw-"+giFrame,raw);if(native||rr)Save("reconstructed-"+giFrame,Texture(native?"HdrColor":"DlssOutput"));
                Array.Clear(sums,0,sums.Length);Array.Clear(means,0,means.Length);Array.Clear(m2,0,m2.Length);windowCount=0;windowStart=giFrame+1;
                Array.Clear(filteredSums,0,filteredSums.Length);Array.Clear(filteredMeans,0,filteredMeans.Length);Array.Clear(filteredM2,0,filteredM2.Length);
            }
            if(giFrame<Count)return;
            report.rawIndirectMean=totalRaw/Count;report.filteredCombinedMean=totalFiltered/Count;report.baselineDelta=maximumBaselineDifference;report.windows=windows.ToArray();
            var secondary=(GraphicsBuffer)Get(SingleResource("_rtxdiResources"),"SecondaryGBuffer");
            var packets=new SecondaryPacket[secondary.count];secondary.GetData(packets);
            var wall=packets.FirstOrDefault(p=>Math.Abs(p.position.x-1.25f)<.005f&&p.pdf>0);
            report.secondaryMaterialSample="diffuse=0x"+wall.diffuse.ToString("X8")+", specularRoughness=0x"+wall.specular.ToString("X8")+", position="+wall.position;
            Debug.Log("[HoReferenceGI] SECONDARY_MATERIAL "+report.secondaryMaterialSample);
            if(report.rawIndirectMean.x<1e-5f)throw new Exception("GI enabled but measured red indirect contribution is absent");
            if(report.rawIndirectMean.x<report.rawIndirectMean.y*1.5f)throw new Exception("Expected red-wall indirect response was not observed");
            if((native||rr)&&report.filteredCombinedMean.sqrMagnitude<1e-10f)throw new Exception("Reconstruction was enabled but actual output stayed black");
            Finish(null);
        }
        catch(Exception error){Finish(error);}
    }
    static void Finish(Exception error)
    {
        if(finished)return;finished=true;EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;
        if(report!=null){report.pass=error==null;report.failure=error?.ToString();report.frames=tick;report.windows=windows.ToArray();report.settings=setting==null?null:JsonUtility.ToJson(setting);File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));}
        if(error!=null)Debug.LogException(error);
        GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;ShaderUtil.allowAsyncCompilation=oldAsync;
        foreach(var obj in owned.AsEnumerable().Reverse())if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);
        Debug.Log("[HoReferenceGI] END pass="+(error==null));EditorApplication.Exit(error==null?0:1);
    }
    [Serializable] sealed class Window {public int first,last;public Vector3 mean,reconstructedCombinedMean;public double temporalVariance,reconstructedTemporalVariance;}
    [StructLayout(LayoutKind.Sequential)] struct SecondaryPacket
    {public Vector3 position;public uint normal,throughput0,throughput1,diffuse,specular;public Vector3 radiance;public float pdf;}
    [Serializable] sealed class Report
    {
        public bool pass,diagnosticOnly=true;public string mode,unity,gpu,api,colorSpace,failure,settings,secondaryMaterialSample;public int frames;
        public double baselineDelta;public Vector3 rawIndirectMean,filteredCombinedMean;public Window[] windows;
        public string protocol="128x128, one point light, IOR=1/metallic=0/roughness=1, no environment or emission, jitter off; raw GI=combined minus converged direct-only baseline; reconstructed is combined, not isolated GI; synchronous readback is diagnostic, not GPU timing";
    }
}
