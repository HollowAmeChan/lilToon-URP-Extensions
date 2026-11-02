#pragma warning disable CS0618
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Hollow.HoMmdStages;
using lilToon.URP.Extensions.ShadowCast;

public static class HoRPShadowModeValidation
{
    static string Arg(string key, string fallback = null)
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }

    public static void RunBatch()
    {
        try
        {
            if (!Application.isBatchMode || !File.Exists("Library/HoLightingBakeAudit.private"))
                throw new InvalidOperationException("Private audit project only.");
            string scenePath = Arg("-hoScene");
            string output = Arg("-hoOutput");
            if (string.IsNullOrEmpty(scenePath) || string.IsNullOrEmpty(output))
                throw new ArgumentException("-hoScene and -hoOutput are required.");
            if (!int.TryParse(Arg("-hoMode"), out int mode) || mode < 1 || mode > 2)
                throw new ArgumentException("-hoMode must be 1 (Main) or 2 (Full).");

            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var rendererDataField = typeof(UniversalRenderPipelineAsset).GetField(
                "m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
            var rendererData = rendererDataField?.GetValue(pipeline) as ScriptableRendererData[];
            int shadowFeatureCount = 0;
            foreach (var feature in rendererData?.SelectMany(data => data.rendererFeatures) ?? Enumerable.Empty<ScriptableRendererFeature>())
            {
                if (feature is HoShadowCastRendererFeature shadowCast)
                {
                    shadowCast.SetActive(true);
                    shadowFeatureCount++;
                }
            }
            if (shadowFeatureCount == 0)
                throw new InvalidOperationException("The isolated renderer has no HoShadowCast feature.");

            int property = Shader.PropertyToID("_HoRPShadowMode");
            int rendererCount = 0;
            int verifiedPropertyCount = 0;

            Directory.CreateDirectory(output);
            HoMmdStageCapture.Begin(scenePath, Path.Combine(output, "main.png"), 1280, 720,
                result => {
                    var snapshot = HoShadowCastRuntimeDiagnostics.CurrentSnapshot;
                    string accepted = string.Join(", ", snapshot.AcceptedLights.Select(light => light.Name));
                    Debug.Log("HO_RP_MODE_RENDER=" + mode + " renderers=" + rendererCount +
                        " verifiedProperties=" + verifiedPropertyCount +
                        " shadowFeatures=" + shadowFeatureCount +
                        " shadowFrame=" + snapshot.IsValid + " accepted=" + accepted + " " + result);
                    EditorApplication.Exit(0);
                },
                error => {
                    Debug.LogException(error);
                    EditorApplication.Exit(1);
                }, camera =>
                {
                    // Capture opens the scene again. Overrides must target this loaded instance.
                    foreach (var renderer in camera.gameObject.scene.GetRootGameObjects()
                                 .SelectMany(root => root.GetComponentsInChildren<Renderer>(true)))
                    {
                        var materials = renderer.sharedMaterials;
                        for (int index = 0; index < materials.Length; index++)
                        {
                            var material = materials[index];
                            if (material == null || !material.HasProperty(property)) continue;
                            var block = new MaterialPropertyBlock();
                            renderer.GetPropertyBlock(block, index);
                            block.SetFloat(property, mode);
                            renderer.SetPropertyBlock(block, index);
                            rendererCount++;
                            var verify = new MaterialPropertyBlock();
                            renderer.GetPropertyBlock(verify, index);
                            if (Mathf.Approximately(verify.GetFloat(property), mode)) verifiedPropertyCount++;
                        }
                    }
                    if (rendererCount == 0 || verifiedPropertyCount != rendererCount)
                        throw new InvalidOperationException("Material mode overrides were not applied.");
                }, 64);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }
}
