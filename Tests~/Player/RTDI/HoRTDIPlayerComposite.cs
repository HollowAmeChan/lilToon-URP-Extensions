using System;
using System.Collections;
using System.Collections.Generic;
using lilToon.URP.Extensions.RTDI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed partial class HoRTDIPlayerProbe
{
    [Serializable]private sealed class CompositeReport
    {
        public bool pass,serializedComposite;
        public string api,mode,error;
        public int observedFrames,automaticCaptures,automaticStartupFrames,automaticContexts,automaticCameraBegins,automaticCameraCount;
        public float disabledError,opaqueError,filteredError,shadowError,emission,weightError,reenabledError;
    }
    private CompositeReport compositeReport;
    private Color CompositeCamera()
    {
        var previous=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();
        var color=image.GetPixel(32,32);Destroy(image);RenderTexture.active=previous;return color;
    }
    private Color CompositeDirect()
    {
        var color=Read(capture.Diffuse)+Read(capture.Specular);
        if(QualitySettings.activeColorSpace==ColorSpace.Gamma)
        {
            color.r=color.r<=.0031308f?color.r*12.9232102f:1.055f*Mathf.Pow(color.r,1f/2.4f)-.055f;
            color.g=color.g<=.0031308f?color.g*12.9232102f:1.055f*Mathf.Pow(color.g,1f/2.4f)-.055f;
            color.b=color.b<=.0031308f?color.b*12.9232102f:1.055f*Mathf.Pow(color.b,1f/2.4f)-.055f;
        }
        return color;
    }
    private static float CompositeError(Color a,Color b)=>Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Max(Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b)));
    private IEnumerator Composite()
    {
        compositeReport=new CompositeReport{api=result.api,mode=result.mode,serializedComposite=feature.Settings.composite};
        Require(compositeReport.serializedComposite,"Use a Player built with the serialized composite setting enabled");
        Require(profile.TryGet<HoRTDIVolume>(out var volume),"DI Volume missing");volume.debugMode.value=HoRTDIDebugMode.Off;
        atmosphere.Settings.enabled=false;feature.Settings.useEnvironment=false;
        blocker.SetActive(false);blue.enabled=false;feature.Settings.denoise=false;feature.Settings.temporal=feature.Settings.spatial=false;
        light.type=LightType.Directional;light.transform.rotation=Quaternion.identity;light.intensity=2;light.color=Color.white;light.shadows=LightShadows.None;
        surfaceMaterial.SetFloat("_Reflectance",0);surfaceMaterial.SetFloat("_Metallic",0);
        surfaceMaterial.SetFloat("_UseEmission",0);
        feature.Settings.composite=false;yield return null;Render();var original=CompositeCamera();
        feature.SetActive(false);yield return null;Render();compositeReport.disabledError=CompositeError(original,CompositeCamera());
        Require(compositeReport.disabledError<.004f,"Player disabled composition changed original raster");
        feature.SetActive(true);feature.Settings.composite=true;yield return null;Render();var full=CompositeCamera();
        compositeReport.opaqueError=CompositeError(full,CompositeDirect());Require(compositeReport.opaqueError<.004f,"Player camera does not consume published DI");
        Require(CompositeError(full,original)>.025f,"Player composite does not change actual camera output");
        feature.Settings.compositeWeight=.5f;yield return null;Render();compositeReport.weightError=CompositeError(CompositeCamera(),Color.Lerp(original,full,.5f));
        Require(compositeReport.weightError<.004f,"Player composite duplicates old lighting");feature.Settings.compositeWeight=1;
        light.type=LightType.Point;light.transform.position=new Vector3(1.5f,0,-1);light.intensity=4;blocker.SetActive(true);
        yield return null;Render();compositeReport.shadowError=CompositeError(CompositeCamera(),Color.black);
        Require(compositeReport.shadowError<.004f,"Player valid world shadow retains raster lighting");
        surfaceMaterial.SetFloat("_UseEmission",1);surfaceMaterial.SetColor("_EmissionColor",new Color(4,1,.25f,1));
        yield return null;Render();compositeReport.emission=CompositeCamera().r;Require(compositeReport.emission>1,"Player composition loses emission");
        surfaceMaterial.SetFloat("_UseEmission",0);blocker.SetActive(false);feature.Settings.denoise=true;
        light.color=Color.red;blue.enabled=true;blue.transform.position=new Vector3(-1.5f,0,-1);
        // Actual enabled-camera rendering, with no SubmitRenderRequest in this
        // loop. Capture and camera target must both come from each completed frame.
        // Use a normal enabled camera which has never been submitted through
        // SingleCameraRequest. Keep the request-based fixture separate.
        var automaticCamera=new GameObject("Automatic RTDI composite camera").AddComponent<Camera>();
        automaticCamera.CopyFrom(probeCamera);automaticCamera.transform.SetPositionAndRotation(probeCamera.transform.position,probeCamera.transform.rotation);
        automaticCamera.GetUniversalAdditionalCameraData().volumeLayerMask=-1;
        automaticCamera.GetUniversalAdditionalCameraData().allowXRRendering=false;
        probeCamera=automaticCamera;probeCamera.targetTexture=target;probeCamera.enabled=true;
        void ObserveContext(ScriptableRenderContext context,List<Camera> cameras){compositeReport.automaticContexts++;compositeReport.automaticCameraCount=cameras.Count;}
        void ObserveCamera(ScriptableRenderContext context,Camera camera){if(camera==probeCamera)compositeReport.automaticCameraBegins++;}
        RenderPipelineManager.beginContextRendering+=ObserveContext;RenderPipelineManager.beginCameraRendering+=ObserveCamera;
        int previousCapture=capture.CaptureCount,previousFrame=-1;
        // Reactivating a camera after synchronous render requests may miss the
        // already-collected camera list of that frame. Wait for its first actual
        // automatic render before requiring one fresh capture per observed frame.
        while(capture.CaptureCount==previousCapture&&compositeReport.automaticStartupFrames<8)
        {
            yield return null;compositeReport.automaticStartupFrames++;
            Debug.Log($"[HoRTDIPlayerProbe] COMPOSITE_AUTO_START frame={Time.frameCount} captures={capture.CaptureCount} contexts={compositeReport.automaticContexts} cameras={compositeReport.automaticCameraCount} begins={compositeReport.automaticCameraBegins} enabled={probeCamera.isActiveAndEnabled} type={probeCamera.cameraType}");
        }
        Require(capture.CaptureCount>previousCapture,"Player automatic camera failed to start within eight application frames");
        previousCapture=capture.CaptureCount;
        for(int frame=0;frame<96;frame++)
        {
            yield return null;
            Require(Time.frameCount!=previousFrame&&capture.CaptureCount>previousCapture,"Player automatic camera did not render a fresh application frame");
            compositeReport.automaticCaptures+=capture.CaptureCount-previousCapture;
            previousCapture=capture.CaptureCount;previousFrame=Time.frameCount;compositeReport.observedFrames++;
        }
        probeCamera.enabled=false;probeCamera.targetTexture=null;probeCamera.aspect=1;
        RenderPipelineManager.beginContextRendering-=ObserveContext;RenderPipelineManager.beginCameraRendering-=ObserveCamera;
        compositeReport.filteredError=CompositeError(CompositeCamera(),CompositeDirect());
        Require(compositeReport.filteredError<.004f,"Player composition does not consume filtered output");
        volume.enable.Override(false);yield return null;Render();var off=CompositeCamera();
        feature.Settings.composite=false;yield return null;Render();Require(CompositeError(off,CompositeCamera())<.004f,"Player disabled Volume leaks composition");
        feature.Settings.composite=true;volume.enable.Override(true);yield return null;Render();
        compositeReport.reenabledError=CompositeError(CompositeCamera(),CompositeDirect());Require(compositeReport.reenabledError<.004f,"Player reenable uses stale composition");
        compositeReport.pass=result.pass=true;Debug.Log("[HoRTDIPlayerProbe] COMPOSITE_PASS "+JsonUtility.ToJson(compositeReport));
    }
}
