using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    // Opt-in passive live observation. No repaint, asset changes or overrides.
    [InitializeOnLoad] internal static class HoRTDIVolumeLiveAudit
    {
        [Serializable] private sealed class Request {public string project,output;public double seconds=20;}
        [Serializable] private sealed class Sample
        {
            public double time,sampleTime;public string camera,type,volumeSource,status;
            public bool atRender,playing,nrd,stable,reset,preview,historyValid,composite,denoise,legacyDenoise,denoiseOverride;
            public float skyGain,addAmount;public int frame,width,height,dispatches,signature,viewHash,projectionHash;
            public uint world,structure,main,fast,specMain,specFast,previewSamples;
            public int nonJitteredHash;public Vector2 jitterPixels;
        }
        [Serializable] private sealed class Report {public List<Sample> samples=new();}
        private static Request request;private static Report report;private static double end,next,waitingUntil;
        private static readonly FieldInfo Contexts=typeof(HoRTDIRendererFeature).GetField("contexts",BindingFlags.Instance|BindingFlags.NonPublic);
        static HoRTDIVolumeLiveAudit()
        {
            if(Application.isBatchMode)return;
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/jp.lilxyzw.liltoon.urp.extensions/package.json");if(package==null)return;
            string path=Path.Combine(package.resolvedPath,".codex-research/rtdi-volume-live-request.json");if(!File.Exists(path))return;
            request=JsonUtility.FromJson<Request>(File.ReadAllText(path));
            if(File.Exists(request.output)||SessionState.GetBool(request.output,false)||!string.Equals(Directory.GetParent(Application.dataPath).FullName.Replace('\\','/'),request.project,StringComparison.OrdinalIgnoreCase))return;
            SessionState.SetBool(request.output,true);report=new Report();end=double.PositiveInfinity;waitingUntil=EditorApplication.timeSinceStartup+300;
            EditorApplication.update+=Poll;RenderPipelineManager.endCameraRendering+=Rendered;AssemblyReloadEvents.beforeAssemblyReload+=Finish;
        }
        private static void Rendered(ScriptableRenderContext rendering,Camera camera){Capture(camera,true);}
        private static void Poll()
        {
            double now=EditorApplication.timeSinceStartup;if(now<next)return;next=now+.25;
            foreach(var view in SceneView.sceneViews)if(view is SceneView s&&s.camera!=null)Capture(s.camera,false);
            if(now>=end||(report.samples.Count==0&&now>=waitingUntil))Finish();
        }
        private static void Capture(Camera camera,bool atRender)
        {
            if(report==null||report.samples.Count>=512)return;
            foreach(var f in Resources.FindObjectsOfTypeAll<HoRTDIRendererFeature>())
            {
                if(Contexts.GetValue(f) is not IDictionary contexts||contexts[camera] is not HoRTDIContext c)continue;
                if(double.IsPositiveInfinity(end))end=EditorApplication.timeSinceStartup+request.seconds;
                var n=c.nrd;var stack=atRender?VolumeManager.instance.stack:camera.GetUniversalAdditionalCameraData().volumeStack;
                var volume=stack?.GetComponent<HoRTDIVolume>();var view=new HoRTDIViewSettings(f.Settings,volume);
                double time=n!=null?(double)n.GetType().GetField("currentSampleTime",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(n):0;
                report.samples.Add(new Sample{time=EditorApplication.timeSinceStartup,sampleTime=time,atRender=atRender,playing=Application.isPlaying,
                    camera=camera.name,type=camera.cameraType.ToString(),volumeSource=atRender?"render stack":stack!=null?"camera stack":"unavailable",status=f.Status,
                    nrd=n!=null,stable=n?.StableDiffuseHistory??false,reset=c.lastNrdReset,preview=n?.SceneViewPreview??false,historyValid=c.nrdHistoryValid,
                    composite=view.composite,denoise=view.denoise,legacyDenoise=f.Settings.denoise,denoiseOverride=volume?.denoise.overrideState??false,
                    skyGain=view.environmentIntensity,addAmount=view.compositeAmount,frame=c.frame,width=c.width,height=c.height,dispatches=n?.LastDispatchCount??0,
                    signature=c.settingsSignature,viewHash=c.previousView.GetHashCode(),projectionHash=c.previousProjection.GetHashCode(),world=c.sceneRevision,structure=c.sceneStructureRevision,
                    main=n?.AppliedSettings.diffFrames??0,fast=n?.AppliedSettings.diffFast??0,specMain=n?.AppliedSettings.specFrames??0,specFast=n?.AppliedSettings.specFast??0,previewSamples=n?.SceneViewAccumulatedFrames??0,
                    nonJitteredHash=c.previousNonJitteredProjection.GetHashCode(),jitterPixels=c.previousJitterPixels});
            }
        }
        private static void Finish()
        {
            EditorApplication.update-=Poll;RenderPipelineManager.endCameraRendering-=Rendered;AssemblyReloadEvents.beforeAssemblyReload-=Finish;
            if(report==null)return;File.WriteAllText(request.output,JsonUtility.ToJson(report,true));report=null;
            Debug.Log("[HoRTDIVolumeLiveAudit] Saved passive diagnostics: "+request.output);
        }
    }
}
