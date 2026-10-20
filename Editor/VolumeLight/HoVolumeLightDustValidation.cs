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
            public int clusters, changedPixels;
            public int defaultChangedPixels;
            public float defaultMeanContribution;
            public int hugeRegionClusters;
            public float hugeRegionError;
            public string gpu;
            public float clustersGpuMs = -1, millionGlobalGpuMs = -1, millionLocalGpuMs = -1;
            public int benchmarkClusterPixels, benchmarkGlobalPixels, benchmarkLocalPixels;
            public float benchmarkClusterMean, benchmarkGlobalMean, benchmarkLocalMean;
            public int deepNearClusters, deepMiddleClusters, deepFarClusters, distantPixels, deepSharedNearSeeds;
            public float distantMean, thicknessRatio, largeSceneGpuMs = -1;
            public DustTimingReport[] timings = new DustTimingReport[4];
            public float meanContribution, movementError, shadowReduction, extinctionRatio, centerExtinctionError, narrowPhaseRatio, cutoutContrast;
        }
        [Serializable] private sealed class DustTimingReport
        {
            public int stage;
            public float mean, median, p95, minimum, maximum;
            public float[] samples;
            public int[] frameIds;
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

            region.dust.brightness = 15; region.dust.flutter = .08f;
            var grains = DustContribution(); Save(Render(), "dust-on"); Save(grains, "dust-contribution");
            dustReport.clusters = lightFeature.DustClusterCount; dustReport.meanContribution = Mean(grains);
            foreach (var p in grains) if (p.r > .01f) dustReport.changedPixels++;
            Require(dustReport.clusters > 0 && dustReport.clusters <= region.dust.maxClusters && lightFeature.HasDustMaterial, "shared dust did not allocate/draw");
            Require(dustReport.changedPixels > 400 && dustReport.meanContribution > .0005f, "cluster dust produced insufficient coverage");
            var field = region.GetDustField(); var buffer = field.buffer; int rebuilds = field.RebuildCount;
            var repeat = Render(); var sameTime = Render(); Require(ImageError(repeat, sameTime) < .00001f, "same world time changed dust positions");
            var observer = camera; camera = Keep(new GameObject("Shared dust second observer")).AddComponent<Camera>(); camera.enabled = false;
            camera.transform.position = new Vector3(1, 1, -7); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.GetUniversalAdditionalCameraData().volumeLayerMask = -1; Render(); camera = observer;
            Render(); Require(region.GetDustField().buffer == buffer && field.RebuildCount == rebuilds + 1, "second observer replaced first visibility buffer");
            var originalSeeds = new Vector4[field.Count]; field.buffer.GetData(originalSeeds);
            var seedSet = new System.Collections.Generic.HashSet<Vector4>(originalSeeds);
            region.transform.position += Vector3.right * .3f; Render();
            var movedSeeds = new Vector4[field.Count]; field.buffer.GetData(movedSeeds); int shared = 0;
            foreach (var seed in movedSeeds) if (seedSet.Contains(seed)) shared++;
            Require(shared > originalSeeds.Length * .75f, "moving a region dragged/reseeded its whole world field"); region.transform.position -= Vector3.right * .3f; Render();
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
            region.dust.maxClusters = 8; Render();
            Require(lightFeature.DustCapacityLimited && lightFeature.DustClusterCount <= 8, "dust cluster budget was not enforced");
            region.dust.maxClusters = 64; Render();
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
            Require(dustReport.defaultChangedPixels > 4000, "shipped dust defaults produced insufficient grain coverage");
            // Enlarging an already encompassing region must not dilute the local grid.
            region.transform.position = camera.transform.position; region.size = Vector3.one * 100;
            var local = DustContribution(); region.size = Vector3.one * 1000;
            var huge = DustContribution(); Save(huge, "dust-huge-region");
            dustReport.hugeRegionClusters = lightFeature.DustClusterCount;
            dustReport.hugeRegionError = ImageError(local, huge);
            Require(dustReport.hugeRegionError < .00001f && dustReport.hugeRegionClusters <= 64, "large region diluted nearby dust");
            region.dust.grainDensity = .2f; var sparse = DustContribution();
            Require(Mean(sparse) < Mean(huge) * .8f, "grain density did not change the texture population"); region.dust.grainDensity = 1;
            var emptyTile = Keep(new Texture2D(1, 1, TextureFormat.RGBA32, false, true)); emptyTile.SetPixel(0, 0, Color.clear); emptyTile.Apply();
            region.dust.texture = emptyTile; Require(Mathf.Abs(Mean(DustContribution())) < .00001f, "custom empty alpha texture was ignored"); region.dust.texture = null;
            ValidateDustDepth();
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
        private static void ValidateDustDepth()
        {
            region.transform.position = camera.transform.position; region.size = Vector3.one * 1000;
            region.density = .02f; region.anisotropy = .3f; region.wind = Vector3.zero;
            region.dust = new HoAtmosphereDustSettings { enabled = true, maxDistance = 60, maxClusters = 128, thickness = 3 };
            camera.farClipPlane = 1000; HoAtmosphereMediumClock.TimeOverride = 0;
            Render(); var field = region.GetDustField();
            var nearSeeds = new Vector4[field.Count]; field.buffer.GetData(nearSeeds, 0, 0, nearSeeds.Length);
            var originalNear = new System.Collections.Generic.HashSet<Vector4>();
            foreach (var seed in nearSeeds) if (Mathf.FloorToInt(seed.w) == 0) originalNear.Add(seed);
            region.dust.maxDistance = 200; var deep = DustContribution(); Save(deep, "dust-depth-200m");
            field = region.GetDustField(); var deepSeeds = new Vector4[field.Count]; field.buffer.GetData(deepSeeds, 0, 0, deepSeeds.Length);
            foreach (var seed in deepSeeds)
            {
                float distance = Vector3.Distance(camera.transform.position, seed);
                if (distance < 24) dustReport.deepNearClusters++;
                else if (distance < 80) dustReport.deepMiddleClusters++;
                else dustReport.deepFarClusters++;
                if (originalNear.Contains(seed)) dustReport.deepSharedNearSeeds++;
            }
            Require(dustReport.deepNearClusters > 0 && dustReport.deepMiddleClusters > 0 && dustReport.deepFarClusters > 0,
                "display-distance budget left one depth range empty");
            Require(dustReport.deepSharedNearSeeds > 4, "extending display distance replaced the nearby world grid");
            region.dust.maxClusters = 8; Render();
            var bounded = region.GetDustField().Prepare(HoAtmosphereRegionRegistry.Capture(), camera);
            Require(bounded.distance == 200 && bounded.count <= 8, "tight budget silently shortened display distance");
            region.dust.maxClusters = 128;
            region.dust.thickness = .5f; var thinImage = DustContribution(); float thin = Mean(thinImage); Save(thinImage, "dust-depth-thin");
            region.dust.thickness = 3; var thick = DustContribution(); Save(thick, "dust-depth-thick");
            dustReport.thicknessRatio = Mean(thick) / Mathf.Max(.00001f, thin);
            Require(dustReport.thicknessRatio > 1.5f, "space thickness did not increase actual layered coverage");
            // Only air 80-160 metres in front of the observer is active. Bright pixels
            // in this test must come from distant cards, never the dense nearby shell.
            region.transform.position = camera.transform.position + camera.transform.forward * 120;
            region.size = new Vector3(240, 160, 80); region.blendDistance = 1;
            var distant = DustContribution(); Save(distant, "dust-distant-80-160m");
            dustReport.distantMean = Mean(distant);
            foreach (var p in distant) if (p.r > .001f) dustReport.distantPixels++;
            Require(dustReport.distantPixels > 4000 && dustReport.distantMean > .0002f, "distant mip filtering erased the dust layer");
        }
    }
}
