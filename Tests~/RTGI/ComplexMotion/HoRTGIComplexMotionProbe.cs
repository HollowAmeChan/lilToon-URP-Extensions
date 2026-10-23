using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using lilToon.URP.Extensions.RTGI;
using UnityEngine;
using UnityEngine.Rendering;

// Dedicated actual enabled-camera Player coverage. Captures completed frames only.
public sealed class HoRTGIComplexMotionProbe:MonoBehaviour
{
    public Camera probeCamera;public HoRTGIRendererFeature feature;
    public Material redMaterial,blueMaterial,cutoutMaterial,excludedSky;
    public Transform blueWall,blueBone,motionReceiver,motionBlocker;
    public Renderer cutoutRenderer;public SkinnedMeshRenderer blueSkin;public VolumeProfile profile;
    public bool baseVertexProbe,historyProbe,precisionProbe;
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    readonly List<Frame> frames=new();readonly List<string> checks=new();
    readonly Vector2Int[] centers={new(256,256),new(128,160),new(256,368)};
    readonly Result result=new();Color[] screen,raster;int capturedFrame=-1,rendered,lastRender=-1,lastObserved,sample,updates;
    Texture2D readback;HoRTGIVolume volume;string output;double started;bool finished;Vector3 originalReceiver,originalBlocker;
    static object Read(object obj,string name){if(obj==null)throw new Exception("Missing object for "+name);var t=obj.GetType();var f=t.GetField(name,Flags);if(f!=null)return f.GetValue(obj);var p=t.GetProperty(name,Flags);if(p!=null)return p.GetValue(obj);throw new MissingMemberException(t.FullName,name);}
    void OnEnable()=>RenderPipelineManager.endCameraRendering+=Ended;
    void Ended(ScriptableRenderContext context,Camera camera){if(camera==probeCamera){rendered++;lastRender=Time.frameCount;}}
    void Require(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);Debug.Log("[HoGIComplexMotion] CHECK "+message);}
    void Start()
    {
        var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--result");output=at>=0?args[at+1]:Path.Combine(Application.persistentDataPath,"ho-gi-complex-motion.json");Directory.CreateDirectory(Path.GetDirectoryName(output));
        started=Time.realtimeSinceStartupAsDouble;Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;OnDemandRendering.renderFrameInterval=1;
        result.unity=Application.unityVersion;result.api=SystemInfo.graphicsDeviceType.ToString();result.device=SystemInfo.graphicsDeviceName;result.batchMode=Application.isBatchMode;
        #if URP_COMPATIBILITY_MODE
        result.compatibility=GraphicsSettings.GetRenderPipelineSettings<UnityEngine.Rendering.Universal.RenderGraphSettings>().enableRenderCompatibilityMode;
        #endif
        try{
            Require(probeCamera.enabled&&probeCamera.targetTexture==null,"Serialized camera enabled with default screen target");
            Require(Screen.width==512&&Screen.height==512&&QualitySettings.activeColorSpace==ColorSpace.Linear,"Actual screen512 and Linear color space");
            Require(baseVertexProbe&&historyProbe&&precisionProbe&&blueSkin!=null&&motionReceiver!=null&&motionBlocker!=null,"Serialized skinned base-vertex, moving receivers and blocker references present");
            profile.TryGet(out volume);if(volume==null)throw new Exception("Missing GI Volume");originalReceiver=motionReceiver.localPosition;originalBlocker=motionBlocker.localPosition;
            readback=new Texture2D(512,512,TextureFormat.RGBAFloat,false,true);feature.Settings.composite=false;StartCoroutine(Capture());
        }catch(Exception e){Finish(e);}
    }
    Color[] Means(Color[] pixels,bool decode)
    {
        var means=new Color[centers.Length];for(int k=0;k<centers.Length;k++){
            Color sum=Color.clear;for(int y=centers[k].y-32;y<centers[k].y+32;y++)for(int x=centers[k].x-32;x<centers[k].x+32;x++){
                Color v=pixels[y*512+x];if(decode)v=v.linear;if(!float.IsFinite(v.r)||!float.IsFinite(v.g)||!float.IsFinite(v.b))throw new Exception("Nonfinite ROI publication");sum+=v;
            }means[k]=sum/4096;
        }return means;
    }
    Color[] ReadTexture(RenderTexture rt)
    {
        if(rt==null||rt.width!=512||rt.height!=512)throw new Exception("Actual GI output dimensions changed");var old=RenderTexture.active;
        try{RenderTexture.active=rt;readback.ReadPixels(new Rect(0,0,512,512),0,0);readback.Apply();return Means(readback.GetPixels(),false);}finally{RenderTexture.active=old;}
    }
    IEnumerator Capture()
    {
        while(!finished){yield return new WaitForEndOfFrame();if(finished)yield break;Texture2D image=null;
            try{
                if(lastRender!=Time.frameCount)throw new Exception("No matching automatic camera event for screenshot");image=ScreenCapture.CaptureScreenshotAsTexture();
                if(image==null||image.width!=512||image.height!=512)throw new Exception("Invalid actual screen capture");screen=Means(image.GetPixels(),true);capturedFrame=Time.frameCount;
                if(sample==31||sample==63||sample==95||sample==143||sample==144||sample==160||sample==176||sample==207||sample==239)File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output),sample.ToString("D3")+"-"+Phase(sample)+".png"),image.EncodeToPNG());
            }catch(Exception e){Finish(e);}finally{if(image!=null)Destroy(image);}
        }
    }
    void Update()
    {
        if(finished)return;updates++;
        try{
            if(Time.realtimeSinceStartupAsDouble-started>8)throw new Exception("Bounded complex motion Player timeout");
            if(rendered==lastObserved){if(updates>=180)throw new Exception("No actual automatic camera completion");return;}
            if(lastRender>=Time.frameCount)return;if(lastRender<Time.frameCount-2||capturedFrame!=lastRender)throw new Exception("Stale or unaligned completed-screen frame");lastObserved=rendered;
            var context=Read(feature,"LastContext");var nrd=Read(context,"nrd");var scene=Read(feature,"scene");var raw=(RTHandle)Read(context,"diffuse");var pub=(RTHandle)Read(context,"PublishedDiffuse");
            var f=new Frame{sample=sample,phase=Phase(sample),applicationFrame=Time.frameCount,renderFrame=lastRender,captureFrame=capturedFrame,giFrame=(int)Read(context,"frame"),time=Time.realtimeSinceStartupAsDouble-started,
                raw=ReadTexture(raw.rt),filtered=ReadTexture(pub.rt),screen=screen,reset=(bool)Read(nrd,"lastReset"),temporal=(bool)Read(context,"lastTemporal"),active=Shader.GetGlobalFloat("_HoRTGIActive")>.5f,
                instances=(int)Read(scene,"InstanceCount"),cutouts=(int)Read(scene,"CutoutCount"),snapshots=(int)Read(scene,"SkinnedMeshCount"),bakes=(uint)Read(scene,"SkinnedBakeCount"),revision=(uint)Read(scene,"Revision"),structureRevision=(uint)Read(scene,"StructureRevision"),alphaVisibility=(bool)Read(context,"alphaVisibility"),
                bone=blueBone.localPosition,blend=blueSkin.GetBlendShapeWeight(0),receiver=motionReceiver.localPosition,blocker=motionBlocker.localPosition};
            frames.Add(f);if(sample==0)result.initialInstances=f.instances;
            if(f.instances!=result.initialInstances||f.instances<8||f.cutouts<1||f.snapshots<3||!f.alphaVisibility)throw new Exception("Fixed initial ray scene/skin/Cutout domain changed");
            if(sample==31){raster=(Color[])f.screen.Clone();Require(f.raw[0].r>.005f&&f.raw[0].b>.005f&&f.filtered[0].r>.005f&&f.filtered[0].b>.005f,"Static screen contains red and blue indirect light");result.staticBlue=f.raw[0].b;}
            if(sample==63||sample==239){float intensity=sample==63?1:.5f;float error=0;for(int k=0;k<3;k++)for(int c=0;c<3;c++)error=Mathf.Max(error,Mathf.Abs(f.screen[k][c]-raster[k][c]-f.filtered[k][c]*intensity));Require(error<.015f,"Three ROI all-RGB screen additive composition intensity="+intensity+" error="+error);result.maxCompositeError=Mathf.Max(result.maxCompositeError,error);}
            if(sample>=64&&sample<112){result.motionFrames++;if(f.reset&&!f.temporal)result.motionResetFrames++;result.motionBlueMin=Mathf.Min(result.motionBlueMin,f.raw[0].b);result.motionBlueMax=Mathf.Max(result.motionBlueMax,f.raw[0].b);}
            if(sample==111){Require(result.motionFrames==48&&result.motionResetFrames>=46,"Continuous object motion records conservative global history resets");Require(result.motionBlueMax-result.motionBlueMin>Mathf.Max(.002f,result.staticBlue*.1f),"Continuous geometry motion produces measurable blue GI response");Require(f.bakes-frames[63].bakes>=47,"Moving bone/blend-shape causes actual owned skin updates");Require(f.structureRevision==frames[63].structureRevision,"Continuous movement preserves instance structure and triangle layout");}
            if(sample==143){Require(f.bakes==frames[113].bakes,"Restored stationary poses reuse owned snapshots without rebaking");Require(!f.reset&&f.temporal,"Restored stationary geometry resumes temporal accumulation");}
            if(sample==144){Require(f.reset,"Cutout edit clears old history in its first frame");RequireZero(f,-1,"Closed Cutout blocks all RGB indirect light immediately");}
            if(sample==160){Require(f.reset,"Cutout reopen/blue emission edit resets first frame");RequireZero(f,2,"Blue emission off leaves no stale blue history");Require(f.raw[0].r>.005f&&f.filtered[0].r>.005f,"Red indirect light survives blue switch");}
            if(sample==176){Require(f.reset,"Red emission edit resets first frame");RequireZero(f,0,"Red emission off leaves no stale red history");Require(f.raw[0].b>.005f&&f.filtered[0].b>.005f,"Blue indirect light survives red switch");}
            if(sample>=192&&sample<208){float error=0;for(int k=0;k<3;k++)for(int c=0;c<3;c++)error=Mathf.Max(error,Mathf.Abs(f.screen[k][c]-raster[k][c]));if(f.active||error>=.01f)throw new Exception("Volume off did not restore static raster");}
            if(sample==208)Require(f.active&&f.reset,"Volume re-enable starts a new active history");
            if(sample==239){Require(rendered==240&&frames.Count==240,"240 distinct native automatic camera frames recorded");result.pass=true;Finish(null);return;}
            sample++;ApplyNext();
        }catch(Exception e){Finish(e);}
    }
    void RequireZero(Frame f,int channel,string message){float value=0;for(int k=0;k<3;k++)for(int c=0;c<3;c++)if(channel<0||channel==c)value=Mathf.Max(value,f.raw[k][c],f.filtered[k][c]);Require(value<.005f,message+" max="+value);}
    void ApplyNext()
    {
        if(sample==32){feature.Settings.composite=true;feature.Settings.intensity=1;}
        if(sample>=64&&sample<112){float p=(sample-64)*.18f;blueBone.localPosition=new Vector3(0,0,.25f*Mathf.Sin(p));blueSkin.SetBlendShapeWeight(0,30+30*Mathf.Sin(p));motionReceiver.localPosition=originalReceiver+new Vector3(.22f*Mathf.Sin(p),0,0);motionReceiver.localRotation=Quaternion.Euler(0,12*Mathf.Sin(p),0);motionBlocker.localPosition=new Vector3(.6f+.4f*Mathf.Sin(p),0,-.55f);}
        if(sample==112){blueBone.localPosition=Vector3.zero;blueSkin.SetBlendShapeWeight(0,0);motionReceiver.localPosition=originalReceiver;motionReceiver.localRotation=Quaternion.identity;motionBlocker.localPosition=originalBlocker;}
        if(sample==144)cutoutMaterial.SetColor("_Color",new Color(0,0,0,1));
        if(sample==160){cutoutMaterial.SetColor("_Color",new Color(0,0,0,0));blueMaterial.SetFloat("_UseEmission",0);}
        if(sample==176){blueMaterial.SetFloat("_UseEmission",1);redMaterial.SetFloat("_UseEmission",0);}
        if(sample==192)volume.enable.Override(false);
        if(sample==208){volume.enable.Override(true);feature.Settings.intensity=.5f;}
    }
    static string Phase(int s)=>s<32?"raster":s<64?"composite":s<112?"continuous-motion":s<144?"restored-static":s<160?"cutout-closed":s<176?"blue-off":s<192?"red-off":s<208?"volume-off":"reenabled-half";
    void Finish(Exception error)
    {
        if(finished)return;finished=true;result.pass=error==null&&result.pass;result.error=error?.ToString();result.frames=frames.ToArray();result.checks=checks.ToArray();result.updates=updates;result.rendered=rendered;result.elapsed=Time.realtimeSinceStartupAsDouble-started;result.width=Screen.width;result.height=Screen.height;
        File.WriteAllText(output,JsonUtility.ToJson(result,true));RenderPipelineManager.endCameraRendering-=Ended;if(probeCamera!=null)probeCamera.enabled=false;feature?.Dispose();if(readback!=null)Destroy(readback);if(error!=null)Debug.LogException(error);Debug.Log("[HoGIComplexMotion] END pass="+result.pass);Application.Quit(result.pass?0:1);
    }
    [Serializable]sealed class Frame{public int sample,applicationFrame,renderFrame,captureFrame,giFrame,instances,cutouts,snapshots;public string phase;public double time;public bool reset,temporal,active,alphaVisibility;public uint bakes,revision,structureRevision;public float blend;public Vector3 bone,receiver,blocker;public Color[] raw,filtered,screen;}
    [Serializable]sealed class Result{public bool pass,compatibility,batchMode;public string error,unity,api,device;public int updates,rendered,width,height,initialInstances,motionFrames,motionResetFrames;public double elapsed;public float staticBlue,motionBlueMin=float.MaxValue,motionBlueMax,maxCompositeError;public string[] checks;public Frame[] frames;}
}
