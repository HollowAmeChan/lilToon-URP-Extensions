using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.Debugging;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.Debugging
{
    internal static class LilUrpDebugShaderValidator
    {
        public readonly struct Result
        {
            public Result(int shaderCount, int variantCount, int missingCount, int skippedVariantCount)
            {
                ShaderCount = shaderCount;
                VariantCount = variantCount;
                MissingCount = missingCount;
                SkippedVariantCount = skippedVariantCount;
            }

            public readonly int ShaderCount;
            public readonly int VariantCount;
            public readonly int MissingCount;
            public readonly int SkippedVariantCount;

            public bool HasWarnings => MissingCount > 0 || SkippedVariantCount > 0;

            public string ToSummary()
            {
                return $"Checked debug shaders. Shaders: {ShaderCount}, supported pass variants: {VariantCount}, missing: {MissingCount}, unsupported pass types: {SkippedVariantCount}.";
            }
        }

        internal static Result Validate()
        {
            ShaderVariantCollection collection = new ShaderVariantCollection();
            int shaderCount = 0;
            int variantCount = 0;
            int missingCount = 0;
            int skippedVariantCount = 0;
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
                int addedVariants = AddVariants(collection, shader, ref skippedVariantCount);
                variantCount += addedVariants;
                if (addedVariants == 0)
                {
                    Debug.LogWarning($"[lilToon URP Extensions] Debug shader variant was not added: {entry.ShaderName}");
                }
            }

            AddDebugTileShader(collection, processedShaders, ref shaderCount, ref variantCount, ref missingCount, ref skippedVariantCount);

            Result result = new Result(shaderCount, variantCount, missingCount, skippedVariantCount);
            Debug.Log("[lilToon URP Extensions] " + result.ToSummary());
            return result;
        }

        private static int AddVariants(ShaderVariantCollection collection, Shader shader, ref int skippedVariantCount)
        {
            int count = 0;
            if (TryAddVariant(collection, shader, PassType.ScriptableRenderPipelineDefaultUnlit, ref skippedVariantCount))
            {
                count++;
            }

            if (TryAddVariant(collection, shader, PassType.Normal, ref skippedVariantCount))
            {
                count++;
            }

            return count;
        }

        private static bool TryAddVariant(ShaderVariantCollection collection, Shader shader, PassType passType, ref int skippedVariantCount)
        {
            try
            {
                return collection.Add(new ShaderVariantCollection.ShaderVariant(shader, passType));
            }
            catch (ArgumentException)
            {
                skippedVariantCount++;
                return false;
            }
        }

        private static void AddDebugTileShader(
            ShaderVariantCollection collection,
            HashSet<string> processedShaders,
            ref int shaderCount,
            ref int variantCount,
            ref int missingCount,
            ref int skippedVariantCount)
        {
            string shaderKey = HoDebugTileShaderConstants.ShaderName + "|" + HoDebugTileShaderConstants.ShaderAssetPath;
            if (!processedShaders.Add(shaderKey))
            {
                return;
            }

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(HoDebugTileShaderConstants.ShaderAssetPath);
            if (shader == null)
            {
                shader = Shader.Find(HoDebugTileShaderConstants.ShaderName);
            }

            if (shader == null)
            {
                missingCount++;
                Debug.LogWarning($"[lilToon URP Extensions] Debug tile shader not found: {HoDebugTileShaderConstants.ShaderName} ({HoDebugTileShaderConstants.ShaderAssetPath}). Automatic debug tile view will be unavailable in builds unless the shader is included by another path.");
                return;
            }

            shaderCount++;
            int addedVariants = AddVariants(collection, shader, ref skippedVariantCount);
            variantCount += addedVariants;
            if (addedVariants == 0)
            {
                Debug.LogWarning($"[lilToon URP Extensions] Debug tile shader variant was not added: {HoDebugTileShaderConstants.ShaderName}");
            }
        }

    }
}
