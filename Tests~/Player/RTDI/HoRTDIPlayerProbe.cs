using System;
using System.Collections;
using System.IO;
using lilToon.URP.Extensions.RTDI;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.Atmosphere;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class HoRTDIPlayerProbe:MonoBehaviour
{
    public Camera probeCamera;
    public HoRTDIRendererFeature feature;
    public HoAtmosphereRendererFeature atmosphere;
    public HoRTDIPlayerCaptureFeature capture;
    public Light light;
    public Light blue;
    public GameObject blocker;
    public GameObject receiver;
    public Material surfaceMaterial;
    public Material cutoutMaterial;
    public VolumeProfile profile;
    private RenderTexture target;
    private bool unsupported;
    private Mesh skinnedProbeMesh;
    private Transform skinnedProbeBone;
    [Serializable]private sealed class Result {public bool pass;public string api,mode,error;public float rawMse,filteredMse,red,blue,boundaryAlbedoMax,boundaryMetalRelative,boundarySpecLeak,pointDepthMax,pointShadowMax,coloredSpecRelative,coloredSpecLeak,noisyMetalMean,noisyMetalReference,noisyCameraMean,noisyCameraTint,hdrRelative,hdrRawRelative,initialVisibilityShadowRatio,mixedSpecularRatio,mixedDiffuseRatio,temporalMixedSpecularRatio,temporalMixedDiffuseRatio,curvedRawL1,curvedFilteredL1,environmentRawRatio,environmentFilteredRatio,environmentShadow,environmentSpecularRawRatio,environmentSpecularFilteredRatio;public int temporalMixedMaxHistory;}
    private readonly Result result=new Result();
    private float skinnedShadow,skinnedUnblocked;
    private IEnumerator Start()
    {
        var args=Environment.GetCommandLineArgs();bool compatibility=Array.Exists(args,x=>x=="--compat");unsupported=Array.Exists(args,x=>x=="--unsupported");
        result.api=SystemInfo.graphicsDeviceType.ToString();result.mode=compatibility?"compatibility":"RenderGraph";
        if(!unsupported)SetupSkinnedBlocker();
        // Pipeline settings are immutable in Player: build each mode separately.
        #if URP_COMPATIBILITY_MODE
        if(GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode!=compatibility)
        {result.error="Player build mode does not match requested mode";Finish(args);yield break;}
        #endif
        target=new RenderTexture(64,64,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);target.Create();
        probeCamera.targetTexture=target;probeCamera.enabled=true;
        yield return null;yield return null;probeCamera.enabled=false;probeCamera.targetTexture=null;
        // SubmitRenderRequest renders a square target; clearing targetTexture
        // otherwise restores the window's aspect for the independent CPU rays.
        probeCamera.aspect=(float)target.width/target.height;
        if(!unsupported&&Array.Exists(args,x=>x=="--motion-protocol"))
        {yield return MotionProtocol();Finish(args);yield break;}
        try{Validate();result.pass=true;Debug.Log("[HoRTDIPlayerProbe] PASS "+JsonUtility.ToJson(result));}
        catch(Exception error){result.error=error.ToString();Debug.LogException(error);}
        Finish(args);
    }
    private void Finish(string[] args)
    {
        var index=Array.IndexOf(args,"--result");if(index>=0&&index+1<args.Length)File.WriteAllText(args[index+1],JsonUtility.ToJson(result,true));
        feature.Dispose();capture.Dispose();if(skinnedProbeMesh!=null)Destroy(skinnedProbeMesh);if(target!=null){target.Release();Destroy(target);}Application.Quit(result.pass?0:1);
    }
    private IEnumerator MotionProtocol()
    {
        capture.CaptureMotion=true;feature.Settings.denoise=true;receiver.GetComponent<Renderer>().motionVectorGenerationMode=MotionVectorGenerationMode.Object;
        Render();
        probeCamera.targetTexture=target;probeCamera.enabled=true;
        var positions=new[]{0f,0,0,.125f,.25f,.25f,0,0};var pixels=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
        for(int sample=0;sample<positions.Length;sample++)
        {
            int beforeCapture=capture.MotionCaptureCount;receiver.transform.position=Vector3.right*positions[sample];yield return null;bool failed=false;
            try
            {
                Require(capture.Motion!=null,"Player motion capture not created by a render pass");
                Require(capture.MotionCaptureCount>beforeCapture,"automatic rendering did not capture this frame; motion source remains unverified");
                ReadInto(capture.Motion,pixels);var value=pixels.GetPixel(32,32);float expected=sample>0?(positions[sample]-positions[sample-1])*.5f:0;
                Debug.Log($"[HoRTDIPlayerProbe] MOTION_REAL frame={Time.frameCount} sample={sample} captures={capture.MotionCaptureCount} cameraEnabled={probeCamera.enabled} motionUV=({value.r:F6},{value.g:F6}) expectedX={expected:F6}");
                if(sample>=3){Near(value.r,expected,"Player real-frame motion X",.0002f);Near(value.g,0,"Player real-frame motion Y",.0002f);}
            }
            catch(Exception error){result.error=error.ToString();Debug.LogException(error);failed=true;}
            if(failed){Destroy(pixels);yield break;}
        }
        result.pass=true;Destroy(pixels);Debug.Log("[HoRTDIPlayerProbe] PASS real-frame motion source protocol");
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
        blocker.SetActive(true);Render();skinnedShadow=Read(capture.Raw).r;Near(skinnedShadow,0,"Player skinned world occlusion",.002f);
        skinnedProbeBone.localPosition=Vector3.right*100;Render();skinnedUnblocked=Read(capture.Raw).r;
        Near(skinnedUnblocked,Expected(32,32),"Player skinned caster moved away",.015f);
        skinnedProbeBone.localPosition=Vector3.zero;Render();Near(Read(capture.Raw).r,0,"Player skinned caster returned",.002f);
        Debug.Log($"[HoRTDIPlayerProbe] SKIN shadow={skinnedShadow:F6} unblocked={skinnedUnblocked:F6}");blocker.SetActive(false);
        ValidateCutoutVisibility();
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
        ValidateCurvedReceiver();
        ValidateEnvironmentLight();
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
        ValidateCutoutMotion();
        ValidatePrimaryOrigin();
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
                for(int i=0;i<192;i++)Render();double spec=0,specReference=0,diff=0,diffReference=0,rawSpec=0;
                Require(Math.Abs(probeCamera.aspect-(float)target.width/target.height)<1e-6,"Player CPU reference aspect differs from render request target");
                Debug.Log($"[HoRTDIPlayerProbe] MIXED_INPUT aspect={probeCamera.aspect:R} screen={Screen.width}x{Screen.height} smoothness={surfaceMaterial.GetFloat("_Smoothness"):R} reflectance={surfaceMaterial.GetFloat("_Reflectance"):R}");
                for(int frame=0;frame<256;frame++)
                {
                    Render();ReadInto(capture.Specular,filtered);ReadInto(capture.Diffuse,raw);
                    for(int y=24;y<40;y++)for(int x=24;x<40;x++)
                    {spec+=filtered.GetPixel(x,y).r;specReference+=ExpectedLocalSpecular(light,x,y)+ExpectedLocalSpecular(blue,x,y);diff+=raw.GetPixel(x,y).r;diffReference+=Expected(light,x,y)+Expected(blue,x,y);}
                    ReadInto(capture.RawSpecular,raw);for(int y=24;y<40;y++)for(int x=24;x<40;x++)rawSpec+=raw.GetPixel(x,y).r;
                }
                if(reuse){result.temporalMixedSpecularRatio=(float)(spec/specReference);result.temporalMixedDiffuseRatio=(float)(diff/diffReference);result.temporalMixedMaxHistory=feature.Settings.maxHistory;}
                else{result.mixedSpecularRatio=(float)(spec/specReference);result.mixedDiffuseRatio=(float)(diff/diffReference);}
                Debug.Log($"[HoRTDIPlayerProbe] MIXED_RESULT temporal={reuse} rawSpecRatio={rawSpec/specReference:F6} filteredSpecRatio={spec/specReference:F6} filteredDiffuseRatio={diff/diffReference:F6}");
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
    private void ValidateCurvedReceiver()
    {
        var position=light.transform.position;float range=light.range,intensity=light.intensity;var color=light.color;
        var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere.transform.localScale=Vector3.one*1.5f;sphere.GetComponent<Renderer>().sharedMaterial=surfaceMaterial;
        var raw=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);var filtered=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
        try
        {
            Require(receiver!=null,"Player curved fixture receiver missing");receiver.SetActive(false);
            light.transform.position=new Vector3(-.3f,.35f,-1.7f);light.range=20;light.intensity=4;light.color=Color.white;
            surfaceMaterial.SetFloat("_Reflectance",.04f);surfaceMaterial.SetFloat("_Smoothness",.7f);feature.Settings.denoise=true;
            var mesh=sphere.GetComponent<MeshFilter>().sharedMesh;var vertices=mesh.vertices;var normals=mesh.normals;var triangles=mesh.triangles;
            for(int i=0;i<vertices.Length;i++)vertices[i]=sphere.transform.TransformPoint(vertices[i]);
            var reference=new double[64*64];for(int y=24;y<40;y++)for(int x=24;x<40;x++)
            {
                var ray=probeCamera.ViewportPointToRay(new Vector3((x+.5f)/64,(y+.5f)/64,0));float closest=float.PositiveInfinity;var N=Vector3.zero;var p=Vector3.zero;
                for(int t=0;t<triangles.Length;t+=3)
                {
                    int a=triangles[t],b=triangles[t+1],c=triangles[t+2];var e1=vertices[b]-vertices[a];var e2=vertices[c]-vertices[a];var cross=Vector3.Cross(ray.direction,e2);float det=Vector3.Dot(e1,cross);if(Mathf.Abs(det)<1e-8f)continue;
                    float inv=1/det;var offset=ray.origin-vertices[a];float u=Vector3.Dot(offset,cross)*inv;if(u<0||u>1)continue;var q=Vector3.Cross(offset,e1);float v=Vector3.Dot(ray.direction,q)*inv;if(v<0||u+v>1)continue;
                    float distance=Vector3.Dot(e2,q)*inv;if(distance<=0||distance>=closest)continue;closest=distance;p=ray.GetPoint(distance);N=(normals[a]*(1-u-v)+normals[b]*u+normals[c]*v).normalized;
                }
                Require(float.IsFinite(closest),"Player curved CPU ray missed the mesh");
                var delta=light.transform.position-p;var L=delta.normalized;var V=probeCamera.orthographic?-probeCamera.transform.forward:(probeCamera.transform.position-p).normalized;var H=(V+L).normalized;
                double nl=Math.Max(0,Vector3.Dot(N,L)),nv=Math.Max(0,Vector3.Dot(N,V)),nh=Math.Max(0,Vector3.Dot(N,H)),vh=Math.Max(0,Vector3.Dot(V,H)),a2=Math.Pow(76.0/255,4);
                double D=a2/(Math.PI*Math.Pow(nh*nh*a2+1-nh*nh,2)),G=2*nl/(nv*Math.Sqrt(a2+(1-a2)*nl*nl)+nl*Math.Sqrt(a2+(1-a2)*nv*nv)),F=10.0/255+(1-10.0/255)*Math.Pow(1-vh,5),fade=Math.Pow(Math.Max(0,1-Math.Pow(delta.magnitude/light.range,4)),2);
                reference[y*64+x]=D*G*F/4*light.intensity/delta.sqrMagnitude*fade;
            }
            for(int i=0;i<192;i++)Render();Require(HoAttributeCompositeConsumerRegistry.LastSnapshot.NeedsHighPrecisionSurfaceNormal,"Player RT normal precision request missing");
            double total=0,rawError=0,filteredError=0;
            for(int frame=0;frame<32;frame++)
            {
                Render();ReadInto(capture.RawSpecular,raw);ReadInto(capture.Specular,filtered);
                for(int y=24;y<40;y++)for(int x=24;x<40;x++){double expected=reference[y*64+x];total+=expected;rawError+=Math.Abs(raw.GetPixel(x,y).r-expected);filteredError+=Math.Abs(filtered.GetPixel(x,y).r-expected);}
            }
            result.curvedRawL1=(float)(rawError/total);result.curvedFilteredL1=(float)(filteredError/total);
            Require(rawError/total<.02&&filteredError/total<.05,"Player curved GGX shape failed");
        }
        finally
        {sphere.SetActive(false);Destroy(sphere);if(receiver!=null)receiver.SetActive(true);light.transform.position=position;light.range=range;light.intensity=intensity;light.color=color;surfaceMaterial.SetFloat("_Reflectance",0);surfaceMaterial.SetFloat("_Smoothness",.5f);feature.Settings.denoise=false;Destroy(raw);Destroy(filtered);}
    }
    private void ValidateEnvironmentLight()
    {
        Require(atmosphere!=null,"Player environment feature missing");bool enabled=light.enabled,blueEnabled=blue.enabled;
        var image=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);var room=GameObject.CreatePrimitive(PrimitiveType.Cube);room.layer=1;room.transform.localScale=Vector3.one*10;room.GetComponent<Renderer>().sharedMaterial=surfaceMaterial;room.SetActive(false);
        try
        {
            light.enabled=blue.enabled=false;atmosphere.Settings.enabled=true;feature.Settings.useEnvironment=true;feature.Settings.denoise=false;
            for(int i=0;i<8;i++)Render();double raw=0;
            for(int frame=0;frame<128;frame++){Render();ReadInto(capture.Raw,image);for(int y=24;y<40;y++)for(int x=24;x<40;x++)raw+=image.GetPixel(x,y).r;}
            result.environmentRawRatio=(float)(raw/(128*256));Require(Math.Abs(raw/(128*256)-1)<.03,"Player sky Lambert integral mismatch");
            feature.Settings.denoise=true;for(int i=0;i<96;i++)Render();double filtered=0;
            for(int frame=0;frame<128;frame++){Render();ReadInto(capture.Diffuse,image);for(int y=24;y<40;y++)for(int x=24;x<40;x++)filtered+=image.GetPixel(x,y).r;}
            result.environmentFilteredRatio=(float)(filtered/(128*256));Require(Math.Abs(filtered/(128*256)-1)<.05,"Player sky NRD energy mismatch");
            surfaceMaterial.SetFloat("_Reflectance",.04f);surfaceMaterial.SetFloat("_Smoothness",.9f);feature.Settings.denoise=false;Render();feature.Settings.denoise=true;
            for(int i=0;i<192;i++)Render();double reference=0,a2=Math.Pow(25.0/255,4);const int intervals=262144;
            for(int i=0;i<intervals;i++)
            {
                double mu=(i+.5)/intervals,nh2=(1+mu)*.5,nh=Math.Sqrt(nh2),den=nh2*a2+1-nh2,D=a2/(Math.PI*den*den),G=2*mu/(Math.Sqrt(a2+(1-a2)*mu*mu)+mu),F=10.0/255+(1-10.0/255)*Math.Pow(1-nh,5);
                reference+=D*G*F*.5*Math.PI/intervals;
            }
            var specFiltered=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);double specRaw=0,spec=0;
            try{for(int frame=0;frame<256;frame++){Render();ReadInto(capture.RawSpecular,image);ReadInto(capture.Specular,specFiltered);for(int y=24;y<40;y++)for(int x=24;x<40;x++){specRaw+=image.GetPixel(x,y).r;spec+=specFiltered.GetPixel(x,y).r;}}}
            finally{Destroy(specFiltered);}
            result.environmentSpecularRawRatio=(float)(specRaw/(256*256*reference));result.environmentSpecularFilteredRatio=(float)(spec/(256*256*reference));
            Require(Math.Abs(result.environmentSpecularRawRatio-1)<.05&&Math.Abs(result.environmentSpecularFilteredRatio-1)<.05,"Player narrow sky GGX/MIS energy mismatch");
            surfaceMaterial.SetFloat("_Reflectance",0);surfaceMaterial.SetFloat("_Smoothness",.5f);
            feature.Settings.denoise=false;room.SetActive(true);for(int i=0;i<4;i++)Render();result.environmentShadow=Read(capture.Raw).r;Require(result.environmentShadow<.001f,"Player enclosed world leaks sky");room.SetActive(false);
            atmosphere.Settings.enabled=false;Render();Near(Read(capture.Raw).r,0,"Player absent sky retains radiance",.001f);
        }
        finally{room.SetActive(false);Destroy(room);Destroy(image);atmosphere.Settings.enabled=false;light.enabled=enabled;blue.enabled=blueEnabled;feature.Settings.denoise=false;surfaceMaterial.SetFloat("_Reflectance",0);surfaceMaterial.SetFloat("_Smoothness",.5f);}
    }
    private void SetupSkinnedBlocker()
    {
        var rigid=blocker.GetComponent<MeshRenderer>();var material=rigid.sharedMaterial;
        skinnedProbeMesh=Instantiate(blocker.GetComponent<MeshFilter>().sharedMesh);
        rigid.enabled=false;Destroy(rigid);Destroy(blocker.GetComponent<MeshFilter>());
        skinnedProbeBone=new GameObject("Player DI caster bone").transform;skinnedProbeBone.SetParent(blocker.transform,false);
        skinnedProbeMesh.bindposes=new[]{Matrix4x4.identity};var weights=new BoneWeight[skinnedProbeMesh.vertexCount];
        for(int i=0;i<weights.Length;i++)weights[i]=new BoneWeight{boneIndex0=0,weight0=1};skinnedProbeMesh.boneWeights=weights;
        var skin=blocker.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=skinnedProbeMesh;skin.sharedMaterial=material;
        skin.bones=new[]{skinnedProbeBone};skin.rootBone=skinnedProbeBone;
    }
    private void ValidateCutoutVisibility()
    {
        Require(cutoutMaterial!=null,"Serialized Player cutout material missing");
        var skin=blocker.GetComponent<SkinnedMeshRenderer>();var original=skin.sharedMaterial;
        var adapter=Instantiate(cutoutMaterial);
        var transparent=new Texture2D(4,4,TextureFormat.RGBA32,false,true);var solid=new Texture2D(4,4,TextureFormat.RGBA32,false,true);
        var data=new Color[16];for(int i=0;i<data.Length;i++)data[i]=new Color(1,1,1,0);transparent.SetPixels(data);transparent.Apply(false,true);
        for(int i=0;i<data.Length;i++)data[i]=Color.white;solid.SetPixels(data);solid.Apply(false,true);
        skinnedProbeMesh.UploadMeshData(true);skin.sharedMaterial=adapter;blocker.SetActive(true);
        try
        {
            adapter.SetTexture("_MainTex",transparent);adapter.SetFloat("_Cutoff",.5f);adapter.SetColor("_Color",Color.white);Render();
            float open=Read(capture.Raw).r;Near(open,Expected(32,32),"Player non-readable cutout transmission",.015f);
            adapter.SetTexture("_MainTex",solid);Render();float shadow=Read(capture.Raw).r;Near(shadow,0,"Player cutout solid shadow",.002f);
            adapter.SetColor("_Color",new Color(1,1,1,.4f));Render();Near(Read(capture.Raw).r,Expected(32,32),"Player color alpha/cutoff",.015f);
            adapter.SetColor("_Color",Color.white);skinnedProbeBone.localPosition=Vector3.right*100;Render();Near(Read(capture.Raw).r,Expected(32,32),"Player cutout bone motion",.015f);
            skinnedProbeBone.localPosition=Vector3.zero;Render();Near(Read(capture.Raw).r,0,"Player cutout bone return",.002f);
            Debug.Log($"[HoRTDIPlayerProbe] CUTOUT shadow={shadow:F6} open={open:F6} non-readable source mesh/textures");
        }
        finally
        {
            skin.sharedMaterial=original;blocker.SetActive(false);Render();Destroy(adapter);Destroy(transparent);Destroy(solid);
        }
    }
    private void ValidateCutoutMotion()
    {
        var skin=blocker.GetComponent<SkinnedMeshRenderer>();var original=skin.sharedMaterial;var adapter=Instantiate(cutoutMaterial);
        var raw=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);var filtered=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
        try
        {
            surfaceMaterial.SetFloat("_Metallic",0);surfaceMaterial.SetFloat("_Reflectance",0);surfaceMaterial.SetFloat("_Smoothness",.5f);
            light.type=blue.type=LightType.Point;light.enabled=blue.enabled=true;
            light.transform.position=new Vector3(1.5f,0,-1);blue.transform.position=new Vector3(-1.5f,0,-1);
            light.color=Color.red;blue.color=Color.blue;light.range=blue.range=10;light.intensity=blue.intensity=4;
            skin.sharedMaterial=adapter;adapter.SetTexture("_MainTex",Texture2D.whiteTexture);adapter.SetColor("_Color",Color.white);adapter.SetFloat("_Cutoff",.5f);
            blocker.SetActive(true);feature.Settings.denoise=feature.Settings.temporal=feature.Settings.spatial=true;feature.Settings.initialSamples=8;
            foreach(bool step in new[]{false,true})
            {
                skinnedProbeBone.localPosition=Vector3.zero;for(int i=0;i<192;i++)Render();
                const System.Reflection.BindingFlags inspect=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var context=feature.GetType().GetProperty("LastContext",inspect).GetValue(feature);var nrd=context.GetType().GetField("nrd",inspect).GetValue(context);
                var applied=nrd.GetType().GetProperty("AppliedSettings",inspect).GetValue(nrd);
                Debug.Log($"[HoRTDIPlayerProbe] CUTOUT_NRD diffFrames={applied.GetType().GetField("diffFrames",inspect).GetValue(applied)} specFrames={applied.GetType().GetField("specFrames",inspect).GetValue(applied)} phi={applied.GetType().GetField("diffPhi",inspect).GetValue(applied)} sigma={applied.GetType().GetField("fastSigma",inspect).GetValue(applied)}");
                double redReference=0,blueReference=0,redRaw=0,blueRaw=0,redFiltered=0,blueFiltered=0,error=0,changedError=0,changedReference=0;
                var previousRed=new float[64*64];var changed=new bool[64*64];
                for(int frame=0;frame<256;frame++)
                {
                    skinnedProbeBone.localPosition=Vector3.right*(step?((frame/16&1)==0?0:2):(.6f+.6f*Mathf.Sin(frame*.07f)));
                    Render();ReadInto(capture.Raw,raw);ReadInto(capture.Diffuse,filtered);
                    for(int y=24;y<40;y++)for(int x=24;x<40;x++)
                    {
                        Require(!CutoutCubeShadow(blue,x,y),"Player cutout motion Far reference occluded");
                        float red=CutoutCubeShadow(light,x,y)?0:Expected(light,x,y),b=Expected(blue,x,y);int pixel=y*64+x;
                        var a=raw.GetPixel(x,y);var f=filtered.GetPixel(x,y);
                        if(step&&frame%16==0)changed[pixel]=frame>0&&Mathf.Abs(red-previousRed[pixel])>1e-6f;
                        if(step&&frame%16<3&&changed[pixel]){changedError+=Math.Abs(f.r-red);changedReference+=Expected(light,x,y);}
                        previousRed[pixel]=red;redReference+=red;blueReference+=b;redRaw+=a.r;blueRaw+=a.b;redFiltered+=f.r;blueFiltered+=f.b;error+=Math.Abs(f.r-red)+Math.Abs(f.b-b);
                    }
                }
                double rawR=redRaw/redReference,rawB=blueRaw/blueReference,filteredR=redFiltered/redReference,filteredB=blueFiltered/blueReference,l1=error/(redReference+blueReference),transition=changedReference>0?changedError/changedReference:0;
                Debug.Log($"[HoRTDIPlayerProbe] CUTOUT_MOTION step={step} rawR={rawR:F6} rawB={rawB:F6} filteredR={filteredR:F6} filteredB={filteredB:F6} frameL1={l1:F6} transitionChangedL1={transition:F6}");
                Require(Math.Abs(rawR-1)<.03&&Math.Abs(rawB-1)<.03,"Player dynamic cutout raw energy");
                Require(Math.Abs(filteredR-1)<.05&&Math.Abs(filteredB-1)<.05&&l1<.35&&(!step||changedReference>0&&transition<.35),"Player dynamic cutout noise/energy/trailing gate");
            }
        }
        finally{skin.sharedMaterial=original;blocker.SetActive(false);skinnedProbeBone.localPosition=Vector3.zero;Destroy(adapter);Destroy(raw);Destroy(filtered);}
    }
    private bool CutoutCubeShadow(Light source,int x,int y)
    {
        var ray=probeCamera.ViewportPointToRay(new Vector3((x+.5f)/64,(y+.5f)/64,0));var p=ray.origin+ray.direction*(-ray.origin.z/ray.direction.z);
        var origin=blocker.transform.InverseTransformPoint(p)-skinnedProbeBone.localPosition;var direction=blocker.transform.InverseTransformVector(source.transform.position-p);
        float lo=0,hi=1;for(int axis=0;axis<3;axis++)
        {
            if(Mathf.Abs(direction[axis])<1e-7f){if(Mathf.Abs(origin[axis])>.5f)return false;continue;}
            float a=(-.5f-origin[axis])/direction[axis],b=(.5f-origin[axis])/direction[axis];lo=Mathf.Max(lo,Mathf.Min(a,b));hi=Mathf.Min(hi,Mathf.Max(a,b));if(lo>hi)return false;
        }
        return hi>0&&lo<1;
    }
    private void ValidatePrimaryOrigin()
    {
        var skin=blocker.GetComponent<SkinnedMeshRenderer>();var original=skin.sharedMaterial;var adapter=Instantiate(cutoutMaterial);
        var raw=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);var filtered=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
        try
        {
            skin.sharedMaterial=adapter;blocker.SetActive(true);skinnedProbeBone.localPosition=Vector3.right*100;
            surfaceMaterial.SetFloat("_Reflectance",.04f);surfaceMaterial.SetFloat("_Smoothness",.6f);surfaceMaterial.SetFloat("_Metallic",0);
            receiver.transform.position=Vector3.zero;for(int i=0;i<192;i++)Render();
            double rawR=0,rawB=0,redReference=0,blueReference=0,error=0;
            for(int frame=0;frame<256;frame++)
            {
                float z=.025f*Mathf.Sin(frame*.11f);receiver.transform.position=Vector3.forward*z;Render();ReadInto(capture.RawSpecular,raw);ReadInto(capture.Specular,filtered);
                for(int y=24;y<40;y++)for(int x=24;x<40;x++)
                {
                    float r=PrimarySpecularReference(light,x,y,z),b=PrimarySpecularReference(blue,x,y,z);var a=raw.GetPixel(x,y);var f=filtered.GetPixel(x,y);
                    redReference+=r;blueReference+=b;rawR+=a.r;rawB+=a.b;error+=Math.Abs(f.r-r)+Math.Abs(f.b-b);
                }
            }
            double l1=error/(redReference+blueReference);
            Debug.Log($"[HoRTDIPlayerProbe] PRIMARY_ORIGIN rawR={rawR/redReference:F6} rawB={rawB/blueReference:F6} specFrameL1={l1:F6}; origin-specific gate");
            Require(Math.Abs(rawR/redReference-1)<.03&&Math.Abs(rawB/blueReference-1)<.03&&l1<.15,"Player moving-primary origin regression");
        }
        finally{receiver.transform.position=Vector3.zero;skin.sharedMaterial=original;blocker.SetActive(false);skinnedProbeBone.localPosition=Vector3.zero;Destroy(adapter);Destroy(raw);Destroy(filtered);}
    }
    private float PrimarySpecularReference(Light source,int x,int y,float z)
    {
        var ray=probeCamera.ViewportPointToRay(new Vector3((x+.5f)/64,(y+.5f)/64,0));var p=ray.origin+ray.direction*((z-ray.origin.z)/ray.direction.z);
        var delta=source.transform.position-p;double d2=delta.sqrMagnitude;var L=delta.normalized;var V=-probeCamera.transform.forward;var H=(V+L).normalized;
        double nl=Math.Max(0,-L.z),nv=Math.Max(0,-V.z),nh=Math.Max(0,-H.z),vh=Math.Max(0,Vector3.Dot(V,H)),a2=Math.Pow(102.0/255,4);
        double D=a2/(Math.PI*Math.Pow(nh*nh*a2+1-nh*nh,2)),G=2*nl/(nv*Math.Sqrt(a2+(1-a2)*nl*nl)+nl*Math.Sqrt(a2+(1-a2)*nv*nv)),F=10.0/255+(1-10.0/255)*Math.Pow(1-vh,5),fade=Math.Max(0,1-Math.Pow(d2/(source.range*source.range),2));
        return(float)(D*G*F/4*source.intensity/d2*fade*fade);
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
