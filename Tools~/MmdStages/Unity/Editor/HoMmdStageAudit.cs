using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Hollow.HoMmdStages;
using lilToon.URP.Extensions.Atmosphere;

public static class HoMmdStageAudit
{
    [Serializable]sealed class Audit
    {public string stage,scene,pipeline,profile;public int meshes,triangles,materials,lights,regions;public string[] features,externalStageReferences,missingShaders;public bool hasCamera,postProcessing;}
    [Serializable]sealed class Report{public string date;public Audit[] stages;public string[] errors;}
    public static void RunBatch()
    {
        var results=new List<Audit>();var errors=new List<string>();
        try
        {
            foreach(var recipe in HoMmdStageBuilder.Recipes())
            {
                string folder=HoMmdStageBuilder.Root+recipe.name;
                string path=folder+"/"+recipe.name+".unity";
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                var roots=scene.GetRootGameObjects();
                var meshes=roots.SelectMany(x=>x.GetComponentsInChildren<MeshFilter>()).ToArray();
                var renderers=roots.SelectMany(x=>x.GetComponentsInChildren<MeshRenderer>()).ToArray();
                var mats=renderers.SelectMany(x=>x.sharedMaterials).Distinct().ToArray();
                var env=roots.SelectMany(x=>x.GetComponentsInChildren<HoMmdStageEnvironment>()).Single();
                var camera=roots.SelectMany(x=>x.GetComponentsInChildren<Camera>()).FirstOrDefault();
                var profile=roots.SelectMany(x=>x.GetComponentsInChildren<Volume>()).Single().sharedProfile;
                var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(folder+"/Rendering/Ho Renderer.asset");
                var invalid=AssetDatabase.GetDependencies(path,true).Where(x=>
                    (x.StartsWith("Assets/mmd场景测试/")&&!x.StartsWith(folder+"/")&&!x.StartsWith("Assets/mmd场景测试/_制作工具/"))||
                    x=="Assets/Settings/PC_Renderer.asset"||x=="Assets/Settings/PC_RPAsset.asset").ToArray();
                var missing=mats.Where(x=>x==null||x.shader==null||!x.shader.name.Contains("lilToon")).Select(x=>x==null?"null":x.name).ToArray();
                int triangleCount=meshes.Sum(x=>(int)x.sharedMesh.GetIndexCount(0)/3);
                var audit=new Audit{stage=recipe.name,scene=path,pipeline=AssetDatabase.GetAssetPath(env.pipeline),
                    profile=AssetDatabase.GetAssetPath(profile),meshes=meshes.Length,triangles=triangleCount,materials=mats.Length,
                    lights=roots.SelectMany(x=>x.GetComponentsInChildren<Light>()).Count(),
                    regions=roots.SelectMany(x=>x.GetComponentsInChildren<HoAtmosphereRegion>()).Count(),
                    hasCamera=camera!=null,postProcessing=camera!=null&&camera.GetUniversalAdditionalCameraData().renderPostProcessing,
                    features=renderer.rendererFeatures.Where(x=>x.isActive).Select(x=>x.GetType().Name).ToArray(),
                    externalStageReferences=invalid,missingShaders=missing};
                results.Add(audit);
                if(invalid.Length>0||missing.Length>0||!audit.hasCamera||!audit.postProcessing||audit.regions!=1||
                    !audit.pipeline.StartsWith(folder+"/")||!audit.profile.StartsWith(folder+"/"))
                    errors.Add(recipe.name+": scene dependency/state audit failed");
            }
            File.WriteAllText(Path.Combine(HoMmdStageWork.Work,"scene-audit.json"),JsonUtility.ToJson(new Report{
                date=DateTime.UtcNow.ToString("O"),stages=results.ToArray(),errors=errors.ToArray()},true));
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            EditorApplication.Exit(errors.Count==0?0:1);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
