using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    // Opt-in, bounded live A/B. Overrides belong to the camera context, not the
    // renderer asset. All overrides are restored on completion/reload/quit.
    [InitializeOnLoad]
    internal static class HoRTDILiveNoiseAudit
    {
        [Serializable]private sealed class Request {public string project,output;}
        [Serializable]private sealed class Case
        {public int reservoirHistory;public HoRTDIConvergenceStatistics.Result diffuse,specular,combined,coarseCombined;}
        [Serializable]private sealed class Report {public bool complete;public string error;public List<Case> cases=new();}
        private static Request request;
        private static Report report;
        private static HoRTDIContext context;
        private static SceneView view;
        private static int phase,frames,lastFrame=-1;
        private static double deadline,next;
        private static int? previousOverride;
        private static Matrix4x4 viewMatrix,projection;
        private static Texture2D image;
        private static HoRTDIConvergenceStatistics diffuse,specular,combined,coarse;
        private static readonly double[] d=new double[64*64*3],s=new double[64*64*3],c=new double[64*64*3],b=new double[8*8*3];
        static HoRTDILiveNoiseAudit()
        {
            if(Application.isBatchMode)return;
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/jp.lilxyzw.liltoon.urp.extensions/package.json");if(package==null)return;
            string path=Path.Combine(package.resolvedPath,".codex-research/rtdi-live-noise-request.json");if(!File.Exists(path))return;
            request=JsonUtility.FromJson<Request>(File.ReadAllText(path));
            if(File.Exists(request.output))return;
            if(!string.Equals(Directory.GetParent(Application.dataPath).FullName.Replace('\\','/'),request.project,StringComparison.OrdinalIgnoreCase)||SessionState.GetBool(request.output,false))return;
            SessionState.SetBool(request.output,true);
            report=new Report();deadline=EditorApplication.timeSinceStartup+120;
            EditorApplication.update+=Tick;AssemblyReloadEvents.beforeAssemblyReload+=Interrupted;EditorApplication.quitting+=Interrupted;
        }
        private static void Interrupted(){Finish("Observation interrupted by reload or quit");}
        private static void StartPhase()
        {
            context.ValidationReservoirHistory=phase==0?20:4;context.historyValid=context.nrdHistoryValid=false;context.frame=0;lastFrame=-1;frames=0;
            diffuse=new HoRTDIConvergenceStatistics(d.Length);specular=new HoRTDIConvergenceStatistics(s.Length);combined=new HoRTDIConvergenceStatistics(c.Length);coarse=new HoRTDIConvergenceStatistics(b.Length);
        }
        private static void Tick()
        {
            try
            {
                double now=EditorApplication.timeSinceStartup;if(now>deadline){Finish("Live A/B timed out");return;}
                if(EditorApplication.isCompiling||EditorApplication.isUpdating||Application.isPlaying)return;
                if(view==null)
                {
                    view=SceneView.lastActiveSceneView;if(view==null)return;
                    foreach(var feature in Resources.FindObjectsOfTypeAll<HoRTDIRendererFeature>())
                    {var candidate=feature.ContextForCamera(view.camera);if(candidate?.nrd!=null){context=candidate;break;}}
                    if(context==null){view=null;return;}
                    previousOverride=context.ValidationReservoirHistory;viewMatrix=context.previousView;projection=context.previousProjection;
                    image=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);StartPhase();
                }
                if(now>=next){next=now+1.0/60;view.Repaint();}
                if(context.frame==lastFrame||context.frame==0)return;lastFrame=context.frame;frames++;
                if(context.previousView!=viewMatrix||context.previousProjection!=projection){Finish("Rendered camera matrices changed during paired observation");return;}
                if(frames<=32)return;
                Read(context.PublishedDiffuse.rt,d);Read(context.PublishedSpecular.rt,s);
                Array.Clear(b,0,b.Length);
                for(int y=0;y<64;y++)for(int x=0;x<64;x++)for(int channel=0;channel<3;channel++)
                {int i=(y*64+x)*3+channel;c[i]=d[i]+s[i];b[((y/8)*8+x/8)*3+channel]+=c[i]/64;}
                diffuse.Add(d);specular.Add(s);combined.Add(c);coarse.Add(b);
                if(frames<96)return;
                report.cases.Add(new Case{reservoirHistory=phase==0?20:4,diffuse=diffuse.Summarize("diffuse",33,96),specular=specular.Summarize("specular",33,96),combined=combined.Summarize("combined",33,96),coarseCombined=coarse.Summarize("coarseCombined",33,96)});
                if(++phase==2){report.complete=true;Finish(null);return;}StartPhase();
            }
            catch(Exception error){Finish(error.ToString());}
        }
        private static void Read(RenderTexture texture,double[] values)
        {
            var previous=RenderTexture.active;
            try{RenderTexture.active=texture;image.ReadPixels(new Rect(texture.width/2-32,texture.height/2-32,64,64),0,0);image.Apply();}
            finally{RenderTexture.active=previous;}
            var pixels=image.GetPixels();int i=0;foreach(var pixel in pixels){values[i++]=pixel.r;values[i++]=pixel.g;values[i++]=pixel.b;}
        }
        private static void Finish(string error)
        {
            EditorApplication.update-=Tick;AssemblyReloadEvents.beforeAssemblyReload-=Interrupted;EditorApplication.quitting-=Interrupted;
            if(context!=null){context.ValidationReservoirHistory=previousOverride;context.historyValid=context.nrdHistoryValid=false;}
            if(image!=null)UnityEngine.Object.DestroyImmediate(image);
            report.error=error;File.WriteAllText(request.output,JsonUtility.ToJson(report,true));
            Debug.Log("[HoRTDILiveNoiseAudit] Paired observation saved; renderer settings were not changed: "+request.output);
        }
    }
}
