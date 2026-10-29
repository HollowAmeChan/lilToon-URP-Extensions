using System;
using System.IO;
using System.Linq;
using Hollow.HoMmdStages;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.VolumeLight;
using lilToon.URP.Extensions.PlanarReflection;
using Object=UnityEngine.Object;

// Scene-specific art direction. Only newly authored H2Cu folders are modified.
public static class HoMmdStagePolish
{
    public static void RunBatch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            foreach(var recipe in HoMmdStageBuilder.Recipes())Polish(recipe.name);
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static Color C(float r,float g,float b)=>new Color(r,g,b,1);
    static string SurfaceName(Material material)
    {
        string name=material.name;
        int separator=name.IndexOf('_');
        return separator>0&&name.Substring(0,separator).All(char.IsDigit)?name.Substring(separator+1):name;
    }
    static void F(Material m,string n,float v){if(m.HasProperty(n))m.SetFloat(n,v);}
    static void ColorValue(Material m,string n,Color v){if(m.HasProperty(n))m.SetColor(n,v);}
    static void Emit(Material m,Color color,float power,bool textured)
    {
        F(m,"_UseEmission",1);ColorValue(m,"_EmissionColor",new Color(color.r*power,color.g*power,color.b*power,1));
        if(m.HasProperty("_EmissionMap"))m.SetTexture("_EmissionMap",textured?m.GetTexture("_MainTex"):null);
        F(m,"_EmissionBlend",1);F(m,"_EmissionMainStrength",0);
    }
    public static void Polish(string name)
    {
        string folder=HoMmdStageBuilder.Root+name;
        var scene=EditorSceneManager.OpenScene(folder+"/"+name+".unity",OpenSceneMode.Single);
        var root=scene.GetRootGameObjects().First();
        var renderers=root.GetComponentsInChildren<MeshRenderer>();
        var env=root.GetComponentInChildren<HoMmdStageEnvironment>();
        var camera=root.GetComponentInChildren<Camera>();
        var volume=root.GetComponentInChildren<Volume>();
        var profile=volume.sharedProfile;
        var r=HoMmdStageBuilder.ReadRecipe(name);
        bool day=name=="花吐电気"||name=="乐园·深命"||name=="茉莉庭院";
        var sky=env.skybox;sky.shader=Shader.Find("Ho/MMD Stage Gradient Sky");
        if(sky.shader==null)throw new Exception("Stage gradient shader missing");
        sky.SetColor("_Top",day?C(.48f,.58f,.65f):C(.12f,.1f,.23f));
        sky.SetColor("_Bottom",day?C(.8f,.82f,.78f):C(.2f,.16f,.3f));
        sky.SetFloat("_Exposure",1);
        if(name=="茉莉庭院"){sky.SetColor("_Top",C(.65f,.75f,.8f));sky.SetColor("_Bottom",C(.86f,.9f,.88f));}
        EditorUtility.SetDirty(sky);
        camera.clearFlags=CameraClearFlags.Skybox;
        if(!day){camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=C(.11f,.08f,.22f);}
        var renderer=HoMmdStageBuilder.ResolveRenderer(folder);
        // Keep the atmosphere contribution restrained; local lights define the set.
        profile.TryGet(out Bloom bloom);bloom.intensity.Override(day?.24f:.38f);bloom.threshold.Override(1);
        profile.TryGet(out ColorAdjustments grading);
        grading.contrast.Override(day?5:9);grading.saturation.Override(-10);
        profile.TryGet(out HoVolumeLightVolume shaft);shaft.intensity.Override(day?2.2f:.7f);
        var medium=root.GetComponentInChildren<HoAtmosphereRegion>();
        medium.density=day?.012f:.006f;medium.volumeLightWeight=1;
        env.sun.shadowStrength=day?.65f:.5f;
        env.ambientSky=day?C(.42f,.5f,.52f):C(.24f,.25f,.36f);
        env.ambientEquator=env.ambientSky*.65f;env.ambientGround=env.ambientSky*.35f;
        var rig=root.transform.Find("05 艺术灯光");
        if(rig!=null)Object.DestroyImmediate(rig.gameObject);
        var go=new GameObject("05 艺术灯光");go.transform.SetParent(root.transform,false);rig=go.transform;
        foreach(var surface in renderers)
        {
            var m=surface.sharedMaterial;if(m==null)continue;
            F(m,"_LightMaxLimit",1.8f);F(m,"_MultiLightIntensity",1.5f);
            F(m,"_ShadowStrength",.5f);F(m,"_ShadowBlur",.35f);
            F(m,"_AsUnlit",day?.12f:.08f);
            string n=SurfaceName(m);
            if(n.Contains("玻璃")||n.Contains("窗帘")||n.Contains("窗簾"))surface.shadowCastingMode=ShadowCastingMode.Off;
            if((name.Contains("万花镜"))&&n.Contains("玻璃"))
            {
                var c=m.GetColor("_Color");c.a=.75f;m.SetColor("_Color",c);
                Color stained=name=="圣诞万花镜"
                    ?(n.Contains("菱")||n.Contains("花")?C(1,.35f,.22f):C(.4f,.8f,.6f))
                    :C(.45f,.65f,1);
                Emit(m,stained,.4f,true);
            }
            if(n.Contains("金灯")||n.Contains("银灯")||n.Contains("红灯")||n.Contains("灯胆")||n.Contains("燈膽"))
            {
                Color tint=n.Contains("银")?C(.7f,.86f,1):n.Contains("红")?C(1,.18f,.08f):name=="琉璃万花镜"?C(.5f,.8f,1):C(1,.7f,.35f);
                Emit(m,tint,1.05f,false);
            }
            if(name=="六角花台绣球")
            {
                if(n=="地板"){ColorValue(m,"_Color",C(.32f,.27f,.46f));F(m,"_AsUnlit",.35f);}
                if(n=="花纹"||n=="线"){Emit(m,C(.6f,.7f,1),1.4f,false);}
                if(n=="珠"||n=="水滴"){Emit(m,C(.44f,.62f,1),1.8f,false);}
                if(n.Contains("花"))F(m,"_AsUnlit",.2f);
            }
            if(name=="茉莉庭院")
            {
                if(n.Contains("水晶")||n=="串珠"){Emit(m,C(.65f,1,.9f),.5f,false);}
                if(n.Contains("茉莉花"))F(m,"_AsUnlit",.25f);
            }
            if(name=="古典机械剧场")
            {
                if(n.Contains("鐘")||n.Contains("齿轮")||n.Contains("圓圈")||n.Contains("圆环"))
                {F(m,"_UseReflection",0);F(m,"_ApplySpecular",0);F(m,"_AsUnlit",.15f);}
                if(n.Contains("幕布"))F(m,"_AsUnlit",.12f);
            }
            if(name=="拟花十字之棺")
            {
                if(n.Contains("十字架")||n.Contains("吊坠")||n.Contains("珍珠"))Emit(m,C(.6f,.65f,1),.3f,true);
                if(n.Contains("花"))F(m,"_AsUnlit",.25f);
            }
            if(name=="琉璃万花镜"||name=="拟花十字之棺")
                if(m.HasProperty("_MainTexHSVG"))m.SetVector("_MainTexHSVG",new Vector4(0,.5f,.9f,1));
            EditorUtility.SetDirty(m);
        }
        if(name=="花吐电気")
        {
            r.camera=new Vector3(.5f,1.25f,3.6f);r.target=new Vector3(-.7f,1.3f,-3.3f);r.fov=57;
            Point(rig,"室内柔和反弹",new Vector3(0,1.8f,1.7f),C(1,.87f,.7f),3.2f,8);
            Point(rig,"窗边日光反弹",new Vector3(1.8f,1.5f,-2.6f),C(1,.94f,.78f),3.8f,6);
            grading.postExposure.Override(1);
        }
        if(name=="乐园·深命")
        {
            r.camera=new Vector3(-6,2,8);r.target=new Vector3(2,5,-10);r.fov=58;
            grading.colorFilter.Override(C(.79f,.94f,1));grading.saturation.Override(-22);
            Point(rig,"青色天光",new Vector3(0,5,0),C(.55f,.83f,1),7,23);
            Point(rig,"地面反弹",new Vector3(-8,2,-7),C(.55f,.82f,.65f),4,15);
        }
        if(name=="圣诞万花镜"||name=="琉璃万花镜")
        {
            bool christmas=name=="圣诞万花镜";
            for(int i=0;i<6;i++)
            {
                float z=-25+i*9;
                Point(rig,"左彩窗反弹 "+(i+1),new Vector3(-4,3,z),christmas?C(.9f,.22f,.13f):C(.24f,.4f,1),4,11);
                Point(rig,"右彩窗反弹 "+(i+1),new Vector3(4,3,z),christmas?C(.24f,.65f,.5f):C(.45f,.8f,1),4,11);
            }
            if(christmas)Point(rig,"圣诞树主光",new Vector3(0,3,-10),C(1,.65f,.3f),5,12);
            else {r.camera=new Vector3(0,1.6f,7);r.target=new Vector3(0,3,-16);r.fov=56;}
            grading.postExposure.Override(.9f);
        }
        if(name=="茉莉庭院")
        {
            grading.postExposure.Override(.85f);grading.colorFilter.Override(C(.82f,.98f,1));
            Point(rig,"花丛柔光",new Vector3(0,1.8f,-2),C(.75f,1,.88f),2,7);
            Point(rig,"吊晶边光",new Vector3(2,3,-5),C(.55f,.85f,1),2.4f,6);
        }
        if(name=="六角花台绣球")
        {
            r.camera=new Vector3(0,3,10);r.target=new Vector3(0,.8f,-1);r.fov=58;
            Point(rig,"绣球冷光",new Vector3(-3,2,-1),C(.38f,.48f,1),4,10);
            Point(rig,"绣球暖紫",new Vector3(3,2,-1),C(.7f,.3f,1),4,10);
            grading.postExposure.Override(.25f);
        }
        if(name=="古典机械剧场")
        {
            r.camera=new Vector3(1,3.5f,3);r.target=new Vector3(0,6,-18);r.fov=56;
            for(int i=0;i<5;i++)Point(rig,"幕布脚光 "+(i+1),new Vector3(-12+i*6,1.8f,-13),C(1,.45f,.18f),5,12);
            Point(rig,"钟表柔光",new Vector3(0,6,-17),C(1,.8f,.5f),4,12);
            grading.postExposure.Override(1.1f);
        }
        if(name=="拟花十字之棺")
        {
            r.camera=new Vector3(0,2.5f,10);r.target=new Vector3(0,5.5f,-2);r.fov=58;
            for(int i=0;i<6;i++)
            {
                float a=i*Mathf.PI/3;Point(rig,"蓝玫瑰环光 "+(i+1),new Vector3(Mathf.Sin(a)*8,3,Mathf.Cos(a)*8),C(.35f,.4f,1),6,15);
            }
            Point(rig,"十字架高光",new Vector3(0,15,0),C(.75f,.7f,1),7,30);
            grading.postExposure.Override(.9f);
        }
        camera.transform.position=r.camera;camera.transform.LookAt(r.target);camera.fieldOfView=r.fov;
        File.WriteAllText(folder+"/Rendering/制作参数.json",JsonUtility.ToJson(r,true));
        env.ApplyNow();
        // Reflection is a scene-owned surface, not a shared fullscreen fake.
        if(name=="六角花台绣球"||name=="茉莉庭院"||name=="古典机械剧场"||name.Contains("万花镜"))
        {
            var floor=renderers.FirstOrDefault(x=>x.sharedMaterial!=null&&(SurfaceName(x.sharedMaterial)=="地面"||SurfaceName(x.sharedMaterial)=="地板"||
                SurfaceName(x.sharedMaterial)=="地砖"||SurfaceName(x.sharedMaterial).StartsWith("地板===")));
            if(floor!=null)
            {
                var feature=renderer.rendererFeatures.OfType<HoPlanarReflectionRendererFeature>().FirstOrDefault();
                if(feature==null){feature=ScriptableObject.CreateInstance<HoPlanarReflectionRendererFeature>();feature.name="Ho 平面反射";AssetDatabase.AddObjectToAsset(feature,renderer);renderer.rendererFeatures.Add(feature);feature.Create();}
                feature.SetActive(true);feature.FeatureSettings.compositeEnabled=false;
                var surface=floor.GetComponent<HoPlanarReflectionSurface>()??floor.gameObject.AddComponent<HoPlanarReflectionSurface>();
                var so=new SerializedObject(surface);so.FindProperty("resolution").intValue=512;so.FindProperty("reflectSceneView").boolValue=false;
                so.ApplyModifiedPropertiesWithoutUndo();
                var mat=floor.sharedMaterial;
                F(mat,"_UseReflection",1);F(mat,"_ApplySpecular",0);F(mat,"_ApplySpecularFA",0);F(mat,"_ApplyReflection",0);
                F(mat,"_UsePlanarReflection",1);F(mat,"_PlanarReflectionStrength",.85f);F(mat,"_Smoothness",.82f);F(mat,"_Reflectance",.08f);
                EditorUtility.SetDirty(mat);EditorUtility.SetDirty(feature);EditorUtility.SetDirty(renderer);
            }
        }
        EditorUtility.SetDirty(profile);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Debug.Log("MMD_STAGE_POLISHED "+name);
    }
    static void Point(Transform parent,string name,Vector3 position,Color color,float intensity,float range)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.position=position;
        var l=go.AddComponent<Light>();l.type=LightType.Point;l.color=color;l.intensity=intensity;l.range=range;l.shadows=LightShadows.None;
    }
}
