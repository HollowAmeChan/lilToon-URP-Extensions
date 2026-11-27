using System;
using System.Linq;
using Hollow.HoMmdStages;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.GeometryData;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.PlanarReflection;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class HoMmdFeatureRepair
{
    static T Ensure<T>(UniversalRendererData renderer,string name) where T:ScriptableRendererFeature
    {
        var feature=renderer.rendererFeatures.OfType<T>().FirstOrDefault();
        if(feature==null)
        {
            feature=ScriptableObject.CreateInstance<T>();feature.name=name;AssetDatabase.AddObjectToAsset(feature,renderer);
            renderer.rendererFeatures.Add(feature);feature.Create();
            feature.SetActive(true);
        }
        EditorUtility.SetDirty(feature);return feature;
    }
    public static void Run()
    {
        try
        {
            foreach(var recipe in HoMmdStageBuilder.Recipes())Repair(recipe.name);
            AssetDatabase.SaveAssets();EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void Repair(string name)
    {
        string folder=HoMmdStageBuilder.Root+name;
        var renderer=HoMmdStageBuilder.ResolveRenderer(folder);
        if(renderer==null)throw new InvalidOperationException("Missing renderer "+name);
        Ensure<HoGeometryDataRendererFeature>(renderer,"Ho-GeometryData");
        Ensure<HoGeometryBufferRendererFeature>(renderer,"Ho-GeometryBuffer");
        Ensure<HoObjectBufferRendererFeature>(renderer,"Ho-ObjectBuffer");
        Ensure<HoSurfaceBufferRendererFeature>(renderer,"Ho-SurfaceBuffer");
        Ensure<HoAttributeCompositeRendererFeature>(renderer,"Ho-AttributeComposite");
        var plr=Ensure<HoPlanarReflectionRendererFeature>(renderer,"Ho-PlanarReflection");
        plr.FeatureSettings.renderGameView=true;plr.FeatureSettings.renderSceneView=true;plr.FeatureSettings.maxSurfacesPerCamera=8;
        bool unified = HoMmdSharedRenderAssets.ActiveHoShadows(renderer) != null;
        var ordered = HoMmdSharedRenderAssets.OrderFeatures(renderer.rendererFeatures, unified);
        HoMmdSharedRenderAssets.ApplyFeatureOrder(renderer, ordered);
        var scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(folder+"/"+name+".unity",UnityEditor.SceneManagement.OpenSceneMode.Single);
        var sky=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<MeshRenderer>(true)).FirstOrDefault(x=>x.name=="天空球")?.sharedMaterial;
        foreach(var root in scene.GetRootGameObjects())foreach(var surface in root.GetComponentsInChildren<HoPlanarReflectionSurface>())
        {
            var so=new SerializedObject(surface);var prop=so.FindProperty("reflectSceneView");if(prop!=null)prop.boolValue=true;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(surface);
        }
        HoMmdPlrRepair.Repair(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);
        EnsurePlanarVolume(folder,sky);
        renderer=HoMmdStageBuilder.ResolveRenderer(folder);
        EditorUtility.SetDirty(renderer);Debug.Log("MMD_FEATURE_REPAIRED "+name+" order="+string.Join(",",ordered.Select(x=>x.GetType().Name)));
    }

    static void EnsurePlanarVolume(string folder,Material sky)
    {
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(folder+"/Rendering/后处理.asset");
        if(profile==null)return;
        if(!profile.TryGet<HoPlanarReflectionVolume>(out var planar))
        {
            planar=profile.Add<HoPlanarReflectionVolume>(true);planar.name="HoPlanarReflectionVolume";
            AssetDatabase.AddObjectToAsset(planar,profile);
        }
        planar.enable.Override(true);planar.renderGameView.Override(true);planar.renderSceneView.Override(true);
        planar.maxSurfacesPerCamera.Override(8);planar.compositeEnabled.Override(false);
        if(sky!=null&&profile.TryGet<HoRTGIVolume>(out var rtgi))rtgi.excludedRayMaterials.Override(new[]{sky});
        EditorUtility.SetDirty(planar);EditorUtility.SetDirty(profile);
    }
}
