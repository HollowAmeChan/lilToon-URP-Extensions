using System;
using System.IO;
using System.Linq;
using Hollow.HoMmdStages;
using Hollow.HoUnityTools.Editor.Pmx;
using lilToon.URP.Extensions.Atmosphere;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// One-time assembly of the recovered complete stage. Keeps the original scene
// GUID and extracted prop; later lighting edits use HoMmdStageDetail normally.
public static class HoMmdYueximeiComplete
{
    const string Name="月昔梅";
    static string Folder=>HoMmdStageBuilder.Root+Name;
    public static void Run()
    {
        try
        {
            string marker=Folder+"/Rendering/完整场景装配.json";
            if(!File.Exists(marker))Assemble(marker);
            RefineMaterials();
            HoMmdStageDetail.Run();
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void F(Material m,string key,float value){if(m.HasProperty(key))m.SetFloat(key,value);}
    [Serializable] sealed class ModelInfo {public string path;public int vertices,triangles;public string[] textures,warnings;}
    [Serializable] sealed class RecoveryAudit {public string stage;public ModelInfo[] models;public bool companionFanPreserved,studioRemoved;public string[] errors;}
    public static void AuditBatch()
    {
        try
        {
            var recipe=HoMmdStageBuilder.ReadRecipe(Name);var errors=new System.Collections.Generic.List<string>();
            var models=recipe.models.Select(file=>{
                string path=Folder+"/Source/完整场景/"+file;
                var report=AssetDatabase.LoadAllAssetsAtPath(path).OfType<HoPmxImportReport>().Single();
                var textures=report.materials.Select(x=>x.baseTexturePath).Where(x=>!string.IsNullOrEmpty(x)).Distinct().ToArray();
                string expected=Path.GetFullPath(Folder+"/Source/完整场景/").Replace('\\','/').TrimEnd('/')+"/";
                foreach(string texture in textures)if(!File.Exists(texture)||!texture.Replace('\\','/').StartsWith(expected,StringComparison.OrdinalIgnoreCase))errors.Add("Invalid stage texture: "+texture);
                foreach(var m in report.materials)if(!AssetDatabase.GetAssetPath(m.material).StartsWith(Folder+"/Materials/完整场景/"))errors.Add("Material not extracted: "+m.sourceName);
                return new ModelInfo{path=path,vertices=report.vertexCount,triangles=report.triangleCount,textures=textures,warnings=report.warnings.ToArray()};
            }).ToArray();
            var scene=EditorSceneManager.OpenScene(Folder+"/"+Name+".unity",OpenSceneMode.Single);
            var root=scene.GetRootGameObjects().Single(x=>x.GetComponentInChildren<HoMmdStageEnvironment>()!=null);
            var group=root.transform.Find("01 场景模型");
            if(group.childCount!=4)errors.Add("Expected four complete-stage models");
            bool fan=File.Exists(Folder+"/Prefabs/月昔梅 团扇道具.prefab")&&root.transform.Find("05 配套团扇（可选）")!=null;
            bool studio=group.GetComponentsInChildren<Transform>().All(x=>x.name!="展台地面");
            if(!fan||!studio)errors.Add("Companion fan / temporary studio cleanup failed");
            File.WriteAllText(Path.Combine(HoMmdStageWork.Work,"月昔梅-complete-audit.json"),JsonUtility.ToJson(new RecoveryAudit{stage=Name,models=models,companionFanPreserved=fan,studioRemoved=studio,errors=errors.ToArray()},true));
            if(errors.Count>0)throw new InvalidOperationException(string.Join("; ",errors));
            HoMmdStageAudit.RunBatch();
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void RefineMaterials()
    {
        var scene=EditorSceneManager.OpenScene(Folder+"/"+Name+".unity",OpenSceneMode.Single);
        var root=scene.GetRootGameObjects().Single(x=>x.GetComponentInChildren<HoMmdStageEnvironment>()!=null);
        foreach(var m in root.transform.Find("01 场景模型").GetComponentsInChildren<MeshRenderer>().Select(x=>x.sharedMaterial).Distinct())
        {
            F(m,"_ApplySpecular",0);F(m,"_ApplySpecularFA",0);F(m,"_Metallic",0);F(m,"_LightMaxLimit",1.4f);
            F(m,"_MultiLightIntensity",.4f);
            if(!m.name.Contains("地板")&&!m.name.EndsWith("_水"))F(m,"_UseReflection",0);
            if(m.name.Contains("树叶"))
            {
                F(m,"_UseEmission",1);m.SetTexture("_EmissionMap",m.GetTexture("_MainTex"));
                m.SetColor("_EmissionColor",new Color(.32f,.28f,.2f,1));F(m,"_EmissionMainStrength",0);
                F(m,"_AODarkStrength",.3f);F(m,"_ShadowStrength",.65f);
            }
            EditorUtility.SetDirty(m);
        }
        EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }
    static void Assemble(string marker)
    {
        var recipe=HoMmdStageBuilder.Recipes().Single(x=>x.name==Name);
        Directory.CreateDirectory(Folder+"/Materials/完整场景");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach(string file in recipe.models)
        {
            string path=Folder+"/Source/完整场景/"+file;
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(path) as HoPmxImporter;
            if(importer==null)throw new InvalidOperationException("Missing PMX importer: "+path);
            importer.generateOutline=false;EditorUtility.SetDirty(importer);importer.SaveAndReimport();
            HoPmxMaterialExtraction.Extract(path,Folder+"/Materials/完整场景");
        }
        var scene=EditorSceneManager.OpenScene(Folder+"/"+Name+".unity",OpenSceneMode.Single);
        var root=scene.GetRootGameObjects().Single(x=>x.GetComponentInChildren<HoMmdStageEnvironment>()!=null);
        var geometry=root.transform.Find("01 场景模型");
        // The recovered source does not include the companion fan. Preserve its
        // prefab and scene transform as an optional disabled object.
        var prop=geometry.GetComponentsInChildren<Transform>().FirstOrDefault(x=>x.name=="團扇 月昔梅");
        if(prop!=null)
        {
            PrefabUtility.SaveAsPrefabAsset(prop.gameObject,Folder+"/Prefabs/月昔梅 团扇道具.prefab");
            var optional=new GameObject("05 配套团扇（可选）").transform;optional.SetParent(root.transform,false);
            prop.SetParent(optional,false);optional.gameObject.SetActive(false);
        }
        foreach(Transform child in geometry.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
        AssetDatabase.DeleteAsset(Folder+"/Materials/展台地面.mat");
        foreach(string file in recipe.models)
        {
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Source/完整场景/"+file);
            var obj=(GameObject)PrefabUtility.InstantiatePrefab(model,scene);obj.transform.SetParent(geometry,false);
        }
        foreach(var mesh in geometry.GetComponentsInChildren<MeshRenderer>())
        {
            mesh.shadowCastingMode=ShadowCastingMode.On;mesh.receiveShadows=true;
            var m=mesh.sharedMaterial;F(m,"_AsUnlit",0);F(m,"_LightMinLimit",.025f);F(m,"_LightMaxLimit",4);
            F(m,"_MultiLightIntensity",1);F(m,"_UseShadow",1);F(m,"_ShadowReceive",1);F(m,"_ShadowStrength",.9f);
            F(m,"_UseReflection",1);F(m,"_ApplyReflection",0);F(m,"_Smoothness",.42f);
            F(m,"_UseRealtimeAO",1);F(m,"_AOStrength",1);F(m,"_AOContrast",1.7f);
            if(m.name.Contains("灯笼纸")||m.name.Contains("光球"))
            {F(m,"_UseEmission",1);m.SetTexture("_EmissionMap",m.GetTexture("_MainTex"));m.SetColor("_EmissionColor",new Color(1,.78f,.4f,1)*.6f);}
            if(m.name.Contains("大月牙"))
            {F(m,"_UseEmission",1);m.SetColor("_EmissionColor",new Color(.62f,.72f,1,1)*.35f);}
            if(m.name.Contains("宝相花纹+"))
            {F(m,"_UseEmission",0);}
            m.SetShaderPassEnabled("ShadowCaster",true);EditorUtility.SetDirty(m);
        }
        var camera=root.GetComponentInChildren<Camera>();camera.transform.position=recipe.camera;camera.transform.LookAt(recipe.target);camera.fieldOfView=recipe.fov;
        var region=root.GetComponentInChildren<HoAtmosphereRegion>();region.transform.position=new Vector3(0,6,-6);region.size=new Vector3(65,24,65);
        PrefabUtility.SaveAsPrefabAsset(geometry.gameObject,Folder+"/Prefabs/月昔梅 模型.prefab");
        File.WriteAllText(Folder+"/Rendering/制作参数.json",JsonUtility.ToJson(recipe,true));
        var look=new HoMmdStageLighting.Look{stage=Name,sun=new Color(1,.92f,.75f,1),ambient=new Color(.34f,.4f,.5f,1),shadow=new Color(.3f,.28f,.38f,1),fog=new Color(.55f,.61f,.7f,1),sunAngles=new Vector3(35,-115,0),sunPower=2,exposure=.15f,bloom=.25f,airDensity=.006f,shafts=4,focus=16,aperture=5.6f,ambientAir=.07f,fogDensity=.003f,fogOpacity=.12f,existingLocalScale=.25f,dofRadius=3,whiteFrame=true,ambientIntensity=.8f,contrast=12,shadowStrength=.9f,bloomThreshold=1.2f,vignette=.1f};
        File.WriteAllText(Folder+"/Rendering/光影精调.json",JsonUtility.ToJson(look,true));
        var advanced=HoMmdStageAdvanced.Default(Name);advanced.giStrength=.32f;advanced.heightFog=.06f;advanced.grade=.15f;advanced.reflection=.4f;advanced.glow=.18f;advanced.postLight=0;
        File.WriteAllText(Folder+"/Rendering/进阶效果.json",JsonUtility.ToJson(advanced,true));
        foreach(var file in new[]{"细节灯光.json","细节灯光基线.json","原灯光记录.json"})
            if(File.Exists(Folder+"/Rendering/"+file))AssetDatabase.DeleteAsset(Folder+"/Rendering/"+file);
        EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        HoMmdStageLighting.Apply(Name);HoMmdStageRemaining.Art(Name);HoMmdStageAdvanced.Apply(Name);
        File.WriteAllText(marker,"{\"source\":\"32903cd061fc7937dd3f529c539d094c\",\"models\":4,\"companionFanPreserved\":true}");
        Debug.Log("MMD_YUEXIMEI_COMPLETE_ASSEMBLED");
    }
}
