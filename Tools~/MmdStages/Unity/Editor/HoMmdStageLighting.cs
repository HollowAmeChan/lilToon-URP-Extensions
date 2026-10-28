using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoMmdStages;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.GTAO;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.PostProcessing;
using lilToon.URP.Extensions.ShadowCast;
using lilToon.URP.Extensions.VolumeLight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Second lighting pass. It never reimports source models or rebuilds model hierarchies.
public static class HoMmdStageLighting
{
    [Serializable] public sealed class Look
    {
        public string stage;
        public Color sun, ambient, shadow, fog;
        public Vector3 sunAngles;
        public float sunPower, exposure, bloom, airDensity, shafts, focus, aperture;
        public float ambientAir = .12f, fogDensity = .005f, fogOpacity = .18f;
        public float existingLocalScale = .25f, dofRadius = 7, dofGain = 1.1f;
        public float frameAspect = 2.39f;
        public bool whiteFrame;
        public float ambientIntensity=.65f, contrast=17, shadowStrength=.92f, emissionScale=1, bloomThreshold=1.1f;
        public float exteriorLightPower, windowScatter, vignette=.22f;
    }
    static Color C(float r, float g, float b) => new Color(r,g,b,1);
    static string Argument(string key)
    {
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);
        return i>=0&&i+1<args.Length?args[i+1]:null;
    }
    public static void RunBatch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            string stage=Argument("-hoStage");
            foreach(var r in HoMmdStageBuilder.Recipes())if(stage==null||r.name==stage)Apply(r.name);
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            AssetDatabase.SaveAssets();EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    public static void RefineAndRenderBatch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            string requested=Argument("-hoStages");
            string[] stages=requested!=null?requested.Split(','):HoMmdStageBuilder.Recipes().Select(x=>x.name).ToArray();
            foreach(string stage in stages)
            {if(!HoMmdStageBuilder.Recipes().Any(x=>x.name==stage))throw new ArgumentException("Unknown stage: "+stage);}
            foreach(string stage in stages)Apply(stage);
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            AssetDatabase.SaveAssets();HoMmdStageBuilder.RenderStagesBatch(stages);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    public static Look Default(string name)
    {
        var v=new Look{stage=name,sun=C(1,.85f,.66f),sunAngles=new Vector3(24,-110,0),sunPower=3.3f,
            ambient=C(.15f,.2f,.26f),shadow=C(.24f,.29f,.38f),fog=C(.5f,.63f,.72f),
            exposure=.05f,bloom=.65f,airDensity=.022f,shafts=22,focus=8,aperture=3.2f};
        switch(name)
        {
            case "花吐电気":
                v.sunAngles=new Vector3(34,-145,0);v.sun=C(1,.96f,.86f);v.sunPower=3.4f;
                v.ambient=C(.38f,.46f,.55f);v.shadow=C(.48f,.52f,.6f);v.ambientIntensity=1;
                v.focus=7.5f;v.aperture=3.5f;v.shafts=18;v.airDensity=.018f;v.dofRadius=6;
                v.exposure=.4f;v.bloom=.45f;v.bloomThreshold=1;v.contrast=6;v.shadowStrength=.65f;v.existingLocalScale=.85f;v.emissionScale=.6f;v.whiteFrame=true;
                v.exteriorLightPower=360;v.windowScatter=.18f;v.vignette=.08f;v.ambientAir=.18f;v.fogDensity=.01f;v.fogOpacity=.22f;break;
            case "乐园·深命": v.sunAngles=new Vector3(35,-125,0);v.sun=C(1,.95f,.84f);v.sunPower=4.2f;v.focus=18;v.aperture=4;v.shafts=12;v.airDensity=.012f;v.exposure=.1f;v.dofRadius=6;v.whiteFrame=true;break;
            case "茉莉庭院": v.sunAngles=new Vector3(28,35,0);v.sun=C(1,.91f,.69f);v.sunPower=2.2f;v.focus=12;v.aperture=3.4f;v.shafts=5;v.airDensity=.009f;v.fog=C(.53f,.75f,.72f);v.bloom=.35f;v.exposure=-.1f;v.ambientAir=.1f;v.whiteFrame=true;break;
            case "圣诞万花镜": v.sunAngles=new Vector3(22,125,0);v.sun=C(.48f,.67f,1);v.sunPower=1.5f;v.ambient=C(.055f,.075f,.13f);v.focus=34;v.aperture=2.4f;v.shafts=6;v.airDensity=.01f;v.bloom=.22f;v.fog=C(.2f,.31f,.49f);v.exposure=-.1f;v.emissionScale=.4f;v.bloomThreshold=1.3f;v.contrast=9;v.vignette=.12f;break;
            case "琉璃万花镜": v.sunAngles=new Vector3(25,-125,0);v.sun=C(.5f,.75f,1);v.sunPower=2.3f;v.ambient=C(.045f,.07f,.14f);v.focus=23;v.aperture=2.8f;v.shafts=7;v.airDensity=.01f;v.bloom=.4f;v.exposure=-.05f;v.fog=C(.19f,.35f,.57f);break;
            case "六角花台绣球": v.sunAngles=new Vector3(46,145,0);v.sun=C(.78f,.84f,1);v.sunPower=1.2f;v.ambient=C(.035f,.035f,.09f);v.shadow=C(.17f,.16f,.3f);v.focus=11;v.aperture=2.8f;v.shafts=4;v.airDensity=.01f;v.bloom=.6f;v.fog=C(.23f,.19f,.48f);v.exposure=0;break;
            case "古典机械剧场": v.sunAngles=new Vector3(32,155,0);v.sun=C(.65f,.73f,1);v.sunPower=.9f;v.ambient=C(.07f,.05f,.085f);v.shadow=C(.25f,.18f,.28f);v.focus=24;v.aperture=3.2f;v.shafts=6;v.airDensity=.009f;v.bloom=.45f;v.fog=C(.35f,.2f,.19f);v.exposure=.15f;break;
            case "拟花十字之棺": v.sunAngles=new Vector3(34,-135,0);v.sun=C(.72f,.78f,1);v.sunPower=1.5f;v.ambient=C(.045f,.04f,.105f);v.shadow=C(.2f,.16f,.32f);v.focus=13;v.aperture=2.8f;v.shafts=6;v.airDensity=.009f;v.bloom=.55f;v.fog=C(.24f,.23f,.49f);v.exposure=.1f;break;
        }
        return v;
    }
    public static void Apply(string name)
    {
        string folder=HoMmdStageBuilder.Root+name;
        string path=folder+"/Rendering/光影精调.json";
        Look look=File.Exists(path)?JsonUtility.FromJson<Look>(File.ReadAllText(path)):Default(name);
        var scene=EditorSceneManager.OpenScene(folder+"/"+name+".unity",OpenSceneMode.Single);
        var root=scene.GetRootGameObjects().Single(x=>x.GetComponentInChildren<HoMmdStageEnvironment>()!=null);
        var env=root.GetComponentInChildren<HoMmdStageEnvironment>();
        var camera=root.GetComponentInChildren<Camera>();
        var profile=root.GetComponentInChildren<Volume>().sharedProfile;
        var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(folder+"/Rendering/Ho Renderer.asset");
        env.ambientSky=look.ambient;env.ambientEquator=look.ambient*.55f;env.ambientGround=look.ambient*.25f;
        env.ambientIntensity=look.ambientIntensity;env.sun.color=look.sun;env.sun.intensity=look.sunPower;
        env.sun.transform.rotation=Quaternion.Euler(look.sunAngles);env.sun.shadowStrength=1;
        env.sun.shadowBias=.035f;env.sun.shadowNormalBias=.12f;env.sun.shadows=LightShadows.Soft;
        foreach(var light in root.GetComponentsInChildren<Light>())
        {
            if(light==env.sun||light.transform.parent.name=="06 摄影灯光")continue;
            // Read old powers from an immutable first-pass record, so repeat runs are stable.
            string lightPath=folder+"/Rendering/原灯光记录.json";
            if(!File.Exists(lightPath))WriteOriginalLights(root,lightPath);
            var old=JsonUtility.FromJson<LightRecord>(File.ReadAllText(lightPath)).lights.FirstOrDefault(x=>x.name==light.name);
            if(old!=null)light.intensity=old.power*look.existingLocalScale;
            if(light.name=="环境补光")light.intensity=.055f;
            if(light.name=="舞台重点光"||light.name=="色彩轮廓光")light.enabled=false;
            if(name=="拟花十字之棺"&&light.name.StartsWith("蓝玫瑰环光"))light.intensity=14;
            if(name=="拟花十字之棺"&&light.name=="十字架高光")light.intensity=45;
        }
        foreach(var surface in root.GetComponentsInChildren<MeshRenderer>())
        {
            var m=surface.sharedMaterial;if(m==null)continue;
            F(m,"_AsUnlit",0);F(m,"_LightMinLimit",.012f);F(m,"_LightMaxLimit",8);
            F(m,"_MultiLightIntensity",1);F(m,"_ShadowStrength",look.shadowStrength);F(m,"_ShadowReceive",1);
            F(m,"_ShadowBlur",.38f);F(m,"_ShadowEnvStrength",.12f);F(m,"_ShadowBorder",.5f);
            if(m.HasProperty("_ShadowColor"))m.SetColor("_ShadowColor",look.shadow);
            string n=SurfaceName(m);
            if(name=="花吐电気"&&new[]{"樹幹","圍欄","石","草","灌木","樹葉"}.Contains(n))
            {
                // Keep the three exterior lights off the indoor surfaces, while ordinary
                // scene lights continue to illuminate both groups through layer 1.
                surface.renderingLayerMask|=2u;
                F(m,"_ShadowStrength",.45f);F(m,"_ShadowEnvStrength",.35f);
                if(m.HasProperty("_ShadowColor"))m.SetColor("_ShadowColor",C(.65f,.71f,.64f));
            }
            if(name=="六角花台绣球"&&n.Contains("花")&&n!="花纹")m.SetColor("_Color",C(.9f,.85f,1));
            if(name=="古典机械剧场"&&(n.Contains("齿轮")||n.Contains("鐘")||n.Contains("圆环")||n.Contains("圓圈")))
            {F(m,"_UseReflection",1);F(m,"_ApplySpecular",1);F(m,"_ApplySpecularFA",1);F(m,"_SpecularToon",0);F(m,"_ApplyReflection",0);F(m,"_Smoothness",.7f);F(m,"_Metallic",.25f);m.SetColor("_ReflectionColor",C(.75f,.52f,.15f));}
            surface.shadowCastingMode=ShadowCastingMode.On;surface.receiveShadows=true;
            m.SetShaderPassEnabled("ShadowCaster",true);
            if(n.Contains("燈膽")||n.Contains("灯胆")||n.Contains("金灯")||n.Contains("银灯")||n.Contains("红灯"))
            {
                Color c=n.Contains("银")?C(.52f,.75f,1):name=="琉璃万花镜"?C(.35f,.65f,1):C(1,.57f,.22f);
                m.SetColor("_EmissionColor",c*(name=="琉璃万花镜"?1.7f:name=="花吐电気"?2.2f:2.5f)*look.emissionScale);
                if(name=="圣诞万花镜"){F(m,"_LightMaxLimit",1);var tint=m.GetColor("_Color");m.SetColor("_Color",new Color(.68f,.68f,.68f,tint.a));}
            }
            EditorUtility.SetDirty(m);
        }
        var oldRig=root.transform.Find("06 摄影灯光");if(oldRig!=null)Object.DestroyImmediate(oldRig.gameObject);
        var rig=new GameObject("06 摄影灯光").transform;rig.SetParent(root.transform,false);
        AddLighting(name,rig,look,root);
        SkySphere(root,env,folder,name);
        T Component<T>() where T:VolumeComponent
        {
            if(profile.TryGet<T>(out var value)){value.active=true;return value;}
            value=profile.Add<T>(true);value.name=typeof(T).Name;AssetDatabase.AddObjectToAsset(value,profile);return value;
        }
        var bloom=Component<Bloom>();bloom.intensity.Override(look.bloom);bloom.threshold.Override(look.bloomThreshold);bloom.scatter.Override(.76f);bloom.highQualityFiltering.Override(true);
        var grade=Component<ColorAdjustments>();grade.postExposure.Override(look.exposure);grade.contrast.Override(look.contrast);grade.saturation.Override(-5);grade.colorFilter.Override(Color.white);
        var split=Component<SplitToning>();split.shadows.Override(C(.45f,.48f,.55f));split.highlights.Override(C(.55f,.515f,.46f));split.balance.Override(8);
        var vignette=Component<Vignette>();vignette.intensity.Override(look.vignette);vignette.smoothness.Override(.68f);
        var grain=Component<FilmGrain>();grain.type.Override(FilmGrainLookup.Thin1);grain.intensity.Override(.045f);grain.response.Override(.85f);
        var chromatic=Component<ChromaticAberration>();chromatic.intensity.Override(.025f);
        var ao=Component<HoGTAOVolume>();ao.worldSpaceRadius.Override(.7f);ao.debugIntensity.Override(1.6f);ao.quality.Override(HoGTAOQuality.High);
        var atmosphere=Component<HoAtmosphereVolume>();atmosphere.intensity.Override(look.ambientAir);
        var vl=Component<HoVolumeLightVolume>();vl.enable.Override(true);vl.intensity.Override(look.shafts);vl.stepCount.Override(96);
        vl.maxDistance.Override(70);vl.antialiasing.Override(HoVolumeLightAntialiasing.Temporal);vl.anisotropy.Override(.36f);
        var region=root.GetComponentInChildren<HoAtmosphereRegion>();region.density=look.airDensity;region.pathExtinction=true;region.volumeLightWeight=1;
        region.tint=Color.Lerp(Color.white,look.sun,.25f);region.anisotropy=.36f;region.noiseStrength=.6f;region.noiseWorldSize=8;region.wind=new Vector3(.035f,0,.01f);
        region.densityTexture=DensityTexture(folder);region.dust.enabled=true;region.dust.grainDensity=.16f;
        region.dust.brightness=9;region.dust.clusterSize=5;region.dust.maxDistance=24;region.dust.maxClusters=48;region.dust.thickness=.75f;
        var screen=Component<ScreenProcessStackVolume>();screen.Enable.Override(true);screen.layers.overrideState=true;
        screen.layers.value=new List<ScreenProcessLayer>{
            new ScreenProcessLayer{name="远景空气透视",effect=ScreenProcessEffect.DepthFog,blendMode=ScreenProcessBlendMode.Normal,color=look.fog,
                parameters0=new Vector4(1,1,4,100),parameters1=new Vector4(look.fogDensity,look.fogOpacity,.65f,.15f),
                parameters2=new Vector4(look.fog.r*.7f,look.fog.g*.7f,look.fog.b,0),parameters5=new Vector4(0,0,0,.6f)},
            new ScreenProcessLayer{name="主机位 Bokeh 景深",effect=ScreenProcessEffect.DepthOfField,blendMode=ScreenProcessBlendMode.Normal,
                parameters0=new Vector4(1,look.focus,50,look.aperture),parameters1=new Vector4(10,30,look.dofRadius,1),
                parameters2=new Vector4(7,1,15,0),parameters3=new Vector4(look.dofGain,1.3f,.8f,1)}
        };
        if(name=="花吐电気"&&look.windowScatter>0)
        {
            screen.layers.value.Insert(0,new ScreenProcessLayer{name="晴日窗景 · 天空散射",effect=ScreenProcessEffect.SkyTyndall,
                blendMode=ScreenProcessBlendMode.Screen,color=C(1,.97f,.88f),intensity=look.windowScatter,
                parameters0=new Vector4(.56f,1.1f,.8f,1.2f),parameters1=new Vector4(.35f,.62f,.89f,2),
                parameters2=new Vector4(.2f,.12f,1.2f,.7f),parameters3=new Vector4(.55f,0,1,1),
                parameters4=new Vector4(-.6f,.8f,126,1),parameters5=new Vector4(.035f,2,0,.8f)});
            var gb=renderer.rendererFeatures.OfType<HoGeometryBufferRendererFeature>().First();
            gb.Settings.enableSkyBuffer=true;
            // The background sphere must not occlude the sky signal captured through the windows.
            gb.Settings.minRenderQueue=1500;EditorUtility.SetDirty(gb);
        }
        var image=Component<ImageProcessStackVolume>();image.Enable.Override(true);image.layers.overrideState=true;
        image.layers.value=new List<ImageProcessLayer>{new ImageProcessLayer{name="灯饰柔光与轻微星芒",effect=ImageProcessEffect.Glow,
            intensity=.22f,blendMode=ImageProcessBlendMode.Add,parameters0=new Vector4(.8f,.1f,2.5f,2),parameters1=new Vector4(.2f,0,0,.8f),parameters2=new Vector4(4,25,0,0)}};
        AddBranding(image,folder,look);
        EnsureFeature<ScreenProcessRendererFeature>(renderer,"Ho 景深与空气透视").UseVolumes=true;
        EnsureFeature<ImageProcessRendererFeature>(renderer,"Ho 镜头柔光").UseVolumes=true;
        var shadow=renderer.rendererFeatures.OfType<HoShadowCastRendererFeature>().FirstOrDefault();
        if(shadow!=null){shadow.Settings.shadowStrength=1;shadow.Settings.punctualShadowStrength=.85f;shadow.Settings.pcssEnabled=true;shadow.Settings.pcssMaxPenumbraRadius=5;EditorUtility.SetDirty(shadow);}
        if(shadow!=null&&name=="圣诞万花镜"){shadow.Settings.lightCapacity=HoShadowCastLightCapacity.Medium;EditorUtility.SetDirty(shadow);}
        var lighting=renderer.rendererFeatures.OfType<HoVolumeLightRendererFeature>().First();
        lighting.FeatureSettings.stepCount=96;lighting.FeatureSettings.antialiasing=HoVolumeLightAntialiasing.Temporal;EditorUtility.SetDirty(lighting);
        var pipeline=new SerializedObject(env.pipeline);
        pipeline.FindProperty("m_MainLightShadowmapResolution").intValue=4096;
        pipeline.FindProperty("m_ShadowDistance").floatValue=name=="花吐电気"?35:90;
        pipeline.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(env.pipeline);
        camera.allowHDR=true;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        env.ApplyNow();EditorUtility.SetDirty(profile);EditorUtility.SetDirty(renderer);
        EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        File.WriteAllText(path,JsonUtility.ToJson(look,true));
        Debug.Log("MMD_LIGHTING_REFINED "+name);
    }
    [Serializable] sealed class LightValue{public string name;public float power;}
    [Serializable] sealed class LightRecord{public LightValue[] lights;}
    static void WriteOriginalLights(GameObject root,string path)=>File.WriteAllText(path,JsonUtility.ToJson(new LightRecord{
        lights=root.GetComponentsInChildren<Light>().Where(x=>x.transform.parent.name!="06 摄影灯光").Select(x=>new LightValue{name=x.name,power=x.intensity}).ToArray()},true));
    static string SurfaceName(Material m){int p=m.name.IndexOf('_');return p>0&&m.name.Substring(0,p).All(char.IsDigit)?m.name.Substring(p+1):m.name;}
    static void F(Material m,string n,float v){if(m.HasProperty(n))m.SetFloat(n,v);}
    static T EnsureFeature<T>(UniversalRendererData data,string name)where T:ScriptableRendererFeature
    {
        var value=data.rendererFeatures.OfType<T>().FirstOrDefault();
        if(value==null){value=ScriptableObject.CreateInstance<T>();value.name=name;AssetDatabase.AddObjectToAsset(value,data);data.rendererFeatures.Add(value);value.Create();}
        value.SetActive(true);EditorUtility.SetDirty(value);return value;
    }
    static Texture3D DensityTexture(string folder)
    {
        string path=folder+"/Rendering/空气密度.asset";var existing=AssetDatabase.LoadAssetAtPath<Texture3D>(path);if(existing!=null)return existing;
        const int n=32;var texture=new Texture3D(n,n,n,TextureFormat.RGBA32,false){name="空气密度",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};
        var colors=new Color[n*n*n];
        for(int z=0;z<n;z++)for(int y=0;y<n;y++)for(int x=0;x<n;x++)
        {
            float u=x/(float)n,v=y/(float)n,w=z/(float)n;
            float value=.3f+.7f*(Mathf.PerlinNoise(u*3+w,v*3)+Mathf.PerlinNoise(v*3+w,u*3))*.5f;
            colors[x+n*(y+n*z)]=new Color(value,value,value,1);
        }
        texture.SetPixels(colors);texture.Apply(false,true);AssetDatabase.CreateAsset(texture,path);return texture;
    }
    static Light Spot(Transform rig,string name,Vector3 position,Vector3 target,Color tint,float intensity,float angle,float range)
    {
        var light=new GameObject(name).AddComponent<Light>();light.transform.SetParent(rig,false);light.transform.position=position;light.transform.LookAt(target);
        light.type=LightType.Spot;light.color=tint;light.intensity=intensity;light.range=range;light.spotAngle=angle;light.innerSpotAngle=angle*.55f;
        light.shadows=LightShadows.Soft;light.shadowStrength=.9f;light.shadowBias=.035f;light.shadowNormalBias=.08f;light.shadowResolution=LightShadowResolution.High;
        return light;
    }
    static void Exterior(Light light)
    {
        light.cullingMask=1;
        var data=light.GetUniversalAdditionalLightData();data.renderingLayers=(RenderingLayerMask)2u;
        data.customShadowLayers=true;data.shadowRenderingLayers=(RenderingLayerMask)uint.MaxValue;
    }
    static void Point(Transform rig,string name,Vector3 position,Color tint,float intensity,float range,bool shadows=true)
    {
        var light=new GameObject(name).AddComponent<Light>();light.transform.SetParent(rig,false);light.transform.position=position;
        light.type=LightType.Point;light.color=tint;light.intensity=intensity;light.range=range;
        light.shadows=shadows?LightShadows.Soft:LightShadows.None;light.shadowStrength=.8f;
        light.shadowBias=.025f;light.shadowNormalBias=.06f;light.shadowResolution=LightShadowResolution.Medium;
    }
    static void AddLighting(string name,Transform rig,Look look,GameObject root)
    {
        switch(name)
        {
            case "花吐电気":
                Spot(rig,"明媚窗光",new Vector3(6,5,-4),new Vector3(-2,.4f,0),C(1,.96f,.86f),90,58,20);
                Spot(rig,"室内天光反弹",new Vector3(0,2.8f,3),new Vector3(0,1,-4),C(.87f,.94f,1),35,100,14);
                var table=root.GetComponentsInChildren<MeshRenderer>().First(x=>SurfaceName(x.sharedMaterial)=="桌子").bounds.center;
                Spot(rig,"桌椅柔光",table+new Vector3(-1,2,-1),table,C(1,.94f,.8f),16,65,7);
                Exterior(Spot(rig,"窗外晴日 · 后庭",new Vector3(0,9,-17),new Vector3(0,1,-11),C(1,.98f,.9f),look.exteriorLightPower,100,30));
                Exterior(Spot(rig,"窗外晴日 · 左庭",new Vector3(-11,8,-3),new Vector3(-6,1,-3),C(.9f,.96f,1),look.exteriorLightPower,100,26));
                Exterior(Spot(rig,"窗外晴日 · 右庭",new Vector3(11,8,-3),new Vector3(6,1,-3),C(1,.98f,.9f),look.exteriorLightPower,100,26));break;
            case "乐园·深命":
                Spot(rig,"天窗纵深主光",new Vector3(5,15,-15),new Vector3(-4,0,-3),C(1,.86f,.56f),360,43,35);
                Spot(rig,"青色藤蔓轮廓",new Vector3(-10,8,-8),new Vector3(0,4,-2),C(.28f,.65f,1),160,55,25);break;
            case "茉莉庭院":
                Spot(rig,"庭院天窗暖光",new Vector3(4,8,-1),new Vector3(-2,0,1),C(1,.92f,.78f),70,50,20);
                Spot(rig,"长椅冷色勾边",new Vector3(-5,4,5),new Vector3(0,1,1),C(.38f,.83f,1),55,55,15);break;
            case "圣诞万花镜":
                var tree=root.GetComponentsInChildren<MeshRenderer>().First(x=>SurfaceName(x.sharedMaterial)=="树").bounds;
                Spot(rig,"圣诞树金色主光",tree.center+new Vector3(-4,4,4),tree.center,C(1,.82f,.55f),70,65,18);
                Spot(rig,"红绿彩窗冷侧光",new Vector3(-8,8,-13),new Vector3(0,2,-4),C(.23f,.7f,.85f),100,42,25);
                for(int i=0;i<5;i++)
                {
                    float z=13-i*7;
                    Point(rig,"地面暖光池 "+(i+1),new Vector3(-2.4f,1.4f,z),C(1,.8f,.55f),8,9);
                    Point(rig,"地面冷光池 "+(i+1),new Vector3(2.4f,1.6f,z-2),C(.5f,.72f,1),6,9);
                }
                Point(rig,"中央树脚 · 暖光",new Vector3(tree.center.x-1.7f,1.5f,tree.center.z+1),C(1,.77f,.39f),8,6);
                Point(rig,"中央树冠 · 柔光",tree.center+new Vector3(-1.5f,0,1.5f),C(1,.9f,.65f),5,5);
                Point(rig,"礼物中心 · 小暖光",new Vector3(0,1.3f,11),C(1,.88f,.65f),5,6);break;
            case "琉璃万花镜":
                Spot(rig,"左彩窗冰蓝光",new Vector3(-8,10,-7),new Vector3(2,0,-3),C(.35f,.64f,1),240,42,28);
                Spot(rig,"长廊月光",new Vector3(6,9,-18),new Vector3(-2,1,-10),C(.55f,.85f,1),260,48,28);break;
            case "六角花台绣球":
                Spot(rig,"舞台中央月光",new Vector3(-4,9,1),new Vector3(0,.4f,0),C(.85f,.88f,1),65,42,18);
                Spot(rig,"绣球洋红轮廓",new Vector3(4,4,-5),new Vector3(0,1,0),C(.77f,.25f,1),45,55,16);break;
            case "古典机械剧场":
                Spot(rig,"钟表金色追光",new Vector3(4,13,-10),new Vector3(0,6,-21),C(1,.82f,.5f),240,42,35);
                Spot(rig,"舞台正面柔光",new Vector3(0,9,3),new Vector3(0,5,-19),C(1,.72f,.5f),160,75,40);
                Spot(rig,"前景花饰暖光",new Vector3(0,6,-5),new Vector3(0,2,-14),C(1,.66f,.44f),140,90,25);
                Spot(rig,"舞台冷光分离",new Vector3(-10,10,-9),new Vector3(1,2,-15),C(.25f,.42f,1),220,46,30);
                Spot(rig,"右幕琥珀光",new Vector3(10,3,-11),new Vector3(9,8,-20),C(1,.25f,.06f),160,40,22);break;
            case "拟花十字之棺":
                Spot(rig,"十字架月光",new Vector3(-6,18,-3),new Vector3(0,5,0),C(.44f,.6f,1),380,46,32);
                Spot(rig,"玫瑰紫红逆光",new Vector3(6,8,-9),new Vector3(0,3,2),C(.8f,.22f,.62f),160,50,25);
                Spot(rig,"棺与玫瑰柔光",new Vector3(5,6,4),new Vector3(-1,3,0),C(.8f,.65f,1),80,50,18);break;
        }
    }
    static void SkySphere(GameObject root,HoMmdStageEnvironment env,string folder,string name)
    {
        bool day=name=="花吐电気"||name=="乐园·深命"||name=="茉莉庭院";
        string path=folder+"/Rendering/天空球.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(Shader.Find("Ho/MMD Stage Sky Sphere"));AssetDatabase.CreateAsset(material,path);}
        material.SetColor("_Top",day?C(.18f,.39f,.65f):C(.001f,.0015f,.008f));
        material.SetColor("_Horizon",day?C(.77f,.84f,.9f):C(.008f,.007f,.025f));
        material.SetColor("_SunColor",day?C(4,3.1f,1.9f):C(.9f,1.2f,1.8f));
        material.SetVector("_SunDirection",-env.sun.transform.forward);material.SetFloat("_Stars",day?0:1);
        material.SetFloat("_SunSize",day?.006f:.012f);
        if(name=="花吐电気")
        {material.SetColor("_Top",C(.6f,1.05f,1.8f));material.SetColor("_Horizon",C(2.1f,2.3f,2.5f));}
        env.skybox.SetColor("_Top",material.GetColor("_Top"));
        env.skybox.SetColor("_Bottom",material.GetColor("_Horizon"));EditorUtility.SetDirty(env.skybox);
        var old=root.transform.Find("04 环境与后处理/天空球");if(old!=null)Object.DestroyImmediate(old.gameObject);
        var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere.name="天空球";
        sphere.transform.SetParent(env.transform,false);sphere.transform.position=new Vector3(0,10,0);sphere.transform.localScale=Vector3.one*280;
        Object.DestroyImmediate(sphere.GetComponent<Collider>());
        sphere.GetComponent<MeshRenderer>().sharedMaterial=material;
        // The enclosing sky is background radiance; model casters are enabled above.
        sphere.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
        sphere.GetComponent<MeshRenderer>().receiveShadows=false;
        sphere.GetComponent<MeshRenderer>().lightProbeUsage=LightProbeUsage.Off;
        sphere.GetComponent<MeshRenderer>().reflectionProbeUsage=ReflectionProbeUsage.Off;
        EditorUtility.SetDirty(material);
    }
    static void AddBranding(ImageProcessStackVolume image,string folder,Look look)
    {
        string path=folder+"/Rendering/Hollow Logo.png";
        if(!File.Exists(path))throw new FileNotFoundException("Copy the user-selected Hollow logo into this scene's Rendering folder.",path);
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if(texture==null){AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);}
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        if(!importer.alphaIsTransparency||importer.mipmapEnabled||importer.maxTextureSize!=4096||importer.textureCompression!=TextureImporterCompression.Uncompressed||importer.npotScale!=TextureImporterNPOTScale.None)
        {importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.maxTextureSize=4096;importer.npotScale=TextureImporterNPOTScale.None;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);}
        image.layers.value.Add(new ImageProcessLayer{name="电影画幅 · "+(look.whiteFrame?"白边":"黑边"),effect=ImageProcessEffect.CinematicBars,
            color=look.whiteFrame?Color.white:Color.black,parameters0=new Vector4(look.frameAspect,0,0,0)});
        image.layers.value.Add(new ImageProcessLayer{name="Hollow · 底部正中",effect=ImageProcessEffect.LogoOverlay,
            color=look.whiteFrame?Color.black:Color.white,logoTexture0=texture,parameters0=new Vector4(.5f,.08f,.15f,1),
            parameters8=new Vector4(0,1,2,3),parameters9=new Vector4(4,5,6,7),parameters10=Vector4.one,parameters11=Vector4.one,
            parameters12=new Vector4(-12347,0,0,0)});
    }
    [Serializable] sealed class CaptureState
    {
        public string stage,volumeStatus,screenReason,imageReason;
        public int frame,screenLayers,imageLayers,dustClusters;
        public bool screenReady,imageReady,skyBufferReady;
        public int lowPointLights;
        public float shafts,airDensity,focus,bloom,exposure;
    }
    public static void RecordRenderState(Camera camera,string scenePath,int frame)
    {
        var screen=ScreenProcessRuntimeDiagnostics.CurrentSnapshot;
        var image=ImageProcessRuntimeDiagnostics.CurrentSnapshot;
        string name=Path.GetFileNameWithoutExtension(scenePath);
        var data=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(HoMmdStageBuilder.Root+name+"/Rendering/Ho Renderer.asset");
        var feature=data.rendererFeatures.OfType<HoVolumeLightRendererFeature>().First();
        var stack=VolumeManager.instance.stack;
        var dof=stack.GetComponent<ScreenProcessStackVolume>().layers.value.FirstOrDefault(x=>x.effect==ScreenProcessEffect.DepthOfField);
        var state=new CaptureState{stage=name,frame=frame,volumeStatus=feature.Status,dustClusters=feature.DustClusterCount,
            screenReady=screen.CameraName==camera.name&&screen.Ready,imageReady=image.CameraName==camera.name&&image.Ready,
            screenLayers=screen.WrittenLayerCount,imageLayers=image.WrittenLayerCount,screenReason=screen.Reason,imageReason=image.Reason,
            skyBufferReady=screen.SkyTextureAvailable,
            lowPointLights=camera.transform.root.GetComponentsInChildren<Light>().Count(x=>x.enabled&&x.type==LightType.Point&&x.transform.parent.name=="06 摄影灯光"&&x.transform.position.y<2),
            shafts=stack.GetComponent<HoVolumeLightVolume>().intensity.value,focus=dof!=null?dof.parameters0.y:0,
            bloom=stack.GetComponent<Bloom>().intensity.value,exposure=stack.GetComponent<ColorAdjustments>().postExposure.value,
            airDensity=camera.GetComponentInParent<Transform>().root.GetComponentInChildren<HoAtmosphereRegion>().density};
        File.WriteAllText(Path.Combine(HoMmdStageWork.Work,name+"-lighting-state.json"),JsonUtility.ToJson(state,true));
    }
}
