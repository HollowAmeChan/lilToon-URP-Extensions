using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using lilToon.URP.Extensions.RTGI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Copied only into the isolated Player project; no product runtime component.
// Hidden hosts cannot run Unity's normal camera loop (DI's recorded finding).
// One request per real Update tests application-time history without pretending
// that it qualifies normal enabled-camera rendering.
public sealed class HoRTGIRuntimeProbe:MonoBehaviour
{
    public Camera probeCamera;
    public HoRTGIRendererFeature feature;
    public Material redMaterial;
    public Transform blueWall;
    public VolumeProfile profile;
    public Material cutoutMaterial,excludedSky;
    public Renderer cutoutRenderer;
    public Transform blueBone;
    public SkinnedMeshRenderer blueSkin;
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    const int Size=64,Count=32*32;
    readonly List<string> checks=new();
    readonly List<Frame> frames=new();
    double[] rawSum=new double[Count*3],filteredSum=new double[Count*3],rawSquare=new double[Count],filteredSquare=new double[Count];
    RenderTexture target;
    Texture2D image;
    HoRTGIVolume volume;
    string resultPath;
    int sample,previousFrame=-1,renderCallbacks;
    double previousTime=-1;
    Color baseline;
    Color rasterBasis;
    bool finished;
    bool cutoutProbe;
    bool skinnedProbe;
    Result report=new();
    static object Field(object value,string name)=>value?.GetType().GetField(name,Flags)?.GetValue(value);
    object Context=>feature.GetType().GetProperty("LastContext",Flags).GetValue(feature);
    object Nrd=>Field(Context,"nrd");
    RTHandle Raw=>(RTHandle)Field(Context,"diffuse");
    RTHandle Published=>(RTHandle)Context.GetType().GetProperty("PublishedDiffuse",Flags).GetValue(Context);
    bool Reset=>(bool)Field(Nrd,"lastReset");
    void Start()
    {
        var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--result");resultPath=at>=0?args[at+1]:Path.Combine(Application.persistentDataPath,"ho-gi-runtime.json");
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
        report.api=SystemInfo.graphicsDeviceType.ToString();report.colorSpace=QualitySettings.activeColorSpace.ToString();report.requestMode="one SubmitRenderRequest per distinct application Update";
        if(feature==null||probeCamera==null||profile==null){Finish(new Exception("Required serialized camera/Feature/profile reference is missing"));return;}
        cutoutProbe=cutoutMaterial!=null;report.cutoutProbe=cutoutProbe;
        skinnedProbe=blueSkin!=null;report.skinnedProbe=skinnedProbe;
        #if URP_COMPATIBILITY_MODE
        report.compatibility=GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode;
        #endif
        QualitySettings.vSyncCount=0;Application.targetFrameRate=60;Application.runInBackground=true;
        target=new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);target.Create();image=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);
        probeCamera.enabled=false;probeCamera.aspect=1;profile.TryGet(out volume);
        feature.Settings.composite=false;
        RenderPipelineManager.endCameraRendering+=OnCamera;
    }
    void OnCamera(ScriptableRenderContext context,Camera camera){if(camera==probeCamera)renderCallbacks++;}
    void Require(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);Debug.Log("[HoGIRuntime] CHECK "+text);}
    Color[] Read(RenderTexture source)
    {
        var old=RenderTexture.active;try{RenderTexture.active=source;image.ReadPixels(new Rect(0,0,Size,Size),0,0);image.Apply();return image.GetPixels();}finally{RenderTexture.active=old;}
    }
    static Color Mean(Color[] pixels)
    {Color c=Color.clear;for(int y=16;y<48;y++)for(int x=16;x<48;x++){var v=pixels[y*Size+x];if(!float.IsFinite(v.r)||!float.IsFinite(v.g)||!float.IsFinite(v.b)||v.a<.99f)throw new Exception("Invalid central sample");c+=v;}return c/Count;}
    void Update()
    {
        if(finished)return;
        try
        {
            if(Time.frameCount<=previousFrame||Time.unscaledTimeAsDouble<=previousTime)throw new Exception("Application frame/time did not advance");
            previousFrame=Time.frameCount;previousTime=Time.unscaledTimeAsDouble;
            if(sample==4)feature.Settings.composite=true;
            if(sample>=180&&sample<240)probeCamera.transform.position=new Vector3(.2f*Mathf.Sin((sample-180)*.1f),0,-3);
            if(sample==240)redMaterial.SetFloat("_UseEmission",0);
            if(sample>=244&&sample<268)blueWall.position=new Vector3(1+Mathf.Sin(sample*.2f)*.2f,0,-1);
            if(sample==268)volume.enable.Override(false);
            if(sample==269)volume.enable.Override(true);
            if(sample==270)feature.Settings.denoise=false;
            if(sample==271)feature.Settings.denoise=true;
            if(cutoutProbe)
            {
                if(sample==304)cutoutMaterial.SetColor("_Color",Color.black);
                if(sample==305)cutoutMaterial.SetColor("_Color",new Color(0,0,0,0));
                if(sample==306){cutoutMaterial.SetColor("_Color",Color.black);feature.Settings.excludedRayMaterials=new[]{excludedSky,cutoutMaterial};}
                if(sample==307)feature.Settings.excludedRayMaterials=new[]{excludedSky};
                if(sample==308)cutoutRenderer.enabled=false;
            }
            if(skinnedProbe)
            {
                if(sample==309)blueBone.localPosition=Vector3.back*2;
                if(sample==310)blueBone.localPosition=Vector3.zero;
                if(sample==311)blueSkin.SetBlendShapeWeight(0,100);
                if(sample==312)blueSkin.SetBlendShapeWeight(0,0);
                if(sample==313)blueSkin.bones=new Transform[]{null};
                if(sample==314)blueSkin.bones=new[]{blueBone};
            }
            int before=renderCallbacks;
            // Match DI's request-based Player helper: a disabled camera does not
            // run the normal automatic Volume framework update for this request.
            VolumeManager.instance.Update(probeCamera.transform,probeCamera.GetUniversalAdditionalCameraData().volumeLayerMask);
            RenderPipeline.SubmitRenderRequest(probeCamera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            if(renderCallbacks!=before+1)throw new Exception("Expected one completed camera request in this actual application frame");
            if(sample<4){if(sample==3){rasterBasis=Mean(Read(target));report.rasterBasis=rasterBasis;}sample++;return;}
            var context=Context;if(context==null)throw new Exception("GI context unavailable: "+feature.Status);
            if(sample==268){Require(Shader.GetGlobalFloat("_HoRTGIActive")==0,"Volume off clears production in an actual application frame");sample++;return;}
            var rawPixels=Read(Raw.rt);var raw=Mean(rawPixels);var filteredPixels=Read(Published.rt);var filtered=Mean(filteredPixels);
            bool reset=Nrd!=null&&Reset;
            frames.Add(new Frame{sample=sample,applicationFrame=Time.frameCount,time=Time.unscaledTimeAsDouble,giFrame=(int)Field(context,"frame"),reset=reset,raw=raw,filtered=filtered});
            if(sample==4)
            {
                Require(Nrd!=null&&Raw!=Published,"Serialized GI/NRD references produce distinct raw and filtered in Mono Player");
                if(cutoutProbe)Require((bool)Field(context,"alphaVisibility")&&((GraphicsBuffer)Field(context,"finalVisibility")).count==Size*Size&&feature.Settings.excludedRayMaterials.Length==1&&feature.Settings.excludedRayMaterials[0]==excludedSky,"Serialized nonempty GI material filter and native Cutout path are active in Player");
                if(skinnedProbe)Require(!blueSkin.sharedMesh.isReadable&&cutoutRenderer is SkinnedMeshRenderer,"Player GI emitter and Cutout use actual non-readable skinned sources");
                var cameraColor=Mean(Read(target));report.compositeError=Mathf.Max(Mathf.Abs(cameraColor.r-rasterBasis.r-filtered.r),Mathf.Abs(cameraColor.g-rasterBasis.g-filtered.g),Mathf.Abs(cameraColor.b-rasterBasis.b-filtered.b));
                Require(report.compositeError<.01f,"Actual Player camera adds filtered GI to its measured raster basis");
            }
            if(sample>=52&&sample<180)
            {
                int p=0;for(int y=16;y<48;y++)for(int x=16;x<48;x++,p++)
                {var a=rawPixels[y*Size+x];var b=filteredPixels[y*Size+x];for(int c=0;c<3;c++){rawSum[p*3+c]+=a[c];filteredSum[p*3+c]+=b[c];}rawSquare[p]+=a.r*a.r;filteredSquare[p]+=b.r*b.r;}
                baseline+=filtered/128;
            }
            if(sample==179)
            {
                double error=0,total=0,rv=0,fv=0;float leftRed=0,rightRed=0;
                for(int p=0;p<Count;p++)
                {
                    for(int c=0;c<3;c++){double a=rawSum[p*3+c]/128,b=filteredSum[p*3+c]/128;error+=Math.Abs(a-b);total+=Math.Abs(a);}
                    rv+=rawSquare[p]/128-Math.Pow(rawSum[p*3]/128,2);fv+=filteredSquare[p]/128-Math.Pow(filteredSum[p*3]/128,2);
                    if(p%32<8)leftRed+=(float)(rawSum[p*3]/128);if(p%32>=24)rightRed+=(float)(rawSum[p*3]/128);
                }
                report.spatialRelativeL1=error/total;report.rawVariance=rv/Count;report.filteredVariance=fv/Count;report.leftRed=leftRed/256;report.rightRed=rightRed/256;
                Require(report.leftRed>report.rightRed*1.2f,"Offscreen colored emission produces a nonuniform indirect field");
                Require(report.spatialRelativeL1<.15,"Filtered time-mean spatial image differs from raw by less than 15%: "+report.spatialRelativeL1);
                Require(report.filteredVariance<report.rawVariance*.4,"Actual-frame stationary variance improves by at least 60%");
                Save(rawPixels,"raw");Save(filteredPixels,"filtered");
            }
            if(sample>=181&&sample<240&&!reset)report.reprojectedCameraFrames++;
            if(sample==239){Require(report.reprojectedCameraFrames>=55,"Camera moves across distinct application frames without conservative world reset");Require(Mathf.Abs(filtered.r-baseline.r)<baseline.r*.3f+.01f,"Moving camera preserves broad indirect energy");}
            if(sample==240)
            {
                Require(reset,"Emission change resets the first affected actual frame");report.removedRedRaw=raw.r;report.removedRedFiltered=filtered.r;
                // RTXDI's packed GI radiance has a small cross-channel floor;
                // compare filtered against this frame's actual decoded raw.
                Require(raw.r<baseline.r*.01f&&filtered.r<baseline.r*.01f&&filtered.r<=raw.r+.001f,"Removed red emitter has no stale red history above decoded raw floor in first affected frame");
            }
            if(sample>=244&&sample<268){if(reset)report.movingGeometryResetFrames++;}
            if(sample==267)Require(report.movingGeometryResetFrames==24,"Moving geometry deliberately resets each changed application frame");
            if(sample==269)Require(reset,"Volume re-enable starts a fresh actual-frame denoiser history");
            if(sample==270)Require(Nrd==null&&Raw==Published,"Denoise off releases NRD and publishes raw in Player");
            if(sample==271)Require(Nrd!=null&&reset,"Denoise re-enable dispatches a new original RELAX instance");
            if(sample==303)
            {
                Require(frames.Count>290&&renderCallbacks==sample+1,"Distinct application frames and camera completion counts recorded");
                Require(Shader.GetGlobalFloat("_HoRTGIActive")==1,"GI production remains active after dynamic sequence");
                if(!cutoutProbe){report.pass=true;Finish(null);return;}
            }
            if(cutoutProbe&&sample==304)Require(reset&&raw.r<.0001f&&raw.b<.0001f&&filtered.r<.0001f&&filtered.b<.0001f,"Player solid Cutout clears old emitter history on first affected frame");
            if(cutoutProbe&&sample==305)Require(reset&&raw.b>.05f&&filtered.b>.05f,"Player alpha hole restores emitter without stale black history");
            if(cutoutProbe&&sample==306)Require(!(bool)Field(context,"alphaVisibility")&&((GraphicsBuffer)Field(context,"finalVisibility")).count==1&&raw.b>.05f,"Player material exclusion removes last Cutout and releases full native visibility while preserving excluded sky");
            if(cutoutProbe&&sample==307)Require((bool)Field(context,"alphaVisibility")&&reset&&raw.b<.0001f&&filtered.b<.0001f,"Player restores solid Cutout and independent denoiser reset");
            if(cutoutProbe&&sample==308){Require(!(bool)Field(context,"alphaVisibility")&&raw.b>.05f&&renderCallbacks==309,"Player disabling Cutout restores opaque compute path in a new application frame");if(!skinnedProbe){report.pass=true;Finish(null);return;}}
            if(skinnedProbe&&(sample==309||sample==311||sample==313))Require(reset&&raw.b<.0001f&&filtered.b<.0001f,"Player changed/invalid skin removes stale blue bounce immediately sample="+sample);
            if(skinnedProbe&&(sample==310||sample==312||sample==314))Require(reset&&raw.b>.05f&&filtered.b>.05f,"Player restored bone/morph/valid pose restores blue bounce sample="+sample);
            if(skinnedProbe&&sample==314){Require(renderCallbacks==315,"Player skin sequence uses distinct application frames");report.pass=true;Finish(null);return;}
            sample++;
        }
        catch(Exception error){Finish(error);}
    }
    void Save(Color[] pixels,string name){image.SetPixels(pixels);image.Apply();File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(resultPath),name+".png"),image.EncodeToPNG());}
    void Finish(Exception error)
    {
        if(finished)return;finished=true;report.pass=error==null&&report.pass;report.error=error?.ToString();report.checks=checks.ToArray();report.frames=frames.ToArray();report.renderCallbacks=renderCallbacks;
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath));File.WriteAllText(resultPath,JsonUtility.ToJson(report,true));
        RenderPipelineManager.endCameraRendering-=OnCamera;feature?.Dispose();target?.Release();if(error!=null)Debug.LogException(error);Debug.Log("[HoGIRuntime] END pass="+report.pass);Application.Quit(report.pass?0:1);
    }
    [Serializable]sealed class Frame{public int sample,applicationFrame,giFrame;public double time;public bool reset;public Color raw,filtered;}
    [Serializable]sealed class Result{public bool pass,compatibility,cutoutProbe,skinnedProbe;public string api,colorSpace,requestMode,error;public double spatialRelativeL1,rawVariance,filteredVariance;public float leftRed,rightRed,compositeError,removedRedRaw,removedRedFiltered;public Color rasterBasis;public int reprojectedCameraFrames,movingGeometryResetFrames,renderCallbacks;public string[] checks;public Frame[] frames;}
}
