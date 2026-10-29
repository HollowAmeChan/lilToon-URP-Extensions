using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoMmdStages;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.PlanarReflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Repair authored H2Cu receivers without rebuilding lights, profiles or renderer assets.</summary>
public static class HoMmdPlrRepair
{
    public static void RunBatch()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the current scene before repairing PLR receivers.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (var recipe in HoMmdStageBuilder.Recipes())
            {
                string path = HoMmdStageBuilder.Root + recipe.name + "/" + recipe.name + ".unity";
                if (!File.Exists(path)) continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int count = Repair(scene);
                if (count == 0) continue;
                EditorSceneManager.SaveScene(scene);
                Debug.Log("MMD_PLR_RECEIVERS " + recipe.name + "=" + count);
            }
            AssetDatabase.SaveAssets();
        }
        finally
        {
            if (setup.Any(x => x.isLoaded && !string.IsNullOrEmpty(x.path)) && setup.Any(x => x.isActive))
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    public static int Repair(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var receivers = roots.SelectMany(x => x.GetComponentsInChildren<HoPlanarReflectionSurface>(true))
            .Select(x => (new SerializedObject(x).FindProperty("targetRenderer").objectReferenceValue as Renderer)
                ?? x.GetComponent<Renderer>()).Where(x => x != null).Distinct().ToArray();
        if (receivers.Length == 0) return 0;
        var assigned = new HashSet<Renderer>();
        var list = new List<Renderer>();
        foreach (var group in roots.SelectMany(x => x.GetComponentsInChildren<HoObjectBufferGroup>(true)))
        {
            group.GetAssignedRenderers(list);
            foreach (var renderer in list) assigned.Add(renderer);
        }
        var missing = receivers.Where(x => !assigned.Contains(x)).OrderBy(x => x.name).ToArray();
        if (missing.Length > 0)
        {
            var env = roots.SelectMany(x => x.GetComponentsInChildren<HoMmdStageEnvironment>(true)).First();
            var holder = new GameObject("反射接收面身份");
            holder.transform.SetParent(env.transform, false);
            var group = holder.AddComponent<HoObjectBufferGroup>();
            group.parts = missing.Select(x => new HoObjectBufferPartEntry
            {
                name = x.name, includeChildren = false, renderers = new Object[] { x },
                tags = HoObjectBufferPartTags.None
            }).ToList();
            group.Apply();
            EditorUtility.SetDirty(group);
        }
        return receivers.Length;
    }

    public static bool HaveIdentities(Scene scene)
    {
        var assigned = new HashSet<Renderer>();
        var list = new List<Renderer>();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var group in root.GetComponentsInChildren<HoObjectBufferGroup>())
            {
                group.GetAssignedRenderers(list);
                foreach (var renderer in list) assigned.Add(renderer);
            }
        return scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<HoPlanarReflectionSurface>())
            .All(x => assigned.Contains((new SerializedObject(x).FindProperty("targetRenderer").objectReferenceValue as Renderer)
                ?? x.GetComponent<Renderer>()));
    }
}
