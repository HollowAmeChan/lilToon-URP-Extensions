using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using ConvergenceStatistics=HoRTDIPlayerConvergenceStatistics;

public sealed partial class HoRTDIPlayerProbe
{
    [Serializable] private sealed class ConvergenceResult
    {
        public bool pass;
        public string api,mode,error;
        public int observedFrames,stableFrames,staticHistoryBudget,motionHistoryBudget,motionRestarts;
        public double elapsedSeconds;
        public float oldColourTail;
        public ConvergenceStatistics.Result raw,filtered;
    }
    private ConvergenceResult convergenceReport;
    private object ConvergenceNrd(out object context)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        context=feature.GetType().GetProperty("LastContext",flags).GetValue(feature);
        Require(context!=null,"Player convergence context missing");
        var nrd=context.GetType().GetField("nrd",flags).GetValue(context);
        Require(nrd!=null,"Player convergence NRD missing");return nrd;
    }
    private static bool ConvergenceStable(object nrd)=>(bool)nrd.GetType().GetProperty("StableDiffuseHistory",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(nrd);
    private static int ConvergenceBudget(object nrd)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object settings=nrd.GetType().GetProperty("AppliedSettings",flags).GetValue(nrd);
        return (int)(uint)settings.GetType().GetField("diffFrames",flags).GetValue(settings);
    }
    private IEnumerator Convergence()
    {
        convergenceReport=new ConvergenceResult{api=result.api,mode=result.mode};
        Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
        feature.Settings.denoise=true;feature.Settings.useEnvironment=false;feature.Settings.initialSamples=8;
        feature.Settings.temporal=feature.Settings.spatial=true;
        feature.Settings.finalSampling=lilToon.URP.Extensions.RTDI.HoRTDIFinalSampling.Single;
        blocker.SetActive(false);light.enabled=blue.enabled=true;
        light.color=Color.red;blue.color=Color.blue;light.intensity=blue.intensity=4;
        light.transform.position=new Vector3(1.5f,0,-1);blue.transform.position=new Vector3(-1.5f,0,-1);
        surfaceMaterial.SetFloat("_Reflectance",.04f);surfaceMaterial.SetFloat("_Metallic",0);surfaceMaterial.SetFloat("_Smoothness",.5f);
        probeCamera.orthographic=false;probeCamera.fieldOfView=40;
        var rawImage=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
        var filteredImage=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
        var raw=new ConvergenceStatistics(16*16*3);var filtered=new ConvergenceStatistics(16*16*3);
        var values=new double[16*16*3];int previousFrame=-1;double start=Time.unscaledTimeAsDouble;
        try
        {
            for(int sample=0;sample<192;sample++)
            {
                yield return null;
                Require(Time.frameCount!=previousFrame,"Player convergence reused an application frame");previousFrame=Time.frameCount;
                Render();object nrd=ConvergenceNrd(out _);convergenceReport.observedFrames++;
                if(ConvergenceStable(nrd))convergenceReport.stableFrames++;
                if(sample<64)continue;
                ReadInto(capture.Raw,rawImage);ReadInto(capture.Diffuse,filteredImage);
                int i=0;for(int y=24;y<40;y++)for(int x=24;x<40;x++){Color c=rawImage.GetPixel(x,y);Require(c.a>.5f,"Player raw receiver invalid");values[i++]=c.r;values[i++]=c.g;values[i++]=c.b;}raw.Add(values);
                i=0;for(int y=24;y<40;y++)for(int x=24;x<40;x++){Color c=filteredImage.GetPixel(x,y);Require(c.a>.5f,"Player filtered receiver invalid");values[i++]=c.r;values[i++]=c.g;values[i++]=c.b;}filtered.Add(values);
            }
            convergenceReport.elapsedSeconds=Time.unscaledTimeAsDouble-start;
            object stableNrd=ConvergenceNrd(out _);convergenceReport.staticHistoryBudget=ConvergenceBudget(stableNrd);
            Require(ConvergenceStable(stableNrd)&&convergenceReport.stableFrames>100,"Player real-time history did not become stable");
            convergenceReport.raw=raw.Summarize("rawDiffuse",65,192);convergenceReport.filtered=filtered.Summarize("filteredDiffuse",65,192);
            Require(convergenceReport.raw.normalizedTemporalVariance>0&&convergenceReport.filtered.normalizedTemporalVariance<convergenceReport.raw.normalizedTemporalVariance*.05,"Player stable denoising variance insufficient");
            light.color=Color.green;
            for(int sample=0;sample<4;sample++)
            {
                yield return null;Render();object nrd=ConvergenceNrd(out object context);
                Require(!ConvergenceStable(nrd),"Changed light retained static history profile");
                if((bool)context.GetType().GetField("lastNrdReset",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(context))convergenceReport.motionRestarts++;
                convergenceReport.motionHistoryBudget=ConvergenceBudget(nrd);
            }
            Require(convergenceReport.motionRestarts==1&&convergenceReport.motionHistoryBudget<convergenceReport.staticHistoryBudget,"Player history transition did not select responsive budget once");
            ReadInto(capture.Diffuse,filteredImage);
            for(int y=24;y<40;y++)for(int x=24;x<40;x++)convergenceReport.oldColourTail=Mathf.Max(convergenceReport.oldColourTail,filteredImage.GetPixel(x,y).r);
            Require(convergenceReport.oldColourTail<.001f,"Player retained old red after light recolouring");
            convergenceReport.pass=result.pass=true;Debug.Log("[HoRTDIPlayerProbe] CONVERGENCE_PASS "+JsonUtility.ToJson(convergenceReport));
        }
        finally{Destroy(rawImage);Destroy(filteredImage);}
    }
}
