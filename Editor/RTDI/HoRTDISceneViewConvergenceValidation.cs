#pragma warning disable CS0618
using System;
using System.IO;
using lilToon.URP.Extensions.RTDI;
using lilToon.URP.Extensions.RTDI.NRD;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool slowSceneViewAudit;
        public static void RunSlowSceneView()
        {Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");slowSceneViewAudit=true;RunDenoise();}
        public static void RunSlowSceneViewCompatibility(){compatibility=true;RunSlowSceneView();}
        [Serializable]private sealed class SlowPreviewCase
        {
            public string profile;public int resets,stableFrames;public uint diffuseHistory,specularHistory;
            public HoRTDIConvergenceStatistics.Result raw,filtered;
        }
        [Serializable]private sealed class SlowPreviewReport
        {public bool pass,compatibility;public string protocol="Injected 1.2-second sample intervals, not a GPU/FPS benchmark";public SlowPreviewCase legacy,repaired,referenceFast;public int changeRestarts;}
        private static SlowPreviewCase MeasureSlowPreview(bool legacy,bool referenceFast=false)
        {
            var context=feature.LastContext;context.historyValid=context.nrdHistoryValid=false;context.frame=0;
            context.nrd.ValidationDisableSceneViewPreview=legacy;
            context.nrd.ValidationSettings=referenceFast?s=>{s.diffFast=6;s.specFast=6;return s;}:null;
            var raw=new HoRTDIConvergenceStatistics(16*16*3);var filtered=new HoRTDIConvergenceStatistics(16*16*3);
            var values=new double[16*16*3];var image=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            var result=new SlowPreviewCase{profile=legacy?"legacy-long-gap-reset":referenceFast?"stationary-reference-fast":"stationary-scene-preview"};
            for(int frame=1;frame<=192;frame++)
            {
                context.nrd.ValidationTimeSeconds=1000+frame*1.2;RenderCamera();
                if(context.lastNrdReset)result.resets++;
                if(context.nrd.StableDiffuseHistory)result.stableFrames++;
                if(frame<=128)continue;
                foreach(bool outputFiltered in new[]{false,true})
                {
                    var source=outputFiltered?context.PublishedDiffuse.rt:context.diffuse.rt;
                    var previous=RenderTexture.active;RenderTexture.active=source;
                    image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();RenderTexture.active=previous;
                    int i=0;for(int y=24;y<40;y++)for(int x=24;x<40;x++){var c=image.GetPixel(x,y);Require(c.a>.5f,"Slow preview receiver invalid");values[i++]=c.r;values[i++]=c.g;values[i++]=c.b;}
                    (outputFiltered?filtered:raw).Add(values);
                }
            }
            result.diffuseHistory=context.nrd.AppliedSettings.diffFrames;result.specularHistory=context.nrd.AppliedSettings.specFrames;
            result.raw=raw.Summarize("rawDiffuse",129,192);result.filtered=filtered.Summarize("filteredDiffuse",129,192);
            return result;
        }
        private static void ValidateSlowSceneView()
        {
            var ordinaryClock=new HoRTDINrdTiming();ordinaryClock.Advance(0);Require(ordinaryClock.Advance(1.2),"Game pause reset changed");
            var previewClock=new HoRTDINrdTiming();previewClock.Advance(0,true);Require(!previewClock.Advance(1.2,true),"Slow preview reset");
            Require(previewClock.Advance(-1,true)&&previewClock.Advance(double.NaN,true),"Preview rewind/invalid clock was accepted");
            camera.cameraType=CameraType.SceneView;feature.Settings.inSceneView=true;feature.Settings.useEnvironment=false;
            feature.Settings.temporal=feature.Settings.spatial=true;feature.Settings.initialSamples=8;feature.Settings.maxHistory=4;
            light.color=Color.red;Point(new Vector3(-1.5f,0,-1),Color.blue,4);blocker.SetActive(false);
            RenderCamera();Require(feature.LastContext?.nrd!=null,"Scene View NRD not active");
            var report=new SlowPreviewReport{compatibility=compatibility};report.legacy=MeasureSlowPreview(true);report.repaired=MeasureSlowPreview(false);
            Require(report.legacy.resets==192,"Legacy control did not reproduce long-gap resets");
            Require(report.repaired.resets==1&&report.repaired.stableFrames>180,"Slow preview did not retain settled history");
            Require(report.repaired.diffuseHistory>=30&&report.repaired.specularHistory>=30,"Low FPS collapsed preview history");
            Require(feature.LastContext.nrd.SceneViewAccumulatedFrames==HoRTDINrdContext.SceneViewSettledSamples,"Preview repaint demand did not settle at its sample cap");
            Require(Math.Abs(report.legacy.raw.mean-report.repaired.raw.mean)<.000001,"Preview repair changed raw sample energy");
            Require(report.repaired.filtered.normalizedTemporalVariance<report.legacy.filtered.normalizedTemporalVariance*.25,"Slow preview variance did not improve by 4x");
            report.referenceFast=MeasureSlowPreview(false,true);feature.LastContext.nrd.ValidationSettings=null;
            var context=feature.LastContext;light.color=Color.green;context.nrd.ValidationTimeSeconds=1232;RenderCamera();
            if(context.lastNrdReset)report.changeRestarts++;
            Require(!context.nrd.StableDiffuseHistory&&context.nrd.AppliedSettings.diffFrames<=3,"Changed preview retained static budget");
            context.nrd.ValidationTimeSeconds=1233.2;RenderCamera();if(context.lastNrdReset)report.changeRestarts++;
            Require(report.changeRestarts==1&&context.nrd.StableDiffuseHistory,"Preview change did not restart once and settle");
            context.nrd.ValidationTimeSeconds=null;report.pass=true;
            File.WriteAllText("rtdi-slow-scene-preview-"+(compatibility?"compat":"rg")+".json",JsonUtility.ToJson(report,true));
            Debug.Log("[HoRTDISceneView] PASS "+JsonUtility.ToJson(report));
        }
    }
}
