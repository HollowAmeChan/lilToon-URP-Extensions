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
        [Serializable] private sealed class DustReport
        {
            public bool passed;
            public int particles, changedPixels;
            public int defaultChangedPixels;
            public float defaultMeanContribution;
            public float subpixelEnergyRatio;
            public float meanContribution, movementError, shadowReduction, extinctionRatio, centerExtinctionError, narrowPhaseRatio, cutoutContrast;
        }
        private static DustReport dustReport;
        private static Color[] DustContribution()
        {
            region.dust.enabled = false; var baseline = Render();
            region.dust.enabled = true; var with = Render();
            var contribution = new Color[with.Length];
            for (int i = 0; i < with.Length; ++i) contribution[i] = with[i] - baseline[i];
            return contribution;
        }
        private static void ValidateDust()
        {
            dustReport = new DustReport();
            lightFeature.FeatureSettings.antialiasing = HoVolumeLightAntialiasing.Off;
            lightFeature.FeatureSettings.overrideAnisotropy = false; lightFeature.FeatureSettings.stepCount = 128;
            region.transform.SetPositionAndRotation(new Vector3(0, 1, 5), Quaternion.identity); region.transform.localScale = Vector3.one;
            region.size = new Vector3(12, 6, 8); region.blendDistance = .5f; region.density = .1f;
            region.anisotropy = 0; region.scatteringAlbedo = Color.white; region.tint = Color.white; region.pathExtinction = false;
            region.dust.enabled = false; region.densityTexture = null; region.wind = Vector3.zero;
            sun.transform.rotation = Quaternion.identity; sun.shadows = LightShadows.None;
            HoAtmosphereMediumClock.TimeOverride = 0;
            Render(); uint environment = probe.environmentRevision, medium = probe.mediumRevision;
            region.scatteringAlbedo = new Color(.2f, .5f, .8f); region.anisotropy = .6f;
            probe.material.SetFloat("_ProbeMode", 2); Render(); var optics = Read(probe.destination.rt)[0];
            Near(optics.r, .2f, "medium albedo R", .001f); Near(optics.g, .5f, "medium albedo G", .001f); Near(optics.a, .6f, "medium anisotropy", .001f);
            Require(probe.environmentRevision == environment && probe.mediumRevision != medium, "optics changed sky revision or retained medium history");
            probe.material.SetFloat("_ProbeMode", 3); probe.Points(Vector3.right, Vector3.left); Render(); var phase = Read(probe.destination.rt);
            Require(phase[0].r > phase[1].r * 10, "shared phase did not distinguish forward/back scattering");
            region.anisotropy = 0; region.scatteringAlbedo = Color.white; probe.material.SetFloat("_ProbeMode", 0);
            var thin = Render(); region.pathExtinction = true; var extinct = Render();
            dustReport.extinctionRatio = Mean(extinct) / Mean(thin);
            float zDirection = camera.ViewportPointToRay(new Vector3(80.5f / 160, 80.5f / 160)).direction.z;
            float opticalDepth = region.density * (region.size.z - region.blendDistance) / zDirection;
            float referenceRatio = (1 - Mathf.Exp(-opticalDepth)) / opticalDepth;
            dustReport.centerExtinctionError = Mathf.Abs(extinct[80 + 80 * 160].r / thin[80 + 80 * 160].r - referenceRatio);
            Require(dustReport.centerExtinctionError < .003f, "uniform medium integration disagrees with analytic Beer-Lambert reference");
            Require(dustReport.extinctionRatio > .4f && dustReport.extinctionRatio < .95f, "path extinction did not attenuate scattered light"); region.pathExtinction = false;

            region.dust.numberDensity = 1.5f; region.dust.size = new Vector2(.12f, .2f); region.dust.brightness = 15; region.dust.flutter = .08f;
            var grains = DustContribution(); Save(Render(), "dust-on"); Save(grains, "dust-contribution");
            dustReport.particles = lightFeature.DustParticleCount; dustReport.meanContribution = Mean(grains);
            foreach (var p in grains) if (p.r > .01f) dustReport.changedPixels++;
            Require(dustReport.particles > 0 && dustReport.particles <= region.dust.maxParticles && lightFeature.HasDustMaterial, "shared dust did not allocate/draw");
            Require(dustReport.changedPixels > 40 && dustReport.changedPixels < grains.Length * .5f && dustReport.meanContribution > .0005f, "dust produced no sparse visible grain");
            var field = region.GetDustField(); var buffer = field.buffer; int rebuilds = field.RebuildCount;
            var repeat = Render(); var sameTime = Render(); Require(ImageError(repeat, sameTime) < .00001f, "same world time changed dust positions");
            var observer = camera; camera = Keep(new GameObject("Shared dust second observer")).AddComponent<Camera>(); camera.enabled = false;
            camera.transform.position = new Vector3(1, 1, -7); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.GetUniversalAdditionalCameraData().volumeLayerMask = -1; Render(); camera = observer;
            Require(region.GetDustField().buffer == buffer && field.RebuildCount == rebuilds, "second camera duplicated or advanced the world field");
            var originalSeeds = new Vector4[field.Count]; field.buffer.GetData(originalSeeds);
            var seedSet = new System.Collections.Generic.HashSet<Vector4>(originalSeeds);
            region.transform.position += Vector3.right * .3f; Render();
            var movedSeeds = new Vector4[field.Count]; field.buffer.GetData(movedSeeds); int shared = 0;
            foreach (var seed in movedSeeds) if (seedSet.Contains(seed)) shared++;
            Require(shared > originalSeeds.Length * .75f, "moving a region dragged/reseeded its whole world field"); region.transform.position -= Vector3.right * .3f; Render();
            ValidateDustSubpixelStability();
            uint staticRevision = probe.mediumRevision;
            region.dust.brightness = 8; Render(); Require(probe.mediumRevision == staticRevision, "dust art parameter invalidated continuous medium history"); region.dust.brightness = 15;
            region.wind = new Vector3(.2f, .07f, 0); HoAtmosphereMediumClock.TimeOverride = 0; var t0 = DustContribution();
            HoAtmosphereMediumClock.TimeOverride = 1; var t1 = DustContribution();
            dustReport.movementError = ImageError(t0, t1); Require(dustReport.movementError > .002f, "world dust did not drift"); Save(t1, "dust-flow-t1");
            HoAtmosphereMediumClock.TimeOverride = 0; region.wind = Vector3.zero;

            sun.shadows = LightShadows.Hard;
            var caster = Box("Dust shadow outside region", new Vector3(0, 1, 0), new Vector3(40, 30, .2f));
            caster.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            var occluded = DustContribution(); caster.SetActive(false); var open = DustContribution();
            dustReport.shadowReduction = 1 - Mean(occluded) / Mathf.Max(.0001f, Mean(open));
            Require(dustReport.shadowReduction > .5f, "dust ignored light-space occlusion");
            Save(occluded, "dust-shadowed");
            var holes = Quad("Dust lilToon Cutout window", new Vector3(0, 1, 0), new Vector3(40, 30, 1));
            holes.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            var lil = Mat("Hidden/lilToonCutout"); lil.SetFloat("_Cutoff", .5f); lil.SetFloat("_Cull", 0); lil.SetFloat("_AlphaToMask", 0); lil.SetFloat("_ZWrite", 1); lil.renderQueue = 2450;
            var alpha = Keep(new Texture2D(2, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
            alpha.SetPixels(new[] { new Color(0, 0, 0, 0), new Color(0, 0, 0, 1) }); alpha.Apply(); lil.SetTexture("_MainTex", alpha);
            holes.GetComponent<Renderer>().sharedMaterial = lil;
            var cutout = DustContribution(); double left = 0, right = 0;
            for (int y = 20; y < 140; ++y)
            {
                for (int x = 20; x < 70; ++x) left += cutout[x + y * 160].r;
                for (int x = 90; x < 140; ++x) right += cutout[x + y * 160].r;
            }
            dustReport.cutoutContrast = (float)((left - right) / Math.Max(.0001, left + right));
            Require(left > 1 && left > right * 10, "lilToon alpha did not shape lit dust"); Save(cutout, "dust-liltoon-cutout");
            screenShadows.SetActive(true); var screened = DustContribution();
            Require(ImageError(cutout, screened) < Mathf.Max(.0001f, Mean(cutout)) * .02f, "SCREEN keyword changed dust light-space visibility");
            screenShadows.SetActive(false); holes.SetActive(false); sun.shadows = LightShadows.None;
            var foreground = Quad("Dust foreground blocker", new Vector3(0, 1, -1), new Vector3(3, 3, 1));
            var blocked = DustContribution();
            Require(Mathf.Abs(blocked[80 + 80 * 160].r) < .0005f, "dust leaked through foreground depth"); foreground.SetActive(false);
            region.scatteringAlbedo = Color.blue; var blue = DustContribution();
            Require(Mean(blue) > .0001f, "blue dust produced no light");
            foreach (var p in blue) Require(Mathf.Abs(p.r) < .001f && Mathf.Abs(p.g) < .001f, "dust ignored shared spectral albedo");
            region.scatteringAlbedo = Color.white; region.anisotropy = .6f;
            sun.transform.rotation = Quaternion.LookRotation(Vector3.back); float forward = Mean(DustContribution());
            sun.transform.rotation = Quaternion.identity; float backward = Mean(DustContribution());
            dustReport.narrowPhaseRatio = forward / Mathf.Max(.00001f, backward);
            Require(dustReport.narrowPhaseRatio > 5, "dust ignored shared angular scattering");
            lightVolume.anisotropy.Override(0); var overridden = DustContribution();
            region.anisotropy = 0; lightVolume.anisotropy.overrideState = false; var regional = DustContribution();
            Require(ImageError(overridden, regional) < .0005f, "explicit Volume phase override did not agree with region phase");
            region.anisotropy = 0;
            var densityMask = Keep(new Texture3D(2, 1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat });
            densityMask.SetPixels(new[] { Color.black, Color.white }); densityMask.Apply();
            region.densityTexture = densityMask; region.noiseStrength = 1; region.noiseWorldSize = 24;
            var masked = DustContribution(); double dense = 0, empty = 0;
            for (int y = 20; y < 140; ++y)
            {
                for (int x = 20; x < 70; ++x) dense += masked[x + y * 160].r;
                for (int x = 90; x < 140; ++x) empty += masked[x + y * 160].r;
            }
            Require(dense > 1 && dense > empty * 10, "dust ignored the shared spatial density texture");
            region.densityTexture = null; region.noiseWorldSize = 5;
            float thinDust = Mean(DustContribution()); region.pathExtinction = true; float extinguishedDust = Mean(DustContribution());
            Require(extinguishedDust > 0 && extinguishedDust < thinDust * .95f, "dust ignored shared path extinction"); region.pathExtinction = false;
            region.dust.maxParticles = 128; region.dust.numberDensity = 50; Render();
            Require(lightFeature.DustCapacityLimited && lightFeature.DustParticleCount <= 128, "dust population budget was not enforced");
            region.dust.maxParticles = 8192; region.dust.numberDensity = 1.5f; Render();
            var owned = region.GetDustField().buffer; owned.Dispose(); Render();
            Require(region.HasDustBuffer, "destroyed shared dust buffer did not recover");
            region.enabled = false; Render(); Require(!region.HasDustBuffer && !lightFeature.HasDustMaterial, "disabled region retained dust resources"); region.enabled = true;
            Require(Mean(DustContribution()) > .0005f, "re-enabled region failed to rebuild dust");
            lightFeature.SetActive(false); Render(); Require(!lightFeature.HasDustMaterial, "disabled feature retained dust material"); lightFeature.SetActive(true);
            // Inspect the shipped small-grain defaults at a useful output resolution as well
            // as the intentionally enlarged 160px regression fixture.
            var smallTarget = target;
            target = Keep(new RenderTexture(640, 640, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)); target.Create();
            region.dust = new HoAtmosphereDustSettings { enabled = true }; region.density = .02f; region.anisotropy = .3f;
            region.wind = new Vector3(.1f, 0, 0); lightFeature.FeatureSettings.intensity = 1;
            sun.transform.rotation = Quaternion.LookRotation(Vector3.back);
            var defaults = DustContribution(); Save(Render(), "dust-defaults-on"); Save(defaults, "dust-defaults-contribution");
            dustReport.defaultMeanContribution = Mean(defaults);
            foreach (var p in defaults) if (p.r > .001f) dustReport.defaultChangedPixels++;
            Require(dustReport.defaultChangedPixels > 20, "shipped dust defaults produced no visible small grains");
            target = smallTarget;
            // Scene close must release the producer's shared buffer as well as effect materials.
            Render(); owned = region.GetDustField().buffer;
            lightFeature.FeatureSettings.antialiasing = HoVolumeLightAntialiasing.Temporal;
            ValidateAASceneSwitch(); Require(!owned.IsValid(), "scene switch retained world dust buffer");
            Require(Resources.Load<Shader>("HoVolumeLightDust") != null, "dust shader missing from runtime resources");
            foreach (var message in ShaderUtil.GetShaderMessages(Shader.Find("Hidden/lilToon/URP/VolumeLight/Dust")))
                Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            dustReport.passed = true;
            Debug.Log("[HoAtmosphereDustValidation] PASS " + JsonUtility.ToJson(dustReport));
        }
        private static void ValidateDustSubpixelStability()
        {
            var oldSize = region.dust.size; float oldFlutter = region.dust.flutter, oldBrightness = region.dust.brightness;
            var oldPosition = camera.transform.position;
            region.dust.size = Vector2.one * .005f; region.dust.flutter = 0; region.dust.brightness = 500;
            double minimum = double.MaxValue, maximum = 0;
            for (int step = 0; step < 9; ++step)
            {
                camera.transform.position = oldPosition + Vector3.right * (step * .012f);
                region.dust.enabled = false; var background = Render(); region.dust.enabled = true; Render();
                var field = region.GetDustField();
                var seeds = new Vector4[field.Count];
                for (int i = 0; i < seeds.Length; ++i) seeds[i] = new Vector4(1000, 1000, 1000, .5f);
                seeds[0] = new Vector4(0, 1, 5, .5f); field.buffer.SetData(seeds);
                var image = Render(); double sum = 0;
                for (int i = 0; i < image.Length; ++i) sum += Math.Max(0, image[i].r - background[i].r);
                minimum = Math.Min(minimum, sum); maximum = Math.Max(maximum, sum);
            }
            dustReport.subpixelEnergyRatio = (float)(maximum / Math.Max(1e-8, minimum));
            Require(minimum > .0001 && dustReport.subpixelEnergyRatio < 1.8f, "subpixel dust blinked or changed too much across pixel phases: " + dustReport.subpixelEnergyRatio);
            region.dust.size = oldSize; region.dust.flutter = oldFlutter; region.dust.brightness = oldBrightness;
            camera.transform.position = oldPosition; region.ReleaseDust(); Render();
        }
    }
}
