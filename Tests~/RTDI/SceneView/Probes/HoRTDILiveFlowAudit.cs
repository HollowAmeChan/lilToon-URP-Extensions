using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RTDI;
using lilToon.URP.Extensions.RTDI.NRD;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    // Temporary, opt-in live diagnosis. Never saves assets; restores settings
    // and context callbacks on completion, reload, timeout or editor quit.
    [InitializeOnLoad] internal static class HoRTDILiveFlowAudit
    {
        [Serializable] private sealed class Request {public string project,output;public string[] modes;}
        [Serializable] private sealed class Signal {public string name;public HoRTDIConvergenceStatistics.Result pixels,blocks;}
        [Serializable] private sealed class Case {public string mode;public int resets,stableFrames;public uint main,fast;public List<Signal> signals=new();}
        [Serializable] private sealed class Report {public bool complete;public string error;public int width,height;public string settings,finalTarget;public List<Case> cases=new();}
        private static Request request;private static Report report;private static SceneView view;private static HoRTDIContext context;private static HoRTDIRendererFeature feature;
        private static Action<CommandBuffer,int,RenderTargetIdentifier> oldCallback;
        private static Func<HoRTDINrdNative.RelaxSettings,HoRTDINrdNative.RelaxSettings> oldSettings;
        private static bool oldSpatial;private static HoRTDILocalSampling oldSampling;
        private static double deadline,next;private static int phase,frames;private static string path;
        private static Matrix4x4 originalView,originalProjection;private static Texture2D image;
        private static RenderTexture temporal,clamped;
        private static Dictionary<string,(HoRTDIConvergenceStatistics pixels,HoRTDIConvergenceStatistics blocks)> stats;
        private static Case current;private static readonly uint[] scale=new uint[3];private static readonly double[] values=new double[64*64*3],blocks=new double[8*8*3];
        static HoRTDILiveFlowAudit()
        {
            if(Application.isBatchMode)return;
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/jp.lilxyzw.liltoon.urp.extensions/package.json");if(package==null)return;
            path=Path.Combine(package.resolvedPath,".codex-research/rtdi-live-flow-v2-request.json");EditorApplication.update+=Tick;
            AssemblyReloadEvents.beforeAssemblyReload+=Interrupted;EditorApplication.quitting+=Interrupted;
        }
        private static void Interrupted(){if(report!=null)Finish("Interrupted by reload or quit");}
        private static void Tick()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||Application.isPlaying)return;
            try
            {
                if(report==null)
                {
                    if(!File.Exists(path))return;var candidate=JsonUtility.FromJson<Request>(File.ReadAllText(path));
                    if(File.Exists(candidate.output)||SessionState.GetBool(candidate.output,false)||!string.Equals(Directory.GetParent(Application.dataPath).FullName.Replace('\\','/'),candidate.project,StringComparison.OrdinalIgnoreCase))return;
                    view=SceneView.lastActiveSceneView;if(view==null)return;
                    foreach(var f in Resources.FindObjectsOfTypeAll<HoRTDIRendererFeature>()){var c=f.ContextForCamera(view.camera);if(c?.nrd!=null){feature=f;context=c;break;}}
                    if(context==null)return;
                    request=candidate;SessionState.SetBool(request.output,true);report=new Report{width=context.width,height=context.height,settings=JsonUtility.ToJson(feature.Settings)};
                    originalView=context.previousView;originalProjection=context.previousProjection;oldSpatial=feature.Settings.spatial;oldSampling=feature.Settings.localSampling;
                    oldSettings=context.nrd.ValidationSettings;oldCallback=context.nrd.ValidationDiffuseStage;
                    image=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);temporal=Target();clamped=Target();
                    context.nrd.ValidationDiffuseStage=Capture;RenderPipelineManager.endCameraRendering+=Rendered;deadline=EditorApplication.timeSinceStartup+240;StartPhase();
                }
                if(EditorApplication.timeSinceStartup>deadline){Finish("Timed out");return;}
                if(EditorApplication.timeSinceStartup>=next){next=EditorApplication.timeSinceStartup+1.0/60;view.Repaint();}
            }
            catch(Exception e){Finish(e.ToString());}
        }
        private static RenderTexture Target(){var t=new RenderTexture(64,64,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);t.Create();return t;}
        private static void Capture(CommandBuffer cmd,int stage,RenderTargetIdentifier source)
        {oldCallback?.Invoke(cmd,stage,source);if(stage==4||stage==6)cmd.CopyTexture(source,0,0,context.width/2-32,context.height/2-32,64,64,stage==4?temporal:clamped,0,0,0,0);}
        private static void StartPhase()
        {
            string mode=request.modes[phase];feature.Settings.spatial=mode=="noSpatial"?false:oldSpatial;feature.Settings.localSampling=mode=="power"?HoRTDILocalSampling.Power:oldSampling;
            context.nrd.ValidationSettings=s=>{if(oldSettings!=null)s=oldSettings(s);if(mode=="legacyFastZero"){s.diffFast=s.specFast=0;}if(mode=="fast6"||mode=="staticAntiLag"||mode=="fast6Both")s.diffFast=6;if(mode=="fast6Both")s.specFast=6;if(mode=="staticAntiLag"){s.acceleration=0;s.reset=0;}return s;};
            context.historyValid=context.nrdHistoryValid=false;context.frame=0;frames=0;stats=new();current=new Case{mode=mode};
        }
        private static void Rendered(ScriptableRenderContext renderContext,Camera camera)
        {
            if(report==null||camera!=view.camera)return;
            try
            {
                if(context.previousView!=originalView||context.previousProjection!=originalProjection){Finish("Rendered camera moved during observation");return;}
                frames++;if(frames<=96)return;current.resets+=context.lastNrdReset?1:0;current.stableFrames+=context.nrd.StableDiffuseHistory?1:0;current.main=context.nrd.AppliedSettings.diffFrames;current.fast=context.nrd.AppliedSettings.diffFast;
                Add("rawDiffuse",context.diffuse.rt);Add("rawSpecular",context.specular.rt);Add("filteredDiffuse",context.filteredDiffuse.rt);Add("filteredSpecular",context.filteredSpecular.rt);
                context.nrdScale.GetData(scale);double inverseScale=1/BitConverter.Int32BitsToSingle((int)scale[1]);
                Add("temporalDemodulated",temporal,inverseScale);Add("clampedDemodulated",clamped,inverseScale);
                var target=camera.targetTexture;if(target!=null&&target.width>=64&&target.height>=64){report.finalTarget=target.name;Add("cameraOutput",target);}
                if(frames!=192)return;
                foreach(var pair in stats)current.signals.Add(new Signal{name=pair.Key,pixels=pair.Value.pixels.Summarize(pair.Key,97,192),blocks=pair.Value.blocks.Summarize(pair.Key+"Blocks",97,192)});
                report.cases.Add(current);File.WriteAllText(request.output+".partial",JsonUtility.ToJson(report,true));
                if(++phase==request.modes.Length){report.complete=true;Finish(null);}else StartPhase();
            }
            catch(Exception e){Finish(e.ToString());}
        }
        private static void Add(string name,RenderTexture texture,double multiplier=1)
        {
            var old=RenderTexture.active;try{RenderTexture.active=texture;image.ReadPixels(new Rect(texture.width/2-32,texture.height/2-32,64,64),0,0);image.Apply();}finally{RenderTexture.active=old;}
            var pixels=image.GetPixels();Array.Clear(blocks,0,blocks.Length);
            for(int y=0;y<64;y++)for(int x=0;x<64;x++)for(int k=0;k<3;k++){double v=pixels[y*64+x][k]*multiplier;values[(y*64+x)*3+k]=v;blocks[((y/8)*8+x/8)*3+k]+=v/64;}
            if(!stats.TryGetValue(name,out var s)){s=(new(values.Length),new(blocks.Length));stats.Add(name,s);}s.pixels.Add(values);s.blocks.Add(blocks);
            if(frames==192&&(name=="cameraOutput"||name=="filteredDiffuse"))File.WriteAllBytes(request.output+"."+current.mode+"."+name+".png",image.EncodeToPNG());
        }
        private static void Finish(string error)
        {
            RenderPipelineManager.endCameraRendering-=Rendered;
            if(feature!=null){feature.Settings.spatial=oldSpatial;feature.Settings.localSampling=oldSampling;}
            if(context?.nrd!=null){context.nrd.ValidationSettings=oldSettings;context.nrd.ValidationDiffuseStage=oldCallback;context.historyValid=context.nrdHistoryValid=false;}
            if(image!=null)UnityEngine.Object.DestroyImmediate(image);if(temporal!=null)UnityEngine.Object.DestroyImmediate(temporal);if(clamped!=null)UnityEngine.Object.DestroyImmediate(clamped);
            if(report!=null){report.error=error;File.WriteAllText(request.output,JsonUtility.ToJson(report,true));Debug.Log("[HoRTDILiveFlowAudit] Restored live settings; result: "+request.output);}
            report=null;context=null;feature=null;view=null;phase=0;
        }
    }
}
