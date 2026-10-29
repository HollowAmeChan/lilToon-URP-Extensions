using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoMmdStages;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.CharacterShadow;
using lilToon.URP.Extensions.CharacterSpecialization;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.GeometryData;
using lilToon.URP.Extensions.GTAO;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.OIT;
using lilToon.URP.Extensions.PostProcessing;
using lilToon.URP.Extensions.PlanarReflection;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.RTShadow;
using lilToon.URP.Extensions.ShadowCast;
using lilToon.URP.Extensions.SurfaceBuffer;
using lilToon.URP.Extensions.VolumeLight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// One renderer/pipeline pair for the MMD stage family. It is deliberately kept
// under the MMD folder so other project scenes can continue to use PC_Renderer.
public static class HoMmdSharedRenderAssets
{
    public const string Root = "Assets/mmd场景测试/_共享渲染/";
    public const string RendererPath = Root + "HoMmd_Renderer.asset";
    public const string PipelinePath = Root + "HoMmd_RPAsset.asset";
    const string SourceRenderer = "Assets/Settings/PC_Renderer.asset";
    const string SourcePipeline = "Assets/Settings/PC_RPAsset.asset";

    static string Arg(string key)
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    public static void Run()
    {
        try
        {
            EnsureAssets();
            if (Arg("-hoMigrateScenes") == "1") MigrateScenes();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene("Assets/Hollow/WTD-summer/WTD.unity", OpenSceneMode.Single);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void EnsureAssets()
    {
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "mmd场景测试/_共享渲染"));
        if (AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath) == null)
            AssetDatabase.CopyAsset(SourceRenderer, RendererPath);
        if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath) == null)
            AssetDatabase.CopyAsset(SourcePipeline, PipelinePath);

        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (renderer == null || pipeline == null) throw new InvalidOperationException("无法建立 MMD shared Renderer/RenderPipelineAsset。");
        ConfigureRenderer(renderer);
        var pipelineObject = new SerializedObject(pipeline);
        var rendererList = pipelineObject.FindProperty("m_RendererDataList");
        rendererList.arraySize = 1;
        rendererList.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
        var volumeProfile = pipelineObject.FindProperty("m_VolumeProfile");
        if (volumeProfile != null) volumeProfile.objectReferenceValue = null;
        pipelineObject.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
        pipelineObject.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = 4096;
        pipelineObject.FindProperty("m_ShadowDistance").floatValue = 90;
        pipelineObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pipeline);
    }

    static void ConfigureRenderer(UniversalRendererData renderer)
    {
        var features = renderer.rendererFeatures.Where(x => x != null && x.isActive).ToList();
        var unique = new List<ScriptableRendererFeature>();
        var types = new HashSet<Type>();
        foreach (var feature in features)
            if (types.Add(feature.GetType())) unique.Add(feature);

        Ensure<HoRTRendererFeature>(unique, types, renderer, "Ho-RT");
        Ensure<HoGeometryDataRendererFeature>(unique, types, renderer, "Ho-GeometryData");
        Ensure<HoGeometryBufferRendererFeature>(unique, types, renderer, "Ho-GeometryBuffer");
        Ensure<HoObjectBufferRendererFeature>(unique, types, renderer, "Ho-ObjectBuffer");
        Ensure<HoSurfaceBufferRendererFeature>(unique, types, renderer, "Ho-SurfaceBuffer");
        Ensure<HoAttributeCompositeRendererFeature>(unique, types, renderer, "Ho-AttributeComposite");
        Ensure<HoCharacterShadowRendererFeature>(unique, types, renderer, "Ho-CharacterShadow");
        Ensure<HoShadowCastRendererFeature>(unique, types, renderer, "Ho-ShadowCast");
        Ensure<HoAtmosphereRendererFeature>(unique, types, renderer, "Ho-Atmosphere");
        Ensure<HoGTAORendererFeature>(unique, types, renderer, "Ho-GTAO");
        Ensure<HoRTShadowRendererFeature>(unique, types, renderer, "Ho-RTShadow");
        Ensure<HoVolumeLightRendererFeature>(unique, types, renderer, "Ho-VolumeLight");
        Ensure<WeightedOITRendererFeature>(unique, types, renderer, "Ho-WeightedOIT");
        Ensure<HoCharacterSpecializationRendererFeature>(unique, types, renderer, "Ho-CharacterSpecialization");
        Ensure<ScreenProcessRendererFeature>(unique, types, renderer, "Ho-ScreenProcess");
        Ensure<ImageProcessRendererFeature>(unique, types, renderer, "Ho-ImageProcess");
        Ensure<HoPlanarReflectionRendererFeature>(unique, types, renderer, "Ho-PlanarReflection");
        Ensure<HoRTGIRendererFeature>(unique, types, renderer, "Ho-RTGI");

        var order = new Type[]
        {
            typeof(HoRTRendererFeature), typeof(HoGeometryDataRendererFeature), typeof(HoGeometryBufferRendererFeature),
            typeof(HoObjectBufferRendererFeature), typeof(HoSurfaceBufferRendererFeature), typeof(HoAttributeCompositeRendererFeature),
            typeof(HoCharacterShadowRendererFeature), typeof(HoShadowCastRendererFeature), typeof(HoAtmosphereRendererFeature),
            typeof(HoGTAORendererFeature), typeof(HoRTShadowRendererFeature), typeof(HoVolumeLightRendererFeature),
            typeof(WeightedOITRendererFeature), typeof(HoCharacterSpecializationRendererFeature), typeof(ScreenProcessRendererFeature),
            typeof(ImageProcessRendererFeature), typeof(HoPlanarReflectionRendererFeature), typeof(HoRTGIRendererFeature)
        };
        var ordered = new List<ScriptableRendererFeature>();
        foreach (var type in order)
        {
            var feature = unique.FirstOrDefault(x => x.GetType() == type);
            if (feature != null && !ordered.Contains(feature)) ordered.Add(feature);
        }
        foreach (var feature in unique)
            if (!ordered.Contains(feature)) ordered.Add(feature);
        foreach (var feature in ordered) feature.SetActive(true);
        renderer.rendererFeatures.Clear();
        renderer.rendererFeatures.AddRange(ordered);

        var plr = ordered.OfType<HoPlanarReflectionRendererFeature>().First();
        plr.FeatureSettings.renderGameView = true;
        plr.FeatureSettings.renderSceneView = true;
        plr.FeatureSettings.maxSurfacesPerCamera = 8;
        plr.FeatureSettings.compositeEnabled = false;
        foreach (var screen in ordered.OfType<ScreenProcessRendererFeature>()) screen.UseVolumes = true;
        foreach (var image in ordered.OfType<ImageProcessRendererFeature>()) image.UseVolumes = true;
        var rtgi = ordered.OfType<HoRTGIRendererFeature>().First();
        rtgi.SetActive(true);
        EditorUtility.SetDirty(renderer);
    }

    static void Ensure<T>(List<ScriptableRendererFeature> list, HashSet<Type> types, UniversalRendererData renderer, string name)
        where T : ScriptableRendererFeature
    {
        if (types.Contains(typeof(T))) return;
        var feature = ScriptableObject.CreateInstance<T>();
        feature.name = name;
        AssetDatabase.AddObjectToAsset(feature, renderer);
        feature.Create();
        list.Add(feature);types.Add(typeof(T));
    }

    static void MigrateScenes()
    {
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        foreach (var recipe in HoMmdStageBuilder.Recipes())
        {
            string path = HoMmdStageBuilder.Root + recipe.name + "/" + recipe.name + ".unity";
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var environment = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<HoMmdStageEnvironment>(true)).Single();
            environment.pipeline = pipeline;
            EditorUtility.SetDirty(environment);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
