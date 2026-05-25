using System.Collections.Generic;
using lilToon.URP.Extensions.Debugging;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.Debugging
{
    internal static class LilUrpDebugShaderCollectionGenerator
    {
        private const string MenuPath = "lilToon URP Extensions/Debug/Generate Debug Shader Collection";
        private const string OutputDirectory = "Assets/lilToon URP Extensions/Debug";
        private const string OutputPath = OutputDirectory + "/LilUrpDebugShaders.shadervariants";

        [MenuItem(MenuPath, false, 2200)]
        private static void Generate()
        {
            EnsureFolder(OutputDirectory);

            ShaderVariantCollection collection = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(OutputPath);
            if (collection == null)
            {
                collection = new ShaderVariantCollection();
                AssetDatabase.CreateAsset(collection, OutputPath);
            }
            else
            {
                collection.Clear();
            }

            int shaderCount = 0;
            int variantCount = 0;
            int missingCount = 0;
            HashSet<string> processedShaders = new HashSet<string>();

            foreach (HoDebugViewInfo entry in HoDebugViewRegistry.ShaderCollectionViews)
            {
                string shaderKey = entry.ShaderName + "|" + entry.ShaderAssetPath;
                if (!processedShaders.Add(shaderKey))
                {
                    continue;
                }

                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(entry.ShaderAssetPath);
                if (shader == null)
                {
                    shader = Shader.Find(entry.ShaderName);
                }

                if (shader == null)
                {
                    missingCount++;
                    Debug.LogWarning($"[lilToon URP Extensions] Debug shader not found for {entry.FeatureName}: {entry.ShaderName} ({entry.ShaderAssetPath}). {entry.MissingFallback}");
                    continue;
                }

                shaderCount++;
                int addedVariants = AddVariants(collection, shader);
                variantCount += addedVariants;
                if (addedVariants == 0)
                {
                    Debug.LogWarning($"[lilToon URP Extensions] Debug shader variant was not added: {entry.ShaderName}");
                }
            }

            EditorUtility.SetDirty(collection);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = collection;
            EditorGUIUtility.PingObject(collection);
            Debug.Log($"[lilToon URP Extensions] Generated debug shader collection at {OutputPath}. Shaders: {shaderCount}, variants: {variantCount}, missing: {missingCount}.");
        }

        private static int AddVariants(ShaderVariantCollection collection, Shader shader)
        {
            int count = 0;
            if (collection.Add(new ShaderVariantCollection.ShaderVariant(shader, PassType.ScriptableRenderPipelineDefaultUnlit)))
            {
                count++;
            }

            if (collection.Add(new ShaderVariantCollection.ShaderVariant(shader, PassType.Normal)))
            {
                count++;
            }

            return count;
        }

        private static void EnsureFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            if (parts.Length == 0)
            {
                return;
            }

            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

    }
}
