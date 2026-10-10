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
    [Serializable]private sealed class Result {public bool pass;public string api,mode,error;public float rawMse,filteredMse,red,blue,boundaryAlbedoMax,boundaryMetalRelative,boundarySpecLeak,pointDepthMax,pointShadowMax,coloredSpecRelative,coloredSpecLeak,noisyMetalMean,noisyMetalReference,noisyCameraMean,noisyCameraTint,hdrRelative,hdrRawRelative,initialVisibilityShadowRatio,mixedSpecularRatio,mixedDiffuseRatio,temporalMixedSpecularRatio,temporalMixedDiffuseRatio;public int temporalMixedMaxHistory;}
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
        // A hidden near lamp must not discard a visible far lamp when RIS
        // considers multiple candidates. Test the actual serialized DXR path.
        var oldBluePosition=blue.transform.position;float oldBlueRange=blue.range,oldBlueIntensity=blue.intensity;var oldBlueColor=blue.color;
        try
        {
            blue.transform.position=new Vector3(-30,0,-50);blue.range=100;blue.intensity=25000;blue.color=Color.white;blue.enabled=true;blocker.SetActive(true);feature.Settings.initialSamples=8;
            var image=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);double actual=0,expected=0;
            try
            {
                for(int frame=0;frame<128;frame++)
                {
                    Render();ReadInto(capture.Raw,image);
                    for(int y=24;y<40;y++)for(int x=24;x<40;x++){actual+=image.GetPixel(x,y).r;expected+=Expected(blue,x,y);}
                }
            }
            finally{Destroy(image);}
            result.initialVisibilityShadowRatio=(float)(actual/expected);
            Require(Math.Abs(actual/expected-1)<.05,"Player multi-candidate visibility lost the visible far light");
        }
        finally{blue.enabled=false;blue.transform.position=oldBluePosition;blue.range=oldBlueRange;blue.intensity=oldBlueIntensity;blue.color=oldBlueColor;blocker.SetActive(false);feature.Settings.initialSamples=1;}
        ValidateMixedProposal();
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
        GameObject strip=null,secondLight=null;Material metal=null;
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
            metal.SetFloat("_Metallic",0);metal.SetColor("_Color",Color.blue);strip.transform.position=new Vector3(0,0,-.35f);
            light.type=LightType.Point;light.transform.position=new Vector3(0,0,-1);light.intensity=.5f;
            for(int i=0;i<48;i++)Render();ReadInto(capture.Raw,raw);ReadInto(capture.Diffuse,filtered);
            for(int y=16;y<48;y++)for(int x=24;x<40;x++)
            {
                bool foreground=x>=30&&x<34;float z=foreground?-.35f:0;
                var ray=probeCamera.ViewportPointToRay(new Vector3((x+.5f)/64,(y+.5f)/64,0));var p=ray.origin+ray.direction*((z-ray.origin.z)/ray.direction.z);
                var delta=light.transform.position-p;float d2=delta.sqrMagnitude,fade=Mathf.Clamp01(1-Mathf.Pow(d2/(light.range*light.range),2));
                bool shadow=!foreground&&Mathf.Abs(p.x*.65f)<4f/64;
                float irradiance=shadow?0:light.intensity/d2*(-delta.normalized.z)/Mathf.PI*fade*fade;
                var expected=(foreground?Color.blue:Color.white)*irradiance;var a=raw.GetPixel(x,y);var b=filtered.GetPixel(x,y);
                Near(a.r,expected.r,"Player point-depth raw R",.003f);Near(a.b,expected.b,"Player point-depth raw B",.003f);
                result.pointDepthMax=Mathf.Max(result.pointDepthMax,Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.b-b.b)));
                if(shadow)result.pointShadowMax=Mathf.Max(result.pointShadowMax,Mathf.Max(b.r,b.b));
            }
            Require(result.pointDepthMax<.008f&&result.pointShadowMax<.003f,"Player NRD blurred unequal depth or thin point shadow");
            strip.SetActive(false);surfaceMaterial.SetFloat("_Metallic",1);surfaceMaterial.SetTexture("_MainTex",pattern);light.type=LightType.Directional;light.intensity=.5f;
            feature.Settings.denoise=false;Render();ReadInto(capture.Specular,raw);
            feature.Settings.denoise=true;for(int i=0;i<48;i++)Render();ReadInto(capture.Specular,filtered);
            double specError=0;int specPixels=0;
            for(int y=16;y<48;y++)for(int x=8;x<56;x++)
            {
                var a=raw.GetPixel(x,y);var b=filtered.GetPixel(x,y);float peak=Mathf.Max(a.r,a.b);Require(peak>.1f,"Player colored specular raw missing");
                specError+=Mathf.Abs(Mathf.Max(b.r,b.b)-peak)/peak;specPixels++;
                result.coloredSpecLeak=Mathf.Max(result.coloredSpecLeak,Mathf.Max(b.g,a.r>.1f?b.b:b.r));
            }
            result.coloredSpecRelative=(float)(specError/specPixels);
            Require(result.coloredSpecRelative<.02f&&result.coloredSpecLeak<.0005f,"Player NRD lost colored specular detail");
            result.noisyMetalReference=Mathf.Max(raw.GetPixel(32,32).r,raw.GetPixel(32,32).b)*1.5f;
            secondLight=new GameObject("Player noisy F0 directional");var extra=secondLight.AddComponent<Light>();extra.type=LightType.Directional;extra.intensity=.25f;
            for(int i=0;i<96;i++)Render();double noisy=0;int noisyCount=0;
            for(int frame=0;frame<64;frame++)
            {
                Render();ReadInto(capture.Specular,filtered);for(int y=16;y<48;y++)for(int x=16;x<48;x++){var b=filtered.GetPixel(x,y);noisy+=Mathf.Max(b.r,b.b);noisyCount++;}
            }
            result.noisyMetalMean=(float)(noisy/noisyCount);Require(Mathf.Abs(result.noisyMetalMean/result.noisyMetalReference-1)<.05f,"Player noisy F0 energy loss");
            double moving=0,tint=0;int movingCount=0;
            for(int frame=0;frame<32;frame++)
            {
                probeCamera.transform.position+=new Vector3(2f/64*.125f,0,0);
                Render();ReadInto(capture.RawSpecular,raw);ReadInto(capture.Specular,filtered);
                for(int y=16;y<48;y++)for(int x=16;x<48;x++){var a=raw.GetPixel(x,y);var b=filtered.GetPixel(x,y);float sa=a.r+a.b,sb=b.r+b.b;moving+=sb;tint+=Mathf.Abs(b.r/Mathf.Max(sb,1e-6f)-a.r/Mathf.Max(sa,1e-6f));movingCount++;}
            }
            result.noisyCameraMean=(float)(moving/movingCount);result.noisyCameraTint=(float)(tint/movingCount);
            Require(Mathf.Abs(result.noisyCameraMean/result.noisyMetalReference-1)<.05f&&result.noisyCameraTint<.02f,"Player moving F0 transport loss");
            surfaceMaterial.SetTexture("_MainTex",Texture2D.whiteTexture);surfaceMaterial.SetColor("_Color",Color.white);surfaceMaterial.SetFloat("_Smoothness",.9f);
            light.transform.rotation=extra.transform.rotation=Quaternion.identity;light.color=extra.color=Color.white;
            // SB FP16 roughness then DI 8-bit decode gives 25/255 for this
            // saved material. Independent GGX normal-incidence D*G*F/4.
            double rough=25.0/255,ggx=1/(4*Math.PI*Math.Pow(rough,4));
            foreach(float intensity in new[]{.5f,16f,.0005f,.5f})
            {
                light.intensity=intensity;extra.intensity=intensity*.5f;
                for(int i=0;i<96;i++)Render();double sum=0,rawSum=0;int count=0;
                for(int frame=0;frame<96;frame++)
                {
                    Render();ReadInto(capture.Specular,filtered);ReadInto(capture.RawSpecular,raw);
                    for(int y=16;y<48;y++)for(int x=16;x<48;x++)
                    {var a=raw.GetPixel(x,y);var b=filtered.GetPixel(x,y);Require(float.IsFinite(a.r)&&float.IsFinite(b.r),"Player HDR non-finite output");sum+=b.r;rawSum+=a.r;count++;}
                }
                double reference=ggx*intensity*1.5;
                result.hdrRelative=Mathf.Max(result.hdrRelative,(float)Math.Abs(sum/count/reference-1));
                result.hdrRawRelative=Mathf.Max(result.hdrRawRelative,(float)Math.Abs(rawSum/count/reference-1));
                Debug.Log($"[HoRTDIPlayerProbe] HDR intensity={intensity} raw={rawSum/count:F6} filtered={sum/count:F6} reference={reference:F6}");
            }
            Require(result.hdrRawRelative<.04f&&result.hdrRelative<.05f,"Player HDR scale/history lost physical energy");
        }
        finally
        {
            if(strip!=null){strip.SetActive(false);Destroy(strip);}if(metal!=null)Destroy(metal);
            if(secondLight!=null){secondLight.SetActive(false);Destroy(secondLight);}
            surfaceMaterial.SetTexture("_MainTex",Texture2D.whiteTexture);surfaceMaterial.SetFloat("_Metallic",0);Destroy(pattern);Destroy(raw);Destroy(filtered);
        }
    }
    private void ValidateMixedProposal()
    {
        var position=light.transform.position;float range=light.range,intensity=light.intensity;var color=light.color;
        var bluePosition=blue.transform.position;float blueRange=blue.range,blueIntensity=blue.intensity;var blueColor=blue.color;
        bool temporal=feature.Settings.temporal,spatial=feature.Settings.spatial;
        var raw=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);var filtered=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
        try
        {
            light.transform.position=new Vector3(0,.15f,-1.5f);light.range=20;light.intensity=4;light.color=Color.white;
            blue.transform.position=new Vector3(-30,0,-50);blue.range=100;blue.intensity=25000;blue.color=Color.white;blue.enabled=true;
            surfaceMaterial.SetFloat("_Reflectance",.04f);surfaceMaterial.SetFloat("_Smoothness",.7f);feature.Settings.initialSamples=8;feature.Settings.denoise=true;
            foreach(bool reuse in new[]{false,true})
            {
                feature.Settings.temporal=reuse;feature.Settings.spatial=false;feature.Settings.denoise=false;Render();feature.Settings.denoise=true;
                for(int i=0;i<192;i++)Render();double spec=0,specReference=0,diff=0,diffReference=0;
                for(int frame=0;frame<256;frame++)
                {
                    Render();ReadInto(capture.Specular,filtered);ReadInto(capture.Diffuse,raw);
                    for(int y=24;y<40;y++)for(int x=24;x<40;x++)
                    {spec+=filtered.GetPixel(x,y).r;specReference+=ExpectedLocalSpecular(light,x,y)+ExpectedLocalSpecular(blue,x,y);diff+=raw.GetPixel(x,y).r;diffReference+=Expected(light,x,y)+Expected(blue,x,y);}
                }
                if(reuse){result.temporalMixedSpecularRatio=(float)(spec/specReference);result.temporalMixedDiffuseRatio=(float)(diff/diffReference);result.temporalMixedMaxHistory=feature.Settings.maxHistory;}
                else{result.mixedSpecularRatio=(float)(spec/specReference);result.mixedDiffuseRatio=(float)(diff/diffReference);}
                Require(Math.Abs(spec/specReference-1)<.05&&Math.Abs(diff/diffReference-1)<.05,"Player mixed diffuse/specular proposal energy mismatch; temporal="+reuse);
            }
        }
        finally
        {light.transform.position=position;light.range=range;light.intensity=intensity;light.color=color;blue.enabled=false;blue.transform.position=bluePosition;blue.range=blueRange;blue.intensity=blueIntensity;blue.color=blueColor;surfaceMaterial.SetFloat("_Reflectance",0);surfaceMaterial.SetFloat("_Smoothness",.5f);feature.Settings.denoise=false;feature.Settings.initialSamples=1;feature.Settings.temporal=temporal;feature.Settings.spatial=spatial;Destroy(raw);Destroy(filtered);}
    }
    private double ExpectedLocalSpecular(Light source,int x,int y)
    {
        var ray=probeCamera.ViewportPointToRay(new Vector3((x+.5f)/64,(y+.5f)/64,0));var p=ray.origin-ray.direction*(ray.origin.z/ray.direction.z);var delta=source.transform.position-p;var L=delta.normalized;var H=(Vector3.back+L).normalized;
        double nl=Math.Max(0,-L.z),nh=Math.Max(0,-H.z),rough=76.0/255,a2=Math.Pow(rough,4);
        double D=a2/(Math.PI*Math.Pow(nh*nh*a2+1-nh*nh,2)),G=2*nl/(Math.Sqrt(a2+(1-a2)*nl*nl)+nl),F=10.0/255+(1-10.0/255)*Math.Pow(1-nh,5);
        double fade=Math.Pow(Math.Max(0,1-Math.Pow(delta.magnitude/source.range,4)),2);return D*G*F/4*source.intensity/delta.sqrMagnitude*fade;
    }
    private void Render(){VolumeManager.instance.Update(probeCamera.transform,-1);RenderPipeline.SubmitRenderRequest(probeCamera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});}
    private float Expected(int x,int y)
        =>Expected(light,x,y);
    private float Expected(Light source,int x,int y)
    {
        var ray=probeCamera.ViewportPointToRay(new Vector3((x+.5f)/64,(y+.5f)/64,0));var p=ray.origin+ray.direction*(-ray.origin.z/ray.direction.z);var delta=source.transform.position-p;float d2=delta.sqrMagnitude;
        float fade=Mathf.Clamp01(1-Mathf.Pow(d2/(source.range*source.range),2));return source.intensity/d2*(-delta.normalized.z)/Mathf.PI*fade*fade;
    }
    private static void ReadInto(RenderTexture texture,Texture2D image)
    {var previous=RenderTexture.active;try{RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();}finally{RenderTexture.active=previous;}}
    private static Color Read(RenderTexture texture,int x=32,int y=32){var image=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);try{ReadInto(texture,image);return image.GetPixel(x,y);}finally{Destroy(image);}}
    private static void Near(float actual,float expected,string name,float tolerance)=>Require(Mathf.Abs(actual-expected)<tolerance,$"{name}: {actual} != {expected}");
    private static void NearRGB(Color actual,Color expected,string name){Near(actual.r,expected.r,name+" R",.015f);Near(actual.g,expected.g,name+" G",.015f);Near(actual.b,expected.b,name+" B",.015f);}
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
