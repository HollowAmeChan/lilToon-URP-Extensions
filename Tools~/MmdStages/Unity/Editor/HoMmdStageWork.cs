using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

// Explicit authoring/review requests for the project where these tools are installed.
// Nothing reapplies recipes or audits scenes in the background.
[InitializeOnLoad]
public static class HoMmdStageWork
{
    [Serializable] public sealed class Request
    {
        public string id, action, stage, scenePath, stageFolder, outputPath, logoPath, motionAsset;
        public string settingsPath;
        public string[] pmxPaths;
        public int width=1280, height=720, view, warmup=16;
        public float motionFrame=-1.0f, motionRate=30.0f;
        public float logoY=.1f, logoSize=.3f;
    }
    [Serializable] public sealed class SceneInfo { public string path; public string name; public bool dirty; }
    [Serializable] public sealed class Status { public string time; public string project; public bool compiling; public bool playing; public string activeScene; public SceneInfo[] scenes; public string pipeline; public string graphics; }
    [Serializable] public sealed class Response { public string id; public string status; public string message; public string result; }
    public static string Work => Path.GetFullPath("Library/HoMmdStages");
    static double next;
    static bool busy;
    static HoMmdStageWork()
    {
        // Never replay a request that was interrupted by an editor crash/reload.
        if(Directory.Exists(Work)&&File.Exists(Path.Combine(Work,"running.json")))
            File.Move(Path.Combine(Work,"running.json"),Path.Combine(Work,"interrupted-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+".json"));
        EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if(busy && File.Exists(Path.Combine(Work,"cancel-capture.txt")))
        { File.Delete(Path.Combine(Work,"cancel-capture.txt")); HoMmdStageCapture.Cancel(); }
        if(EditorApplication.timeSinceStartup<next || busy) return;
        next=EditorApplication.timeSinceStartup+1;
        Directory.CreateDirectory(Work);
        var scenes=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i))
            .Select(s=>new SceneInfo{path=s.path,name=s.name,dirty=s.isDirty}).ToArray();
        File.WriteAllText(Path.Combine(Work,"status.json"),JsonUtility.ToJson(new Status{
            time=DateTime.UtcNow.ToString("O"),project=Application.dataPath,compiling=EditorApplication.isCompiling,
            playing=EditorApplication.isPlaying,activeScene=SceneManager.GetActiveScene().path,scenes=scenes,
            pipeline=AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline),graphics=SystemInfo.graphicsDeviceType.ToString()},true));
        string request=Path.Combine(Work,"request.json");
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode ||
           File.Exists("Library/HoMmdVideo/request.json"))return;
        Request command=null;
        bool deferred=false;
        busy=true;
        try
        {
            command=JsonUtility.FromJson<Request>(File.ReadAllText(request));
            File.Move(request,Path.Combine(Work,"running.json"));
            if(command.action=="render")
            {
                if(!string.IsNullOrEmpty(command.scenePath))
                    HoMmdSceneWorkshop.RenderOne(command,path=>Complete(command,"ok",path,null),
                        error=>Complete(command,"error",null,error.ToString()));
                else HoMmdStageBuilder.BeginRender(command,path=>Complete(command,"ok",path,null),
                    error=>Complete(command,"error",null,error.ToString()));
                deferred=true;return;
            }
            if(command.action=="review")
            {
                HoMmdSceneWorkshop.Review(command,path=>Complete(command,"ok",path,null),
                    error=>Complete(command,"error",null,error.ToString()));
                deferred=true;return;
            }
            string result;
            if(command.action=="refresh"){AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);result="refreshed";}
            else if(command.action=="status")result=File.ReadAllText(Path.Combine(Work,"status.json"));
            else if(command.action=="post-inventory" || command.action=="post-migrate" || command.action=="post-tonemap" || command.action=="post-tune")result=HoMmdHoPostReview.Execute(command);
            else if(command.action=="logo")result=HoMmdStageBranding.SetLogos(
                string.IsNullOrEmpty(command.stageFolder)?"Assets/00动画/MMD场景":command.stageFolder,command.logoY,command.logoSize);
            else if(command.action=="create")result=HoMmdSceneWorkshop.Create(command);
            else if(command.action=="fixtures")result=HoMmdPracticalLighting.Inventory(
                string.IsNullOrEmpty(command.scenePath)?HoMmdStageBuilder.Root+command.stage+"/"+command.stage+".unity":command.scenePath);
            else if(command.action=="stage-survey")result=HoMmdPracticalLighting.Inventory(command.scenePath,true);
            else if(command.action=="practicals")result=HoMmdPracticalLighting.Apply(
                string.IsNullOrEmpty(command.scenePath)?HoMmdStageBuilder.Root+command.stage+"/"+command.stage+".unity":command.scenePath);
            else
            {
                Type type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("HoMmdStageBuilder")).FirstOrDefault(x=>x!=null);
                if(type==null)throw new InvalidOperationException("Scene builder not compiled yet.");
                result=(string)type.GetMethod("Execute",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{command});
            }
            File.WriteAllText(Path.Combine(Work,"result.json"),JsonUtility.ToJson(new Response{id=command.id,status="ok",result=result},true));
        }
        catch(Exception e)
        {
            Exception actual=e is TargetInvocationException && e.InnerException!=null?e.InnerException:e;
            File.WriteAllText(Path.Combine(Work,"result.json"),JsonUtility.ToJson(new Response{id=command?.id,status="error",message=actual.ToString()},true));
            Debug.LogException(actual);
        }
        finally { if(!deferred){string running=Path.Combine(Work,"running.json");if(File.Exists(running))File.Delete(running);busy=false;} }
    }
    static void Complete(Request command,string status,string result,string message)
    {
        File.WriteAllText(Path.Combine(Work,"result.json"),JsonUtility.ToJson(new Response{id=command.id,status=status,result=result,message=message},true));
        string running=Path.Combine(Work,"running.json");if(File.Exists(running))File.Delete(running);busy=false;
    }
}
