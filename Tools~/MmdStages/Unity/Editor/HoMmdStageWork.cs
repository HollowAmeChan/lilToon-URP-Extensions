using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

// Project-local authoring queue. Only this project and a fixed scene-building entry point
// are handled; requests/results are kept under Library, never among scene resources.
[InitializeOnLoad]
public static class HoMmdStageWork
{
    [Serializable] public sealed class Request { public string id; public string action; public string stage; public int width=1280; public int height=720; public int view; }
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
        if(EditorApplication.timeSinceStartup<next || busy) return;
        next=EditorApplication.timeSinceStartup+1;
        if(!Application.dataPath.Replace('\\','/').EndsWith("/BREAK_URP/Assets",StringComparison.OrdinalIgnoreCase))return;
        Directory.CreateDirectory(Work);
        var scenes=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i))
            .Select(s=>new SceneInfo{path=s.path,name=s.name,dirty=s.isDirty}).ToArray();
        File.WriteAllText(Path.Combine(Work,"status.json"),JsonUtility.ToJson(new Status{
            time=DateTime.UtcNow.ToString("O"),project=Application.dataPath,compiling=EditorApplication.isCompiling,
            playing=EditorApplication.isPlaying,activeScene=SceneManager.GetActiveScene().path,scenes=scenes,
            pipeline=AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline),graphics=SystemInfo.graphicsDeviceType.ToString()},true));
        string request=Path.Combine(Work,"request.json");
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlaying)return;
        Request command=null;
        bool deferred=false;
        busy=true;
        try
        {
            command=JsonUtility.FromJson<Request>(File.ReadAllText(request));
            File.Move(request,Path.Combine(Work,"running.json"));
            if(command.action=="render")
            {
                HoMmdStageBuilder.BeginRender(command,path=>Complete(command,"ok",path,null),
                    error=>Complete(command,"error",null,error.ToString()));
                deferred=true;return;
            }
            string result;
            if(command.action=="refresh"){AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);result="refreshed";}
            else if(command.action=="status")result=File.ReadAllText(Path.Combine(Work,"status.json"));
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
