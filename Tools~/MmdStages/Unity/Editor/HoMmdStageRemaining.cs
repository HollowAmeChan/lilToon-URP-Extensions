using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Hollow.HoMmdStages;
using lilToon.URP.Extensions.PlanarReflection;
using lilToon.URP.Extensions.ShadowCast;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

// Catalog and scene-owned art direction for the remaining local packages.
public static class HoMmdStageRemaining
{
    const string CatalogPath="Assets/mmd场景测试/_制作工具/Editor/H2CuRemaining.json";
    [Serializable] public sealed class Spec
    {
        public HoMmdStageBuilder.Recipe recipe;
        public bool day;
        public string hdr;
        public string[] floors;
        public float aoLevel=.15f,aoContrast=1.7f,aoDarkStrength=.85f,plrStrength=.5f,skyExposure=.2f,shadowStrength=.95f;
        public float floorY;
        public float artLightScale=1;
    }
    [Serializable] sealed class Catalog{public Spec[] stages;}
    public static Spec[] Specs()=>File.Exists(CatalogPath)?JsonUtility.FromJson<Catalog>(File.ReadAllText(CatalogPath)).stages:Array.Empty<Spec>();
    public static HoMmdStageBuilder.Recipe[] Recipes()=>Specs().Select(x=>x.recipe).ToArray();
    static Color C(float r,float g,float b)=>new Color(r,g,b,1);
    static void F(Material m,string key,float value){if(m.HasProperty(key))m.SetFloat(key,value);}
    static string Name(Material m){int i=m.name.IndexOf('_');return i>0&&m.name.Substring(0,i).All(char.IsDigit)?m.name.Substring(i+1).Trim():m.name.Trim();}
    public static void BuildAndRenderBatch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            foreach(var spec in Specs())
            {
                string name=spec.recipe.name,folder=HoMmdStageBuilder.Root+name;
                if(!File.Exists(folder+"/"+name+".unity"))HoMmdStageBuilder.Build(spec.recipe);
                Prepare(name);HoMmdStageLighting.Apply(name);Art(name);HoMmdStageAdvanced.Apply(name);
            }
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            HoMmdStageBuilder.RenderStagesBatch(Recipes().Select(x=>x.name));
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    public static void RefineBatch()
    {
        try
        {
            string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-hoStages");
            var names=i>=0?args[i+1].Split(','):Recipes().Select(x=>x.name).ToArray();
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            foreach(string name in names)
            {
                if(Specs().Any(x=>x.recipe.name==name)){Prepare(name);HoMmdStageLighting.Apply(name);}
                Art(name);HoMmdStageAdvanced.Apply(name);
            }
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            HoMmdStageBuilder.RenderStagesBatch(names);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    // Applies the authoring contract without spending time on another capture.
    // This keeps saved scenes and the subsequent audit authoritative after code-only refinements.
    public static void ApplyOnlyBatch()
    {
        try
        {
            string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-hoStages");
            var names=i>=0?args[i+1].Split(','):HoMmdStageBuilder.Recipes().Select(x=>x.name).ToArray();
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            foreach(string name in names)
            {
                if(Specs().Any(x=>x.recipe.name==name)){Prepare(name);HoMmdStageLighting.Apply(name);Art(name);}
                HoMmdStageAdvanced.Apply(name);
            }
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            AssetDatabase.SaveAssets();EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void Prepare(string name)
    {
        var scene=EditorSceneManager.OpenScene(HoMmdStageBuilder.Root+name+"/"+name+".unity",OpenSceneMode.Single);
        var env=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<HoMmdStageEnvironment>()).Single();
        env.skybox.shader=Shader.Find("Ho/MMD Stage Gradient Sky");EditorUtility.SetDirty(env.skybox);
        EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }
    public static void Art(string name)
    {
        string folder=HoMmdStageBuilder.Root+name;
        var spec=Specs().FirstOrDefault(x=>x.recipe.name==name);
        bool day=spec!=null?spec.day:new[]{"花吐电気","乐园·深命","茉莉庭院"}.Contains(name);
        var scene=EditorSceneManager.OpenScene(folder+"/"+name+".unity",OpenSceneMode.Single);
        var root=scene.GetRootGameObjects().Single(x=>x.GetComponentInChildren<HoMmdStageEnvironment>()!=null);
        var env=root.GetComponentInChildren<HoMmdStageEnvironment>();
        var camera=root.GetComponentInChildren<Camera>();var recipe=HoMmdStageBuilder.ReadRecipe(name);
        var renderer=HoMmdStageBuilder.ResolveRenderer(folder);
        if(name=="月昔梅"&&recipe.models.Length==1)Studio(root,folder);
        var meshes=root.transform.Find("01 场景模型").GetComponentsInChildren<MeshRenderer>();
        foreach(var m in meshes.Select(x=>x.sharedMaterial).Where(x=>x!=null).Distinct())
        {
            string n=Name(m);
            F(m,"_UseRealtimeAO",1);F(m,"_AOStrength",1);F(m,"_AOLevel",spec?.aoLevel??.1f);
            F(m,"_AOContrast",spec?.aoContrast??1.6f);F(m,"_AODarkStrength",spec?.aoDarkStrength??.8f);
            if(m.HasProperty("_AOColor"))m.SetColor("_AOColor",day?C(.13f,.16f,.2f):C(.06f,.07f,.14f));
            F(m,"_ShadowReceive",1);F(m,"_ShadowStrength",spec?.shadowStrength??.95f);F(m,"_ShadowBlur",.2f);
            F(m,"_MultiLightCastShadowStrength",1);F(m,"_AsUnlit",0);
            if(spec!=null)
            {
                bool emissive=new[]{"灯泡","燈紙","燈+","灯笼纸","灯笼","灯光","月亮灯","星星灯","地板条","舞台条","柱子前","柱子后"}.Any(n.Contains);
                if(emissive)
                {
                    F(m,"_UseEmission",1);m.SetColor("_EmissionColor",spec.recipe.accent*(day?1.2f:.85f));
                    m.SetTexture("_EmissionMap",m.GetTexture("_MainTex"));F(m,"_EmissionMainStrength",0);
                }
                if(name=="万象天文"&&(n.Contains("線")||n.Contains("圆环")||n.Contains("星芒")))
                {F(m,"_UseEmission",1);m.SetColor("_EmissionColor",C(1,.8f,.3f)*1.8f);}
                if(name=="秘月之泉"&&(n.Contains("月亮")||n.Contains("星星")))
                {F(m,"_UseEmission",1);m.SetColor("_EmissionColor",C(.6f,.85f,1)*.9f);}
                if(name=="炫光夜厅"&&(n.Contains("水晶")||n.Contains("条")||n.Contains("柱子")))
                {F(m,"_UseEmission",1);m.SetColor("_EmissionColor",C(.5f,.4f,1)*2);}
            }
            if(name=="古典机械剧场黑蓝白"&&(n.Contains("鐘")||n.Contains("齿轮")||n.Contains("圆环")||n.Contains("圓圈")))
            {F(m,"_ApplySpecular",n.Contains("錶盤")?0:1);F(m,"_SpecularToon",0);F(m,"_Smoothness",.72f);F(m,"_Metallic",.25f);F(m,"_ApplyReflection",0);m.SetColor("_ReflectionColor",C(.3f,.42f,.65f));}
            EditorUtility.SetDirty(m);
        }
        foreach(var mesh in meshes){mesh.shadowCastingMode=ShadowCastingMode.On;mesh.receiveShadows=true;mesh.sharedMaterial.SetShaderPassEnabled("ShadowCaster",true);}
        var shadow=renderer.rendererFeatures.OfType<HoShadowCastRendererFeature>().First();shadow.Settings.shadowStrength=1;shadow.Settings.punctualShadowStrength=1;
        shadow.Settings.pcssMaxPenumbraRadius=3.5f;shadow.Settings.punctualPcssSoftness=.35f;
        shadow.Settings.atlasSize=4096;shadow.Settings.directionalResolution=2048;shadow.Settings.spotResolution=1024;shadow.Settings.pointFaceResolution=512;EditorUtility.SetDirty(shadow);
        if(spec!=null)
        {
            HdrSky(spec,env,folder);
            var old=root.transform.Find("07 场景设计灯");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var rig=new GameObject("07 场景设计灯").transform;rig.SetParent(root.transform,false);
            Lights(spec,rig,meshes);
            foreach(var light in rig.GetComponentsInChildren<Light>())light.intensity*=spec.artLightScale;
            camera.transform.position=recipe.camera;camera.transform.LookAt(recipe.target);camera.fieldOfView=recipe.fov;
        }
        string[] floorNames=spec?.floors;
        if(name=="拟花十字之棺")floorNames=new[]{"笼底"};
        if(name=="乐园·深命")floorNames=new[]{"地板啊"};
        if(name=="茉莉庭院")floorNames=new[]{"地砖"};
        var floorCandidates=meshes.Where(x=>(floorNames!=null||x.bounds.size.y<Mathf.Max(.3f,x.bounds.size.x*.12f))&&x.bounds.size.x>.1f)
            .Where(x=>floorNames!=null?floorNames.Contains(Name(x.sharedMaterial)):new[]{"地板","地面","地砖"}.Any(y=>Name(x.sharedMaterial).StartsWith(y)))
            .OrderBy(x=>floorNames!=null?Array.IndexOf(floorNames,Name(x.sharedMaterial)):0).ThenByDescending(x=>x.bounds.size.x*x.bounds.size.z).ToArray();
        var floor=floorCandidates.FirstOrDefault();
        if(floor!=null)
        {
            var reflectionY=spec?.floorY??(name=="古典机械剧场"?floor.bounds.max.y:floor.bounds.min.y);
            // Every explicitly named horizontal floor/water receiver gets its
            // own PLR surface. The feature cap is raised below so this remains
            // useful for scenes with a water sheet plus a walkable floor.
            foreach(var candidate in floorCandidates.Take(8))Reflection(candidate,renderer,spec?.plrStrength??.3f,
                name=="月昔梅"&&Name(candidate.sharedMaterial)=="水"?candidate.bounds.center.y:reflectionY);
        }
        else throw new Exception("No usable PLR floor in "+name);
        // Remove a receiver left by an earlier candidate selection. A terrain or
        // fountain bowl with several elevations is not another horizontal floor.
        foreach(var other in root.GetComponentsInChildren<HoPlanarReflectionSurface>().Where(x=>!floorCandidates.Contains(x.GetComponent<MeshRenderer>())).ToArray())
        {var otherMat=other.GetComponent<MeshRenderer>()?.sharedMaterial;F(otherMat,"_UsePlanarReflection",0);Object.DestroyImmediate(other);}
        if(name!="月昔梅"||recipe.models.Length>1)Dapple(root,folder,env.sun,recipe.target,day?12:18);
        AddPost(root,spec,name);
        env.ApplyNow();EditorUtility.SetDirty(renderer);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Debug.Log("MMD_REMAINING_ART "+name);
    }
    static void Reflection(MeshRenderer floor,UniversalRendererData renderer,float strength,float y)
    {
        var f=renderer.rendererFeatures.OfType<HoPlanarReflectionRendererFeature>().FirstOrDefault();
        if(f==null){f=ScriptableObject.CreateInstance<HoPlanarReflectionRendererFeature>();f.name="Ho 地面反射";AssetDatabase.AddObjectToAsset(f,renderer);renderer.rendererFeatures.Add(f);f.Create();}
        f.SetActive(true);f.FeatureSettings.compositeEnabled=false;f.FeatureSettings.maxSurfacesPerCamera=8;EditorUtility.SetDirty(f);
        var surface=floor.GetComponent<HoPlanarReflectionSurface>()??floor.gameObject.AddComponent<HoPlanarReflectionSurface>();
        var anchor=floor.transform.Find("地面反射平面");if(anchor==null){anchor=new GameObject("地面反射平面").transform;anchor.SetParent(floor.transform,false);}
        anchor.position=new Vector3(floor.bounds.center.x,y,floor.bounds.center.z);anchor.rotation=Quaternion.identity;
        var so=new SerializedObject(surface);so.FindProperty("resolution").intValue=1024;so.FindProperty("reflectSceneView").boolValue=true;
        so.FindProperty("planeTransform").objectReferenceValue=anchor;so.ApplyModifiedPropertiesWithoutUndo();
        var mat=floor.sharedMaterial;F(mat,"_UseReflection",1);F(mat,"_UsePlanarReflection",1);F(mat,"_PlanarReflectionStrength",strength);
        F(mat,"_Smoothness",.78f);F(mat,"_ApplySpecular",0);F(mat,"_ApplyReflection",0);EditorUtility.SetDirty(mat);
    }
    static void HdrSky(Spec spec,HoMmdStageEnvironment env,string folder)
    {
        string path=folder+"/Rendering/"+spec.hdr;
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        if(importer==null){AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);importer=(TextureImporter)AssetImporter.GetAtPath(path);}
        if(importer==null)throw new InvalidOperationException("HDR importer unavailable "+path);
        if(importer.textureShape!=TextureImporterShape.TextureCube){importer.textureShape=TextureImporterShape.TextureCube;importer.generateCubemap=TextureImporterGenerateCubemap.AutoCubemap;importer.maxTextureSize=2048;importer.SaveAndReimport();}
        var cube=AssetDatabase.LoadAssetAtPath<Cubemap>(path);if(cube==null)throw new Exception("HDR cubemap import failed "+path);
        env.skybox.shader=Shader.Find("Skybox/Cubemap");env.skybox.SetTexture("_Tex",cube);env.skybox.SetFloat("_Exposure",spec.skyExposure);env.skybox.SetFloat("_Rotation",20);
        var sky=env.GetComponentsInChildren<MeshRenderer>().Single(x=>x.name=="天空球").sharedMaterial;
        sky.SetTexture("_HdrTex",cube);sky.SetFloat("_UseHdr",1);sky.SetFloat("_HdrExposure",spec.skyExposure);sky.SetFloat("_HdrRotation",20);
        EditorUtility.SetDirty(sky);EditorUtility.SetDirty(env.skybox);
    }
    static void Light(Transform rig,string name,Vector3 p,Vector3 target,Color color,float power,float range,bool point=false)
    {
        var l=new GameObject(name).AddComponent<Light>();l.transform.SetParent(rig,false);l.transform.position=p;l.transform.LookAt(target);
        l.type=point?LightType.Point:LightType.Spot;l.color=color;l.intensity=power;l.range=range;l.spotAngle=65;l.innerSpotAngle=35;
        l.shadows=LightShadows.Soft;l.shadowStrength=1;l.shadowBias=.025f;l.shadowNormalBias=.05f;
    }
    static void Lights(Spec s,Transform rig,MeshRenderer[] meshes)
    {
        var r=s.recipe;string n=r.name;var target=r.target;
        float scale=n=="月昔梅"&&r.models.Length==1?.05f:1;
        Light(rig,"主体方向重点",target+new Vector3(-4,7,5)*scale,target,r.key,100*scale*scale,25*scale);
        Light(rig,"色彩轮廓",target+new Vector3(5,4,-4)*scale,target,r.accent,65*scale*scale,23*scale);
        if(n=="孔雀月令红墨")
        {
            for(int i=0;i<4;i++){float z=-5+i*2;Light(rig,"左荷花暖光 "+i,new Vector3(-3,1.8f,z),target,C(1,.7f,.45f),5,7,true);Light(rig,"右荷花暖光 "+i,new Vector3(3,1.8f,z),target,C(1,.7f,.45f),5,7,true);}
            Light(rig,"孔雀案台追光",new Vector3(0,3,-5),new Vector3(0,1,-8.5f),C(1,.8f,.5f),24,10);
        }
        else if(n=="古典机械剧场黑蓝白")
        {
            Light(rig,"钟表银蓝追光",new Vector3(4,12,-10),new Vector3(0,6,-21),C(.68f,.84f,1),80,35);
            for(int i=0;i<5;i++)Light(rig,"舞台前沿 "+i,new Vector3(-10+i*5,1.7f,-13),Vector3.up,C(.6f,.75f,1),5,10,true);
        }
        else if(n=="百鸟溯归")
            for(int i=0;i<8;i++){float a=i*Mathf.PI/4;Light(rig,"河灯暖反弹 "+i,new Vector3(Mathf.Sin(a)*14,1.5f,Mathf.Cos(a)*14),target,C(1,.5f,.18f),6,12,true);}
        else if(n=="炫光夜厅")
            for(int i=0;i<7;i++){float z=-26+i*8;Light(rig,"左霓虹补光 "+i,new Vector3(-5,2,z),target,C(.4f,.35f,1),5,11,true);Light(rig,"右霓虹补光 "+i,new Vector3(5,2,z),target,C(.25f,.65f,1),5,11,true);}
        else if(n=="冷色古堂")
        {
            for(int i=0;i<5;i++){float z=-7+i*4;Light(rig,"左灯笼反弹 "+i,new Vector3(-5,2.8f,z),target,C(.72f,.88f,1),6,9,true);Light(rig,"右灯笼反弹 "+i,new Vector3(5,2.8f,z),target,C(.72f,.88f,1),6,9,true);}
            Light(rig,"圆屏柔和重点",new Vector3(-2,5,-4),new Vector3(0,2.5f,-10),C(.8f,.88f,1),65,16);
        }
        else if(n!="月昔梅")
        {
            var bulbs=meshes.Where(x=>new[]{"灯","燈","水晶","星星","月亮"}.Any(Name(x.sharedMaterial).Contains)).Take(5).ToArray();
            int i=0;foreach(var b in bulbs){var p=b.bounds.center;Light(rig,"灯饰反弹 "+(++i),p,target,r.accent,4,8,true);}
            Light(rig,"地面中心反弹",target+new Vector3(0,-Mathf.Min(target.y-1,2),2),target,r.fill,3.5f,10,true);
        }
    }
    static void Studio(GameObject root,string folder)
    {
        var group=root.transform.Find("01 场景模型");if(group.Find("展台地面")!=null)return;
        var plane=GameObject.CreatePrimitive(PrimitiveType.Plane);plane.name="展台地面";plane.transform.SetParent(group,false);plane.transform.position=new Vector3(0,-.155f,0);plane.transform.localScale=Vector3.one*.3f;
        Object.DestroyImmediate(plane.GetComponent<Collider>());var mat=new Material(Shader.Find("lilToon")){name="展台地面"};mat.SetColor("_Color",C(.09f,.11f,.12f));
        AssetDatabase.CreateAsset(mat,folder+"/Materials/展台地面.mat");plane.GetComponent<MeshRenderer>().sharedMaterial=mat;
    }
    static void Dapple(GameObject root,string folder,Light sun,Vector3 target,float size)
    {
        var old=root.transform.Find("08 斑驳投影");if(old!=null)Object.DestroyImmediate(old.gameObject);
        var go=new GameObject("08 斑驳投影");go.transform.SetParent(root.transform,false);
        go.transform.position=target-sun.transform.forward*10;go.transform.rotation=sun.transform.rotation;
        string meshPath=folder+"/Rendering/斑驳遮片.asset",matPath=folder+"/Rendering/斑驳遮片.mat";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if(mesh==null)
        {
            var random=new System.Random(8423);var v=new List<Vector3>();var t=new List<int>();
            for(int i=0;i<160;i++)
            {
                float x=((float)random.NextDouble()-.5f)*size,y=((float)random.NextDouble()-.5f)*size;
                float rx=.1f+(float)random.NextDouble()*.5f,ry=rx*(.4f+(float)random.NextDouble());int start=v.Count;
                v.Add(new Vector3(x,y,0));for(int j=0;j<9;j++){float a=j*Mathf.PI/4;v.Add(new Vector3(x+Mathf.Cos(a)*rx,y+Mathf.Sin(a)*ry,0));}
                for(int j=0;j<8;j++){t.Add(start);t.Add(start+j+1);t.Add(start+j+2);t.Add(start);t.Add(start+j+2);t.Add(start+j+1);}
            }
            mesh=new Mesh{name="斑驳光影遮片"};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,meshPath);
        }
        var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);if(mat==null){mat=new Material(Shader.Find("lilToon")){name="斑驳遮片"};AssetDatabase.CreateAsset(mat,matPath);}
        F(mat,"_Cull",0);mat.SetColor("_Color",Color.black);mat.SetShaderPassEnabled("ShadowCaster",true);EditorUtility.SetDirty(mat);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=mat;renderer.shadowCastingMode=ShadowCastingMode.ShadowsOnly;renderer.receiveShadows=true;
    }
    static void AddPost(GameObject root,Spec spec,string name)
    {
        var p=root.GetComponentInChildren<Volume>().sharedProfile;
        p.TryGet<ColorAdjustments>(out var grade);grade.contrast.Override(spec!=null?24:Mathf.Max(grade.contrast.value,16));
        var image=p.TryGet<ImageProcessStackVolume>(out var stack)?stack:null;if(image==null)return;
        image.layers.value.RemoveAll(x=>x.name.StartsWith("场景设计 · "));
        int i=image.layers.value.FindIndex(x=>x.effect==ImageProcessEffect.CinematicBars);if(i<0)i=image.layers.value.Count;
        var key=spec?.recipe.key??C(.8f,.85f,1);
        image.layers.value.Insert(i,new ImageProcessLayer{name="场景设计 · 顶底整体渐变",effect=ImageProcessEffect.Gradient,intensity=.22f,blendMode=ImageProcessBlendMode.SoftLight,
            color=key,parameters0=new Vector4(1,1,5,.5f),parameters1=Vector4.zero,parameters3=new Vector4(.04f,.06f,.1f,1)});
        image.layers.value.Insert(i+1,new ImageProcessLayer{name="场景设计 · 中心调色",effect=ImageProcessEffect.CenterColorCorrection,intensity=.25f,
            parameters0=new Vector4(.1f,.1f,.04f,0),parameters1=new Vector4(.5f,.53f,0,0),parameters2=new Vector4(.8f,0,0,0)});
        EditorUtility.SetDirty(p);
    }
}
