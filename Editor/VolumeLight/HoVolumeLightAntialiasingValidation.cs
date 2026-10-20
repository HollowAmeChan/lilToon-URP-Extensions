#pragma warning disable CS0618
using System;
using lilToon.URP.Extensions.Atmosphere;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.VolumeLight
{
    public static partial class HoVolumeLightValidation
    {
        [Serializable] private sealed class AAReport
        {
            public bool passed;
            public float legacy256RMSE, interval256RMSE, spatial256RMSE, temporal256RMSE, temporal48RMSE;
            public float interval48RMSE, spatial48RMSE, steadyFrameDifference, historyAge, narrowBeamEnergyRatio, narrowBeamPeakRatio;
            public float cameraTAAHistoryAge;
            public float legacy256Banding, temporal256Banding, temporal48Banding, motionRMSE;
            public float legacyMean, referenceMean, temporalMean, blockerLeak, movedShadowError;
        }
        private static AAReport aaReport;

        private static void ValidateAntialiasing()
        {
            aaReport = new AAReport();
            HoAtmosphereMediumClock.TimeOverride = 0;
            lightFeature.FeatureSettings.enabled = true; lightFeature.FeatureSettings.intensity = 10;
            lightFeature.FeatureSettings.anisotropy = 0; lightFeature.FeatureSettings.maxDistance = 100; lightFeature.FeatureSettings.stepCount = 256;
            camera.transform.SetPositionAndRotation(new Vector3(0, 1, -8), Quaternion.identity);
            region.transform.SetPositionAndRotation(new Vector3(0, 1, 9), Quaternion.identity); region.transform.localScale = Vector3.one;
            region.size = new Vector3(24, 8, 16); region.blendDistance = .4f; region.density = .15f; region.densityTexture = null; region.tint = Color.white;
            sun.color = Color.white; sun.intensity = 2; sun.shadows = LightShadows.Hard;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(.85f, .04f, .53f));
            var panel = Quad("AA narrow-window pattern outside medium", new Vector3(-8, 1, 0), new Vector3(18, 14, 1));
            var renderer = panel.GetComponent<Renderer>(); renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            var alpha = Keep(new Texture2D(256, 256, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
            var texels = new Color[256 * 256];
            for (int y = 0; y < 256; ++y)
                for (int x = 0; x < 256; ++x)
                    texels[x + y * 256] = new Color(0, 0, 0, ((x + y / 3) % 8 >= 2 || y % 64 < 3) ? 1 : 0);
            alpha.SetPixels(texels); alpha.Apply();
            var mat = renderer.sharedMaterial; mat.SetTexture("_BaseMap", alpha); mat.SetFloat("_AlphaClip", 1); mat.SetFloat("_Cutoff", .5f); mat.EnableKeyword("_ALPHATEST_ON"); mat.renderQueue = 2450;
            Require(!region.WorldBounds.Contains(panel.transform.position), "AA caster must remain outside medium");
            var referenceShader = Shader.Find("Hidden/lilToon/URP/VolumeLight/AAReference");
            var legacyShader = Shader.Find("Hidden/lilToon/URP/VolumeLight/AALegacy");
            Require(referenceShader != null && legacyShader != null, "AA reference shaders missing");
            Color[] Reference()
            {
                lightFeature.FeatureSettings.shader = referenceShader;
                var image = Render(); lightFeature.FeatureSettings.shader = null; return image;
            }
            lightFeature.FeatureSettings.antialiasing = HoVolumeLightAntialiasing.Off;
            var reference = Reference(); Save(reference, "aa-reference-4096");
            lightFeature.FeatureSettings.shader = legacyShader;
            var legacy = Render(); Save(legacy, "aa-legacy-256"); lightFeature.FeatureSettings.shader = null;
            var interval = Render(); Save(interval, "aa-interval-256");
            lightFeature.FeatureSettings.antialiasing = HoVolumeLightAntialiasing.Spatial;
            var spatial = Render(); Save(spatial, "aa-spatial-256");
            lightFeature.FeatureSettings.antialiasing = HoVolumeLightAntialiasing.Temporal;
            lightFeature.ResetHistory(); Color[] temporal = null;
            for (int i = 0; i < 32; ++i) temporal = Render();
            Require(lightFeature.HistoryCameraCount == 1, "temporal history not owned by its camera");
            Save(temporal, "aa-temporal-256");
            screenShadows.SetActive(true); var screenResult = Render();
            Require(RMSE(screenResult, temporal) < Mean(reference) * .02f, "reconstruction changed air shadow semantics under SCREEN keyword"); screenShadows.SetActive(false);
            var history = CurrentHistory(camera);
            var historyPixels = Read(history.color[history.index].rt);
            foreach (var pixel in historyPixels) aaReport.historyAge = Mathf.Max(aaReport.historyAge, pixel.a);
            Require(aaReport.historyAge > 8, "AA did not actually accumulate multiple frames on GPU");
            aaReport.legacy256RMSE = RMSE(legacy, reference); aaReport.interval256RMSE = RMSE(interval, reference);
            aaReport.spatial256RMSE = RMSE(spatial, reference); aaReport.temporal256RMSE = RMSE(temporal, reference);
            aaReport.legacy256Banding = Banding(legacy, reference); aaReport.temporal256Banding = Banding(temporal, reference);
            aaReport.legacyMean = Mean(legacy); aaReport.referenceMean = Mean(reference); aaReport.temporalMean = Mean(temporal);
            lightFeature.FeatureSettings.stepCount = 48; lightFeature.ResetHistory();
            lightFeature.FeatureSettings.antialiasing = HoVolumeLightAntialiasing.Off;
            aaReport.interval48RMSE = RMSE(Render(), reference);
            lightFeature.FeatureSettings.antialiasing = HoVolumeLightAntialiasing.Spatial;
            aaReport.spatial48RMSE = RMSE(Render(), reference);
            lightFeature.FeatureSettings.antialiasing = HoVolumeLightAntialiasing.Temporal;
            for (int i = 0; i < 32; ++i) temporal = Render();
            Save(temporal, "aa-temporal-48");
            aaReport.temporal48RMSE = RMSE(temporal, reference); aaReport.temporal48Banding = Banding(temporal, reference);
            aaReport.steadyFrameDifference = RMSE(Render(), temporal);
            Require(aaReport.steadyFrameDifference < aaReport.referenceMean * .025f, "AA steady-frame noise remained excessive");
            Require(aaReport.temporal256RMSE < aaReport.legacy256RMSE * .25f, "AA did not reduce legacy integration error by at least 75%");
            Require(aaReport.temporal256RMSE < aaReport.interval256RMSE * .8f, "temporal reconstruction did not improve same-budget interval sampling");
            Require(aaReport.temporal256Banding < aaReport.legacy256Banding * .4f, "AA did not suppress correlated bands by at least 60%");
            Require(aaReport.temporal48RMSE < aaReport.interval48RMSE * .8f, "temporal AA did not improve same-budget 48-step error");
            Require(aaReport.temporal48Banding < aaReport.legacy256Banding * .5f, "48-step AA did not halve legacy 256-step banding");
            Require(Mathf.Abs(aaReport.temporalMean - aaReport.referenceMean) < aaReport.referenceMean * .02f, "AA changed scattering energy");
            Debug.Log("[HoVolumeLightValidation] AA metrics " + JsonUtility.ToJson(aaReport));

            // Moving an opaque foreground across a converged volume must not carry old light with it.
            var blocker = Quad("AA moving foreground", new Vector3(-.5f, 1, -1), new Vector3(2, 2, 1));
            var blocked = Render(); lightFeature.FeatureSettings.enabled = false; var baseline = Render(); lightFeature.FeatureSettings.enabled = true;
            Vector3 viewport = camera.WorldToViewportPoint(blocker.transform.position);
            int px = (int)(viewport.x * 160), py = (int)(viewport.y * 160);
            aaReport.blockerLeak = Mathf.Abs(blocked[px + py * 160].r - baseline[px + py * 160].r);
            Require(aaReport.blockerLeak < .0005f, "AA leaked across foreground depth");
            blocker.SetActive(false);
            // History must react to caster motion even when the foreground depth did not move.
            for (int i = 0; i < 16; ++i) Render();
            panel.transform.position += new Vector3(.12f, 0, 0);
            var moved = Render(); var movedReference = Reference();
            aaReport.movedShadowError = RMSE(moved, movedReference);
            Require(aaReport.movedShadowError < aaReport.referenceMean * .07f, "moving shadow left excessive first-frame error");
            Save(moved, "aa-moving-caster-first-frame");
            // Reprojection under modest rotation, then translation (parallax confidence rejection).
            for (int i = 0; i < 16; ++i) Render();
            camera.transform.Rotate(0, 1, 0); var rotated = Render(); var rotatedReference = Reference();
            aaReport.motionRMSE = RMSE(rotated, rotatedReference); Save(rotated, "aa-camera-rotation");
            camera.transform.position += Vector3.right * .1f; var translated = Render(); var translatedReference = Reference();
            aaReport.motionRMSE = Mathf.Max(aaReport.motionRMSE, RMSE(translated, translatedReference)); Save(translated, "aa-camera-translation");
            Require(aaReport.motionRMSE < aaReport.referenceMean * .08f, "camera motion left excessive volume error");
            // No stale light after parameter/region changes or a camera cut.
            region.volumeLightWeight = 0; Require(Mean(Render()) < .0001f && lightFeature.HistoryCameraCount == 0, "AA retained disabled region history"); region.volumeLightWeight = 1;
            camera.transform.SetPositionAndRotation(new Vector3(0, 1, -8), Quaternion.identity);
            Render(); sun.intensity = 0; Require(Mean(Render()) < .0001f, "AA retained previous sun brightness"); sun.intensity = 2;
            Render(); var oldShadowSignature = CurrentHistory(camera).signature;
            pipeline.shadowDistance = 25; Render(); Require(CurrentHistory(camera).signature != oldShadowSignature, "AA retained changed shadow distance"); pipeline.shadowDistance = 50;
            Render(); oldShadowSignature = CurrentHistory(camera).signature;
            sun.shadowStrength = .5f; Render(); Require(CurrentHistory(camera).signature != oldShadowSignature, "AA retained changed shadow strength"); sun.shadowStrength = 1;
            for (int i = 0; i < 8; ++i) Render();
            var originalCamera = camera;
            camera = Keep(new GameObject("AA second observer")).AddComponent<Camera>(); camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(1, 1, -7); camera.GetUniversalAdditionalCameraData().volumeLayerMask = -1;
            Render(); Require(lightFeature.HistoryCameraCount == 2, "AA histories were shared between observers");
            lightVolume.enable.Override(false); Render();
            Require(lightFeature.HistoryCameraCount == 1, "disabled observer cleared another camera's AA history"); lightVolume.enable.overrideState = false;
            camera = originalCamera;
            // Externally destroyed Unity objects must not leave live managed history handles unusable.
            var destroyedHistory = CurrentHistory(camera); Object.DestroyImmediate(destroyedHistory.color[destroyedHistory.index].rt);
            Require(Mean(Render()) > .01f, "AA failed to recover a destroyed history texture");
            CurrentHistory(camera).depth[0].rt.Release(); Require(Mean(Render()) > .01f, "AA failed to recover released native history storage");
            pipeline.renderScale = .5f; Require(Mean(Render()) > .01f, "AA render-scale resize lost volume");
            Require(CurrentHistory(camera).color[0].rt.width == 80, "AA history did not resize with render scale"); pipeline.renderScale = 1;
            pipeline.msaaSampleCount = 4; Require(Mean(Render()) > .01f, "AA MSAA4 failed"); pipeline.msaaSampleCount = 1;
            camera.orthographic = true; camera.orthographicSize = 5; Render();
            Require(CurrentHistory(camera).orthographic, "AA retained perspective history after orthographic switch"); camera.orthographic = false;
            var cameraSettings = camera.GetUniversalAdditionalCameraData(); cameraSettings.antialiasing = AntialiasingMode.TemporalAntiAliasing;
            for (int i = 0; i < 8; ++i) Render();
            Require(Mean(Render()) > .01f, "AA could not coexist with camera TAA jitter");
            var jitteredHistory = CurrentHistory(camera);
            foreach (var p in Read(jitteredHistory.color[jitteredHistory.index].rt)) aaReport.cameraTAAHistoryAge = Mathf.Max(aaReport.cameraTAAHistoryAge, p.a);
            Require(aaReport.cameraTAAHistoryAge >= 2, "camera TAA jitter prevented all volume history reuse"); cameraSettings.antialiasing = AntialiasingMode.None;
            lightVolume.antialiasing.Override(HoVolumeLightAntialiasing.Spatial); Render();
            Require(lightFeature.HistoryCameraCount <= 1, "AA Volume override retained current camera history"); lightVolume.antialiasing.overrideState = false;
            ValidateNarrowBeam(panel, alpha, Reference);
            ValidateAAInputResets(panel, Reference);
            ValidateAASceneSwitch();
            lightFeature.FeatureSettings.enabled = false; Render(); Require(lightFeature.HistoryCameraCount == 0, "AA disabled effect retained resources"); lightFeature.FeatureSettings.enabled = true;
            sun.shadows = LightShadows.None;
            lightFeature.FeatureSettings.antialiasing = HoVolumeLightAntialiasing.Off;
            lightFeature.FeatureSettings.stepCount = 128; lightFeature.FeatureSettings.maxDistance = 30; lightFeature.ResetHistory();
            HoAtmosphereMediumClock.TimeOverride = null;
            aaReport.passed = true;
            Debug.Log("[HoVolumeLightValidation] AA GPU metrics + motion/depth/history ownership PASS " + JsonUtility.ToJson(aaReport));
        }
        private static HoVolumeLightHistory CurrentHistory(Camera observer)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var reconstruction = typeof(HoVolumeLightRendererFeature).GetField("reconstruction", flags).GetValue(lightFeature);
            var histories = (System.Collections.Generic.Dictionary<int, HoVolumeLightHistory>)reconstruction.GetType().GetField("histories", flags).GetValue(reconstruction);
            return histories[observer.GetInstanceID()];
        }
        private static void ValidateNarrowBeam(GameObject panel, Texture2D alpha, Func<Color[]> reference)
        {
            panel.transform.SetPositionAndRotation(new Vector3(0, 1, 0), Quaternion.identity); panel.transform.localScale = new Vector3(8, 14, 1);
            sun.transform.rotation = Quaternion.identity;
            var pixels = new Color[256 * 256];
            for (int y = 0; y < 256; ++y)
                for (int x = 0; x < 256; ++x) pixels[x + y * 256] = new Color(0, 0, 0, x >= 124 && x < 132 ? 0 : 1);
            alpha.SetPixels(pixels); alpha.Apply();
            var expected = reference(); lightFeature.ResetHistory(); Color[] actual = null;
            for (int i = 0; i < 32; ++i) actual = Render();
            float actualPeak = 0, referencePeak = 0;
            foreach (var p in actual) actualPeak = Mathf.Max(actualPeak, p.r);
            foreach (var p in expected) referencePeak = Mathf.Max(referencePeak, p.r);
            aaReport.narrowBeamEnergyRatio = Mean(actual) / Mathf.Max(.0001f, Mean(expected));
            aaReport.narrowBeamPeakRatio = actualPeak / Mathf.Max(.0001f, referencePeak);
            Require(referencePeak > .1f, "narrow-beam test did not contain a bright shaft");
            Require(aaReport.narrowBeamEnergyRatio > .9f && aaReport.narrowBeamEnergyRatio < 1.1f, "AA erased or bloated narrow-beam energy");
            Require(aaReport.narrowBeamPeakRatio > .85f, "AA blurred the narrow-beam peak");
            Save(expected, "aa-narrow-reference"); Save(actual, "aa-narrow-temporal-48");
        }
        private static void ValidateAAInputResets(GameObject panel, Func<Color[]> reference)
        {
            for (int i = 0; i < 8; ++i) Render();
            region.tint = Color.blue; var blue = Render();
            Require(Mean(blue) > .001f && blue[80 + 80 * 160].r < .0005f, "AA retained stale medium tint"); region.tint = Color.white;
            for (int i = 0; i < 8; ++i) Render();
            sun.transform.rotation = Quaternion.Euler(0, 8, 0); var changedSun = Render();
            var freshSun = reference(); Require(RMSE(changedSun, freshSun) < .04f, "AA retained changed solar direction");
            camera.transform.position = new Vector3(3, 1, -5); Render();
            Require(CurrentHistory(camera).position == camera.transform.position, "AA did not reset on camera cut");
            lightVolume.debugMode.value = HoVolumeLightDebugMode.Contribution;
            Require(Mean(Render()) > .001f, "AA contribution debug lost reconstructed output"); lightVolume.debugMode.value = HoVolumeLightDebugMode.Off;
            var noise = Keep(new Texture3D(2, 1, 1, TextureFormat.RGBAFloat, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat });
            noise.SetPixels(new[] { Color.black, Color.white }); noise.Apply(); region.densityTexture = noise; region.noiseStrength = 1; region.wind = Vector3.one; region.noiseWorldSize = 4;
            HoAtmosphereMediumClock.TimeOverride = 0; for (int i = 0; i < 8; ++i) Render();
            HoAtmosphereMediumClock.TimeOverride = 1; var advected = Render(); var expected = reference();
            Require(RMSE(advected, expected) < .06f, "AA froze world-space moving density into history"); region.densityTexture = null;
            camera.transform.SetPositionAndRotation(new Vector3(0, 1, -8), Quaternion.identity);
            panel.SetActive(false);
        }
        private static void ValidateAASceneSwitch()
        {
            Render(); Require(lightFeature.HistoryCameraCount > 0, "scene switch requires an allocated history");
            var oldCamera = camera;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Require(oldCamera == null && lightFeature.HistoryCameraCount == 0, "single scene switch retained camera/history");
            camera = Keep(new GameObject("AA replacement scene camera")).AddComponent<Camera>(); camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.allowHDR = true;
            camera.transform.position = new Vector3(0, 1, -8); camera.fieldOfView = 60; camera.aspect = 1; camera.farClipPlane = 100;
            camera.GetUniversalAdditionalCameraData().volumeLayerMask = -1;
            region = Keep(new GameObject("AA replacement scene medium")).AddComponent<HoAtmosphereRegion>();
            region.anisotropy = 0;
            region.transform.position = new Vector3(0, 1, 5); region.size = new Vector3(12, 6, 8); region.density = .1f; region.blendDistance = .5f;
            sun = Keep(new GameObject("AA replacement scene sun")).AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 2;
            sun.shadows = LightShadows.None; sun.transform.rotation = Quaternion.LookRotation(new Vector3(.6f, 0, .8f)); RenderSettings.sun = sun;
            var holder = Keep(new GameObject("AA replacement scene controls")).AddComponent<Volume>(); holder.isGlobal = true;
            holder.sharedProfile = Keep(ScriptableObject.CreateInstance<VolumeProfile>());
            lightVolume = holder.sharedProfile.Add<HoVolumeLightVolume>(false); atmosphereVolume = holder.sharedProfile.Add<HoAtmosphereVolume>(false);
            Require(Mean(Render()) > .01f && lightFeature.HistoryCameraCount == 1, "AA did not rebuild in replacement scene");
            lightFeature.SetActive(false); Render(); Require(lightFeature.HistoryCameraCount == 0, "SetActive(false) retained AA history"); lightFeature.SetActive(true);
        }
        private static float RMSE(Color[] a, Color[] b)
        {
            double sum = 0; int count = 0;
            for (int y = 12; y < 148; ++y)
                for (int x = 12; x < 148; ++x) { double d = a[x + y * 160].r - b[x + y * 160].r; sum += d * d; ++count; }
            return (float)Math.Sqrt(sum / count);
        }
        private static float Banding(Color[] a, Color[] b)
        {
            // Correlated low-frequency error, measured separately from stochastic grain.
            double sum = 0; int count = 0;
            for (int y = 16; y < 144; ++y)
                for (int x = 16; x < 144; ++x)
                {
                    double d = 0;
                    for (int oy = -2; oy <= 2; ++oy)
                        for (int ox = -2; ox <= 2; ++ox) d += a[x + ox + (y + oy) * 160].r - b[x + ox + (y + oy) * 160].r;
                    sum += d * d / 625; ++count;
                }
            return (float)Math.Sqrt(sum / count);
        }
    }
}
