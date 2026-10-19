#pragma warning disable CS0618
using System;
using System.IO;
using System.Reflection;
using lilToon.URP.Extensions.RTDI;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool temporalJitterAudit;private static int jitterPhase,jitterSample,jitterClock,lastJitterEngineFrame=-1;
        private static HoRTDIConvergenceStatistics jitterRaw,jitterFiltered;private static Texture2D jitterImage;private static JitterCase jitterCase;
        private static readonly FieldInfo JitterOffset=typeof(TemporalAA.Settings).GetField("jitterFrameCountOffset",BindingFlags.Instance|BindingFlags.NonPublic);
        [Serializable] private sealed class JitterCase
        {
            public string profile;public int resets,stableFrames,jitteredFrames,engineFrames;public uint main,fast,previewSamples;
            public HoRTDIConvergenceStatistics.Result raw,filtered;
        }
        [Serializable] private sealed class JitterReport
        {public bool pass,compatibility,sceneClockProgressedWithFrozenGameTime;public string protocol="Actual URP TAA projection, real Editor update frames; injected .25-second NRD intervals with Scene View policy";public JitterCase legacy,repaired;public int lensChangeRestarts,sceneClockRenders;public double sceneClockElapsed;public float worstJitterPixelError;}
        private static JitterReport jitterReport;
        public static void RunTemporalJitter(){Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");temporalJitterAudit=true;RunDenoise();}
        public static void RunTemporalJitterCompatibility(){compatibility=true;RunTemporalJitter();}
        private static void BeginJitterCase()
        {
            var c=feature.LastContext;c.historyValid=c.nrdHistoryValid=false;c.frame=0;c.nrd.ValidationForceSceneViewPreview=true;c.nrd.ValidationUseJitteredProjection=jitterPhase==0;
            jitterCase=new JitterCase{profile=jitterPhase==0?"legacy-jittered-stability":"separate-jitter-and-lens"};jitterSample=0;
            jitterRaw=new HoRTDIConvergenceStatistics(16*16*3);jitterFiltered=new HoRTDIConvergenceStatistics(16*16*3);
        }
        private static void TickTemporalJitter()
        {
            if(jitterReport==null)
            {
                camera.orthographic=false;camera.fieldOfView=40;camera.allowDynamicResolution=false;
                var additional=camera.GetUniversalAdditionalCameraData();additional.renderPostProcessing=true;additional.antialiasing=AntialiasingMode.TemporalAntiAliasing;additional.taaSettings.jitterScale=1;
                feature.Settings.useEnvironment=false;feature.Settings.temporal=feature.Settings.spatial=true;feature.Settings.initialSamples=8;feature.Settings.maxHistory=4;
                surfaceMaterial.SetFloat("_Reflectance",.04f);light.color=Color.red;Point(new Vector3(-1.5f,0,-1),Color.blue,4);blocker.SetActive(false);
                RenderCamera();Require(feature.LastContext?.nrd!=null&&JitterOffset!=null,"Actual TAA fixture unavailable");
                jitterImage=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));jitterReport=new JitterReport{compatibility=compatibility};BeginJitterCase();
            }
            if(Time.frameCount==lastJitterEngineFrame){UnityEditor.EditorApplication.QueuePlayerLoopUpdate();return;}lastJitterEngineFrame=Time.frameCount;
            var c=feature.LastContext;var data=camera.GetUniversalAdditionalCameraData();object settings=data.taaSettings;
            // URP's existing diagnostic seed offset aligns actual Halton samples
            // across the two cases; no camera or shader projection is fabricated.
            JitterOffset.SetValue(settings,jitterSample-Time.frameCount);data.taaSettings=(TemporalAA.Settings)settings;
            c.nrd.ValidationTimeSeconds=1000+(++jitterClock)*.25;RenderCamera();jitterSample++;jitterCase.engineFrames++;
            if(c.lastNrdReset)jitterCase.resets++;if(c.nrd.StableDiffuseHistory)jitterCase.stableFrames++;
            if(c.previousProjection!=camera.projectionMatrix)jitterCase.jitteredFrames++;
            if(jitterPhase==1)
            {
                Require(c.previousNonJitteredProjection==camera.projectionMatrix,"Real lens matrix was lost");
                var viewPoint=c.previousProjection.inverse.MultiplyPoint(new Vector3(0,0,.5f));var clip=camera.projectionMatrix.MultiplyPoint(viewPoint);
                var expected=new Vector2(clip.x*32,clip.y*32);float error=(expected-c.previousJitterPixels).magnitude;
                jitterReport.worstJitterPixelError=Mathf.Max(jitterReport.worstJitterPixelError,error);Require(error<.001f,"NRD sample jitter sign/units mismatch");
            }
            if(jitterSample>128)
            {
                var values=new double[16*16*3];foreach(bool filtered in new[]{false,true})
                {
                    ReadImage(filtered?c.PublishedDiffuse.rt:c.diffuse.rt,jitterImage);int i=0;
                    for(int y=24;y<40;y++)for(int x=24;x<40;x++){var color=jitterImage.GetPixel(x,y);Require(color.a>.5f,"Jitter fixture receiver invalid");values[i++]=color.r;values[i++]=color.g;values[i++]=color.b;}
                    (filtered?jitterFiltered:jitterRaw).Add(values);
                }
            }
            if(jitterSample<192)return;
            jitterCase.main=c.nrd.AppliedSettings.diffFrames;jitterCase.fast=c.nrd.AppliedSettings.diffFast;jitterCase.previewSamples=c.nrd.SceneViewAccumulatedFrames;
            jitterCase.raw=jitterRaw.Summarize("rawDiffuse",129,192);jitterCase.filtered=jitterFiltered.Summarize("filteredDiffuse",129,192);
            if(jitterPhase==0){jitterReport.legacy=jitterCase;jitterPhase=1;BeginJitterCase();return;}
            jitterReport.repaired=jitterCase;
            Require(jitterReport.legacy.jitteredFrames>180&&jitterReport.repaired.jitteredFrames>180,"URP did not actually jitter the render projection");
            Require(jitterReport.legacy.stableFrames==0&&jitterReport.legacy.fast==0,"Legacy control did not reproduce history starvation");
            Require(jitterCase.resets==1&&jitterCase.stableFrames>180&&jitterCase.main>=30&&jitterCase.fast>=6&&jitterCase.previewSamples==64,"TAA still blocks settled sample history");
            Require(Math.Abs(jitterReport.legacy.raw.mean-jitterCase.raw.mean)<1e-6,"Jitter separation changed raw sampling");
            Require(jitterCase.filtered.normalizedTemporalVariance<jitterReport.legacy.filtered.normalizedTemporalVariance*.25,"TAA static temporal variance did not improve");
            camera.fieldOfView=45;c.nrd.ValidationTimeSeconds=1000+(++jitterClock)*.25;RenderCamera();if(c.lastNrdReset)jitterReport.lensChangeRestarts++;
            Require(!c.nrd.StableDiffuseHistory,"Real FOV change was treated as TAA jitter");
            for(int i=0;i<3;i++){c.nrd.ValidationTimeSeconds=1000+(++jitterClock)*.25;RenderCamera();if(c.lastNrdReset)jitterReport.lensChangeRestarts++;}
            Require(jitterReport.lensChangeRestarts==1&&c.nrd.StableDiffuseHistory,"Real FOV change did not restart once and settle");
            c.nrd.ValidationTimeSeconds=null;c.nrd.ValidationUseJitteredProjection=c.nrd.ValidationForceSceneViewPreview=false;
            // Separate protocol: actual requested Scene View GPU renders in one
            // engine frame. The editor clock must progress while game time does not.
            camera.cameraType=CameraType.SceneView;int frozenFrame=Time.frameCount;double frozenTime=Time.unscaledTimeAsDouble;
            var sampleTime=c.nrd.GetType().GetField("currentSampleTime",BindingFlags.Instance|BindingFlags.NonPublic);
            RenderCamera();double firstTime=(double)sampleTime.GetValue(c.nrd);
            while((UnityEditor.EditorApplication.timeSinceStartup-firstTime<.75||jitterReport.sceneClockRenders<8)&&jitterReport.sceneClockRenders<2048)
            {RenderCamera();Read(c.PublishedDiffuse.rt);jitterReport.sceneClockRenders++;}
            jitterReport.sceneClockElapsed=(double)sampleTime.GetValue(c.nrd)-firstTime;
            Require(Time.frameCount==frozenFrame&&Time.unscaledTimeAsDouble==frozenTime,"Scene clock fixture did not retain frozen game time");
            Require(jitterReport.sceneClockElapsed>=.5&&c.nrd.StableDiffuseHistory&&c.nrd.AppliedSettings.diffFrames>=30&&c.nrd.AppliedSettings.diffFast>=6,"Frozen game time still blocks Scene View accumulation");
            jitterReport.sceneClockProgressedWithFrozenGameTime=true;jitterReport.pass=true;
            File.WriteAllText("rtdi-temporal-jitter-"+(compatibility?"compat":"rg")+".json",JsonUtility.ToJson(jitterReport,true));
            Debug.Log("[HoRTDITemporalJitter] PASS "+JsonUtility.ToJson(jitterReport));Finish(null);
        }
    }
}
