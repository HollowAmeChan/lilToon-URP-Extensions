using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.HoMmdStages;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.PlanarReflection;
using lilToon.URP.Extensions.PostProcessing;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.ShadowCast;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Additive look development on the saved lighting; never reruns the lighting recipe.
public static class HoMmdStageAdvanced
{
    const string Prefix="进阶 · ";
    [Serializable] public sealed class Look
    {
        public string stage,gi="RTGI";
        public float giStrength=.35f,grade=.22f,heightFog=.12f,postLight=.16f,glow=.25f,flare=.05f;
        public float reflection=.55f;
        public Color shadow,mid,highlight,air;
        public Vector2 flareSource=new Vector2(-.55f,.35f);
        public bool denoise=true;
    }
    static Color C(float r,float g,float b)=>new Color(r,g,b,1);
    static void F(Material m,string key,float value){if(m!=null&&m.HasProperty(key))m.SetFloat(key,value);}
    static string Arg(string key){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:null;}
    public static Look Default(string name)
    {
        var l=new Look{stage=name,shadow=C(.025f,.045f,.075f),mid=C(.4f,.48f,.55f),highlight=C(1,.93f,.8f),air=C(.21f,.32f,.43f)};
        switch(name)
        {
            case "花吐电気":l.giStrength=.12f;l.grade=.12f;l.heightFog=0;l.postLight=.07f;l.glow=.18f;l.flare=.045f;l.shadow=C(.08f,.15f,.17f);l.mid=C(.66f,.68f,.55f);l.highlight=C(1,.99f,.91f);l.flareSource=new Vector2(-.72f,.22f);break;
            case "乐园·深命":l.giStrength=.55f;l.grade=.3f;l.heightFog=.23f;l.postLight=.2f;l.glow=.25f;l.flare=.065f;l.shadow=C(.025f,.09f,.1f);l.mid=C(.4f,.55f,.4f);l.air=C(.35f,.52f,.52f);break;
            case "圣诞万花镜":l.giStrength=.2f;l.grade=.18f;l.heightFog=.06f;l.postLight=.1f;l.glow=.22f;l.flare=.025f;l.shadow=C(.025f,.045f,.09f);l.mid=C(.48f,.38f,.37f);l.highlight=C(1,.93f,.7f);break;
            case "茉莉庭院":l.giStrength=.38f;l.grade=.23f;l.heightFog=.08f;l.postLight=.14f;l.glow=.22f;l.flare=.045f;l.shadow=C(.07f,.16f,.17f);l.mid=C(.48f,.65f,.59f);l.highlight=C(1,1,.88f);l.air=C(.43f,.65f,.6f);break;
            case "六角花台绣球":l.giStrength=.5f;l.grade=.33f;l.heightFog=.28f;l.postLight=.25f;l.glow=.42f;l.flare=.025f;l.shadow=C(.025f,.02f,.075f);l.mid=C(.37f,.31f,.64f);l.highlight=C(.89f,.96f,1);l.air=C(.2f,.15f,.4f);l.reflection=.75f;break;
            case "琉璃万花镜":l.giStrength=.26f;l.grade=.22f;l.heightFog=.14f;l.postLight=.16f;l.glow=.25f;l.flare=.04f;l.shadow=C(.015f,.03f,.09f);l.mid=C(.22f,.42f,.67f);l.highlight=C(.78f,.96f,1);l.air=C(.13f,.29f,.48f);break;
            case "古典机械剧场":l.gi="RTGI";l.giStrength=2;l.denoise=true;l.grade=.32f;l.heightFog=.18f;l.postLight=.28f;l.glow=.3f;l.flare=0;l.shadow=C(.035f,.02f,.045f);l.mid=C(.51f,.29f,.19f);l.highlight=C(1,.85f,.53f);l.air=C(.27f,.13f,.15f);break;
            case "拟花十字之棺":l.giStrength=.42f;l.grade=.34f;l.heightFog=.25f;l.postLight=.24f;l.glow=.36f;l.flare=.025f;l.shadow=C(.02f,.015f,.065f);l.mid=C(.3f,.25f,.52f);l.highlight=C(.93f,.86f,1);l.air=C(.23f,.16f,.39f);break;
        }
        l.flare=name=="花吐电気"||name=="乐园·深命"||name=="茉莉庭院"?.015f:0;
        if(name=="拟花十字之棺"){l.gi="RTGI";l.giStrength=.8f;l.denoise=true;}
        if(name=="乐园·深命")l.giStrength=.6f;
        if(name=="六角花台绣球")l.giStrength=.8f;
        if(name=="花吐电気")l.giStrength=.2f;
        if(name=="圣诞万花镜"||name=="琉璃万花镜")l.giStrength=.35f;
        if(name=="茉莉庭院")l.giStrength=.45f;
        return l;
    }
    public static void ApplyAndRenderBatch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            string list=Arg("-hoStages");var names=list!=null?list.Split(','):HoMmdStageBuilder.Recipes().Select(x=>x.name).ToArray();
            foreach(var name in names){if(!HoMmdStageBuilder.Recipes().Any(x=>x.name==name))throw new ArgumentException(name);Apply(name);}
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            AssetDatabase.SaveAssets();HoMmdStageBuilder.RenderStagesBatch(names);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    public static void RenderSelectedBatch()
    {
        string list=Arg("-hoStages");
        HoMmdStageBuilder.RenderStagesBatch(list!=null?list.Split(','):HoMmdStageBuilder.Recipes().Select(x=>x.name));
    }
    static T Volume<T>(VolumeProfile p)where T:VolumeComponent
    {
        if(p.TryGet<T>(out var value)){value.active=true;return value;}
        value=p.Add<T>(true);value.name=typeof(T).Name;AssetDatabase.AddObjectToAsset(value,p);return value;
    }
    static T Feature<T>(UniversalRendererData d,string name)where T:ScriptableRendererFeature
    {
        var f=d.rendererFeatures.OfType<T>().FirstOrDefault();
        if(f==null){f=ScriptableObject.CreateInstance<T>();f.name=name;AssetDatabase.AddObjectToAsset(f,d);d.rendererFeatures.Add(f);f.Create();}
        f.SetActive(true);EditorUtility.SetDirty(f);return f;
    }
    public static void Apply(string name)
    {
        string folder=HoMmdStageBuilder.Root+name,path=folder+"/Rendering/进阶效果.json";
        var l=File.Exists(path)?JsonUtility.FromJson<Look>(File.ReadAllText(path)):Default(name);
        if(l.gi!="RTGI")throw new InvalidOperationException("This stage workflow permits RTGI only. SSGI is prohibited for character rendering.");
        var scene=EditorSceneManager.OpenScene(folder+"/"+name+".unity",OpenSceneMode.Single);
        var root=scene.GetRootGameObjects().Single(x=>x.GetComponentInChildren<HoMmdStageEnvironment>()!=null);
        var profile=root.GetComponentInChildren<Volume>().sharedProfile;
        var d=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(folder+"/Rendering/Ho Renderer.asset");
        var sky=root.GetComponentsInChildren<MeshRenderer>().Single(x=>x.name=="天空球").sharedMaterial;
        var gb=d.rendererFeatures.OfType<HoGeometryBufferRendererFeature>().Single();
        gb.Settings.minRenderQueue=1500;gb.Settings.renderScale=HoGeometryBufferRenderScale.Full;
        var shadow=d.rendererFeatures.OfType<HoShadowCastRendererFeature>().FirstOrDefault();
        if(shadow!=null)
        {
            shadow.Settings.atlasSize=4096;shadow.Settings.directionalResolution=2048;
            shadow.Settings.spotResolution=1024;shadow.Settings.pointFaceResolution=512;
            shadow.Settings.shadowStrength=1;shadow.Settings.punctualShadowStrength=1;
            EditorUtility.SetDirty(shadow);
        }
        // Keep the artistic AO contract consistent for every stage, including the
        // eight scenes that were authored before the remaining-scene pass.
        var spec=HoMmdStageRemaining.Specs().FirstOrDefault(x=>x.recipe.name==name);
        bool day=spec!=null?spec.day:new[]{"花吐电気","乐园·深命","茉莉庭院"}.Contains(name);
        var modelRoot=root.transform.Find("01 场景模型");
        if(modelRoot!=null)
        {
            float aoLevel=spec!=null?spec.aoLevel:(day?.1f:.14f);
            float aoContrast=spec!=null?spec.aoContrast:1.7f;
            float aoDark=spec!=null?spec.aoDarkStrength:(day?.75f:.85f);
            foreach(var m in modelRoot.GetComponentsInChildren<MeshRenderer>().Select(x=>x.sharedMaterial).Where(x=>x!=null).Distinct())
            {
                F(m,"_UseRealtimeAO",1);F(m,"_AOStrength",1);F(m,"_AOLevel",aoLevel);
                F(m,"_AOContrast",aoContrast);F(m,"_AODarkStrength",aoDark);
                if(m.HasProperty("_AOColor"))m.SetColor("_AOColor",day?C(.13f,.16f,.2f):C(.06f,.07f,.14f));
                EditorUtility.SetDirty(m);
            }
        }
        // User constraint: remove the screen-space GI feature and its serialized Volume,
        // including inactive/orphaned subassets, so it cannot be accidentally re-enabled.
        foreach(var f in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(d)).OfType<ScriptableRendererFeature>()
            .Where(x=>x.GetType().Name=="HoSSGIRendererFeature").ToArray())
        {d.rendererFeatures.Remove(f);UnityEngine.Object.DestroyImmediate(f,true);}
        foreach(var v in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(profile)).OfType<VolumeComponent>()
            .Where(x=>x.GetType().Name=="HoSSGIVolume").ToArray())
        {profile.components.Remove(v);UnityEngine.Object.DestroyImmediate(v,true);}
        var featureMap=new SerializedObject(d);var ids=featureMap.FindProperty("m_RendererFeatureMap");
        if(ids!=null){ids.arraySize=0;featureMap.ApplyModifiedPropertiesWithoutUndo();}
        {
            var host=Feature<HoRTRendererFeature>(d,"Ho-RT 场景宿主");d.rendererFeatures.Remove(host);d.rendererFeatures.Insert(0,host);
            var gi=Feature<HoRTGIRendererFeature>(d,"Ho-RTGI 一跳间接光");
            gi.Settings.excludedRayMaterials=new[]{sky};gi.Settings.composite=true;gi.Settings.intensity=l.giStrength;
            gi.Settings.secondaryLightSamples=4;gi.Settings.secondaryEnvironmentSamples=2;gi.Settings.maxDistance=70;
            gi.Settings.maxHistory=20;gi.Settings.spatialSamples=6;gi.Settings.denoise=l.denoise;gi.Settings.includeDirectEnvironment=false;
            var v=Volume<HoRTGIVolume>(profile);v.enable.Override(true);v.composite.Override(true);v.intensity.Override(l.giStrength);v.denoise.Override(l.denoise);
        }
        // RTGI currently does not implement raster light-linking. Restrict these
        // exterior-only artist lights to geometry layer 0, making them ineligible
        // for RTGI analytic-light sampling without changing their raster receivers.
        foreach(var light in root.GetComponentsInChildren<Light>().Where(x=>x.name.StartsWith("窗外晴日")))light.cullingMask=1;
        var screen=Volume<ScreenProcessStackVolume>(profile);screen.layers.value.RemoveAll(x=>x.name.StartsWith(Prefix));
        if(l.heightFog>0)
        {
            screen.layers.value.Insert(0,new ScreenProcessLayer{name=Prefix+"贴地空气层",effect=ScreenProcessEffect.DepthFog,color=l.air,
                blendMode=ScreenProcessBlendMode.Normal,intensity=l.heightFog,
                parameters0=new Vector4(0,1,2,80),parameters1=new Vector4(.012f,.35f,.7f,.08f),
                parameters2=new Vector4(l.air.r,l.air.g,l.air.b,1),parameters3=new Vector4(2,0,.15f,.18f),
                parameters4=new Vector4(1.4f,.5f,l.air.r,l.air.g),parameters5=new Vector4(l.air.b,0,0,.65f)});
        }
        screen.layers.value.Insert(0,new ScreenProcessLayer{name=Prefix+"主体分区塑形",effect=ScreenProcessEffect.PostLighting,
            blendMode=ScreenProcessBlendMode.SoftLight,intensity=l.postLight,
            parameters0=new Vector4(1,.45f,.1f,.45f),parameters1=new Vector4(60,1,0,.5f),
            parameters2=new Vector4(.52f,.5f,.65f,.75f),parameters3=new Vector4(l.highlight.r,l.highlight.g,l.highlight.b,1),
            parameters4=new Vector4(l.mid.r,l.mid.g,l.mid.b,1),parameters5=new Vector4(.4f,.06f,0,.45f)});
        var image=Volume<ImageProcessStackVolume>(profile);image.layers.value.RemoveAll(x=>x.name.StartsWith(Prefix));
        int index=image.layers.value.FindIndex(x=>x.effect==ImageProcessEffect.CinematicBars);if(index<0)index=image.layers.value.Count;
        var ramp=new Gradient();ramp.SetKeys(new[]{new GradientColorKey(l.shadow,0),new GradientColorKey(l.mid,.52f),new GradientColorKey(l.highlight,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
        image.layers.value.Insert(index++,new ImageProcessLayer{name=Prefix+"Oklab 电影色调",effect=ImageProcessEffect.GradientMap,
            intensity=l.grade,blendMode=ImageProcessBlendMode.Color,ramp=ramp,parameters0=new Vector4(0,0,1,.35f),parameters6=new Vector4(2,0,0,0)});
        image.layers.value.Insert(index++,new ImageProcessLayer{name=Prefix+"镜头扩散与星芒",effect=ImageProcessEffect.Glow,
            intensity=l.glow,parameters0=new Vector4(.76f,.18f,4,2),parameters1=new Vector4(.35f,0,0,.75f),parameters2=new Vector4(4,20,0,0)});
        if(l.flare>0)image.layers.value.Insert(index,new ImageProcessLayer{name=Prefix+"横向镜头光晕",effect=ImageProcessEffect.LensFlare,intensity=l.flare,
            color=l.highlight,parameters0=new Vector4(l.flareSource.x,l.flareSource.y,-15,.65f),parameters1=new Vector4(.004f,.08f,.1f,4),
            parameters2=new Vector4(.03f,.9f,.02f,.25f),parameters3=new Vector4(.25f,.4f,0,0)});
        foreach(var s in root.GetComponentsInChildren<HoPlanarReflectionSurface>())
        {
            var so=new SerializedObject(s);so.FindProperty("resolution").intValue=1024;so.ApplyModifiedPropertiesWithoutUndo();
            var mr=s.GetComponent<MeshRenderer>();var m=mr!=null?mr.sharedMaterial:null;
            if(m!=null&&m.HasProperty("_PlanarReflectionStrength"))m.SetFloat("_PlanarReflectionStrength",l.reflection);EditorUtility.SetDirty(m);
        }
        EditorUtility.SetDirty(profile);EditorUtility.SetDirty(d);EditorUtility.SetDirty(gb);
        EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();File.WriteAllText(path,JsonUtility.ToJson(l,true));
        Debug.Log("MMD_ADVANCED_APPLIED "+name+" "+l.gi);
    }
    public static void ConfigureCapture(Camera camera)
    {
        string[] args=Environment.GetCommandLineArgs();var p=camera.transform.root.GetComponentInChildren<Volume>().sharedProfile;
        string name=camera.gameObject.scene.name;
        var d=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(HoMmdStageBuilder.Root+name+"/Rendering/Ho Renderer.asset");
        if(args.Contains("-hoNoGi"))foreach(var f in d.rendererFeatures.OfType<HoRTGIRendererFeature>())f.SetActive(false);
        if(args.Contains("-hoGiOnly"))
        {
            if(p.TryGet<HoRTGIVolume>(out var rt)&&d.rendererFeatures.OfType<HoRTGIRendererFeature>().Any(x=>x.isActive))rt.debugMode.Override(HoRTGIDebugMode.Diffuse);
        }
    }
    [Serializable] sealed class State
    {
        public string stage,api,mode,rtStatus;
        public bool rayTracing,giTexture,skyExcluded;
        public int width,height,frame,spLayers,ipLayers;
        public Color meanGi;
        public float maxGi,nonzeroFraction;
    }
    public static void Record(Camera camera,int frame)
    {
        if(frame!=32||Environment.GetCommandLineArgs().Contains("-hoNoGi")||Environment.GetCommandLineArgs().Contains("-hoGiOnly"))return;
        string name=camera.gameObject.scene.name,folder=HoMmdStageBuilder.Root+name;
        if(!File.Exists(folder+"/Rendering/进阶效果.json"))return;
        var d=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(folder+"/Rendering/Ho Renderer.asset");
        var rt=d.rendererFeatures.OfType<HoRTGIRendererFeature>().FirstOrDefault(x=>x.isActive);
        // Read the actual main camera's persistent output. Reflection cameras reset
        // compatibility globals after the main render, so globals are not evidence.
        Texture tex=null;
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        if(rt!=null)
        {
            var context=typeof(HoRTGIRendererFeature).GetMethod("ContextForCamera",flags).Invoke(rt,new object[]{camera});
            var handle=context!=null?context.GetType().GetProperty("PublishedDiffuse",flags).GetValue(context) as RTHandle:null;
            tex=handle?.rt;
        }
        var s=new State{stage=name,api=SystemInfo.graphicsDeviceType.ToString(),mode=rt!=null?"RTGI":"Off",frame=frame,
            rayTracing=SystemInfo.supportsRayTracing,rtStatus=rt!=null?rt.Status:"",giTexture=tex!=null&&tex.width>2,width=tex!=null?tex.width:0,height=tex!=null?tex.height:0,
            skyExcluded=rt==null||rt.Settings.excludedRayMaterials.Length==1,
            spLayers=ScreenProcessRuntimeDiagnostics.CurrentSnapshot.WrittenLayerCount,ipLayers=ImageProcessRuntimeDiagnostics.CurrentSnapshot.WrittenLayerCount};
        if(s.giTexture)
        {
            var target=RenderTexture.GetTemporary(32,32,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var read=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);var active=RenderTexture.active;
            try
            {
                Graphics.Blit(tex,target);RenderTexture.active=target;read.ReadPixels(new Rect(0,0,32,32),0,0);read.Apply();
                var pixels=read.GetPixels();Color sum=Color.clear;int nonzero=0;
                foreach(var v in pixels){sum+=v;s.maxGi=Mathf.Max(s.maxGi,v.r,v.g,v.b);if(v.maxColorComponent>.0001f)nonzero++;}
                s.meanGi=sum/pixels.Length;s.nonzeroFraction=nonzero/(float)pixels.Length;
            }
            finally{RenderTexture.active=active;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(read);}
        }
        File.WriteAllText(Path.Combine(HoMmdStageWork.Work,name+"-advanced-state.json"),JsonUtility.ToJson(s,true));
    }
}
