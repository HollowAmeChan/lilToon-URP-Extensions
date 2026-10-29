using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.Editor.Pmx;
using Hollow.HoMmdStages;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.GTAO;
using lilToon.URP.Extensions.VolumeLight;
using Object=UnityEngine.Object;

public static class HoMmdStageBuilder
{
    public const string Root="Assets/mmd场景测试/H2Cu阿相/";
    public static void BuildRemainingBatch()
    {
        try{EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            Execute(new HoMmdStageWork.Request{action="buildRemaining"});EditorApplication.Exit(0);}
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    public static void AdjustAllBatch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            foreach(var recipe in Recipes())Adjust(ReadRecipe(recipe.name));
            EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    public static void RenderOneBatch()
    {
        try
        {
            string[] args=Environment.GetCommandLineArgs();
            int index=Array.IndexOf(args,"-hoStage");
            string stage=index>=0?args[index+1]:"花吐电気";
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            BeginRender(new HoMmdStageWork.Request{stage=stage,width=1280,height=720},path=>{
                File.WriteAllText(Path.Combine(HoMmdStageWork.Work,"capture-"+stage+".json"),
                    "{\"status\":\"ok\",\"path\":\""+path.Replace("\\","/")+"\"}");
                EditorApplication.Exit(0);
            },e=>{Debug.LogException(e);EditorApplication.Exit(1);});
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static int captureIndex;
    static string[] captureStages;
    static readonly List<string> captured=new List<string>();
    public static void RenderAllBatch()
    {
        RenderStagesBatch(Recipes().Select(x=>x.name));
    }
    public static void RenderStagesBatch(IEnumerable<string> stages)
    {
        captureIndex=0;captured.Clear();captureStages=stages.ToArray();
        EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
        EditorApplication.delayCall+=CaptureNext;
    }
    public static void RefineAndRenderAllBatch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            foreach(var recipe in Recipes())Adjust(ReadRecipe(recipe.name));
            RenderAllBatch();
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void CaptureNext()
    {
        if(captureIndex==captureStages.Length)
        {File.WriteAllLines(Path.Combine(HoMmdStageWork.Work,"all-captures.txt"),captured);EditorApplication.Exit(0);return;}
        string stage=captureStages[captureIndex];
        try
        {
            BeginRender(new HoMmdStageWork.Request{stage=stage,width=1280,height=720},path=>{
                captured.Add(path);captureIndex++;Debug.Log("MMD_STAGE_CAPTURE "+stage+" "+path);
                File.WriteAllLines(Path.Combine(HoMmdStageWork.Work,"all-captures.txt"),captured);
                EditorApplication.delayCall+=CaptureNext;
            },e=>{Debug.LogException(e);EditorApplication.Exit(1);});
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    public static void BeginRender(HoMmdStageWork.Request request,Action<string> done,Action<Exception> failed)
    {
        Recipe r=ReadRecipe(request.stage);
        string path=Root+r.name+"/Renders/"+r.name+(request.view==0?"_主视角":request.view==1?"_左侧视角":"_右侧视角")+".png";
        if(Environment.GetCommandLineArgs().Contains("-hoDiagnostic"))
            path=Path.Combine(HoMmdStageWork.Work,r.name+"-diagnostic.png");
        int outputIndex=Array.IndexOf(Environment.GetCommandLineArgs(),"-hoOutputSuffix");
        if(outputIndex>=0)path=Path.Combine(HoMmdStageWork.Work,r.name+"-"+Environment.GetCommandLineArgs()[outputIndex+1]+(request.view==0?"":"-view"+request.view)+".png");
        int widthIndex=Array.IndexOf(Environment.GetCommandLineArgs(),"-hoWidth");
        if(widthIndex>=0){request.width=int.Parse(Environment.GetCommandLineArgs()[widthIndex+1]);request.height=request.width*9/16;}
        HoMmdStageCapture.Begin(Root+r.name+"/"+r.name+".unity",path,request.width,request.height,done,failed,
            camera=>{
                if(Environment.GetCommandLineArgs().Contains("-hoDiagnostic"))
                {
                    var renderer=Asset<UniversalRendererData>(Root+r.name+"/Rendering/Ho Renderer.asset");
                    foreach(var f in renderer.rendererFeatures)f.SetActive(false);
                }
                if(Environment.GetCommandLineArgs().Contains("-hoNoPost"))camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
                if(Environment.GetCommandLineArgs().Contains("-hoNoVolume"))
                {
                    var renderer=Asset<UniversalRendererData>(Root+r.name+"/Rendering/Ho Renderer.asset");
                    foreach(var feature in renderer.rendererFeatures.OfType<HoVolumeLightRendererFeature>())feature.SetActive(false);
                }
                if(request.view>0)
                {
                    string name=request.view==1?"左侧观察相机":"右侧观察相机";
                    var review=camera.transform.root.GetComponentsInChildren<Camera>().FirstOrDefault(x=>x.name==name);
                    if(review==null)throw new InvalidOperationException("Missing authored review camera: "+name);
                    camera.transform.SetPositionAndRotation(review.transform.position,review.transform.rotation);camera.fieldOfView=review.fieldOfView;
                }
                HoMmdStageAdvanced.ConfigureCapture(camera);
            },32);
    }
    [Serializable] public sealed class Recipe
    {
        public string name; public string[] models; public Color key,fill,accent,ambient; public float keyIntensity=1.7f;
        public Vector3 camera,target,keyAngles=new Vector3(40,-30,0);
        public float fov=50,bloom=.18f,exposure=.2f,fog=.002f,volume=.15f;
    }
    static Color C(float r,float g,float b)=>new Color(r,g,b,1);
    public static Recipe[] Recipes()=>new[]{
        new Recipe{name="花吐电気",models=new[]{"H2CU式stage06·花吐电気.pmx"},key=C(1,.82f,.58f),fill=C(.48f,.7f,1),accent=C(1,.63f,.26f),ambient=C(.45f,.49f,.56f),camera=new Vector3(2,1.35f,2.6f),target=new Vector3(-2.7f,1.25f,-2.3f),keyAngles=new Vector3(32,-65,0),exposure=.6f,volume=.35f},
        new Recipe{name="乐园·深命",models=new[]{"主体.pmx"},key=C(1,.9f,.67f),fill=C(.42f,.68f,.72f),accent=C(.75f,1,.64f),ambient=C(.27f,.36f,.36f),camera=new Vector3(15,5,-22),target=new Vector3(0,4,0),keyAngles=new Vector3(38,-30,0),keyIntensity=2.6f,volume=.35f},
        new Recipe{name="圣诞万花镜",models=new[]{"主体.pmx","吊饰AL+.pmx","圣诞树&礼物AL+.pmx"},key=C(.72f,.83f,1),fill=C(.29f,.43f,.65f),accent=C(1,.58f,.19f),ambient=C(.22f,.27f,.38f),camera=new Vector3(0,3,17),target=new Vector3(0,5,-17),keyAngles=new Vector3(38,155,0),keyIntensity=1.2f,bloom=.4f,volume=.23f},
        new Recipe{name="茉莉庭院",models=new[]{"茉莉庭院 合并版.pmx"},key=C(1,.96f,.8f),fill=C(.68f,.85f,1),accent=C(.68f,1,.84f),ambient=C(.38f,.46f,.46f),camera=new Vector3(0,2.6f,-12),target=new Vector3(0,2.5f,0),keyAngles=new Vector3(42,22,0),keyIntensity=2.2f,exposure=.6f,volume=.22f},
        new Recipe{name="六角花台绣球",models=new[]{"主体.pmx"},key=C(.9f,.88f,1),fill=C(.47f,.68f,1),accent=C(.93f,.6f,1),ambient=C(.3f,.3f,.44f),camera=new Vector3(8,4,-14),target=new Vector3(0,2.5f,0),keyAngles=new Vector3(38,-30,0),keyIntensity=1.7f,bloom=.32f,volume=.1f},
        new Recipe{name="琉璃万花镜",models=new[]{"主体·琉璃万花镜.pmx","吊饰（AL+）·琉璃万花镜.pmx"},key=C(.7f,.84f,1),fill=C(.44f,.52f,.9f),accent=C(.45f,.88f,1),ambient=C(.19f,.24f,.4f),camera=new Vector3(0,3,-22),target=new Vector3(0,5,10),keyAngles=new Vector3(30,-30,0),keyIntensity=1.45f,bloom=.32f,volume=.2f},
        new Recipe{name="古典机械剧场",models=new[]{"主体.pmx","吊饰.pmx","地面装饰.pmx"},key=C(1,.73f,.4f),fill=C(.4f,.53f,.8f),accent=C(1,.32f,.12f),ambient=C(.24f,.19f,.25f),camera=new Vector3(0,4,27),target=new Vector3(0,5,0),keyAngles=new Vector3(35,175,0),keyIntensity=1.7f,bloom=.22f,volume=.3f},
        new Recipe{name="拟花十字之棺",models=new[]{"场景.pmx"},key=C(.85f,.76f,1),fill=C(.38f,.53f,.9f),accent=C(.92f,.35f,.57f),ambient=C(.25f,.21f,.35f),camera=new Vector3(13,7,-21),target=new Vector3(0,9,0),keyAngles=new Vector3(42,15,0),keyIntensity=1.65f,bloom=.3f,volume=.18f}
,
    }.Concat(HoMmdStageRemaining.Recipes()).ToArray();

    public static string Execute(HoMmdStageWork.Request request)
    {
        if(request.action=="catalog") return JsonUtility.ToJson(new Catalog{stages=Recipes()},true);
        if(request.action=="buildRemaining")
        {foreach(var item in Recipes())if(!File.Exists(Root+item.name+"/"+item.name+".unity"))Build(item);return "Eight base scenes saved";}
        Recipe recipe=ReadRecipe(request.stage);
        if(request.action=="build")return Build(recipe);
        if(request.action=="render")throw new InvalidOperationException("Use asynchronous stage capture.");
        if(request.action=="audit")return Audit(recipe);
        if(request.action=="adjust")return Adjust(recipe);
        throw new ArgumentException("Unknown stage action: "+request.action);
    }
    [Serializable]sealed class Catalog{public Recipe[] stages;}
    public static Recipe ReadRecipe(string stage)
    {
        string path=Root+stage+"/Rendering/制作参数.json";
        return File.Exists(path)?JsonUtility.FromJson<Recipe>(File.ReadAllText(path)):Recipes().Single(x=>x.name==stage);
    }
    static string Adjust(Recipe r)
    {
        var old=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.OpenScene(Root+r.name+"/"+r.name+".unity",OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var root=scene.GetRootGameObjects().First();
            var renderers=root.GetComponentsInChildren<MeshRenderer>(true);
            foreach(var mat in renderers.Select(x=>x.sharedMaterial).Where(x=>x!=null).Distinct())TuneMaterial(mat,r);
            foreach(var surface in renderers)
                if(surface.name.Contains("玻璃"))surface.shadowCastingMode=ShadowCastingMode.Off;
            var camera=root.GetComponentInChildren<Camera>();
            camera.transform.position=r.camera;camera.transform.LookAt(r.target);camera.fieldOfView=r.fov;
            camera.GetUniversalAdditionalCameraData().volumeLayerMask=1<<31;
            var environment=root.GetComponentInChildren<HoMmdStageEnvironment>();
            camera.clearFlags=CameraClearFlags.Skybox;
            environment.gameObject.layer=31;
            environment.ambientSky=r.ambient;environment.ambientEquator=r.ambient*.7f;
            environment.ambientGround=r.ambient*.3f;
            environment.sun.color=r.key;environment.sun.intensity=r.keyIntensity;
            environment.sun.transform.rotation=Quaternion.Euler(r.keyAngles);environment.ApplyNow();
            var lights=root.transform.Find("02 灯光");
            var fill=lights.GetComponentsInChildren<Light>().FirstOrDefault(x=>x.name=="环境补光");
            if(fill!=null){fill.color=r.fill;fill.intensity=.4f;fill.transform.rotation=Quaternion.Euler(20,r.keyAngles.y+160,0);}
            foreach(var light in lights.GetComponentsInChildren<Light>())
            {
                if(light.name=="舞台重点光"){light.transform.position=r.target+new Vector3(2,6,3);light.transform.LookAt(r.target);}
                if(light.name=="色彩轮廓光"){light.color=r.accent;light.transform.position=r.target+new Vector3(-4,4,-5);light.transform.LookAt(r.target);}
            }
            if(r.name=="花吐电気"&&lights.Find("窗侧漫反射 1")==null)
            {
                for(int i=0;i<4;i++)
                {
                    var local=Light(lights,"窗侧漫反射 "+(i+1),LightType.Point,C(1,.91f,.73f),1.6f,new Vector3(3.3f,1.8f,-2+i*3));
                    local.range=5;local.shadows=LightShadows.None;
                }
            }
            if(r.name=="乐园·深命"&&lights.Find("温室天空补光")==null)
            {
                var local=Light(lights,"温室天空补光",LightType.Point,C(.58f,.78f,1),4,new Vector3(0,8,-6));
                local.range=30;local.shadows=LightShadows.None;
            }
            var profile=root.GetComponentInChildren<Volume>().sharedProfile;
            profile.TryGet(out ColorAdjustments color);color.postExposure.Override(r.exposure);
            profile.TryGet(out Bloom bloom);bloom.intensity.Override(r.bloom);
            profile.TryGet(out HoVolumeLightVolume lightVolume);lightVolume.intensity.Override(r.volume);
            var rendererData=Asset<UniversalRendererData>(Root+r.name+"/Rendering/Ho Renderer.asset");
            var kept=new HashSet<string>();
            foreach(var feature in rendererData.rendererFeatures)
            {
                string n=feature.GetType().Name;
                feature.SetActive(new[]{"HoGeometryDataRendererFeature","HoGeometryBufferRendererFeature","HoSurfaceBufferRendererFeature",
                    "HoAtmosphereRendererFeature","HoGTAORendererFeature","HoShadowCastRendererFeature","HoVolumeLightRendererFeature",
                    "ImageProcessRendererFeature","WeightedOITRendererFeature"}.Contains(n)&&kept.Add(n));
                if(feature is HoGTAORendererFeature gtao)gtao.Settings.temporalFrameCount=0;
                EditorUtility.SetDirty(feature);
            }
            PruneRenderer(rendererData);
            EditorUtility.SetDirty(profile);EditorUtility.SetDirty(rendererData);
            EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            return Audit(r);
        }
        finally{EditorSceneManager.CloseScene(scene,true);if(old.IsValid())SceneManager.SetActiveScene(old);}
    }
    static void Folders(string path){Directory.CreateDirectory(path);}
    static T Asset<T>(string path) where T:Object=>AssetDatabase.LoadAssetAtPath<T>(path);
    static string ModelPath(Recipe r,string file)
    {
        string[] paths=Directory.GetFiles(Root+r.name+"/Source",file,SearchOption.AllDirectories);
        if(paths.Length!=1)throw new Exception("Expected one source model: "+r.name+"/"+file);
        return paths[0].Replace('\\','/');
    }
    public static string Build(Recipe r)
    {
        string folder=Root+r.name;
        string scenePath=folder+"/"+r.name+".unity";
        if(File.Exists(scenePath))throw new Exception("Scene exists; use targeted edits instead of overwriting: "+scenePath);
        Folders(folder+"/Materials");Folders(folder+"/Rendering");Folders(folder+"/Prefabs");Folders(folder+"/Renders");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach(string file in r.models)
        {
            string path=ModelPath(r,file);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(path) as HoPmxImporter;
            if(importer==null)throw new Exception("Ho PMX importer missing "+path);
            importer.generateOutline=false; EditorUtility.SetDirty(importer);importer.SaveAndReimport();
            HoPmxMaterialExtraction.Extract(path,folder+"/Materials");
        }
        var setup=EditorSceneManager.GetSceneManagerSetup();
        Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var root=new GameObject(r.name+" · H2CU阿相");
            var geometry=Child(root.transform,"01 场景模型");
            foreach(string file in r.models)
            {
                GameObject obj=(GameObject)PrefabUtility.InstantiatePrefab(Asset<GameObject>(ModelPath(r,file)),scene);
                obj.transform.SetParent(geometry,false);
            }
            var renderers=geometry.GetComponentsInChildren<MeshRenderer>();
            Bounds bounds=new Bounds(Vector3.zero,Vector3.one);
            bool first=true;
            foreach(var renderer in renderers)
            {
                // The source flower stage contains a stray shrub far outside the set.
                if(renderer.bounds.size.x>100)continue;
                if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);
            }
            foreach(var mat in renderers.Select(x=>x.sharedMaterial).Where(x=>x!=null).Distinct())TuneMaterial(mat,r);
            GameObject prefab=PrefabUtility.SaveAsPrefabAsset(root,folder+"/Prefabs/"+r.name+" 模型.prefab");
            // Keep ordinary model hierarchy in the scene so individual surfaces are easy to edit.
            var lights=Child(root.transform,"02 灯光");
            var sun=Light(lights,"主光 · "+r.name,LightType.Directional,r.key,r.keyIntensity,new Vector3(0,12,0));
            sun.transform.rotation=Quaternion.Euler(r.keyAngles);sun.shadows=LightShadows.Soft;sun.shadowStrength=.85f;
            var fill=Light(lights,"环境补光",LightType.Directional,r.fill,.45f,Vector3.zero);
            fill.transform.rotation=Quaternion.Euler(20,r.keyAngles.y+160,0);fill.shadows=LightShadows.None;
            AddPracticalLights(lights,renderers,r,bounds);
            var mainCam=Child(root.transform,"03 相机").gameObject.AddComponent<Camera>();
            mainCam.name="主相机";mainCam.tag="MainCamera";mainCam.transform.position=r.camera;mainCam.transform.LookAt(r.target);
            mainCam.fieldOfView=r.fov;mainCam.nearClipPlane=.05f;mainCam.farClipPlane=250;
            mainCam.allowHDR=true;mainCam.backgroundColor=r.ambient*.22f;mainCam.clearFlags=CameraClearFlags.SolidColor;
            var cameraData=mainCam.GetUniversalAdditionalCameraData();cameraData.renderPostProcessing=true;
            cameraData.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality=AntialiasingQuality.High;
            var renderRoot=Child(root.transform,"04 环境与后处理");renderRoot.gameObject.layer=31;
            cameraData.volumeLayerMask=1<<31;
            var pipeline=CreatePipeline(folder,r,bounds);
            var environment=renderRoot.gameObject.AddComponent<HoMmdStageEnvironment>();
            environment.pipeline=pipeline;environment.sun=sun;environment.ambientSky=r.ambient;
            environment.ambientEquator=r.ambient*.7f;environment.ambientGround=r.ambient*.3f;
            environment.ambientIntensity=1;environment.reflectionIntensity=.35f;environment.fog=false;
            var sky=new Material(Shader.Find("Skybox/Procedural")){name=r.name+" 天空"};
            sky.SetColor("_SkyTint",r.ambient);sky.SetColor("_GroundColor",r.ambient*.3f);sky.SetFloat("_Exposure",.6f);
            AssetDatabase.CreateAsset(sky,folder+"/Rendering/天空.mat");environment.skybox=sky;environment.ApplyNow();
            var volume=renderRoot.gameObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=50;
            volume.sharedProfile=CreateProfile(folder,r);
            var medium=Child(renderRoot,"局部空气介质").gameObject.AddComponent<HoAtmosphereRegion>();
            medium.transform.position=bounds.center;medium.size=bounds.size+Vector3.one*5;
            medium.density=.008f;medium.tint=Color.Lerp(Color.white,r.key,.2f);medium.blendDistance=2;medium.volumeLightWeight=.5f;
            // Only the stage's own renderer/pipeline/settings are serialized here.
            EditorSceneManager.SaveScene(scene,scenePath);
            File.WriteAllText(folder+"/Rendering/制作参数.json",JsonUtility.ToJson(r,true));
            AssetDatabase.SaveAssets();
            File.WriteAllText(folder+"/场景说明.md","# "+r.name+"\n\n模型作者：H2CU阿相。原始文件和使用规约在 Source/。\n\n使用 Ho PMX 与 Ho lilToon。Materials/ 为可直接编辑的外置材质；Rendering/ 仅属于本场景；主相机为初始构图。\n\n打开本场景后“环境与后处理”组件选择本场景独立 HoURP 配置，离开时恢复。Source 保留作者原始材质，不把配布图当作最终渲染。\n");
            return Audit(r);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene,true);
            if(setup.Length>0) {var previous=SceneManager.GetSceneByPath(setup.First(x=>x.isActive).path);if(previous.IsValid())SceneManager.SetActiveScene(previous);}
        }
    }
    static Transform Child(Transform parent,string name){var o=new GameObject(name);o.transform.SetParent(parent,false);return o.transform;}
    static Light Light(Transform parent,string name,LightType type,Color color,float intensity,Vector3 position)
    {
        var o=Child(parent,name);o.position=position;var l=o.gameObject.AddComponent<Light>();
        l.type=type;l.color=color;l.intensity=intensity;l.range=15;return l;
    }
    static void Set(Material m,string key,float v){if(m.HasProperty(key))m.SetFloat(key,v);}
    static void TuneMaterial(Material mat,Recipe recipe)
    {
        string n=mat.name;
        Set(mat,"_AsUnlit",.05f);Set(mat,"_LightMinLimit",.08f);Set(mat,"_LightMaxLimit",1.6f);
        Set(mat,"_MonochromeLighting",0);Set(mat,"_MultiLightIntensity",1);Set(mat,"_MultiLightCastShadowStrength",.7f);
        Set(mat,"_ShadowStrength",.65f);Set(mat,"_ShadowBorder",.35f);Set(mat,"_ShadowBlur",.3f);
        Set(mat,"_UseReflection",0);Set(mat,"_ApplySpecular",0);Set(mat,"_ApplySpecularFA",0);
        Set(mat,"_Smoothness",.28f);Set(mat,"_Metallic",0);Set(mat,"_Reflectance",.035f);
        if(new[]{"金","银","黑铁","框架","镶边","齿轮","钟","鐘","指針","指针"}.Any(n.Contains))
        {Set(mat,"_UseReflection",1);Set(mat,"_ApplySpecular",1);Set(mat,"_ApplySpecularFA",1);
            Set(mat,"_Metallic",.55f);Set(mat,"_Smoothness",.58f);Set(mat,"_ApplyReflection",1);}
        if(new[]{"玻璃","水晶","水滴"}.Any(n.Contains))
        {
            mat.shader=Shader.Find("Hidden/lilToonTransparent");Set(mat,"_ZWrite",0);Set(mat,"_Cutoff",.001f);
            Set(mat,"_AlphaToMask",0);Set(mat,"_SrcBlend",1);Set(mat,"_DstBlend",10);
            mat.SetOverrideTag("RenderType","Transparent");
            Color c=mat.GetColor("_Color");c.a=Mathf.Min(c.a,n.Contains("水晶")?.72f:.22f);mat.SetColor("_Color",c);
            Set(mat,"_UseReflection",1);Set(mat,"_ApplySpecular",1);Set(mat,"_Smoothness",.82f);Set(mat,"_ApplyReflection",1);mat.renderQueue=3000;
        }
        bool bulb=new[]{"燈膽","灯胆","燈胆","金灯","银灯","红灯","星星"}.Any(n.Contains);
        if(bulb)
        {
            Set(mat,"_UseEmission",1);
            if(mat.HasProperty("_EmissionColor"))mat.SetColor("_EmissionColor",recipe.accent*2.2f);
            Set(mat,"_EmissionBlend",1);
        }
        EditorUtility.SetDirty(mat);
    }
    static void AddPracticalLights(Transform parent,MeshRenderer[] renderers,Recipe r,Bounds bounds)
    {
        var lamps=renderers.Where(x=>new[]{"燈膽","灯胆","金灯","银灯","红灯","灯胆"}.Any(x.name.Contains)).ToArray();
        int count=0;
        foreach(var lamp in lamps)
        {
            Bounds b=lamp.bounds;
            int rows=Mathf.Clamp(Mathf.RoundToInt(b.size.z/5),1,6);
            for(int i=0;i<rows&&count<14;i++,count++)
            {
                var pos=new Vector3(b.center.x,b.center.y,Mathf.Lerp(b.min.z,b.max.z,(i+.5f)/rows));
                var l=Light(parent,"灯具暖光 "+(count+1),LightType.Point,r.accent,2.2f,pos);
                l.range=6;l.shadows=LightShadows.None;
            }
        }
        var spot=Light(parent,"舞台重点光",LightType.Spot,r.key,12,r.target+new Vector3(2,6,-3));
        spot.transform.LookAt(r.target);spot.spotAngle=65;spot.innerSpotAngle=35;spot.range=25;spot.shadows=LightShadows.Soft;
        var rim=Light(parent,"色彩轮廓光",LightType.Spot,r.accent,9,r.target+new Vector3(-4,4,5));
        rim.transform.LookAt(r.target);rim.spotAngle=80;rim.range=25;
    }
    static UniversalRenderPipelineAsset CreatePipeline(string folder,Recipe r,Bounds bounds)
    {
        string rendererPath=folder+"/Rendering/Ho Renderer.asset";
        if(!AssetDatabase.CopyAsset("Assets/Settings/PC_Renderer.asset",rendererPath))throw new Exception("Cannot copy Ho Renderer");
        var renderer=Asset<UniversalRendererData>(rendererPath);
        var kept=new HashSet<string>();
        foreach(var feature in renderer.rendererFeatures)
        {
            string n=feature.GetType().Name;
            bool enable=new[]{"HoGeometryDataRendererFeature","HoGeometryBufferRendererFeature","HoSurfaceBufferRendererFeature",
                "HoAtmosphereRendererFeature","HoGTAORendererFeature","HoShadowCastRendererFeature",
                "HoVolumeLightRendererFeature","ImageProcessRendererFeature","WeightedOITRendererFeature"}.Contains(n)&&kept.Add(n);
            feature.SetActive(enable);
            if(feature is HoGTAORendererFeature ao){ao.Settings.quality=HoGTAOQuality.Medium;ao.Settings.worldSpaceRadius=.55f;}
            if(feature is HoVolumeLightRendererFeature vl){vl.FeatureSettings.intensity=r.volume;vl.FeatureSettings.stepCount=32;vl.FeatureSettings.maxDistance=80;vl.FeatureSettings.antialiasing=HoVolumeLightAntialiasing.Spatial;}
            EditorUtility.SetDirty(feature);
        }
        PruneRenderer(renderer);
        EditorUtility.SetDirty(renderer);
        string pipelinePath=folder+"/Rendering/Ho Pipeline.asset";
        AssetDatabase.CopyAsset("Assets/Settings/PC_RPAsset.asset",pipelinePath);
        var pipeline=Asset<UniversalRenderPipelineAsset>(pipelinePath);
        var so=new SerializedObject(pipeline);
        so.FindProperty("m_RendererDataList").arraySize=1;
        so.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue=renderer;
        so.FindProperty("m_MainLightShadowmapResolution").intValue=2048;
        so.FindProperty("m_AdditionalLightsShadowmapResolution").intValue=2048;
        so.FindProperty("m_ShadowDistance").floatValue=80;
        so.FindProperty("m_MSAA").intValue=1;
        so.FindProperty("m_VolumeProfile").objectReferenceValue=null;
        so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(pipeline);return pipeline;
    }
    static void PruneRenderer(UniversalRendererData renderer)
    {
        string path=AssetDatabase.GetAssetPath(renderer);
        if(!path.StartsWith(Root,StringComparison.Ordinal))throw new Exception("Only stage-owned renderer copies may be pruned");
        renderer.rendererFeatures.RemoveAll(x=>x==null||!x.isActive);
        foreach(var child in AssetDatabase.LoadAllAssetsAtPath(path).OfType<ScriptableRendererFeature>())
            if(!renderer.rendererFeatures.Contains(child))Object.DestroyImmediate(child,true);
        var serialized=new SerializedObject(renderer);
        var map=serialized.FindProperty("m_RendererFeatureMap");
        if(map!=null)map.arraySize=0;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
    static VolumeProfile CreateProfile(string folder,Recipe r)
    {
        var profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.name=r.name+" 后处理";
        AssetDatabase.CreateAsset(profile,folder+"/Rendering/后处理.asset");
        T Add<T>()where T:VolumeComponent{var c=profile.Add<T>(true);c.name=typeof(T).Name;AssetDatabase.AddObjectToAsset(c,profile);return c;}
        var bloom=Add<Bloom>();bloom.intensity.Override(r.bloom);bloom.threshold.Override(1.1f);bloom.scatter.Override(.65f);
        var color=Add<ColorAdjustments>();color.postExposure.Override(r.exposure);color.contrast.Override(8);color.saturation.Override(-4);
        var tone=Add<Tonemapping>();tone.mode.Override(TonemappingMode.ACES);
        var vignette=Add<Vignette>();vignette.intensity.Override(.15f);vignette.smoothness.Override(.5f);
        var ao=Add<HoGTAOVolume>();ao.enable.Override(true);ao.quality.Override(HoGTAOQuality.Medium);ao.worldSpaceRadius.Override(.55f);
        var vl=Add<HoVolumeLightVolume>();vl.enable.Override(true);vl.intensity.Override(r.volume);vl.stepCount.Override(32);vl.antialiasing.Override(HoVolumeLightAntialiasing.Spatial);
        var atmos=Add<HoAtmosphereVolume>();atmos.enable.Override(true);atmos.intensity.Override(.6f);
        EditorUtility.SetDirty(profile);return profile;
    }
    public static string Audit(Recipe r)
    {
        string folder=Root+r.name;
        return JsonUtility.ToJson(new StageAudit{name=r.name,scene=folder+"/"+r.name+".unity",
            models=r.models,materials=Directory.GetFiles(folder+"/Materials","*.mat").Length,
            rendering=Directory.GetFiles(folder+"/Rendering","*",SearchOption.TopDirectoryOnly).Where(x=>!x.EndsWith(".meta")).ToArray()},true);
    }
    [Serializable]sealed class StageAudit{public string name,scene;public string[]models,rendering;public int materials;}
}
