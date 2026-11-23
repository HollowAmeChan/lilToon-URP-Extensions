using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.CharacterShadow;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Copy this file and the sibling shader into an isolated graphics-enabled project.
// The existing HoCharacterShadowSamplingBaseline.hlsl is a prerequisite and is not replaced.
public static class HoCSOptimizationEdgeValidation
{
    private const int ImageSize = 128;
    private const float ErrorTolerance = 0.0004f;
    private const float TexelWorld = 0.0005f;
    private const string OutputDirectory = "D:/Unity_Fork/lilToon-URP-Extensions/Temp~/CharacterShadowOptimization-20261123";

    public static void Run()
    {
        try
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run edge validation in an isolated batch editor.");
            ValidateImages();
            Debug.Log("HO_CS_OPTIMIZATION_EDGE_VALIDATION_PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void ValidateImages()
    {
        bool previousAsync = ShaderUtil.allowAsyncCompilation;
        RenderTexture previousTarget = RenderTexture.active;
        Texture previousAtlas = Shader.GetGlobalTexture("_HoCSAtlas");
        Vector4 previousSize = Shader.GetGlobalVector("_HoCSAtlasSize");
        Vector4 previousPcss = Shader.GetGlobalVector("_HoCSPcssParams");
        Vector4 previousPcss2 = Shader.GetGlobalVector("_HoCSPcssParams2");
        Vector4 previousOffsetCounts = Shader.GetGlobalVector("_HoCSSampleOffsetCounts");
        Vector4[] previousOffsets = Shader.GetGlobalVectorArray("_HoCSSampleOffsets");
        Material material = null;
        RenderTexture atlas = null, target = null;
        Texture2D image = null;
        var lines = new List<string>(1284);
        int count = 0;
        float maximum = 0;
        bool passed = false;
        Directory.CreateDirectory(OutputDirectory);
        string report = Path.Combine(OutputDirectory, "edge-image-equivalence.txt");
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            Shader shader = Shader.Find("Hidden/Ho-CS/FilteringEdgeAB");
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("FilteringEdgeAB shader is missing or failed compilation.");
            material = new Material(shader);
            target = new RenderTexture(ImageSize, ImageSize, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            if (!target.Create()) throw new InvalidOperationException("Cannot allocate edge validation target.");
            image = new Texture2D(ImageSize, ImageSize, TextureFormat.RGBAFloat, false, true);
            // Each pair is (filter budget, blocker budget).
            Vector2Int[] budgets =
            {
                new Vector2Int(1, 1), new Vector2Int(2, 2), new Vector2Int(3, 3), new Vector2Int(7, 7),
                new Vector2Int(16, 8), new Vector2Int(31, 31), new Vector2Int(63, 32), new Vector2Int(64, 32)
            };
            int[] patterns = { 0, 2, 4, 5, 9 };
            float[] radii = { 2.25f, 4f };
            lines.Add("Focused baseline/current GPU image comparison; threshold=0.0004; image=128x128.");
            lines.Add("640 configurations each run direct SampleDisk plus Mode2. Mode2 PCSS is balanced by budget/pattern/radius parity.");
            lines.Add("For every size/fallback/cache/budget/pattern, the two radii exercise one Mode2 PCSS-off and one PCSS-on case.");

            foreach (int size in new[] { 4096, 6144 })
            {
                if (size > SystemInfo.maxTextureSize) throw new InvalidOperationException("Requested atlas exceeds the GPU texture limit: " + size);
                atlas = new RenderTexture(size, size, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
                if (!atlas.Create()) throw new InvalidOperationException("Cannot allocate atlas " + size);
                Shader.SetGlobalVector("_HoCSAtlasSize", new Vector4(1f / size, 1f / size, size, size));
                foreach (bool fallback in new[] { false, true })
                {
                    if (fallback) material.EnableKeyword("HO_CS_FILTER_FORCE_FALLBACK");
                    else material.DisableKeyword("HO_CS_FILTER_FORCE_FALLBACK");
                    for (int patternIndex = 0; patternIndex < patterns.Length; patternIndex++)
                    {
                        int pattern = patterns[patternIndex];
                        material.SetFloat("_Pattern", pattern);
                        material.SetFloat("_PlaneSlope", pattern == 5 ? 200 : 10);
                        material.SetFloat("_CropTexels", 32);
                        material.SetFloat("_ShiftTexels", 0.37f);
                        material.DisableKeyword("HO_CS_REFERENCE_IMPLEMENTATION");
                        material.SetFloat("_Mode", -1);
                        // Avoid binding one atlas as both the generated render target and an SRV.
                        Shader.SetGlobalTexture("_HoCSAtlas", Texture2D.blackTexture);
                        Graphics.Blit(null, atlas, material);
                        Shader.SetGlobalTexture("_HoCSAtlas", atlas);
                        foreach (bool cached in new[] { false, true })
                            for (int budgetIndex = 0; budgetIndex < budgets.Length; budgetIndex++)
                                for (int radiusIndex = 0; radiusIndex < radii.Length; radiusIndex++)
                                {
                                    Vector2Int budget = budgets[budgetIndex];
                                    float radius = radii[radiusIndex];
                                    Vector4 parameters = new Vector4(0.0005f, budget.y, budget.x, radius * TexelWorld);
                                    Shader.SetGlobalVector("_HoCSPcssParams2", parameters);
                                    HoCharacterShadowShaderContract.BindSamplingPattern(parameters);
                                    if (!cached) Shader.SetGlobalVector("_HoCSSampleOffsetCounts", Vector4.zero);
                                    material.SetFloat("_Radius", radius);
                                    Compare(1, false, size, fallback, cached, budget, pattern, radius);
                                    bool pcss = ((budgetIndex + patternIndex + radiusIndex) & 1) != 0;
                                    Compare(2, pcss, size, fallback, cached, budget, pattern, radius);
                                }
                    }
                }
                Shader.SetGlobalTexture("_HoCSAtlas", Texture2D.blackTexture);
                RenderTexture.active = previousTarget;
                atlas.Release();
                Object.DestroyImmediate(atlas);
                atlas = null;
            }
            if (count != 1280) throw new InvalidOperationException("Unexpected edge case count: " + count);
            passed = true;
            lines.Add(FormattableString.Invariant($"PASS cases={count}, maximumError={maximum:R}"));
            Debug.Log(FormattableString.Invariant($"HO_CS_EDGE_IMAGE_EQUIVALENCE_PASSED cases={count} maximumError={maximum:R}"));
        }
        catch (Exception error)
        {
            lines.Add("FAIL: " + error);
            throw;
        }
        finally
        {
            lines.Add(FormattableString.Invariant($"completedCases={count}, maximumError={maximum:R}, passed={passed}"));
            File.WriteAllLines(report, lines);
            RenderTexture.active = previousTarget;
            ShaderUtil.allowAsyncCompilation = previousAsync;
            Shader.SetGlobalTexture("_HoCSAtlas", previousAtlas);
            Shader.SetGlobalVector("_HoCSAtlasSize", previousSize);
            Shader.SetGlobalVector("_HoCSPcssParams", previousPcss);
            Shader.SetGlobalVector("_HoCSPcssParams2", previousPcss2);
            Shader.SetGlobalVector("_HoCSSampleOffsetCounts", previousOffsetCounts);
            if (previousOffsets != null && previousOffsets.Length > 0)
                Shader.SetGlobalVectorArray("_HoCSSampleOffsets", previousOffsets);
            if (atlas != null) { atlas.Release(); Object.DestroyImmediate(atlas); }
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (image != null) Object.DestroyImmediate(image);
            if (material != null) Object.DestroyImmediate(material);
        }

        void Compare(int mode, bool pcss, int size, bool fallback, bool cached,
            Vector2Int budget, int pattern, float radius)
        {
            Shader.SetGlobalVector("_HoCSPcssParams", new Vector4(pcss ? 1 : 0, Mathf.Tan(Mathf.Deg2Rad), 0.072f, 0.04f));
            material.SetFloat("_Mode", mode);
            material.EnableKeyword("HO_CS_REFERENCE_IMPLEMENTATION");
            Color[] baseline = Read();
            material.DisableKeyword("HO_CS_REFERENCE_IMPLEMENTATION");
            Color[] optimized = Read();
            float error = 0;
            for (int i = 0; i < baseline.Length; i++)
            {
                float a = baseline[i].r, b = optimized[i].r;
                if (float.IsNaN(a) || float.IsInfinity(a) || float.IsNaN(b) || float.IsInfinity(b))
                    throw new InvalidOperationException("Non-finite edge image sample.");
                error = Mathf.Max(error, Mathf.Abs(a - b));
            }
            maximum = Mathf.Max(maximum, error);
            count++;
            string context = FormattableString.Invariant($"atlas={size}, fallback={fallback}, cached={cached}, filter={budget.x}, blocker={budget.y}, pattern={pattern}, radius={radius:R}, mode={mode}, pcss={pcss}, maxError={error:R}");
            lines.Add(context);
            if (error > ErrorTolerance) throw new InvalidOperationException("Edge image changed: " + context);
        }

        Color[] Read()
        {
            Graphics.Blit(null, target, material);
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, ImageSize, ImageSize), 0, 0);
            image.Apply();
            return image.GetPixels();
        }
    }
}
