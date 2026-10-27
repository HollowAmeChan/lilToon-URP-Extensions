using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.CharacterShadow
{
    public static partial class HoCharacterShadowValidation
    {
        // Real GPU sampling of a known 4096 atlas; independent of mesh rasterization and scene setup.
        public static void ValidateFiltering()
        {
            Require(Application.isBatchMode, "Filtering validation runs in an isolated batch editor only");
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            RenderTexture previousTarget = RenderTexture.active;
            Texture previousAtlas = Shader.GetGlobalTexture("_HoCSAtlas");
            Vector4 previousSize = Shader.GetGlobalVector("_HoCSAtlasSize");
            Vector4 previousPcss = Shader.GetGlobalVector("_HoCSPcssParams");
            Vector4 previousPcss2 = Shader.GetGlobalVector("_HoCSPcssParams2");
            var created = new List<Object>();
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                Shader shader = Shader.Find("Hidden/Ho-CharacterShadow/FilteringProbe");
                Require(shader != null && !ShaderUtil.ShaderHasError(shader), "Filtering probe shader failed");
                var material = new Material(shader); created.Add(material);
                var atlas = new RenderTexture(4096, 4096, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear); created.Add(atlas);
                var target = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); created.Add(target);
                Require(atlas.Create() && target.Create(), "Filtering render targets unavailable");
                string output = Path.Combine(Path.GetTempPath(), "HoCSFilteringValidation", SystemInfo.graphicsDeviceType.ToString());
                Directory.CreateDirectory(output);
                var report = new List<string>();
                Shader.SetGlobalVector("_HoCSAtlasSize", new Vector4(1f / 4096, 1f / 4096, 4096, 4096));
                Shader.SetGlobalVector("_HoCSPcssParams", new Vector4(1, Mathf.Tan(Mathf.Deg2Rad), 0.072f, 0.008f));
                Shader.SetGlobalVector("_HoCSPcssParams2", new Vector4(0, 32, 64, 0.0001f));

                Color[] Sample(string label, int pattern, int mode, float radius = 0, float crop = 16, float shift = 0, float slope = 0.1f, float angle = 0)
                {
                    material.SetFloat("_Mode", -1); material.SetFloat("_Pattern", pattern);
                    material.SetFloat("_PlaneSlope", slope);
                    material.SetFloat("_EdgeAngle", angle * Mathf.Deg2Rad);
                    Graphics.Blit(null, atlas, material);
                    Shader.SetGlobalTexture("_HoCSAtlas", atlas);
                    material.SetFloat("_Mode", mode); material.SetFloat("_Radius", radius);
                    material.SetFloat("_CropTexels", crop); material.SetFloat("_ShiftTexels", shift);
                    Graphics.Blit(null, target, material);
                    RenderTexture.active = target;
                    var image = new Texture2D(512, 512, TextureFormat.RGBAFloat, false, true);
                    try
                    {
                        image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); image.Apply();
                        Color[] pixels = image.GetPixels();
                        var png = new Texture2D(512, 512, TextureFormat.RGBA32, false, true);
                        try { png.SetPixels(pixels); png.Apply(); File.WriteAllBytes(Path.Combine(output, label + ".png"), png.EncodeToPNG()); }
                        finally { Object.DestroyImmediate(png); }
                        float min = float.MaxValue, max = float.MinValue; double sum = 0;
                        foreach (Color pixel in pixels)
                        {
                            Require(!float.IsNaN(pixel.r) && !float.IsInfinity(pixel.r), label + " has non-finite visibility");
                            min = Mathf.Min(min, pixel.r); max = Mathf.Max(max, pixel.r); sum += pixel.r;
                        }
                        string metric = $"{label}: min={min:F6}, max={max:F6}, mean={sum / pixels.Length:F6}";
                        report.Add(metric); Debug.Log("[Ho-CS Filtering] " + metric);
                        return pixels;
                    }
                    finally { RenderTexture.active = previousTarget; Object.DestroyImmediate(image); }
                }
                float Maximum(Color[] pixels) { float value = float.MinValue; foreach (Color p in pixels) value = Mathf.Max(value, p.r); return value; }
                float Minimum(Color[] pixels) { float value = float.MaxValue; foreach (Color p in pixels) value = Mathf.Min(value, p.r); return value; }
                var raw = Sample("raw-point", 0, 0);
                var filtered = Sample("small-radius", 0, 1, 0.2f);
                var levels = new HashSet<int>();
                float maxJump = 0;
                for (int y = 1; y < 511; y++)
                    for (int x = 1; x < 511; x++)
                    {
                        int i = y * 512 + x;
                        levels.Add(Mathf.RoundToInt(filtered[i].r * 65535));
                        maxJump = Mathf.Max(maxJump, Mathf.Abs(filtered[i].r - filtered[i + 1].r));
                    }
                Require(Minimum(raw) < 0.01f && Maximum(raw) > 0.99f, "The reference edge is missing");
                Require(levels.Count > 256 && maxJump < 0.05f, $"Small filter is quantized: levels={levels.Count}, jump={maxJump}");
                Require(Maximum(Sample("gather-reference-error", 4, 3)) < 0.00002f, "Gather PCF differs from four independent point comparisons");
                Require(Minimum(Sample("sloped-unoccluded", 1, 1, 8)) > 0.999f, "Sloped plane falsely shadows itself");
                Require(Minimum(Sample("sloped-unoccluded-pcss", 1, 2)) > 0.999f, "PCSS finds the receiver itself as a blocker");
                Require(Minimum(Sample("grazing-unoccluded", 1, 1, 16, slope: 10)) > 0.999f, "Grazing receiver falsely shadows itself");
                Require(Minimum(Sample("grazing-unoccluded-pcss", 1, 2, slope: 10)) > 0.999f, "PCSS finds a grazing receiver as a blocker");
                Require(Maximum(Sample("sloped-occluded", 3, 1, 8)) < 0.001f, "Receiver-plane correction leaks through an actual occluder");
                // A nearly light-parallel receiver lies wholly behind/in front of a foreign
                // surface. Unbounded RPDB used to turn it into a half-lit triangle as radius grew.
                foreach (float radius in new[] { 0f, 1f, 1.01f, 1.6f, 2f, 8f, 16f })
                {
                    Require(Maximum(Sample("foreign-shadow-r" + radius, 5, 1, radius, crop: 0.125f, slope: 200)) < 0.001f,
                        "PCF invented light through a foreign surface at radius " + radius);
                    Require(Minimum(Sample("foreign-lit-r" + radius, 6, 1, radius, crop: 0.125f, slope: 200)) > 0.999f,
                        "PCF invented shadow on a fully lit receiver at radius " + radius);
                }
                Require(Maximum(Sample("foreign-shadow-pcss", 5, 2, crop: 0.125f, slope: 200)) < 0.001f,
                    "PCSS receiver-plane extrapolation invented light through a foreign surface");
                Require(Minimum(Sample("foreign-lit-pcss", 6, 2, crop: 0.125f, slope: 200)) > 0.999f,
                    "PCSS receiver-plane extrapolation invented a shadow");
                // On reversed-Z platforms the left tile is lit; on conventional Z it is shadowed.
                var tile = Sample("tile-boundary", 2, 1, 16);
                Require(SystemInfo.usesReversedZBuffer ? Minimum(tile) > 0.999f : Maximum(tile) < 0.001f, "Filtering reads the adjacent atlas tile");
                material.EnableKeyword("HO_CS_FILTER_FORCE_FALLBACK");
                Require(Maximum(Sample("fallback-reference-error", 4, 3)) < 0.00002f, "Non-Gather PCF differs from independent point comparisons");
                Require(Minimum(Sample("fallback-grazing-unoccluded", 1, 1, 16, slope: 10)) > 0.999f, "Non-Gather receiver correction failed");
                material.DisableKeyword("HO_CS_FILTER_FORCE_FALLBACK");
                Shader.SetGlobalVector("_HoCSPcssParams", new Vector4(1, Mathf.Tan(Mathf.Deg2Rad), 0.072f, 0.2f));
                Color invariant = Sample("depth-range-invariance", 0, 4)[0];
                float expected = Mathf.Tan(Mathf.Deg2Rad);
                Require(Mathf.Abs(invariant.r - expected) < 1e-6f && Mathf.Abs(invariant.g - expected) < 1e-6f,
                    "Directional penumbra depends on the depth crop");
                Require(Mathf.Abs(invariant.b - 16) < 1e-5f, "World-space filter radius was silently reduced");
                Shader.SetGlobalVector("_HoCSPcssParams", new Vector4(1, Mathf.Tan(Mathf.Deg2Rad), 0.072f, 0.008f));
                var user = Sample("user-4096-pcss", 0, 2, crop: 128);
                Require(Minimum(user) < 0.01f && Maximum(user) > 0.99f, "User settings lose the shadow core or lit region");
                var shifted = Sample("user-4096-shifted", 0, 2, crop: 128, shift: 1f / 64);
                float maxDelta = 0;
                for (int i = 0; i < user.Length; i++) maxDelta = Mathf.Max(maxDelta, Mathf.Abs(user[i].r - shifted[i].r));
                Require(maxDelta < 0.08f, "Subtexel motion changes the PCSS sampling pattern: " + maxDelta);
                var blockerBase = Sample("blocker-depth-step", 7, 5, crop: 32);
                var blockerShift = Sample("blocker-depth-step-shifted", 7, 5, crop: 32, shift: 1f / 128);
                float maxBlockerDepthDelta = 0;
                for (int i = 0; i < blockerBase.Length; i++)
                    maxBlockerDepthDelta = Mathf.Max(maxBlockerDepthDelta, Mathf.Abs(blockerBase[i].r - blockerShift[i].r));
                Require(maxBlockerDepthDelta < 0.0002f, "Blocker average jumps across texel boundaries: " + maxBlockerDepthDelta);
                var previousBase = Sample("previous-blocker-depth-step", 7, 7, crop: 32);
                var previousShift = Sample("previous-blocker-depth-step-shifted", 7, 7, crop: 32, shift: 1f / 128);
                float previousMaxDepthDelta = 0;
                for (int i = 0; i < previousBase.Length; i++)
                    previousMaxDepthDelta = Mathf.Max(previousMaxDepthDelta, Mathf.Abs(previousBase[i].r - previousShift[i].r));
                Require(maxBlockerDepthDelta < previousMaxDepthDelta * 0.1f, "Continuous blocker search did not reduce depth-step motion");
                var coverageBase = Sample("blocker-coverage", 4, 5, crop: 32);
                var coverageShift = Sample("blocker-coverage-shifted", 4, 5, crop: 32, shift: 1f / 128);
                float maxCoverageDelta = 0;
                for (int i = 0; i < coverageBase.Length; i++)
                    maxCoverageDelta = Mathf.Max(maxCoverageDelta, Mathf.Abs(coverageBase[i].g - coverageShift[i].g));
                Require(maxCoverageDelta < 0.01f, "Blocker coverage changes in whole tap steps: " + maxCoverageDelta);
                var firstBlocker = Sample("first-thin-blocker", 9, 8, crop: 32);
                var firstBlockerShift = Sample("first-thin-blocker-shifted", 9, 8, crop: 32, shift: 1f / 128);
                float firstBlockerDelta = 0;
                int lowSupportPixels = 0;
                for (int i = 0; i < firstBlocker.Length; i++)
                {
                    if (firstBlocker[i].g < 0.25f && firstBlockerShift[i].g < 0.25f)
                    {
                        firstBlockerDelta = Mathf.Max(firstBlockerDelta, Mathf.Abs(firstBlocker[i].r - firstBlockerShift[i].r));
                        if (firstBlocker[i].g > 0 || firstBlockerShift[i].g > 0) lowSupportPixels++;
                    }
                }
                Require(lowSupportPixels > 0 && firstBlockerDelta < 0.15f,
                    "First blocker entry jumps to a finite penumbra: " + firstBlockerDelta);
                material.EnableKeyword("HO_CS_FILTER_FORCE_FALLBACK");
                var fallbackBlockers = Sample("fallback-blocker-statistics", 7, 5, crop: 32);
                float maxFallbackError = 0;
                for (int i = 0; i < fallbackBlockers.Length; i++)
                    maxFallbackError = Mathf.Max(maxFallbackError, Mathf.Abs(fallbackBlockers[i].r - blockerBase[i].r),
                        Mathf.Abs(fallbackBlockers[i].g - blockerBase[i].g));
                material.DisableKeyword("HO_CS_FILTER_FORCE_FALLBACK");
                Require(maxFallbackError < 0.00002f, "Fallback blocker statistics differ from Gather: " + maxFallbackError);
                report.Add($"firstBlockerRadiusMotionDelta={firstBlockerDelta:F8}, lowSupportPixels={lowSupportPixels}, fallbackBlockerError={maxFallbackError:F8}");
                report.Add($"blockerDepthMotionDelta={maxBlockerDepthDelta:F8}, previousPointSearchMotionDelta={previousMaxDepthDelta:F8}, blockerCoverageMotionDelta={maxCoverageDelta:F8}");
                foreach (float radius in new[] { 0f, 0.5f, 1f, 1.6f, 2f, 2.49f })
                    Require(Maximum(Sample("dense-reference-r" + radius, 4, 6, radius)) < 0.0004f,
                        "Dense paired fetches differ from individual cell integration at radius " + radius);
                float PixelAt(Color[] pixels, float x, float y)
                {
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, 510), y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, 510);
                    return Mathf.Lerp(Mathf.Lerp(pixels[y0 * 512 + x0].r, pixels[y0 * 512 + x0 + 1].r, x - x0),
                        Mathf.Lerp(pixels[(y0 + 1) * 512 + x0].r, pixels[(y0 + 1) * 512 + x0 + 1].r, x - x0), y - y0);
                }
                float TransitionWidth(Color[] pixels, float angle)
                {
                    float dx = Mathf.Cos(angle * Mathf.Deg2Rad), dy = Mathf.Sin(angle * Mathf.Deg2Rad);
                    float first = float.NaN, last = float.NaN;
                    for (float t = -150; t <= 150; t += 0.125f)
                    {
                        float value = PixelAt(pixels, 255.5f + dx * t, 255.5f + dy * t);
                        if (value > 0.1f && value < 0.9f)
                        {
                            if (float.IsNaN(first)) first = t;
                            last = t;
                        }
                    }
                    Require(!float.IsNaN(first), "Missing measurable straight-edge transition");
                    return last - first;
                }
                foreach (float angle in new[] { 0f, 22.5f, 45f, 67.5f, 90f, 112.5f, 135f, 157.5f })
                {
                    float previousWidth = 0;
                    foreach (float radius in new[] { 0f, 0.5f, 1f, 1.25f, 1.5f, 2f, 2.25f, 2.5f, 3f, 4f })
                    {
                        float width = TransitionWidth(Sample($"radius-{radius}-angle-{angle}", 8, 1, radius, angle: angle), angle);
                        Require(width >= previousWidth - 1f, $"Increasing softness sharpened the edge: r={radius}, angle={angle}, {previousWidth}->{width}");
                        previousWidth = width;
                        report.Add($"radius={radius}, angle={angle}, edgeWidthPixels={width:F3}");
                    }
                }
                report.Add($"levels={levels.Count}, maximumAdjacentJump={maxJump:F6}, subtexelMotionDelta={maxDelta:F6}");
                File.WriteAllLines(Path.Combine(output, "metrics.txt"), report);
                Debug.Log("[Ho-CS Filtering] PASS: continuous PCF, gather order, receiver plane, real occluder, tile isolation, depth crop and user 4096 settings. Output=" + output);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                ShaderUtil.allowAsyncCompilation = previousAsync;
                Shader.SetGlobalTexture("_HoCSAtlas", previousAtlas);
                Shader.SetGlobalVector("_HoCSAtlasSize", previousSize);
                Shader.SetGlobalVector("_HoCSPcssParams", previousPcss);
                Shader.SetGlobalVector("_HoCSPcssParams2", previousPcss2);
                for (int i = created.Count - 1; i >= 0; i--) Object.DestroyImmediate(created[i]);
            }
        }
    }
}
