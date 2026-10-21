using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using lilToon.URP.Extensions.RTGI;
using UnityEngine;
using UnityEngine.Rendering;

// Separate automatic-camera proof. No request, Camera.Render, manual Volume
// update or warm-up rendering call is used anywhere in this component.
public sealed class HoRTGIAutomaticProbe:MonoBehaviour
{
    public Camera probeCamera;
    public HoRTGIRendererFeature feature;
    public Material redMaterial,cutoutMaterial,excludedSky;
    public Transform blueWall,blueBone;
    public Renderer cutoutRenderer;
    public SkinnedMeshRenderer blueSkin;
    public VolumeProfile profile;
    public bool baseVertexProbe,historyProbe,precisionProbe;
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    readonly List<Frame> frames=new();readonly List<string> checks=new();
    Texture2D image;HoRTGIVolume volume;string path;
    int rendered,lastRenderFrame=-1,lastObserved,updates,sample;
    double started;bool finished;Color raster;
    Result result=new();
    static object Field(object value,string name)=>value?.GetType().GetField(name,Flags)?.GetValue(value);
    object Context=>feature.GetType().GetProperty("LastContext",Flags).GetValue(feature);
    void OnEnable()=>RenderPipelineManager.endCameraRendering+=CameraEnded;
    void CameraEnded(ScriptableRenderContext context,Camera camera){if(camera==probeCamera){rendered++;lastRenderFrame=Time.frameCount;}}
    void Start()
    {
        var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--result");path=at>=0?args[at+1]:Path.Combine(Application.persistentDataPath,"ho-gi-automatic.json");
        started=Time.realtimeSinceStartupAsDouble;Directory.CreateDirectory(Path.GetDirectoryName(path));
        Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;OnDemandRendering.renderFrameInterval=1;
        result.batchMode=Application.isBatchMode;result.api=SystemInfo.graphicsDeviceType.ToString();result.cameraEnabledAtStart=probeCamera!=null&&probeCamera.enabled;
        result.targetAtStart=probeCamera!=null&&probeCamera.targetTexture!=null;
        #if URP_COMPATIBILITY_MODE
        result.compatibility=UnityEngine.Rendering.GraphicsSettings.GetRenderPipelineSettings<UnityEngine.Rendering.Universal.RenderGraphSettings>().enableRenderCompatibilityMode;
        #endif
        try
        {
            Require(result.cameraEnabledAtStart&&result.targetAtStart,"Camera and target are enabled/assigned in the serialized scene before Start");
            if(feature==null||profile==null)throw new Exception("Serialized automatic fixture references missing");
            profile.TryGet(out volume);if(volume==null)throw new Exception("Automatic fixture Volume missing");
            if(!probeCamera.targetTexture.IsCreated())probeCamera.targetTexture.Create();
            Require(probeCamera.targetTexture.IsCreated(),"Automatic target GPU allocation is ready without a rendering request");
            image=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);feature.Settings.composite=false;
        }
        catch(Exception e){Finish(e);}
    }
    void Require(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);Debug.Log("[HoGIAutomatic] CHECK "+message);}
    Color Mean(RenderTexture texture)
    {
        var previous=RenderTexture.active;
        try
        {
            RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();var values=image.GetPixels();Color sum=Color.clear;
            for(int y=16;y<48;y++)for(int x=16;x<48;x++){var v=values[y*64+x];if(!float.IsFinite(v.r)||!float.IsFinite(v.g)||!float.IsFinite(v.b))throw new Exception("Non-finite automatic publication");sum+=v;}
            return sum/1024;
        }
        finally{RenderTexture.active=previous;}
    }
    void Update()
    {
        if(finished)return;updates++;
        try
        {
            if(rendered==lastObserved)
            {
                if(updates>=180||Time.realtimeSinceStartupAsDouble-started>12)throw new Exception("Serialized-enabled hidden host produced no new automatic camera completion");
                return;
            }
            if(lastRenderFrame>=Time.frameCount)return;
            if(lastRenderFrame<Time.frameCount-2)throw new Exception("Automatic capture is not a fresh previous application frame");
            lastObserved=rendered;var context=Context;if(context==null)throw new Exception("Automatic camera completed without GI context");
            var raw=(RTHandle)Field(context,"diffuse");var published=(RTHandle)context.GetType().GetProperty("PublishedDiffuse",Flags).GetValue(context);
            var nrd=Field(context,"nrd");bool active=Shader.GetGlobalFloat("_HoRTGIActive")>.5f;
            var frame=new Frame{sample=sample,applicationFrame=Time.frameCount,renderFrame=lastRenderFrame,time=Time.unscaledTimeAsDouble,rendered=rendered,
                giFrame=(int)Field(context,"frame"),phase=Phase(sample),cameraEnabled=probeCamera.enabled,active=active,cameraColor=Mean(probeCamera.targetTexture),
                raw=Mean(raw.rt),filtered=Mean(published.rt),reset=nrd!=null&&(bool)Field(nrd,"lastReset")};
            frames.Add(frame);
            if(sample==31){raster=frame.cameraColor;Require(frame.raw.r>0&&frame.filtered.r>0,"Automatic raw and NRD publications contain indirect light");}
            if(sample==95||sample==127)
            {
                float amount=sample==95?.25f:1;
                float error=Mathf.Max(Mathf.Abs(frame.cameraColor.r-raster.r-frame.filtered.r*amount),Mathf.Abs(frame.cameraColor.b-raster.b-frame.filtered.b*amount));
                Require(error<.015f,"Automatic camera adds filtered GI at intensity "+amount+" error="+error);
            }
            if(sample==159)Require(!frame.active&&Mathf.Abs(frame.cameraColor.r-raster.r)<.01f,"Automatic Volume off removes GI from the rendered target");
            if(sample==160)Require(frame.active&&frame.reset,"Automatic Volume re-enable starts fresh NRD history");
            if(sample>=180&&sample<210&&!frame.reset)result.reprojectedFrames++;
            if(sample==209)Require(result.reprojectedFrames>=27,"Enabled automatic camera movement retains valid static-world reprojection");
            if(sample==210)Require(frame.reset&&frame.raw.r<.005f&&frame.filtered.r<.005f,"Automatic emission edit rejects stale red history in the first rendered frame");
            if(sample==239)
            {
                Require(rendered>=240&&frames.Count==240,"Continuous automatic camera completions recorded without a manual render path");
                result.pass=true;Finish(null);return;
            }
            sample++;
            if(sample==32){feature.Settings.composite=true;feature.Settings.intensity=.25f;}
            if(sample==96)feature.Settings.intensity=1;
            if(sample==128)volume.enable.Override(false);
            if(sample==160)volume.enable.Override(true);
            if(sample>=180&&sample<210)probeCamera.transform.position=new Vector3(.1f*Mathf.Sin((sample-180)*.1f),0,-3);
            if(sample==210)redMaterial.SetFloat("_UseEmission",0);
        }
        catch(Exception e){Finish(e);}
    }
    static string Phase(int sample)=>sample<32?"raster":sample<96?"quarter":sample<128?"one":sample<160?"volume-off":sample<180?"re-enabled":sample<210?"moving":"emission-off";
    void Finish(Exception error)
    {
        if(finished)return;finished=true;result.pass=error==null&&result.pass;result.error=error?.ToString();result.frames=frames.ToArray();result.checks=checks.ToArray();
        result.updates=updates;result.renderedCameras=rendered;result.lastRenderFrame=lastRenderFrame;result.allCameras=Camera.allCamerasCount;
        result.focused=Application.isFocused;result.screenWidth=Screen.width;result.screenHeight=Screen.height;result.interval=OnDemandRendering.renderFrameInterval;
        result.willRender=OnDemandRendering.willCurrentFrameRender;result.cameraEnabled=probeCamera!=null&&probeCamera.enabled;result.targetCreated=probeCamera!=null&&probeCamera.targetTexture!=null&&probeCamera.targetTexture.IsCreated();
        Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(result,true));
        RenderPipelineManager.endCameraRendering-=CameraEnded;if(probeCamera!=null)probeCamera.enabled=false;feature?.Dispose();if(image!=null)Destroy(image);if(error!=null)Debug.LogException(error);
        Debug.Log("[HoGIAutomatic] END pass="+result.pass+" automaticCameras="+rendered);Application.Quit(result.pass?0:1);
    }
    [Serializable]sealed class Frame{public int sample,applicationFrame,renderFrame,rendered,giFrame;public double time;public string phase;public bool cameraEnabled,active,reset;public Color cameraColor,raw,filtered;}
    [Serializable]sealed class Result{public bool pass,batchMode,compatibility,cameraEnabledAtStart,targetAtStart,focused,cameraEnabled,targetCreated,willRender;public string api,error;public int updates,renderedCameras,lastRenderFrame,allCameras,screenWidth,screenHeight,interval,reprojectedFrames;public string[] checks;public Frame[] frames;}
}
