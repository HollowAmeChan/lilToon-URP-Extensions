#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RTShadow;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTShadow
{
    public static partial class HoRTShadowValidation
    {
        private static bool spatialAudit;
        [Serializable] private sealed class SpatialReport
        {
            public bool pass, compatibility;
            public int outputSize = 64, referenceScale = 8, renderScale = 2;
            public List<SpatialPose> poses = new();
            public List<string> checks = new();
        }
        [Serializable] private sealed class SpatialPose
        { public string name; public float singleMse, supersampledMse; public int singleLevels, supersampledLevels; }
        public static void RunSpatial() { spatialAudit = true; nprIntegration = true; rawOnly = true; Run(); }
        public static void RunSpatialCompatibility() { compatibility = true; RunSpatial(); }

        private static void ValidateSpatialSampling()
        {
            var result = new SpatialReport { compatibility = compatibility };
            var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
            float oldScale = pipeline.renderScale;
            far.shadowCastingMode = ShadowCastingMode.Off;
            near.transform.rotation = Quaternion.Euler(0, 0, 23);
            near.transform.localScale = new Vector3(.85f, 1.9f, 1);
            toon.SetFloat("_ShadowBlur", .0001f); toon.SetFloat("_ShadowBorder", .5f);
            shadow.Settings.reconstruction = HoRTShadowReconstruction.Raw; shadow.Settings.angularDiameter = 0; shadow.Settings.samplesPerPixel = 1;
            int frame = Time.frameCount;
            try
            {
                foreach (float offset in new[] { 0f, .011f, -.023f })
                {
                    near.transform.position = new Vector3(2.5f + offset, -.13f, -4);
                    string name = result.poses.Count == 0 ? "rest" : "micro" + result.poses.Count;
                    pipeline.renderScale = 1; target = Target(512); Render();
                    var reference = BoxResolve(Read(target), 512, 8);
                    target = Target(64); Render(); var single = Read(target);
                    SpatialCheck(shadow.LastOutput.width == 64 && shadow.LastOutput.height == 64, "Native sample domain is full 64x64");
                    pipeline.renderScale = 2; Render(); var supersampled = Read(target);
                    SpatialCheck(shadow.LastOutput.width == 128 && shadow.LastOutput.height == 128 && target.width == 64, "URP scales actual primary and Cast domain to 128x128 before final 64x64 color");
                    var pose = new SpatialPose { name = name, singleMse = ColorMse(single, reference), supersampledMse = ColorMse(supersampled, reference), singleLevels = ColorLevels(single), supersampledLevels = ColorLevels(supersampled) };
                    SpatialCheck(pose.supersampledMse < pose.singleMse * .8f, "Final-color coverage MSE improves for " + name + ": " + pose.singleMse + " -> " + pose.supersampledMse);
                    SpatialCheck(pose.supersampledLevels > pose.singleLevels, "Subpixel final-color coverage retains more than binary ramp levels for " + name);
                    var repeated = ReadSpatialBeauty();
                    CheckImages(repeated, supersampled, 0, "Same-state supersampled export repeats without history or application frame advance");
                    result.poses.Add(pose);
                    SaveSpatial(name + "-reference", reference); SaveSpatial(name + "-single", single); SaveSpatial(name + "-supersampled", supersampled);
                }
                near.transform.position = new Vector3(2.5f, -.13f, -4);
                shadow.Settings.angularDiameter = 8; shadow.Settings.samplesPerPixel = 256;
                toon.SetFloat("_ShadowBlur", .01f);
                pipeline.renderScale = 1; target = Target(512); Render();
                var softReference = BoxResolve(Read(target), 512, 8);
                shadow.Settings.reconstruction = HoRTShadowReconstruction.SigmaSingleFrame; shadow.Settings.singleFrameEstimates = 16;
                target = Target(64); Render(); var softSingle = Read(target);
                pipeline.renderScale = 2; Render(); var softSpatial = Read(target);
                var soft = new SpatialPose { name = "sigma-soft", singleMse = ColorMse(softSingle, softReference), supersampledMse = ColorMse(softSpatial, softReference), singleLevels = ColorLevels(softSingle), supersampledLevels = ColorLevels(softSpatial) };
                SpatialCheck(soft.supersampledMse < soft.singleMse, "Finite-angle SIGMA and narrow ramp benefit from actual final spatial coverage: " + soft.singleMse + " -> " + soft.supersampledMse);
                SpatialCheck(shadow.LastOutput.sigma.Estimates == 16 && shadow.LastOutput.sigma.nrd.AppliedSigmaSettings.maxStabilizedFrameNum == 0, "Spatial SIGMA keeps explicit same-frame estimate budget and zero temporal stabilization");
                CheckImages(ReadSpatialBeauty(), softSpatial, 0, "Finite-angle SIGMA spatial export repeats without waiting for later frames");
                result.poses.Add(soft);
                SaveSpatial("sigma-soft-reference", softReference); SaveSpatial("sigma-soft-single", softSingle); SaveSpatial("sigma-soft-supersampled", softSpatial);
                SpatialCheck(Time.frameCount == frame, "All current-pose spatial exports complete without advancing the application frame");
                result.pass = true;
                File.WriteAllText(Path.Combine(Folder(), "spatial-report.json"), JsonUtility.ToJson(result, true));
                Debug.Log("[HoRTShadowSpatial] PASS actual higher-resolution primary / native Cast / nonlinear NPR ramp / final-color coverage resolve / three poses / same-frame deterministic export");
            }
            finally { pipeline.renderScale = oldScale; }
            void SpatialCheck(bool pass, string message) { Check(pass, message); result.checks.Add(message); }
        }
        private static Color[] ReadSpatialBeauty() { Render(); return Read(target); }
        private static Color[] BoxResolve(Color[] input, int width, int scale)
        {
            int size = width / scale; var output = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            { Color sum = Color.clear; for (int dy = 0; dy < scale; dy++) for (int dx = 0; dx < scale; dx++) sum += input[(y * scale + dy) * width + x * scale + dx]; output[y * size + x] = sum / (scale * scale); }
            return output;
        }
        private static float ColorMse(Color[] image, Color[] reference)
        { double sum = 0; for (int i = 0; i < image.Length; i++) { double r = image[i].r - reference[i].r, g = image[i].g - reference[i].g, b = image[i].b - reference[i].b; sum += (r * r + g * g + b * b) / 3; } return (float)(sum / image.Length); }
        private static int ColorLevels(Color[] image)
        { var levels = new HashSet<int>(); foreach (var color in image) levels.Add(Mathf.RoundToInt(color.r * 4096)); return levels.Count; }
        private static void SaveSpatial(string name, Color[] image)
        {
            File.WriteAllBytes(Path.Combine(Folder(), "spatial-" + name + ".rgba32"), Bytes(image));
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            try { texture.SetPixels(image); texture.Apply(); File.WriteAllBytes(Path.Combine(Folder(), "spatial-" + name + ".png"), texture.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
