using System;
using System.IO;
using System.Linq;
using System.Reflection;
using lilToon.URP.Extensions.PlanarReflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class HoMmdPlrValidation
{
    static string stage;
    static string mode, root;
    static string[] modes;
    static int modeIndex;
    static int frames;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        var args=Environment.GetCommandLineArgs(); int i=Array.IndexOf(args,"-plrMode");
        modes=(i>=0?args[i+1]:"on").Split(',');modeIndex=0;
        foreach(var value in modes)if(!new[]{"on","off","debug","strong","scene","scene-off"}.Contains(value))throw new ArgumentException("Unknown PLR mode "+value);
        i=Array.IndexOf(args,"-hoStage");stage=i>=0?args[i+1]:"孔雀月令";
        root=Path.GetFullPath("Library/HoMmdStages/PLR-validation/"+stage);Directory.CreateDirectory(root);
        RenderPipelineManager.endCameraRendering+=Inspect;
        EditorApplication.delayCall+=CaptureNext;
    }
    static void CaptureNext()
    {
        if(modeIndex>=modes.Length){RenderPipelineManager.endCameraRendering-=Inspect;EditorApplication.Exit(0);return;}
        mode=modes[modeIndex++];frames=0;
            try{HoMmdStageCapture.Begin(HoMmdStageBuilder.Root+stage+"/"+stage+".unity",Path.Combine(root,mode+".png"),1280,720,
                p=>{Debug.Log("PLR_VALIDATED_CAPTURE="+p);EditorApplication.delayCall+=CaptureNext;},
                e=>{Debug.LogException(e);EditorApplication.Exit(1);},Configure,8);}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static string[] reviewStages;
    static int reviewStage,reviewView;
    // Capture only: do not rerun the lighting authoring pass or overwrite artist edits.
    public static void RunReview()
    {
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-hoStages");
        reviewStages=i>=0?args[i+1].Split(','):new[]{"孔雀月令","花吐电気"};
        reviewStage=0;reviewView=0;EditorApplication.delayCall+=ReviewNext;
    }
    static void ReviewNext()
    {
        if(reviewStage>=reviewStages.Length){EditorApplication.Exit(0);return;}
        try{HoMmdStageBuilder.BeginRender(new HoMmdStageWork.Request{stage=reviewStages[reviewStage],view=reviewView,width=1280,height=720},
            p=>{Debug.Log("PLR_REVIEW_CAPTURE="+p);if(++reviewView==3){reviewView=0;reviewStage++;}EditorApplication.delayCall+=ReviewNext;},
            e=>{Debug.LogException(e);EditorApplication.Exit(1);});}
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void Configure(Camera camera)
    {
        var volumes=Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
        foreach(var v in volumes)if(v.profile.TryGet<HoPlanarReflectionVolume>(out var plr))
        {
            plr.enable.Override(true);plr.compositeEnabled.Override(false);plr.maxSurfacesPerCamera.Override(8);
            plr.debugMode.Override(mode=="debug"?HoPlanarReflectionDebugMode.All:HoPlanarReflectionDebugMode.Off);
        }
        foreach(var s in Object.FindObjectsByType<HoPlanarReflectionSurface>(FindObjectsSortMode.None))
        {
            if(mode=="off"||mode=="scene-off"){s.enabled=false;continue;}
            if(mode=="strong")
            {
                var r=(Renderer)typeof(HoPlanarReflectionSurface).GetField("targetRenderer",Private).GetValue(s);
                var mats=r.sharedMaterials.Select(m=>new Material(m){hideFlags=HideFlags.DontSave}).ToArray();
                foreach(var m in mats){m.SetFloat("_UseReflection",1);m.SetFloat("_UsePlanarReflection",1);m.SetFloat("_PlanarReflectionStrength",1);m.SetFloat("_Smoothness",1);m.SetFloat("_Reflectance",1);m.SetFloat("_PlanarReflectionMinSmoothness",0);m.SetFloat("_PlanarReflectionEdgeFade",0);m.SetFloat("_PlanarReflectionFadeEnd",0);m.SetColor("_PlanarReflectionTint",new Color(0,4,0,1));}
                r.sharedMaterials=mats;
            }
        }
        HoMmdStageAdvanced.ConfigureCapture(camera);
        if(mode.StartsWith("scene"))camera.cameraType=CameraType.SceneView;
    }
    static void Inspect(ScriptableRenderContext context,Camera camera)
    {
        if((camera.cameraType!=CameraType.Game&&camera.cameraType!=CameraType.SceneView) || ++frames!=8)return;
        var lines=new System.Text.StringBuilder();
        lines.AppendLine("globalParams="+Shader.GetGlobalVector("_LILPBRPlanarReflectionParams"));
        foreach(var s in Object.FindObjectsByType<HoPlanarReflectionSurface>(FindObjectsSortMode.None))
        {
            var r=(Renderer)typeof(HoPlanarReflectionSurface).GetField("targetRenderer",Private).GetValue(s);
            var b=new MaterialPropertyBlock();r.GetPropertyBlock(b);
            var tex=typeof(HoPlanarReflectionSurface).GetField("reflectionTexture",Private).GetValue(s) as RenderTexture;
            lines.AppendLine(s.name+" enabled="+s.enabled+" params="+b.GetVector("_LILPBRPlanarReflectionParams")+" toggle="+b.GetFloat("_UsePlanarReflection")+" texture="+b.GetTexture("_LILPBRPlanarReflectionTexture")+" mat="+r.sharedMaterial.shader.name);
            if(tex!=null && s.name=="000_地板")
            {
                Save(tex,Path.Combine(root,mode+"-source.png"));
                for(int mip=1;mip<=3;mip++)
                {
                    var small=RenderTexture.GetTemporary(Mathf.Max(1,tex.width>>mip),Mathf.Max(1,tex.height>>mip),0,RenderTextureFormat.ARGBHalf);
                    Graphics.CopyTexture(tex,0,mip,small,0,0);Save(small,Path.Combine(root,mode+"-mip"+mip+".png"));RenderTexture.ReleaseTemporary(small);
                }
            }
        }
        File.WriteAllText(Path.Combine(root,mode+"-bindings.txt"),lines.ToString());
        if(mode=="debug")File.WriteAllText(Path.Combine(root,"gpu-prefilter.txt"),HoMmdPlrGpuCheck.CheckPrefilter(camera));
    }
    static void Save(RenderTexture source,string path)
    {
        var prev=RenderTexture.active;RenderTexture.active=source;
        var t=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
        t.ReadPixels(new Rect(0,0,source.width,source.height),0,0);t.Apply();RenderTexture.active=prev;
        var pixels=t.GetPixels();var bytes=new Color32[pixels.Length];for(int i=0;i<pixels.Length;i++)bytes[i]=pixels[i].gamma;
        var png=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false);png.SetPixels32(bytes);png.Apply();File.WriteAllBytes(path,png.EncodeToPNG());Object.DestroyImmediate(t);Object.DestroyImmediate(png);
    }
}
