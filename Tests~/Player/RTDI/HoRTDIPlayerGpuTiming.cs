using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

public sealed partial class HoRTDIPlayerProbe
{
    [Serializable] private sealed class GpuTimingReport
    {
        public bool pass,development,frameTimingEnabled;public string gpu,api,error,renderDriver;public int size;public ulong gpuTimerHz;public GpuTimingCase[] cases;
    }
    [Serializable] private sealed class GpuTimingCase
    {
        public string mode;public int frameSamples,frameRecords,freshFrameRecords,renderedCameras,finalVisibilitySamples,gradientSamples,visibilityCpuSamples,gradientCpuSamples,tracedFrames;
        public double frameGpuMs=-1,finalVisibilityMs=-1,gradientMs=-1;
    }
    private GpuTimingReport gpuTimingReport;
    private IEnumerator GpuTiming()
    {
        gpuTimingReport=new GpuTimingReport{development=Debug.isDebugBuild,frameTimingEnabled=FrameTimingManager.IsFeatureEnabled(),gpuTimerHz=FrameTimingManager.GetGpuTimerFrequency(),gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),size=512};
        bool automatic=Array.Exists(Environment.GetCommandLineArgs(),x=>x=="--gpu-auto");gpuTimingReport.renderDriver=automatic?"automatic camera":"manual request once per application frame";
        Require(Debug.isDebugBuild,"Use BuildGpuTiming for GPU timing");
        QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;Profiler.enabled=true;capture.SetActive(false);
        target.Release();target.width=target.height=512;target.Create();probeCamera.aspect=1;
        probeCamera.targetTexture=null;probeCamera.enabled=automatic;
        feature.Settings.initialSamples=8;feature.Settings.temporal=feature.Settings.spatial=feature.Settings.denoise=true;
        light.transform.position=new Vector3(.4f,0,-1.5f);light.intensity=4;light.color=Color.red;
        blue.transform.position=new Vector3(-.4f,0,-1.5f);blue.intensity=4;blue.color=Color.blue;blue.enabled=true;
        var visibility=CustomSampler.Create("HoRTDI Final Visibility",true).GetRecorder();visibility.enabled=true;
        var gradients=CustomSampler.Create("HoRTDI Visibility Gradients",true).GetRecorder();gradients.enabled=true;
        var frameTimings=new FrameTiming[1];var cases=new List<GpuTimingCase>();
        var contextProperty=feature.GetType().GetProperty("LastContext",BindingFlags.Instance|BindingFlags.NonPublic);
        var skin=blocker.GetComponent<SkinnedMeshRenderer>();var original=skin.sharedMaterial;
        int rendered=0;void EndCamera(ScriptableRenderContext ctx,Camera camera){if(camera==probeCamera)rendered++;}
        RenderPipelineManager.endCameraRendering+=EndCamera;
        try
        {
        foreach(string mode in new[]{"base","opaque","movingCutout"})
        {
            bool moving=mode=="movingCutout";feature.SetActive(mode!="base");blocker.SetActive(moving);skin.sharedMaterial=moving?cutoutMaterial:original;
            var item=new GpuTimingCase{mode=mode};ulong lastTimestamp=0;double frameSum=0,visibilitySum=0,gradientSum=0;rendered=0;
            for(int frame=0;frame<160;frame++)
            {
                if(moving)skinnedProbeBone.localPosition=Vector3.right*(.12f*Mathf.Sin(frame*.09f));
                if(!automatic)Render();FrameTimingManager.CaptureFrameTimings();yield return null;
                if(frame<36)continue; // Recorder has a 3-frame delay; phase warmup is longer.
                if(FrameTimingManager.GetLatestTimings(1,frameTimings)>0)
                {
                    item.frameRecords++;
                    var ft=frameTimings[0];if(ft.frameStartTimestamp>0&&ft.frameStartTimestamp!=lastTimestamp)
                    {
                        lastTimestamp=ft.frameStartTimestamp;item.freshFrameRecords++;
                        if(ft.gpuFrameTime>0&&double.IsFinite(ft.gpuFrameTime)){frameSum+=ft.gpuFrameTime;item.frameSamples++;}
                    }
                }
                if(visibility.gpuSampleBlockCount>0&&visibility.gpuElapsedNanoseconds>0)
                {visibilitySum+=visibility.gpuElapsedNanoseconds/1e6/visibility.gpuSampleBlockCount;item.finalVisibilitySamples++;}
                if(gradients.gpuSampleBlockCount>0&&gradients.gpuElapsedNanoseconds>0)
                {gradientSum+=gradients.gpuElapsedNanoseconds/1e6/gradients.gpuSampleBlockCount;item.gradientSamples++;}
                if(visibility.sampleBlockCount>0)item.visibilityCpuSamples++;if(gradients.sampleBlockCount>0)item.gradientCpuSamples++;
                if(moving)
                {
                    var c=contextProperty.GetValue(feature);if(c!=null&&(bool)c.GetType().GetField("lastVisibilityGradients",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c))item.tracedFrames++;
                }
            }
            item.renderedCameras=rendered;item.frameGpuMs=item.frameSamples>=32?frameSum/item.frameSamples:-1;
            item.finalVisibilityMs=item.finalVisibilitySamples>=32?visibilitySum/item.finalVisibilitySamples:-1;
            item.gradientMs=item.gradientSamples>=32?gradientSum/item.gradientSamples:-1;cases.Add(item);
            Debug.Log($"[HoRTDIPlayerProbe] GPU_TIMING mode={mode} frameSamples={item.frameSamples} frameMs={item.frameGpuMs:F6} visibilitySamples={item.finalVisibilitySamples} visibilityMs={item.finalVisibilityMs:F6} gradientSamples={item.gradientSamples} gradientMs={item.gradientMs:F6} traced={item.tracedFrames}");
        }
        }
        finally{RenderPipelineManager.endCameraRendering-=EndCamera;probeCamera.enabled=false;visibility.enabled=gradients.enabled=false;Profiler.enabled=false;skin.sharedMaterial=original;blocker.SetActive(false);}
        gpuTimingReport.cases=cases.ToArray();gpuTimingReport.pass=cases.TrueForAll(x=>x.frameSamples>=32&&x.renderedCameras>=128)&&cases[2].finalVisibilitySamples>=32&&cases[2].gradientSamples>=32&&cases[2].tracedFrames>=32;
        if(!gpuTimingReport.pass)gpuTimingReport.error="Insufficient GPU frame/marker samples; no DI GPU budget is established";
        result.pass=gpuTimingReport.pass;result.error=gpuTimingReport.error;
    }
}
