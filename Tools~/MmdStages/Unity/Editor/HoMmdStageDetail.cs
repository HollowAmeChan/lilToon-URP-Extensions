using System;
using System.IO;
using System.Linq;
using Hollow.HoMmdStages;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.PostProcessing;
using lilToon.URP.Extensions.VolumeLight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

// Additive, repeatable light detailing. Baseline values are captured once so
// later reviews do not compound fog, exposure or material edits.
public static class HoMmdStageDetail
{
    public static int CurrentView { get; private set; }
    static string[] captureNames;static int captureStage,captureView;static readonly System.Collections.Generic.List<string> captured=new();
    [Serializable] public sealed class LayerValue { public string name; public float intensity; }
    [Serializable] public sealed class EmissionValue { public string path; public Color color; }
    [Serializable] public sealed class LightValue { public string name; public float power; }
    [Serializable] public sealed class Baseline
    {
        public float sun,ambient,density,air,shafts,bloom,exposure,contrast;
        public Color sky,equator,ground;
        public LayerValue[] screen,image;public EmissionValue[] emission;public LightValue[] lights;
    }
    [Serializable] public sealed class Look
    {
        public string stage;public float sun=.75f,ambient=.75f,density=.5f,solar=.14f,air=.4f,fog=.22f,glow=.45f,emission=.8f,exposure=-.08f,contrast=3;
        public string[] beams;public float beam=7,rim=2,point=.35f,localPower=4;
    }
    static string Arg(string key){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:null;}
    static Look Default(string name)
    {
        var l=new Look{stage=name,beams=new[]{"主体方向重点","色彩轮廓"}};
        switch(name)
        {
            case "花吐电気":l.sun=.95f;l.ambient=.92f;l.solar=.12f;l.density=.45f;l.beam=3;l.rim=0;l.exposure=0;l.glow=.5f;l.beams=new[]{"明媚窗光"};break;
            case "乐园·深命":l.sun=.85f;l.density=.45f;l.solar=.1f;l.beam=12;l.rim=2;l.beams=new[]{"天窗纵深主光","青色藤蔓轮廓"};break;
            case "圣诞万花镜":l.sun=.8f;l.ambient=.9f;l.exposure=.05f;l.beam=4;l.rim=2;l.emission=.68f;l.beams=new[]{"红绿彩窗冷侧光","圣诞树金色主光"};break;
            case "茉莉庭院":l.sun=.85f;l.exposure=0;l.beam=8;l.rim=2;l.beams=new[]{"庭院天窗暖光","长椅冷色勾边"};break;
            case "六角花台绣球":l.sun=.85f;l.ambient=.9f;l.exposure=.1f;l.beam=6;l.rim=3;l.beams=new[]{"舞台中央月光","绣球洋红轮廓"};break;
            case "琉璃万花镜":l.sun=.65f;l.emission=.45f;l.exposure=-.1f;l.beam=5;l.rim=3;l.beams=new[]{"左彩窗冰蓝光","长廊月光"};break;
            case "古典机械剧场":l.sun=.8f;l.beam=5;l.rim=2;l.exposure=.18f;l.beams=new[]{"钟表金色追光","舞台冷光分离"};break;
            case "拟花十字之棺":l.sun=.85f;l.emission=.7f;l.beam=6;l.rim=3;l.beams=new[]{"十字架月光","玫瑰紫红逆光"};break;
            case "百鸟溯归":l.sun=.65f;l.density=.35f;l.solar=.05f;l.fog=.1f;l.exposure=-.05f;l.beam=10;break;
            case "孔雀月令红墨":l.sun=.65f;l.beam=5;l.rim=2;l.exposure=-.05f;l.beams=new[]{"孔雀案台追光","色彩轮廓"};break;
            case "乐园·墟实":l.sun=.9f;l.solar=.08f;l.beam=9;l.rim=1;l.exposure=.05f;break;
            case "秘月之泉":l.sun=.7f;l.emission=.5f;l.beam=7;l.rim=3;l.exposure=-.1f;break;
            case "万象天文":l.sun=.6f;l.emission=.65f;l.beam=5;l.rim=2;l.exposure=-.15f;break;
            case "炫光夜厅":l.sun=.5f;l.density=.32f;l.solar=.05f;l.air=.2f;l.fog=.1f;l.glow=.25f;l.emission=.55f;l.beam=5;l.rim=3;l.exposure=-.2f;break;
            case "夜月蓝":l.sun=.7f;l.emission=.6f;l.beam=5;l.rim=2;l.exposure=-.05f;break;
            case "芋泥咩咩":l.sun=.7f;l.solar=.06f;l.air=.3f;l.exposure=-.1f;l.emission=.7f;l.beam=2;l.rim=1;break;
            case "月昔梅":l.sun=.5f;l.ambient=1.1f;l.solar=.16f;l.air=.4f;l.fog=.15f;l.beam=2;l.rim=1;l.exposure=-.12f;l.emission=.5f;l.localPower=2;break;
            case "古典机械剧场黑蓝白":l.sun=.8f;l.ambient=.65f;l.solar=.08f;l.beam=6;l.rim=2;l.exposure=.1f;l.beams=new[]{"钟表银蓝追光","色彩轮廓"};break;
            case "冷色古堂":l.sun=.7f;l.emission=.55f;l.solar=.08f;l.exposure=-.08f;l.beam=6;l.rim=2;l.beams=new[]{"圆屏柔和重点","色彩轮廓"};break;
        }
        return l;
    }
    public static void Run()
    {
        try
        {
            var names=Arg("-hoStages")?.Split(',')??HoMmdStageBuilder.Recipes().Select(x=>x.name).ToArray();
            foreach(var name in names)Apply(name);
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);AssetDatabase.SaveAssets();
            if(Arg("-hoApplyOnly")=="1")EditorApplication.Exit(0);else BeginCaptures(names);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void BeginCaptures(string[] names)
    {captureNames=names;captureStage=0;captureView=0;captured.Clear();EditorApplication.delayCall+=NextCapture;}
    static void NextCapture()
    {
        if(captureStage>=captureNames.Length){File.WriteAllLines(Path.Combine(HoMmdStageWork.Work,"all-captures.txt"),captured);EditorApplication.Exit(0);return;}
        string name=captureNames[captureStage];CurrentView=captureView;
        HoMmdStageBuilder.BeginRender(new HoMmdStageWork.Request{stage=name,view=captureView,width=1280,height=720},path=>{
            captured.Add(path);File.WriteAllLines(Path.Combine(HoMmdStageWork.Work,"detail-captures.txt"),captured);Debug.Log("MMD_DETAIL_CAPTURE "+name+" view="+captureView+" "+path);
            if(++captureView==3){captureView=0;captureStage++;}EditorApplication.delayCall+=NextCapture;
        },e=>{Debug.LogException(e);EditorApplication.Exit(1);});
    }
    static void Apply(string name)
    {
        string folder=HoMmdStageBuilder.Root+name;
        var scene=EditorSceneManager.OpenScene(folder+"/"+name+".unity",OpenSceneMode.Single);
        var root=scene.GetRootGameObjects().Single(x=>x.GetComponentInChildren<HoMmdStageEnvironment>()!=null);
        var env=root.GetComponentInChildren<HoMmdStageEnvironment>();var profile=root.GetComponentInChildren<Volume>().sharedProfile;
        profile.TryGet<HoVolumeLightVolume>(out var volume);profile.TryGet<HoAtmosphereVolume>(out var atmosphere);
        profile.TryGet<Bloom>(out var bloom);profile.TryGet<ColorAdjustments>(out var grade);
        profile.TryGet<ScreenProcessStackVolume>(out var screen);profile.TryGet<ImageProcessStackVolume>(out var image);
        var region=root.GetComponentInChildren<HoAtmosphereRegion>();
        var mats=root.transform.Find("01 场景模型").GetComponentsInChildren<MeshRenderer>().Select(x=>x.sharedMaterial).Where(x=>x!=null).Distinct().ToArray();
        var lights=root.GetComponentsInChildren<Light>();string baselinePath=folder+"/Rendering/细节灯光基线.json",lookPath=folder+"/Rendering/细节灯光.json";
        Baseline b;
        if(File.Exists(baselinePath))b=JsonUtility.FromJson<Baseline>(File.ReadAllText(baselinePath));
        else
        {
            b=new Baseline{sun=env.sun.intensity,ambient=env.ambientIntensity,sky=env.ambientSky,equator=env.ambientEquator,ground=env.ambientGround,
                density=region.density,air=atmosphere.intensity.value,shafts=volume.intensity.value,bloom=bloom.intensity.value,exposure=grade.postExposure.value,contrast=grade.contrast.value,
                screen=screen.layers.value.Select(x=>new LayerValue{name=x.name,intensity=x.intensity}).ToArray(),image=image.layers.value.Select(x=>new LayerValue{name=x.name,intensity=x.intensity}).ToArray(),
                emission=mats.Where(x=>x.HasProperty("_EmissionColor")).Select(x=>new EmissionValue{path=AssetDatabase.GetAssetPath(x),color=x.GetColor("_EmissionColor")}).ToArray(),
                lights=lights.Select(x=>new LightValue{name=x.name,power=x.intensity}).ToArray()};
            File.WriteAllText(baselinePath,JsonUtility.ToJson(b,true));
        }
        var l=File.Exists(lookPath)?JsonUtility.FromJson<Look>(File.ReadAllText(lookPath)):Default(name);
        // Keep this stage's original ruin haze as its own art direction.
        if(name=="乐园·墟实"){l.density=1;l.solar=1;l.fog=1;l.air=1;l.sun=1;l.ambient=1;l.localPower=1.5f;}
        if(name=="百鸟溯归"){l.ambient=1.15f;l.sun=.95f;l.exposure=.2f;}
        env.sun.intensity=b.sun*l.sun;env.ambientIntensity=b.ambient*l.ambient;
        // Trilight colors drive lilToon's ambient input directly.
        env.ambientSky=b.sky*l.ambient;env.ambientEquator=b.equator*l.ambient;env.ambientGround=b.ground*Mathf.Max(.8f,l.ambient);env.fog=false;
        region.density=b.density*l.density;region.dust.brightness=4;region.dust.grainDensity=.08f;
        volume.intensity.Override(b.shafts*l.solar);atmosphere.intensity.Override(b.air*l.air);
        bloom.intensity.Override(b.bloom*.6f);bloom.threshold.Override(1.25f);
        grade.postExposure.Override(b.exposure+l.exposure);grade.contrast.Override(b.contrast+l.contrast);
        if(profile.TryGet<FilmGrain>(out var grain))grain.intensity.Override(.008f);
        foreach(var layer in screen.layers.value)
        {
            var old=b.screen.FirstOrDefault(x=>x.name==layer.name);if(old==null)continue;
            if(layer.effect==ScreenProcessEffect.DepthFog)layer.intensity=old.intensity*l.fog;
            else if(layer.effect==ScreenProcessEffect.SkyTyndall)layer.intensity=old.intensity*.35f;
        }
        foreach(var layer in image.layers.value)
        {var old=b.image.FirstOrDefault(x=>x.name==layer.name);if(old!=null&&layer.effect==ImageProcessEffect.Glow)layer.intensity=old.intensity*l.glow;}
        screen.layers.value.RemoveAll(x=>x.effect==ScreenProcessEffect.PostLighting);
        image.layers.value.RemoveAll(x=>x.effect==ImageProcessEffect.CenterColorCorrection);
        foreach(var e in b.emission){var m=AssetDatabase.LoadAssetAtPath<Material>(e.path);if(m==null)continue;m.SetColor("_EmissionColor",e.color*l.emission);EditorUtility.SetDirty(m);}
        if(name=="冷色古堂")foreach(var m in mats.Where(x=>x.name.EndsWith("背景板")||x.name.EndsWith("背景圆前")||x.name.EndsWith("背景圆后")))
        {m.SetFloat("_LightMaxLimit",.85f);m.SetColor("_Color",new Color(.68f,.75f,.87f,1));EditorUtility.SetDirty(m);}
        if(name=="月昔梅"&&HoMmdStageBuilder.ReadRecipe(name).models.Length==1)
        {
            var sky=root.GetComponentsInChildren<MeshRenderer>().Single(x=>x.name=="天空球").sharedMaterial;
            sky.SetFloat("_UseHdr",0);sky.SetColor("_Top",new Color(.012f,.019f,.035f,1));sky.SetColor("_Horizon",new Color(.055f,.075f,.1f,1));sky.SetColor("_SunColor",Color.black);sky.SetFloat("_Stars",0);EditorUtility.SetDirty(sky);
            var floor=mats.First(x=>x.name=="展台地面");floor.SetColor("_Color",new Color(.14f,.16f,.19f,1));floor.SetFloat("_LightMinLimit",.35f);EditorUtility.SetDirty(floor);
        }
        foreach(var source in root.GetComponentsInChildren<HoVolumeLightSource>())Object.DestroyImmediate(source);
        var oldRig=root.transform.Find("09 细节灯光");if(oldRig!=null)Object.DestroyImmediate(oldRig.gameObject);
        var rig=new GameObject("09 细节灯光").transform;rig.SetParent(root.transform,false);
        DetailLights(name,rig,volume.intensity.value);
        CenterFill(name,rig);
        lights=root.GetComponentsInChildren<Light>();
        foreach(var light in lights.Where(x=>x!=null))
        {
            var old=b.lights.FirstOrDefault(x=>x.name==light.name);if(old==null||light==env.sun)continue;
            // Retain the existing low fills. Only trim the overbright panel key.
            light.intensity=old.power*(name=="冷色古堂"&&light.name=="圆屏柔和重点"?.55f:1);
        }
        if(name=="乐园·墟实")
        {
            var window=mats.FirstOrDefault(x=>x.name.EndsWith("玻璃"));
            if(window!=null){window.SetFloat("_UseEmission",1);window.SetColor("_EmissionColor",new Color(2.1f,1.62f,1f,1));window.SetTexture("_EmissionMap",window.GetTexture("_MainTex"));EditorUtility.SetDirty(window);}
            var pane=mats.FirstOrDefault(x=>x.name.EndsWith("窗"));
            if(pane!=null){pane.SetFloat("_LightMinLimit",.4f);EditorUtility.SetDirty(pane);}
        }
        int count=0;
        for(int i=0;i<l.beams.Length;i++)
        {
            var light=lights.FirstOrDefault(x=>x.enabled&&x.name==l.beams[i]&&x.type==LightType.Spot);if(light==null)continue;
            float contribution=i==0?l.beam:l.rim;if(contribution<=0)continue;
            var source=light.gameObject.AddComponent<HoVolumeLightSource>();source.quality=HoVolumeLightQuality.HD;
            source.intensity=contribution/Mathf.Max(.1f,volume.intensity.value);source.stepCount=192;source.shadowResolution=1024;
            source.shadowStrength=1;source.sourceRadius=.8f;source.edgeSoftness=.28f;count++;
            if(i==0&&name!="月昔梅"&&name!="芋泥咩咩"&&name!="圣诞万花镜")Gobo(rig,light,folder);
        }
        if(name=="圣诞万花镜"||name=="冷色古堂")
        {
            var point=name=="圣诞万花镜"?lights.FirstOrDefault(x=>x.name=="中央树脚 · 暖光"):lights.FirstOrDefault(x=>x.name=="左灯笼反弹 1");
            if(point!=null){var source=point.gameObject.AddComponent<HoVolumeLightSource>();source.quality=HoVolumeLightQuality.HD;source.intensity=l.point/Mathf.Max(.1f,volume.intensity.value);source.sourceRadius=1;source.stepCount=96;source.shadowResolution=256;count++;}
        }
        foreach(var source in root.GetComponentsInChildren<HoVolumeLightSource>()){source.intensity*=l.localPower;source.stepCount=256;source.jitter=false;}
        ReviewCameras(root,HoMmdStageBuilder.ReadRecipe(name));
        env.ApplyNow();EditorUtility.SetDirty(profile);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        File.WriteAllText(lookPath,JsonUtility.ToJson(l,true));Debug.Log("MMD_DETAIL_APPLIED "+name+" local="+count+" solar="+volume.intensity.value);
    }
    static void ReviewCameras(GameObject root,HoMmdStageBuilder.Recipe recipe)
    {
        var main=root.GetComponentsInChildren<Camera>().First(x=>x.name=="主相机");
        var old=root.transform.Find("10 观察机位");if(old!=null)Object.DestroyImmediate(old.gameObject);
        var rig=new GameObject("10 观察机位").transform;rig.SetParent(root.transform,false);
        float distance=Vector3.Distance(main.transform.position,recipe.target);
        for(int i=1;i<=2;i++)
        {
            var camera=new GameObject(i==1?"左侧观察相机":"右侧观察相机").AddComponent<Camera>();camera.transform.SetParent(rig,false);
            camera.CopyFrom(main);camera.enabled=false;camera.tag="Untagged";
            float side=Mathf.Min(4,distance*.22f)*(i==1?-1:1);
            camera.transform.position=main.transform.position+main.transform.right*side+main.transform.forward*Mathf.Min(2,distance*.12f);
            camera.transform.LookAt(recipe.target);var data=camera.GetUniversalAdditionalCameraData();EditorUtility.CopySerialized(main.GetUniversalAdditionalCameraData(),data);
        }
    }
    static void CenterFill(string name,Transform rig)
    {
        if(name=="月昔梅"||name=="花吐电気"||name=="百鸟溯归"||name=="乐园·墟实")return;
        // World-space subject pools replace screen-center post lighting.
        var r=HoMmdStageBuilder.ReadRecipe(name);var target=r.target;
        if(name=="古典机械剧场"||name=="古典机械剧场黑蓝白")target=new Vector3(0,4,-18);
        if(name=="冷色古堂")target=new Vector3(0,1,-5);
        var forward=(r.camera-target).normalized;var side=Vector3.Cross(Vector3.up,forward).normalized;
        var tint=Color.Lerp(r.key,Color.white,.45f);
        float power=name=="圣诞万花镜"?45:name=="芋泥咩咩"?18:70;
        Spot(rig,"主体真实补光",target+forward*5+side*3+Vector3.up*4,target,tint,power,72,18,0,1);
    }
    static Light Spot(Transform rig,string name,Vector3 position,Vector3 target,Color tint,float power,float angle,float range,float beam,float global)
    {
        var light=new GameObject(name).AddComponent<Light>();light.transform.SetParent(rig,false);light.transform.position=position;light.transform.LookAt(target);
        light.type=LightType.Spot;light.intensity=power;light.color=tint;light.spotAngle=angle;light.innerSpotAngle=angle*.6f;light.range=range;
        light.shadows=LightShadows.Soft;light.shadowBias=.015f;light.shadowNormalBias=.025f;light.shadowStrength=1;
        if(beam>0){var v=light.gameObject.AddComponent<HoVolumeLightSource>();v.intensity=beam/Mathf.Max(.1f,global);v.stepCount=192;v.shadowResolution=1024;v.sourceRadius=.9f;v.edgeSoftness=.3f;}
        return light;
    }
    static void DetailLights(string name,Transform rig,float global)
    {
        var warm=new Color(1,.71f,.4f);var cool=new Color(.34f,.6f,1);
        if(name=="百鸟溯归")
        {
            Spot(rig,"树体琥珀塑形",new Vector3(-6,7,3),new Vector3(0,4,-7),warm,240,48,32,2,global);
            Spot(rig,"枝梢冷月轮廓",new Vector3(6,9,-12),new Vector3(0,5,-7),cool,140,45,24,1,global);
            Spot(rig,"左书墙远景暖光",new Vector3(-8,13,5),new Vector3(-22,9,-12),new Color(1,.73f,.49f),1000,78,65,0,global);
            Spot(rig,"右书墙远景暖光",new Vector3(8,13,5),new Vector3(22,9,-12),new Color(1,.73f,.49f),1000,78,65,0,global);
            Spot(rig,"远书架中性月光",new Vector3(0,18,-5),new Vector3(0,8,-27),new Color(.64f,.72f,1),1100,88,65,0,global);
        }
        else if(name=="乐园·墟实")
        {
            Spot(rig,"窗前暖色反弹",new Vector3(-7,8,5),new Vector3(0,4,-6),new Color(1,.85f,.64f),240,75,34,0,global);
            Spot(rig,"残骸冷色分离",new Vector3(7,7,-9),new Vector3(0,3,-6),cool,90,52,24,2,global);
        }
        else if(name=="古典机械剧场"||name=="古典机械剧场黑蓝白")
        {
            Spot(rig,"舞台左缘细节",new Vector3(-8,5,-3),new Vector3(-5,2,-15),name=="古典机械剧场"?warm:cool,110,48,26,1,global);
            Spot(rig,"舞台右缘细节",new Vector3(8,4,-4),new Vector3(5,2,-15),name=="古典机械剧场"?new Color(1,.37f,.16f):new Color(.6f,.75f,1),100,48,26,1,global);
        }
        else if(name=="炫光夜厅")
        {
            Spot(rig,"紫色前景擦光",new Vector3(-4,5,10),new Vector3(1,0,2),new Color(.65f,.2f,1),100,38,22,2,global);
            Spot(rig,"蓝色远景擦光",new Vector3(4,5,-7),new Vector3(-1,0,-15),cool,100,38,22,2,global);
        }
        else if(name=="秘月之泉")
            Spot(rig,"泉水近景蓝色擦光",new Vector3(-4,4,4),new Vector3(0,.1f,-3),new Color(.34f,.8f,1),40,55,16,1,global);
        else if(name=="六角花台绣球")
            Spot(rig,"花台近景柔和塑形",new Vector3(3,3,-7),new Vector3(0,.5f,0),new Color(.64f,.76f,1),38,70,16,0,global);
        else if(name=="冷色古堂")
            Spot(rig,"前景地毯月光",new Vector3(4,5,7),new Vector3(-1,.1f,1),new Color(.5f,.69f,1),42,55,18,1,global);
        else if(name=="月昔梅")
        {
            Spot(rig,"梅花暖日主光",new Vector3(6,7,1),new Vector3(0,1.2f,-9),new Color(1,.88f,.6f),180,52,28,2,global);
            Spot(rig,"月洞冷色分离",new Vector3(-6,6,-12),new Vector3(0,1.8f,-7),new Color(.57f,.7f,1),95,55,20,1,global);
            Spot(rig,"花纹与月牙柔光",new Vector3(-2,5,3),new Vector3(0,.5f,-9),new Color(1,.93f,.78f),55,70,23,0,global);
            Spot(rig,"左梅枝低位补光",new Vector3(-4,3,5),new Vector3(-4,1,-6),new Color(1,.9f,.7f),130,72,24,0,global);
            Spot(rig,"右梅枝低位补光",new Vector3(4,3,5),new Vector3(4,1,-6),new Color(1,.9f,.7f),130,72,24,0,global);
        }
    }
    static void Gobo(Transform rig,Light light,string folder)
    {
        var go=new GameObject("斑驳投影 · "+light.name);go.transform.SetParent(rig,false);go.transform.SetPositionAndRotation(light.transform.position+light.transform.forward*2,light.transform.rotation);
        string path=folder+"/Rendering/局部斑驳遮片.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(mesh==null)
        {
            var v=new System.Collections.Generic.List<Vector3>();var t=new System.Collections.Generic.List<int>();var random=new System.Random(537);
            float radius=Mathf.Tan(light.spotAngle*.5f*Mathf.Deg2Rad)*2*.8f;
            for(int i=0;i<36;i++)
            {
                float x=((float)random.NextDouble()-.5f)*radius*2,y=((float)random.NextDouble()-.5f)*radius*2,r=radius*(.035f+.065f*(float)random.NextDouble());int start=v.Count;
                v.Add(new Vector3(x,y,0));for(int j=0;j<9;j++){float a=j*Mathf.PI/4;v.Add(new Vector3(x+Mathf.Cos(a)*r,y+Mathf.Sin(a)*r*.45f,0));}
                for(int j=0;j<8;j++){t.Add(start);t.Add(start+j+1);t.Add(start+j+2);t.Add(start);t.Add(start+j+2);t.Add(start+j+1);}
            }
            mesh=new Mesh{name="局部光叶片遮影"};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);
        }
        string mp=folder+"/Rendering/局部斑驳遮片.mat";var m=AssetDatabase.LoadAssetAtPath<Material>(mp);
        if(m==null){m=new Material(Shader.Find("lilToon")){name="局部斑驳遮片"};m.SetFloat("_Cull",0);AssetDatabase.CreateAsset(m,mp);}m.SetShaderPassEnabled("ShadowCaster",true);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterial=m;mr.shadowCastingMode=ShadowCastingMode.ShadowsOnly;
    }
    [Serializable] sealed class LocalState {public string stage;public int frame,count,view;public Vector3 camera;public bool centerPostLighting;public string[] sources,status;public float[] intensities;}
    public static void Record(Camera camera,int frame)
    {
        if(frame!=32)return;var sources=camera.transform.root.GetComponentsInChildren<HoVolumeLightSource>();
        var profile=camera.transform.root.GetComponentInChildren<Volume>().sharedProfile;profile.TryGet<ScreenProcessStackVolume>(out var screen);profile.TryGet<ImageProcessStackVolume>(out var image);
        var s=new LocalState{stage=camera.gameObject.scene.name,frame=frame,count=sources.Length,view=CurrentView,camera=camera.transform.position,
            centerPostLighting=screen.layers.value.Any(x=>x.enabled&&x.effect==ScreenProcessEffect.PostLighting)||image.layers.value.Any(x=>x.enabled&&x.effect==ImageProcessEffect.CenterColorCorrection),
            sources=sources.Select(x=>x.name).ToArray(),status=sources.Select(x=>x.Status).ToArray(),intensities=sources.Select(x=>x.intensity).ToArray()};
        File.WriteAllText(Path.Combine(HoMmdStageWork.Work,s.stage+"-detail-state.json"),JsonUtility.ToJson(s,true));
        File.WriteAllText(Path.Combine(HoMmdStageWork.Work,s.stage+"-view"+CurrentView+"-detail-state.json"),JsonUtility.ToJson(s,true));
        foreach(var kind in new[]{"advanced","lighting"}){var p=Path.Combine(HoMmdStageWork.Work,s.stage+"-"+kind+"-state.json");if(File.Exists(p))File.Copy(p,Path.Combine(HoMmdStageWork.Work,s.stage+"-view"+CurrentView+"-"+kind+"-state.json"),true);}
    }
}
