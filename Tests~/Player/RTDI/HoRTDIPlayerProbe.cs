using System;
using System.Collections;
using System.IO;
using lilToon.URP.Extensions.RTDI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class HoRTDIPlayerProbe:MonoBehaviour
{
    public Camera probeCamera;
    public HoRTDIRendererFeature feature;
    public HoRTDIPlayerCaptureFeature capture;
    public Light light;
    public Light blue;
    public GameObject blocker;
    public Material surfaceMaterial;
    public VolumeProfile profile;
    private RenderTexture target;
    private bool unsupported;
    [Serializable]private sealed class Result {public bool pass;public string api,mode,error;public float rawMse,filteredMse,red,blue,boundaryAlbedoMax,boundaryMetalRelative,boundarySpecLeak;}
    private readonly Result result=new Result();
    private IEnumerator Start()
    {
        var args=Environment.GetCommandLineArgs();bool compatibility=Array.Exists(args,x=>x=="--compat");unsupported=Array.Exists(args,x=>x=="--unsupported");
        result.api=SystemInfo.graphicsDeviceType.ToString();result.mode=compatibility?"compatibility":"RenderGraph";
        // Pipeline settings are immutable in Player: build each mode separately.
        #if URP_COMPATIBILITY_MODE
        if(GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode!=compatibility)
        {result.error="Player build mode does not match requested mode";Finish(args);yield break;}
        #endif
        target=new RenderTexture(64,64,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);target.Create();
        probeCamera.targetTexture=target;probeCamera.enabled=true;
        yield return null;yield return null;probeCamera.enabled=false;probeCamera.targetTexture=null;
        try{Validate();result.pass=true;Debug.Log("[HoRTDIPlayerProbe] PASS "+JsonUtility.ToJson(result));}
        catch(Exception error){result.error=error.ToString();Debug.LogException(error);}
        Finish(args);
    }
    private void Finish(string[] args)
    {
        var index=Array.IndexOf(args,"--result");if(index>=0&&index+1<args.Length)File.WriteAllText(args[index+1],JsonUtility.ToJson(result,true));
        feature.Dispose();capture.Dispose();if(target!=null){target.Release();Destroy(target);}Application.Quit(result.pass?0:1);
    }
    private void Validate()
    {
        if(unsupported)
        {
            Require(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Direct3D11,"Fallback probe must actually use D3D11");
            Render();Require(Shader.GetGlobalFloat("_HoRTDIActive")==0,"Unsupported Player published DI");return;
        }
        Require(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Direct3D12,"DXR probe must actually use D3D12");
        feature.Settings.denoise=false;feature.Settings.temporal=false;feature.Settings.spatial=false;feature.Settings.initialSamples=1;
        blue.enabled=false;light.color=Color.white;
        for(int i=0;i<3;i++)Render();var single=Read(capture.Raw);
        Near(single.r,Expected(32,32),"Player single-light energy",.015f);Near(single.a,1,"receiver validity",.01f);
        var originalBake=light.bakingOutput;
        light.bakingOutput=new LightBakingOutput{lightmapBakeType=LightmapBakeType.Baked,isBaked=true};Render();Near(Read(capture.Raw).r,0,"Player baked-only exclusion",.002f);
        light.bakingOutput=new LightBakingOutput{lightmapBakeType=LightmapBakeType.Mixed,isBaked=true};Render();Near(Read(capture.Raw).r,Expected(32,32),"Player mixed direct light",.015f);light.bakingOutput=originalBake;
        surfaceMaterial.SetFloat("_Metallic",1);Render();Near(Read(capture.Raw).r,0,"Player metal diffuse",.002f);Require(Read(capture.Specular).r>0,"Player independent specular missing");surfaceMaterial.SetFloat("_Metallic",0);
        blocker.SetActive(true);Render();Near(Read(capture.Raw).r,0,"Player world occlusion",.002f);blocker.SetActive(false);
        feature.Settings.localSampling=HoRTDILocalSampling.Power;Render();Near(Read(capture.Raw).r,Expected(32,32),"Player power path",.015f);
        Require(profile.TryGet<HoRTDIVolume>(out var volume),"DI debug Volume missing");
        volume.debugMode.value=HoRTDIDebugMode.RawDiffuse;Render();NearRGB(Read(target),Read(capture.Raw),"Player raw debug");
        volume.debugMode.value=HoRTDIDebugMode.Receiver;Render();NearRGB(Read(target),Color.green,"Player receiver debug");
        Require(Shader.Find("Hidden/lilToon/URP/Debug/GridOverlay")!=null,"Player grid label shader stripped");
        volume.debugMode.value=HoRTDIDebugMode.All;Render();NearRGB(Read(target,10,32),Color.green,"Player grid receiver tile");volume.debugMode.value=HoRTDIDebugMode.Off;
        feature.Settings.localSampling=HoRTDILocalSampling.Uniform;feature.Settings.denoise=true;
        light.color=Color.red;blue.enabled=true;
        for(int i=0;i<48;i++)Render();Require(feature.Status.Contains("RELAX")&&!feature.Status.Contains("unavailable"),"Player NRD failed: "+feature.Status);
        double rawMse=0,filteredMse=0,redSum=0,blueSum=0;int count=0;
        var raw=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);var filtered=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
        try
        {
            for(int frame=0;frame<24;frame++)
            {
                Render();ReadInto(capture.Raw,raw);ReadInto(capture.Diffuse,filtered);
                for(int y=24;y<40;y++)for(int x=24;x<40;x++)
                {
                    float expected=Expected(x,y);var a=raw.GetPixel(x,y);var b=filtered.GetPixel(x,y);
                    Require(float.IsFinite(b.r)&&float.IsFinite(b.b),"Non-finite Player NRD result");rawMse+=(a.r-expected)*(a.r-expected);filteredMse+=(b.r-expected)*(b.r-expected);
                    redSum+=b.r;blueSum+=b.b;count++;
                }
            }
        }
        finally{Destroy(raw);Destroy(filtered);}
        result.rawMse=(float)(rawMse/count);result.filteredMse=(float)(filteredMse/count);result.red=(float)(redSum/count);result.blue=(float)(blueSum/count);
        Require(filteredMse<rawMse*.3,"Player RELAX did not reduce noise");float reference=0;for(int y=24;y<40;y++)for(int x=24;x<40;x++)reference+=Expected(x,y);reference/=256;
        Near(result.red,reference,"Player NRD red energy",.04f);Near(result.blue,reference,"Player NRD blue energy",.04f);
        volume.debugMode.value=HoRTDIDebugMode.Diffuse;Render();NearRGB(Read(target),Read(capture.Diffuse),"Player filtered debug");
        volume.debugMode.value=HoRTDIDebugMode.RawDiffuse;Render();NearRGB(Read(target),Read(capture.Raw),"Player noisy raw debug");volume.debugMode.value=HoRTDIDebugMode.Off;
        feature.Settings.denoise=false;Render();Near(Read(capture.Diffuse).r,Read(capture.Raw).r,"Player raw fallback",.002f);
        feature.Settings.denoise=true;Render();Require(feature.Status.Contains("RELAX")&&!feature.Status.Contains("unavailable"),"Player NRD recreate failed");
        feature.Settings.temporal=feature.Settings.spatial=true;feature.Settings.initialSamples=8;light.color=Color.white;blue.enabled=false;
        for(int i=0;i<40;i++)Render();light.color=Color.red;
        for(int i=0;i<16;i++)Render();var recolored=Read(capture.Diffuse);
        Require(Mathf.Max(recolored.g,recolored.b)<.01f,"Player retained stale light color");
        light.enabled=false;for(int i=0;i<4;i++)Render();Near(Read(capture.Raw).r,0,"Player switched-off raw light",.002f);Near(Read(capture.Diffuse).r,0,"Player switched-off filtered ghost",.002f);
        ValidateBoundaries();
    }
    private void ValidateBoundaries()
    {
        feature.Settings.temporal=feature.Settings.spatial=false;feature.Settings.denoise=true;
        blue.enabled=false;light.enabled=true;light.color=Color.white;light.type=LightType.Directional;light.intensity=.5f;light.transform.rotation=Quaternion.identity;
        var pattern=new Texture2D(64,1,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
        var raw=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);var filtered=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
        GameObject strip=null;Material metal=null;
        try
        {
            for(int x=0;x<64;x++)pattern.SetPixel(x,0,(x/2)%2==0?Color.red:Color.blue);pattern.Apply();surfaceMaterial.SetTexture("_MainTex",pattern);
            for(int i=0;i<48;i++)Render();ReadInto(capture.Raw,raw);ReadInto(capture.Diffuse,filtered);
            int redPixels=0,bluePixels=0;
            for(int y=16;y<48;y++)for(int x=8;x<56;x++)
            {
                var a=raw.GetPixel(x,y);var b=filtered.GetPixel(x,y);Near(Mathf.Max(a.r,a.b),.5f/Mathf.PI,"Player albedo raw truth",.003f);
                Near(Mathf.Min(a.r,a.b),0,"Player albedo absent raw channel",.001f);if(a.r>.1f)redPixels++;if(a.b>.1f)bluePixels++;
                result.boundaryAlbedoMax=Mathf.Max(result.boundaryAlbedoMax,Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b)));
            }
            Require(redPixels==32*24&&bluePixels==32*24,"Player fixture did not produce both albedo colors");
            Require(result.boundaryAlbedoMax<.008f,"Player NRD blurred albedo channels");surfaceMaterial.SetTexture("_MainTex",Texture2D.whiteTexture);
            strip=GameObject.CreatePrimitive(PrimitiveType.Quad);strip.transform.position=new Vector3(0,0,-.001f);strip.transform.localScale=new Vector3(4*2f/64,2,1);
            metal=new Material(surfaceMaterial);metal.SetFloat("_Metallic",1);strip.GetComponent<Renderer>().sharedMaterial=metal;
            feature.Settings.denoise=false;Render();float specReference=Read(capture.Specular).r;Require(specReference>.1f,"Player thin metal raw specular missing");
            feature.Settings.denoise=true;for(int i=0;i<48;i++)Render();
            result.boundaryMetalRelative=Mathf.Abs(Read(capture.Specular).r-specReference)/specReference;
            result.boundarySpecLeak=Read(capture.Specular,28,32).r;
            Near(Read(capture.Diffuse).r,0,"Player thin metal diffuse",.0005f);Near(Read(capture.Diffuse,28,32).r,.5f/Mathf.PI,"Player adjacent diffuse",.004f);
            Require(result.boundaryMetalRelative<.05f&&result.boundarySpecLeak<.0005f,"Player NRD mixed material classes");
        }
        finally
        {
            if(strip!=null){strip.SetActive(false);Destroy(strip);}if(metal!=null)Destroy(metal);
            surfaceMaterial.SetTexture("_MainTex",Texture2D.whiteTexture);Destroy(pattern);Destroy(raw);Destroy(filtered);
        }
    }
    private void Render(){VolumeManager.instance.Update(probeCamera.transform,-1);RenderPipeline.SubmitRenderRequest(probeCamera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});}
    private float Expected(int x,int y)
    {
        var ray=probeCamera.ViewportPointToRay(new Vector3((x+.5f)/64,(y+.5f)/64,0));var p=ray.origin+ray.direction*(-ray.origin.z/ray.direction.z);var delta=light.transform.position-p;float d2=delta.sqrMagnitude;
        float fade=Mathf.Clamp01(1-Mathf.Pow(d2/(light.range*light.range),2));return light.intensity/d2*(-delta.normalized.z)/Mathf.PI*fade*fade;
    }
    private static void ReadInto(RenderTexture texture,Texture2D image)
    {var previous=RenderTexture.active;try{RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();}finally{RenderTexture.active=previous;}}
    private static Color Read(RenderTexture texture,int x=32,int y=32){var image=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);try{ReadInto(texture,image);return image.GetPixel(x,y);}finally{Destroy(image);}}
    private static void Near(float actual,float expected,string name,float tolerance)=>Require(Mathf.Abs(actual-expected)<tolerance,$"{name}: {actual} != {expected}");
    private static void NearRGB(Color actual,Color expected,string name){Near(actual.r,expected.r,name+" R",.015f);Near(actual.g,expected.g,name+" G",.015f);Near(actual.b,expected.b,name+" B",.015f);}
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
