using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.HoMmdStages;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.GeometryData;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.PlanarReflection;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.SurfaceBuffer;
using lilToon.URP.Extensions.ShadowCast;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.GTAO;
using lilToon.URP.Extensions.VolumeLight;
using lilToon.URP.Extensions.OIT;
using lilToon.URP.Extensions.PostProcessing;
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
        }
        feature.SetActive(true);EditorUtility.SetDirty(feature);return feature;
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
        var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(folder+"/Rendering/Ho Renderer.asset");
        if(renderer==null)throw new InvalidOperationException("Missing renderer "+name);
        Ensure<HoGeometryDataRendererFeature>(renderer,"Ho-GeometryData");
        Ensure<HoGeometryBufferRendererFeature>(renderer,"Ho-GeometryBuffer");
        Ensure<HoObjectBufferRendererFeature>(renderer,"Ho-ObjectBuffer");
        Ensure<HoSurfaceBufferRendererFeature>(renderer,"Ho-SurfaceBuffer");
        Ensure<HoAttributeCompositeRendererFeature>(renderer,"Ho-AttributeComposite");
        var plr=Ensure<HoPlanarReflectionRendererFeature>(renderer,"Ho-PlanarReflection");
        plr.FeatureSettings.renderGameView=true;plr.FeatureSettings.renderSceneView=true;plr.FeatureSettings.maxSurfacesPerCamera=8;
        var desired=new Type[]{typeof(HoRTRendererFeature),typeof(HoGeometryDataRendererFeature),typeof(HoGeometryBufferRendererFeature),typeof(HoObjectBufferRendererFeature),typeof(HoSurfaceBufferRendererFeature),typeof(HoAttributeCompositeRendererFeature),typeof(HoShadowCastRendererFeature),typeof(HoAtmosphereRendererFeature),typeof(HoGTAORendererFeature),typeof(HoVolumeLightRendererFeature),typeof(WeightedOITRendererFeature),typeof(ScreenProcessRendererFeature),typeof(ImageProcessRendererFeature),typeof(HoPlanarReflectionRendererFeature),typeof(HoRTGIRendererFeature)};
        var ordered=new List<ScriptableRendererFeature>();
        foreach(var type in desired){var f=renderer.rendererFeatures.FirstOrDefault(x=>x!=null&&x.GetType()==type);if(f!=null&&!ordered.Contains(f))ordered.Add(f);}
        foreach(var f in renderer.rendererFeatures)if(f!=null&&!ordered.Contains(f)&&f.isActive)ordered.Add(f);
        renderer.rendererFeatures.Clear();renderer.rendererFeatures.AddRange(ordered);
        var scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(folder+"/"+name+".unity",UnityEditor.SceneManagement.OpenSceneMode.Single);
        foreach(var root in scene.GetRootGameObjects())foreach(var surface in root.GetComponentsInChildren<HoPlanarReflectionSurface>())
        {
            var so=new SerializedObject(surface);var prop=so.FindProperty("reflectSceneView");if(prop!=null)prop.boolValue=true;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(surface);
        }
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);
        EditorUtility.SetDirty(renderer);Debug.Log("MMD_FEATURE_REPAIRED "+name+" order="+string.Join(",",ordered.Select(x=>x.GetType().Name)));
    }
}
