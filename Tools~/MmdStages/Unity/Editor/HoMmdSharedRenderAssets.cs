using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.HoMmdStages;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.CharacterShadow;
using lilToon.URP.Extensions.CharacterSpecialization;
using lilToon.URP.Extensions.Debugging;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.GeometryData;
using lilToon.URP.Extensions.GTAO;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.OIT;
using lilToon.URP.Extensions.PostProcessing;
using lilToon.URP.Extensions.PlanarReflection;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTDI;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.RTShadow;
using lilToon.URP.Extensions.ShadowCast;
using lilToon.URP.Extensions.Shadows;
using lilToon.URP.Extensions.SSGI;
using lilToon.URP.Extensions.SubsurfaceScattering;
using lilToon.URP.Extensions.SurfaceBuffer;
using lilToon.URP.Extensions.VolumeLight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// The project-wide Ho renderer boundary. MMD scenes deliberately use the
// existing PC assets so no second RendererData/RenderPipelineAsset family is
// scattered through the project. Volume profiles still remain scene-owned.
public static class HoMmdSharedRenderAssets
{
    public const string Root = "Assets/Settings/";
    public const string RendererPath = Root + "PC_Renderer.asset";
    public const string PipelinePath = Root + "PC_RPAsset.asset";

    static readonly Type[] LegacyFeatureOrder =
    {
        typeof(HoGeometryDataRendererFeature), typeof(HoRTRendererFeature), typeof(HoCharacterShadowRendererFeature),
        typeof(HoShadowCastRendererFeature), typeof(HoAtmosphereRendererFeature), typeof(HoPlanarReflectionRendererFeature),
        typeof(HoGeometryBufferRendererFeature), typeof(HoObjectBufferRendererFeature), typeof(HoSurfaceBufferRendererFeature),
        typeof(HoRTShadowRendererFeature), typeof(HoAttributeCompositeRendererFeature), typeof(HoGTAORendererFeature),
        typeof(HoSSGIRendererFeature), typeof(HoRTDIRendererFeature), typeof(HoRTGIRendererFeature),
        typeof(HoSubsurfaceScatteringRendererFeature), typeof(HoVolumeLightRendererFeature), typeof(WeightedOITRendererFeature),
        typeof(HoCharacterSpecializationRendererFeature), typeof(ScreenProcessRendererFeature), typeof(ImageProcessRendererFeature),
        typeof(HoDebugTileRendererFeature)
    };

    internal static HoShadowsRendererFeature ActiveHoShadows(UniversalRendererData renderer) =>
        renderer == null ? null : renderer.rendererFeatures.OfType<HoShadowsRendererFeature>().FirstOrDefault(feature => feature.isActive);

    internal static HoShadowCastRendererFeature LegacyShadowCast(UniversalRendererData renderer) =>
        renderer == null || ActiveHoShadows(renderer) != null ? null : renderer.rendererFeatures.OfType<HoShadowCastRendererFeature>().FirstOrDefault();

    static bool IsLegacyShadowProducer(Type type) =>
        type == typeof(HoShadowCastRendererFeature) || type == typeof(HoCharacterShadowRendererFeature) ||
        type == typeof(HoRTShadowRendererFeature);

    internal static IEnumerable<Type> CanonicalFeatureOrder(bool unified, bool includeDebugTile = false)
    {
        foreach (var type in LegacyFeatureOrder)
        {
            if (!includeDebugTile && type == typeof(HoDebugTileRendererFeature))
                continue;
            if (unified && type == typeof(WeightedOITRendererFeature))
                yield return typeof(HoShadowsRendererFeature);
            yield return type;
        }
    }

    internal static List<ScriptableRendererFeature> OrderFeatures(IEnumerable<ScriptableRendererFeature> features,
        bool unified, bool includeDebugTile = false)
    {
        var existing = features.Where(feature => feature != null).ToArray();
        var ordered = new List<ScriptableRendererFeature>();
        foreach (var type in CanonicalFeatureOrder(unified, includeDebugTile))
        {
            var feature = existing.FirstOrDefault(value => value.GetType() == type &&
                (type != typeof(HoShadowsRendererFeature) || value.isActive));
            if (feature != null) ordered.Add(feature);
        }
        foreach (var feature in existing)
            if (!ordered.Contains(feature)) ordered.Add(feature);
        return ordered;
    }

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
            EditorSceneManager.OpenScene("Assets/Editor/MMD\u5236\u4F5C\u9A8C\u8BC1/WTD\u5236\u4F5C\u53C2\u8003/WTD\u9884\u89C8.unity", OpenSceneMode.Single);
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
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (renderer == null || pipeline == null) throw new InvalidOperationException("固定 PC_Renderer/PC_RPAsset 不存在。");

        var pipelineObject = new SerializedObject(pipeline);
        var rendererList = pipelineObject.FindProperty("m_RendererDataList");
        if (rendererList == null) throw new InvalidOperationException("PC_RPAsset 缺少 Renderer 列表。");
        var firstRenderer = rendererList.arraySize > 0 ? rendererList.GetArrayElementAtIndex(0).objectReferenceValue : null;
        if (firstRenderer != null && firstRenderer != renderer)
            throw new InvalidOperationException("PC_RPAsset 的 Renderer 索引 0 未指向 PC_Renderer，未修改资产。");

        if (ActiveHoShadows(renderer) == null)
        {
            RemoveDuplicateFeatureSubassets(renderer);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(RendererPath, ImportAssetOptions.ForceSynchronousImport);
            renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        }
        ConfigureRenderer(renderer);
        if (firstRenderer == null)
        {
            if (rendererList.arraySize == 0) rendererList.arraySize = 1;
            rendererList.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
        }
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
        bool unified = ActiveHoShadows(renderer) != null;
        var features = renderer.rendererFeatures.Where(x => x != null).ToList();
        var unique = new List<ScriptableRendererFeature>();
        var types = new HashSet<Type>();
        foreach (var feature in features)
        {
            var type = feature.GetType();
            if (types.Add(type) || (unified && (IsLegacyShadowProducer(type) || type == typeof(HoShadowsRendererFeature)))) unique.Add(feature);
            else
            {
                renderer.rendererFeatures.Remove(feature);
                Object.DestroyImmediate(feature, true);
            }
        }
        Ensure<HoRTRendererFeature>(unique, types, renderer, "Ho-RT");
        Ensure<HoGeometryDataRendererFeature>(unique, types, renderer, "Ho-GeometryData");
        Ensure<HoGeometryBufferRendererFeature>(unique, types, renderer, "Ho-GeometryBuffer");
        Ensure<HoObjectBufferRendererFeature>(unique, types, renderer, "Ho-ObjectBuffer");
        Ensure<HoSurfaceBufferRendererFeature>(unique, types, renderer, "Ho-SurfaceBuffer");
        Ensure<HoAttributeCompositeRendererFeature>(unique, types, renderer, "Ho-AttributeComposite");
        if (!unified)
        {
            Ensure<HoCharacterShadowRendererFeature>(unique, types, renderer, "Ho-CharacterShadow");
            Ensure<HoShadowCastRendererFeature>(unique, types, renderer, "Ho-ShadowCast");
        }
        Ensure<HoAtmosphereRendererFeature>(unique, types, renderer, "Ho-Atmosphere");
        Ensure<HoGTAORendererFeature>(unique, types, renderer, "Ho-GTAO");
        if (!unified) Ensure<HoRTShadowRendererFeature>(unique, types, renderer, "Ho-RTShadow");
        Ensure<HoVolumeLightRendererFeature>(unique, types, renderer, "Ho-VolumeLight");
        Ensure<WeightedOITRendererFeature>(unique, types, renderer, "Ho-WeightedOIT");
        Ensure<HoCharacterSpecializationRendererFeature>(unique, types, renderer, "Ho-CharacterSpecialization");
        Ensure<ScreenProcessRendererFeature>(unique, types, renderer, "Ho-ScreenProcess");
        Ensure<ImageProcessRendererFeature>(unique, types, renderer, "Ho-ImageProcess");
        Ensure<HoPlanarReflectionRendererFeature>(unique, types, renderer, "Ho-PlanarReflection");
        Ensure<HoRTGIRendererFeature>(unique, types, renderer, "Ho-RTGI");
        Ensure<HoSSGIRendererFeature>(unique, types, renderer, "Ho-SSGI");
        Ensure<HoRTDIRendererFeature>(unique, types, renderer, "Ho-RTDI");
        Ensure<HoDebugTileRendererFeature>(unique, types, renderer, "Ho-DebugTile");

        var ordered = OrderFeatures(unique, unified, includeDebugTile: true);
        var postFeatures=ordered.Where(x=>x.GetType().Name=="HoPostProcessRendererFeature"||x.GetType().Name=="ShoostPostProcessRendererFeature").ToList();
        foreach(var feature in postFeatures)ordered.Remove(feature);
        int debugIndex=ordered.FindIndex(x=>x.GetType()==typeof(HoDebugTileRendererFeature));
        if(debugIndex<0)ordered.AddRange(postFeatures);else ordered.InsertRange(debugIndex,postFeatures);
        var required = new HashSet<Type>(CanonicalFeatureOrder(unified, includeDebugTile: true));
        var disabled = new HashSet<Type>{typeof(HoSSGIRendererFeature),typeof(HoRTDIRendererFeature),typeof(HoDebugTileRendererFeature)};
        foreach (var feature in ordered)
        {
            var type = feature.GetType();
            if (unified && (feature is HoShadowsRendererFeature || IsLegacyShadowProducer(type))) continue;
            if (required.Contains(type)) feature.SetActive(!disabled.Contains(type));
        }
        ApplyFeatureOrder(renderer, ordered);

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
        renderer.SetDirty();
        var validate=typeof(ScriptableRendererData).GetMethod("ValidateRendererFeatures",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        if(validate!=null)validate.Invoke(renderer,null);
        AssetDatabase.SaveAssets();
        Debug.Log("MMD_SHARED_RENDERER "+string.Join(",",ordered.Select(x=>{AssetDatabase.TryGetGUIDAndLocalFileIdentifier(x,out _,out long id);return x.GetType().Name+"@"+AssetDatabase.GetAssetPath(x)+"#"+AssetDatabase.Contains(x)+"/"+id;})));
        Debug.Log("MMD_SHARED_SUBASSETS "+string.Join(",",AssetDatabase.LoadAllAssetsAtPath(RendererPath).OfType<ScriptableRendererFeature>().Select(x=>x.GetType().Name)));
    }

    internal static void ApplyFeatureOrder(UniversalRendererData renderer, List<ScriptableRendererFeature> ordered)
    {
        var serialized = new SerializedObject(renderer);
        var list = serialized.FindProperty("m_RendererFeatures");
        var map = serialized.FindProperty("m_RendererFeatureMap");
        list.arraySize = ordered.Count;
        map.arraySize = ordered.Count;
        for (int i = 0; i < ordered.Count; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = ordered[i];
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ordered[i], out _, out long localId);
            map.GetArrayElementAtIndex(i).longValue = localId;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        renderer.SetDirty();
        EditorUtility.SetDirty(renderer);
    }

    static void RemoveDuplicateFeatureSubassets(UniversalRendererData renderer)
    {
        var all=renderer.rendererFeatures.Where(x=>x!=null).ToList();
        var unique=new List<ScriptableRendererFeature>();var types=new HashSet<Type>();
        foreach(var feature in all)
        {
            if(types.Add(feature.GetType()))unique.Add(feature);
            else{renderer.rendererFeatures.Remove(feature);Object.DestroyImmediate(feature,true);}
        }
        string path=AssetDatabase.GetAssetPath(renderer);
        foreach(var orphan in AssetDatabase.LoadAllAssetsAtPath(path).OfType<ScriptableRendererFeature>().Where(x=>!unique.Contains(x)).ToArray())
            Object.DestroyImmediate(orphan,true);
        renderer.rendererFeatures.Clear();renderer.rendererFeatures.AddRange(unique);
        var so=new SerializedObject(renderer);var list=so.FindProperty("m_RendererFeatures");var map=so.FindProperty("m_RendererFeatureMap");
        if(list!=null){list.arraySize=unique.Count;for(int i=0;i<unique.Count;i++)list.GetArrayElementAtIndex(i).objectReferenceValue=unique[i];}
        if(map!=null){map.arraySize=unique.Count;for(int i=0;i<unique.Count;i++){AssetDatabase.TryGetGUIDAndLocalFileIdentifier(unique[i],out _,out long id);map.GetArrayElementAtIndex(i).longValue=id;}}
        so.ApplyModifiedProperties();renderer.SetDirty();EditorUtility.SetDirty(renderer);
    }

    static void Ensure<T>(List<ScriptableRendererFeature> list, HashSet<Type> types, UniversalRendererData renderer, string name)
        where T : ScriptableRendererFeature
    {
        if (types.Contains(typeof(T))) return;
        var feature = ScriptableObject.CreateInstance<T>();
        feature.name = name;
        AssetDatabase.AddObjectToAsset(feature, renderer);
        feature.Create();
        EditorUtility.SetDirty(feature);
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
