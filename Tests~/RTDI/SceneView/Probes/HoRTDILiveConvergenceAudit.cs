using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    // Passive, opt-in editor observation. It never requests a render, changes
    // a scene or saves assets, so the actual Scene View time domain is retained.
    [InitializeOnLoad]
    internal static class HoRTDILiveConvergenceAudit
    {
        [Serializable]private sealed class Request {public string project,output;public double seconds=12;}
        [Serializable]private sealed class Sample
        {
            public double editorTime,unscaledTime,realtime,sampleTime;
            public bool playing,stable,reset;
            public string camera,type,status;
            public int frame,width,height,dispatches;
            public uint diffuseHistory,specularHistory,worldRevision,structureRevision;
            public uint diffuseFast,specularFast;
            public uint accumulatedPreviewFrames;
        }
        [Serializable]private sealed class Report {public string project;public List<Sample> samples=new();}
        private static Request request;
        private static Report report;
        private static double end,next;
        private static readonly FieldInfo Contexts=typeof(HoRTDIRendererFeature).GetField("contexts",BindingFlags.Instance|BindingFlags.NonPublic);
        static HoRTDILiveConvergenceAudit()
        {
            if(Application.isBatchMode)return;
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/jp.lilxyzw.liltoon.urp.extensions/package.json");
            if(package==null)return;
            string path=Path.Combine(package.resolvedPath,".codex-research/rtdi-live-request.json");
            if(!File.Exists(path))return;
            request=JsonUtility.FromJson<Request>(File.ReadAllText(path));
            if(File.Exists(request.output))return;
            string project=Directory.GetParent(Application.dataPath).FullName.Replace('\\','/');
            if(!string.Equals(project,request.project,StringComparison.OrdinalIgnoreCase)||SessionState.GetBool(request.output,false))return;
            SessionState.SetBool(request.output,true);report=new Report{project=project};end=EditorApplication.timeSinceStartup+request.seconds;
            EditorApplication.update+=Observe;
        }
        private static void Observe()
        {
            double now=EditorApplication.timeSinceStartup;if(now<next)return;next=now+.25;
            foreach(var feature in Resources.FindObjectsOfTypeAll<HoRTDIRendererFeature>())
            {
                if(Contexts.GetValue(feature) is not IDictionary contexts)continue;
                foreach(DictionaryEntry entry in contexts)
                {
                    if(entry.Key is not Camera camera||entry.Value is not HoRTDIContext context)continue;
                    var nrd=context.nrd;
                    double time=nrd!=null?(double)nrd.GetType().GetField("currentSampleTime",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(nrd):double.NaN;
                    report.samples.Add(new Sample{editorTime=now,unscaledTime=Time.unscaledTimeAsDouble,realtime=Time.realtimeSinceStartupAsDouble,
                        sampleTime=time,playing=Application.isPlaying,camera=camera.name,type=camera.cameraType.ToString(),frame=context.frame,width=context.width,height=context.height,
                        status=feature.Status,stable=nrd?.StableDiffuseHistory??false,reset=context.lastNrdReset,dispatches=nrd?.LastDispatchCount??0,
                        diffuseHistory=nrd?.AppliedSettings.diffFrames??0,specularHistory=nrd?.AppliedSettings.specFrames??0,diffuseFast=nrd?.AppliedSettings.diffFast??0,specularFast=nrd?.AppliedSettings.specFast??0,
                        worldRevision=context.sceneRevision,structureRevision=context.sceneStructureRevision,accumulatedPreviewFrames=nrd?.SceneViewAccumulatedFrames??0});
                }
            }
            if(now<end)return;
            EditorApplication.update-=Observe;File.WriteAllText(request.output,JsonUtility.ToJson(report,true));
            Debug.Log("[HoRTDILiveConvergenceAudit] Saved passive Scene View diagnostics: "+request.output);
        }
    }
}
