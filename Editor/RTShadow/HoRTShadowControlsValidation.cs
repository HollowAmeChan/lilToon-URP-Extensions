#pragma warning disable CS0618
using System;
using System.IO;
using lilToon.URP.Extensions.Debugging;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTShadow;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.RTShadow
{
    public static partial class HoRTShadowValidation
    {
        private static bool uiAudit;
        public static void RunControls() { uiAudit = true; nprIntegration = true; Run(); }
        public static void RunControlsCompatibility() { compatibility = true; RunControls(); }

        private static void ValidateControls()
        {
            var holder = Keep(new GameObject("RTShadow override audit")).AddComponent<Volume>();
            holder.gameObject.layer = 28; holder.isGlobal = true; holder.priority = 100;
            holder.sharedProfile = Keep(ScriptableObject.CreateInstance<VolumeProfile>());
            var volume = holder.sharedProfile.Add<HoRTShadowVolume>(false);
            Check(HoRTShadowDebugViewInfo.Views.Length == 6, "Six RTShadow views registered in the shared debug collection");
            bool registered = false;
            foreach (var view in HoDebugViewRegistry.ShaderCollectionViews) registered |= view.FeatureName == "RTShadow";
            Check(registered, "RTShadow debug participates in shared shader collection validation");
            foreach (Object inspected in new Object[] { shadow, host, volume })
            {
                var editor = UnityEditor.Editor.CreateEditor(inspected);
                try { Check(editor.GetType().Name.EndsWith(inspected.GetType().Name + "Editor"), "Custom inspector resolves for " + inspected.GetType().Name); }
                finally { Object.DestroyImmediate(editor); }
            }
            var baseline = Render(); var beauty = Read(target);
            Check(shadow.VolumeOverrideCount == 0 && shadow.LastEffectiveSettings.angularDiameter == 0, "Uncovered Volume preserves Feature defaults");
            volume.enable.Override(false); Render(false);
            Check(shadow.LastOutput == null && Shader.GetGlobalFloat("_HoRTShadowActive") == 0 && shadow.Settings.enabled, "Volume disables only current request without mutating Feature settings");
            volume.enable.Override(true); volume.reconstruction.Override(HoRTShadowReconstruction.SigmaSingleFrame);
            volume.angularDiameter.Override(8); volume.samplesPerPixel.Override(63); volume.singleFrameEstimates.Override(4); volume.samplingSeed.Override(42);
            Render();
            Check(shadow.LastOutput.sigma != null && shadow.LastOutput.sigma.Estimates == 4 && shadow.LastOutput.reconstruction == HoRTShadowReconstruction.SigmaSingleFrame, "Volume selects actual same-frame SIGMA context and budget");
            Check(shadow.Settings.reconstruction == HoRTShadowReconstruction.Raw && shadow.Settings.singleFrameEstimates == 1 && shadow.Settings.samplingSeed == 0, "Serialized fallback quality unchanged by Volume");
            Check(shadow.LastEffectiveSettings.samplesPerPixel == 63 && shadow.LastEffectiveSettings.samplingSeed == 42, "Resolved request snapshot reports overrides");
            volume.active = false; Render();
            Check(shadow.LastOutput.sigma == null && shadow.VolumeOverrideCount == 0, "Inactive component returns to Feature fallback"); volume.active = true;
            shadow.Settings.enabled = false; Render(false);
            Check(shadow.LastOutput == null, "Volume enable cannot bypass Feature production master switch"); shadow.Settings.enabled = true;

            var firstCamera = camera;
            var secondCamera = Keep(new GameObject("RTShadow other camera")).AddComponent<Camera>(); secondCamera.CopyFrom(firstCamera); secondCamera.enabled = false;
            secondCamera.GetUniversalAdditionalCameraData().volumeLayerMask = ~(1 << 28);
            camera = secondCamera; Render(false);
            Check(shadow.LastCamera == secondCamera && shadow.LastOutput != null && shadow.LastOutput.sigma == null && shadow.VolumeOverrideCount == 0, "Second camera Volume layer mask prevents override leakage");
            camera = firstCamera; Render();
            Check(shadow.LastCamera == firstCamera && shadow.LastOutput.sigma != null, "First camera restores its own Volume reconstruction");

            var declaration = light.gameObject.AddComponent<HoRTShadowLight>(); declaration.angularDiameter = 0;
            Render(); foreach (var item in shadow.LightDiagnostics) Check(item.AngularDiameter == 0 && item.Samples == 1 && item.Estimates == 4, "Per-light angle takes priority and diagnostics show actual SIGMA inputs");
            declaration.enabled = false;
            volume.reconstruction.Override(HoRTShadowReconstruction.Raw); volume.angularDiameter.Override(0); volume.samplesPerPixel.Override(1);
            volume.debugMode.Override(HoRTShadowDebugMode.RawVisibility); volume.debugInGameView.Override(false);
            Render(); CheckImages(Read(target), beauty, .00001f, "Game View opt-out leaves beauty intact");
            volume.debugInGameView.Override(true);
            var raw = Render(); var visible = Read(target);
            CheckVisibility(visible, raw, raw, "Raw debug shows actual valid visibility"); SaveControl("raw", visible);
            volume.reconstruction.Override(HoRTShadowReconstruction.SigmaSingleFrame); volume.angularDiameter.Override(8); volume.samplesPerPixel.Override(256);
            volume.debugMode.Override(HoRTShadowDebugMode.Visibility); raw = Render(); var reconstructed = Read(shadow.LastOutput.Visibility.rt);
            CheckVisibility(Read(target), raw, reconstructed, "Reconstructed debug uses decoded SIGMA rather than binary raw"); SaveControl("visibility", Read(target));
            volume.debugMode.Override(HoRTShadowDebugMode.Difference); raw = Render(); reconstructed = Read(shadow.LastOutput.Visibility.rt);
            var difference = Read(target); int compared = 0;
            for (int i = 0; i < raw.Length; i++) if (raw[i].b == 1) { if (Mathf.Abs(difference[i].r - Mathf.Abs(reconstructed[i].r - raw[i].r)) > .001f) throw new Exception("Debug difference mismatch"); compared++; }
            Check(compared > 100, "Difference debug displays absolute change, not an invented error oracle"); SaveControl("difference", difference);
            volume.debugMode.Override(HoRTShadowDebugMode.BlockerDistance); volume.debugDistance.Override(10); Render();
            Near(Read(target)[32 * 64 + 32].r, Read(shadow.LastOutput.raw.rt)[32 * 64 + 32].g / 10, "Actual sampled nearest blocker metres displayed", .001f); SaveControl("blocker", Read(target));
            volume.debugMode.Override(HoRTShadowDebugMode.Receiver); Render();
            Check(Read(target)[32 * 64 + 32].maxColorComponent > .2f, "Receiver identity debug produces a visible identity color"); SaveControl("receiver", Read(target));
            volume.debugMode.Override(HoRTShadowDebugMode.Status); Render();
            NearColor(Read(target)[32 * 64 + 32], Color.green, "Status valid is green"); SaveControl("status", Read(target));
            sb.SetActive(false); Render(false); NearColor(Read(target)[32 * 64 + 32], Color.red, "Missing primary surface is red, not a valid shadow"); sb.SetActive(true);
            volume.debugLight.Override(999); Render(false); NearColor(Read(target)[32 * 64 + 32], Color.magenta, "Missing slot is purple instead of stale product");
            volume.debugLight.Override(0); host.SetActive(false); Render(false); NearColor(Read(target)[32 * 64 + 32], Color.magenta, "Missing host clears debug source"); host.SetActive(true);

            declaration.enabled = true; shadow.Settings.lightSelection = HoRTShadowLightSelection.Declared;
            var fill = Keep(new GameObject("UI additional direction")).AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = 0; fill.transform.rotation = Quaternion.LookRotation(Direction);
            fill.gameObject.AddComponent<HoRTShadowLight>().angularDiameter = 0;
            volume.reconstruction.Override(HoRTShadowReconstruction.Raw); volume.debugMode.Override(HoRTShadowDebugMode.RawVisibility); Render();
            int fillSlot = -1; foreach (var item in shadow.LightDiagnostics) if (item.Light == fill) fillSlot = item.Slot;
            Check(fillSlot >= 0 && fillSlot != shadow.LastOutput.slot, "Additional light gets its own debug-selectable slot");
            volume.debugLight.Override(fillSlot); Render(); NearColor(Read(target)[32 * 64 + 32], Color.white, "Selected additional light debug reads unoccluded product");
            Check(shadow.LastOutput.light == light && Read(shadow.LastOutput.raw.rt)[32 * 64 + 32].r == 0, "Debug selection never overwrites main Cast publication");
            lifetime.retire = true; Render(false);
            NearColor(Read(target)[32 * 64 + 32], Color.white, "Mid-graph Feature Create retains recorded diagnostic material and product");
            volume.debugMode.Override(HoRTShadowDebugMode.All); Render(); SaveControl("all", Read(target));
            var grid = Read(target); bool hasBright = false, hasDark = false;
            foreach (var pixel in grid) { hasBright |= pixel.r > .9f; hasDark |= pixel.r < .1f; }
            Check(hasBright && hasDark, "All grid produces distinct panels and final presentation");
            volume.debugMode.Override(HoRTShadowDebugMode.Off); shadow.Settings.lightSelection = HoRTShadowLightSelection.Main; Render();
            CheckImages(Read(target), beauty, .00001f, "Turning debug off restores exact original beauty");
            volume.enable.Override(false); volume.debugMode.Override(HoRTShadowDebugMode.Status); Render(false);
            NearColor(Read(target)[32 * 64 + 32], Color.magenta, "Disabled production can still diagnose missing product");
            Debug.Log("[HoRTShadowControls] PASS Volume camera isolation / actual quality / master switch / per-light angle / six debug modes / additional light / missing input / beauty restoration; checks=" + report.checks.Count + " requests=" + report.requests);
        }
        private static void NearColor(Color actual, Color expected, string label)
        { Check(Mathf.Abs(actual.r - expected.r) < .003f && Mathf.Abs(actual.g - expected.g) < .003f && Mathf.Abs(actual.b - expected.b) < .003f, label + ": " + actual); }
        private static void CheckVisibility(Color[] image, Color[] raw, Color[] expected, string label)
        {
            float error = 0; int compared = 0;
            for (int i = 0; i < raw.Length; i++) if (raw[i].b == 1)
            { error = Mathf.Max(error, Mathf.Abs(image[i].r - expected[i].r), Mathf.Abs(image[i].g - expected[i].r), Mathf.Abs(image[i].b - expected[i].r)); compared++; }
            Check(compared > 100 && error < .001f, label + " maxError=" + error + " pixels=" + compared);
        }
        private static void CheckImages(Color[] a, Color[] b, float tolerance, string label)
        {
            float error = 0; for (int i = 0; i < a.Length; i++) error = Mathf.Max(error, Mathf.Abs(a[i].r - b[i].r), Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b));
            Check(error <= tolerance, label + " maxError=" + error);
        }
        private static void SaveControl(string name, Color[] pixels)
        {
            var image = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
            try { image.SetPixels(pixels); image.Apply(); File.WriteAllBytes(Path.Combine(Folder(), "controls-" + name + ".png"), image.EncodeToPNG()); }
            finally { Object.DestroyImmediate(image); }
            File.WriteAllBytes(Path.Combine(Folder(), "controls-" + name + ".rgba32"), Bytes(pixels));
        }
    }
}
