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
using lilToon.URP.Extensions.PostProcessing;
using lilToon.URP.Extensions.ShadowCast;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.PlanarReflection;

public static class HoMmdStageAudit
{
    [Serializable]sealed class Audit
    {public string stage,scene,pipeline,profile,giMode;public int meshes,triangles,materials,lights,enabledLights,regions,modelShadowCasters,modelRenderers,lowFillPoints,exteriorLights,advancedImageLayers,advancedScreenLayers,reflectionResolution,ssgiFeatures,ssgiVolumes,aoArtMaterials,aoEligibleMaterials,plrSurfaces,reviewCameras;public string[] features,externalStageReferences,missingShaders;public bool hasCamera,postProcessing,skySphere,depthOfField,cinematicBars,centeredLogo,logoContrast,exteriorLayerIsolation,centerPostLighting;}
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
                var modelRenderers=renderers.Where(x=>x.transform.parent.name!="04 环境与后处理").ToArray();
                bool skySphere=renderers.Any(x=>x.name=="天空球"&&x.sharedMaterial!=null&&x.sharedMaterial.shader!=null&&x.sharedMaterial.shader.name=="Ho/MMD Stage Sky Sphere");
                var missing=mats.Where(x=>x==null||x.shader==null||(!x.shader.name.Contains("lilToon")&&x.shader.name!="Ho/MMD Stage Sky Sphere")).Select(x=>x==null?"null":x.name).ToArray();
                bool dof=profile.TryGet<ScreenProcessStackVolume>(out var screen)&&screen.layers.value.Any(x=>x.enabled&&x.effect==ScreenProcessEffect.DepthOfField);
                profile.TryGet<ImageProcessStackVolume>(out var image);
                var bars=image!=null?image.layers.value.FirstOrDefault(x=>x.enabled&&x.effect==ImageProcessEffect.CinematicBars):null;
                var logo=image!=null?image.layers.value.FirstOrDefault(x=>x.enabled&&x.effect==ImageProcessEffect.LogoOverlay):null;
                bool centeredLogo=logo!=null&&logo.logoTexture0!=null&&Mathf.Abs(logo.parameters0.x-.5f)<.001f&&logo.parameters0.y<.15f&&
                    AssetDatabase.GetAssetPath(logo.logoTexture0)==folder+"/Rendering/Hollow Logo.png";
                bool contrast=bars!=null&&logo!=null&&Mathf.Abs(bars.color.r+logo.color.r-1)<.001f&&Mathf.Abs(bars.color.r-logo.color.r)>.9f;
                var sceneLights=roots.SelectMany(x=>x.GetComponentsInChildren<Light>()).ToArray();
                var exterior=sceneLights.Where(x=>x.name.StartsWith("窗外晴日")).ToArray();
                int lowPoints=sceneLights.Count(x=>x.enabled&&x.type==LightType.Point&&x.transform.parent.name=="06 摄影灯光"&&x.transform.position.y<2);
                bool layerIsolation=exterior.Length>0&&exterior.All(x=>(uint)x.GetUniversalAdditionalLightData().renderingLayers==2u)&&
                    modelRenderers.Count(x=>(x.renderingLayerMask&2u)!=0)==6;
                var rtgi=renderer.rendererFeatures.OfType<HoRTGIRendererFeature>().FirstOrDefault(x=>x.isActive);
                int ssgiFeatures=AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(renderer)).Count(x=>x!=null&&x.GetType().Name=="HoSSGIRendererFeature");
                int ssgiVolumes=AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(profile)).Count(x=>x!=null&&x.GetType().Name=="HoSSGIVolume");
                var advanced=File.Exists(folder+"/Rendering/进阶效果.json");
                int reflectionResolution=0;
                var plrSurfaces=roots.SelectMany(x=>x.GetComponentsInChildren<HoPlanarReflectionSurface>()).ToArray();
                foreach(var surface in plrSurfaces)
                    reflectionResolution=new SerializedObject(surface).FindProperty("resolution").intValue;
                var eligible=roots.SelectMany(x=>x.GetComponentsInChildren<Transform>()).Where(x=>x.name=="01 场景模型")
                    .SelectMany(x=>x.GetComponentsInChildren<MeshRenderer>()).Select(x=>x.sharedMaterial).Where(x=>x!=null).Distinct().ToArray();
                int triangleCount=meshes.Sum(x=>(int)x.sharedMesh.GetIndexCount(0)/3);
                var audit=new Audit{stage=recipe.name,scene=path,pipeline=AssetDatabase.GetAssetPath(env.pipeline),
                    profile=AssetDatabase.GetAssetPath(profile),meshes=meshes.Length,triangles=triangleCount,materials=mats.Length,
                    lights=roots.SelectMany(x=>x.GetComponentsInChildren<Light>()).Count(),
                    enabledLights=roots.SelectMany(x=>x.GetComponentsInChildren<Light>()).Count(x=>x.enabled),
                    regions=roots.SelectMany(x=>x.GetComponentsInChildren<HoAtmosphereRegion>()).Count(),
                    modelRenderers=modelRenderers.Length,modelShadowCasters=modelRenderers.Count(x=>x.shadowCastingMode!=ShadowCastingMode.Off&&x.receiveShadows),skySphere=skySphere,depthOfField=dof,
                    cinematicBars=bars!=null,centeredLogo=centeredLogo,logoContrast=contrast,
                    reviewCameras=roots.SelectMany(x=>x.GetComponentsInChildren<Camera>()).Count(),
                    centerPostLighting=screen.layers.value.Any(x=>x.enabled&&x.effect==ScreenProcessEffect.PostLighting)||image.layers.value.Any(x=>x.enabled&&x.effect==ImageProcessEffect.CenterColorCorrection),
                    lowFillPoints=lowPoints,exteriorLights=exterior.Length,exteriorLayerIsolation=layerIsolation,
                    giMode=rtgi!=null?"RTGI":"Off",advancedImageLayers=image.layers.value.Count(x=>x.name.StartsWith("进阶 · ")),
                    ssgiFeatures=ssgiFeatures,ssgiVolumes=ssgiVolumes,
                    aoEligibleMaterials=eligible.Length,aoArtMaterials=eligible.Count(x=>x.HasProperty("_UseRealtimeAO")&&x.GetFloat("_UseRealtimeAO")>.5f&&x.GetFloat("_AOContrast")>=1.5f),plrSurfaces=plrSurfaces.Length,
                    advancedScreenLayers=screen.layers.value.Count(x=>x.name.StartsWith("进阶 · ")),reflectionResolution=reflectionResolution,
                    hasCamera=camera!=null,postProcessing=camera!=null&&camera.GetUniversalAdditionalCameraData().renderPostProcessing,
                    features=renderer.rendererFeatures.Where(x=>x.isActive).Select(x=>x.GetType().Name).ToArray(),
                    externalStageReferences=invalid,missingShaders=missing};
                results.Add(audit);
                if(invalid.Length>0||missing.Length>0||!audit.hasCamera||!audit.postProcessing||audit.regions!=1||
                    !audit.pipeline.StartsWith(folder+"/")||!audit.profile.StartsWith(folder+"/")||!skySphere||!dof||!audit.cinematicBars||!centeredLogo||!contrast||audit.modelShadowCasters!=audit.modelRenderers||
                    modelRenderers.Any(x=>x.sharedMaterial!=null&&!x.sharedMaterial.GetShaderPassEnabled("ShadowCaster")))
                    errors.Add(recipe.name+": scene dependency/state audit failed");
                if(recipe.name=="花吐电気"&&(!layerIsolation||exterior.Length!=3||!screen.layers.value.Any(x=>x.enabled&&x.effect==ScreenProcessEffect.SkyTyndall)))
                    errors.Add(recipe.name+": exterior lighting / sky scattering audit failed");
                if(recipe.name=="圣诞万花镜"&&(lowPoints!=12||(int)renderer.rendererFeatures.OfType<HoShadowCastRendererFeature>().Single().Settings.lightCapacity<(int)HoShadowCastLightCapacity.Medium))
                    errors.Add(recipe.name+": low point fill / shadow capacity audit failed");
                if(audit.aoArtMaterials!=audit.aoEligibleMaterials||audit.plrSurfaces<1)
                    errors.Add(recipe.name+": AO art / all-floor PLR audit failed");
                if(File.Exists(folder+"/Rendering/细节灯光.json")&&(audit.reviewCameras!=3||audit.centerPostLighting))
                    errors.Add(recipe.name+": three cameras / world-space lighting audit failed");
                if(advanced)
                {
                    if(rtgi==null||ssgiFeatures!=0||ssgiVolumes!=0||audit.advancedImageLayers<2||
                        (reflectionResolution>0&&reflectionResolution!=1024))errors.Add(recipe.name+": advanced feature / look audit failed");
                    if(rtgi!=null)
                    {
                        var host=renderer.rendererFeatures.OfType<HoRTRendererFeature>().SingleOrDefault(x=>x.isActive);
                        if(host==null||renderer.rendererFeatures.IndexOf(host)>renderer.rendererFeatures.IndexOf(rtgi)||
                            rtgi.Settings.excludedRayMaterials.Length!=1||rtgi.Settings.excludedRayMaterials[0].shader.name!="Ho/MMD Stage Sky Sphere")
                            errors.Add(recipe.name+": RT host / sky exclusion audit failed");
                    }
                }
            }
            File.WriteAllText(Path.Combine(HoMmdStageWork.Work,"scene-audit.json"),JsonUtility.ToJson(new Report{
                date=DateTime.UtcNow.ToString("O"),stages=results.ToArray(),errors=errors.ToArray()},true));
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity",OpenSceneMode.Single);
            EditorApplication.Exit(errors.Count==0?0:1);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
