#pragma warning disable CS0618
using System;
using lilToon.URP.Extensions.Atmosphere;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.VolumeLight
{
    public static partial class HoVolumeLightValidation
    {
        [Serializable] private sealed class LocalReport
        {
            public bool passed;
            public float spotMean, spotShadowReduction, cutoutContrast, pointMean, rangeRatio, linearityError, depthLeak, twoLightRatio;
            public float[] pointFaceReduction = new float[6];
        }
        private static LocalReport localReport;
        private static System.Collections.IEnumerator localSteps;
        private static void TickLocal()
        {
            try { if (!localSteps.MoveNext()) { EditorApplication.update -= TickLocal; Finish(null); } }
            catch (Exception error) { EditorApplication.update -= TickLocal; Finish(error); }
        }
        private static System.Collections.IEnumerator ValidateLocal()
        {
            localReport = new LocalReport();
            sun.enabled = false; RenderSettings.sun = null;
            lightFeature.FeatureSettings.intensity = 1; lightFeature.FeatureSettings.maxDistance = 100;
            lightFeature.FeatureSettings.antialiasing = HoVolumeLightAntialiasing.Off;
            camera.transform.SetPositionAndRotation(new Vector3(0, 2, -8), Quaternion.identity);
            region.transform.position = new Vector3(0, 2, 6); region.size = new Vector3(16, 12, 12);
            region.blendDistance = .5f; region.density = .06f; region.anisotropy = 0; region.wind = Vector3.zero; region.dust.enabled = false;
            HoAtmosphereMediumClock.TimeOverride = 0;
            var asset = new SerializedObject(pipeline);
            asset.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
            asset.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
            asset.ApplyModifiedPropertiesWithoutUndo();
            var light = Keep(new GameObject("Local spot")).AddComponent<Light>();
            light.type = LightType.Spot; light.range = 18; light.spotAngle = 55; light.innerSpotAngle = 40;
            light.intensity = 40; light.color = new Color(1, .6f, .2f); light.shadows = LightShadows.Hard;
            light.transform.position = new Vector3(0, 2, -2); light.transform.rotation = Quaternion.identity;
            var source = light.gameObject.AddComponent<HoVolumeLightSource>(); source.stepCount = 128; source.shadowResolution = 512; source.intensity = 8;
            source.jitter = false;
            yield return null;
            var open = Render(); localReport.spotMean = Mean(open); Save(open, "local-spot-open");
            Require(localReport.spotMean > .001f && lightFeature.LocalLightCount == 1, "local spot needs sunlight or produced no volume: " + source.Status);
            light.intensity *= 2; var doubled = Render(); localReport.linearityError = Mathf.Abs(Mean(doubled) / Mean(open) - 2);
            Require(localReport.linearityError < .01f, "Light intensity did not sync"); light.intensity *= .5f;
            light.innerSpotAngle = 12; light.spotAngle = 25; Require(Mean(Render()) < Mean(open) * .7f, "spot angles not synced"); light.innerSpotAngle = 40; light.spotAngle = 55;
            var other = Keep(new GameObject("Second local spot")).AddComponent<Light>();
            other.type = light.type; other.range = light.range; other.spotAngle = light.spotAngle; other.innerSpotAngle = light.innerSpotAngle; other.intensity = light.intensity;
            other.color = light.color; other.transform.SetPositionAndRotation(light.transform.position, light.transform.rotation);
            var second = other.gameObject.AddComponent<HoVolumeLightSource>(); second.intensity = source.intensity; second.stepCount = source.stepCount; second.jitter = false;
            yield return null;
            var both = Render();
            other.enabled = false; var firstAlone = Render(); other.enabled = true; light.enabled = false; var secondAlone = Render(); light.enabled = true;
            Debug.Log($"[HoLocalVolumeValidation] ADD both={Mean(both)} first={Mean(firstAlone)} second={Mean(secondAlone)} original={Mean(open)}");
            localReport.twoLightRatio = Mean(both) / Mathf.Max(.00001f, Mean(firstAlone)+Mean(secondAlone));
            Require(Mathf.Abs(localReport.twoLightRatio - 1) < .02f, "multiple local lights failed to sum"); other.enabled = false;
            open = Render();
            var wall = Box("External local caster", new Vector3(0, 2, -.5f), new Vector3(40, 30, .1f));
            wall.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            yield return null;
            Require(!region.WorldBounds.Contains(wall.transform.position), "test caster must be outside SDF");
            var debugDepth = Keep(new RenderTexture(512, 512, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)); debugDepth.Create();
            HoVolumeLightLocalPass.DebugShadowTarget = RTHandles.Alloc(debugDepth);
            var shadowed = Render(); Save(shadowed, "local-spot-blocked");
            var previousTarget = target; target = debugDepth; Save(Read(debugDepth), "local-raw-depth"); target = previousTarget;
            HoVolumeLightLocalPass.DebugShadowTarget.Release(); HoVolumeLightLocalPass.DebugShadowTarget = null;
            localReport.spotShadowReduction = 1 - Mean(shadowed) / Mean(open);
            Require(localReport.spotShadowReduction > .8f, "HD failed external caster: " + source.Status + " reduction=" + localReport.spotShadowReduction);
            pipeline.shadowDistance = 1; asset.Update(); asset.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false; asset.ApplyModifiedPropertiesWithoutUndo();
            yield return null;
            Require(Mean(Render()) < Mean(open) * .2f, "HD depended on pipeline additional shadow support/distance");
            pipeline.shadowDistance = 50; asset.Update(); asset.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true; asset.ApplyModifiedPropertiesWithoutUndo();
            source.quality = HoVolumeLightQuality.SD; var sd = Render(); Require(ImageError(sd, open) < .001f, "SD unexpectedly uses HD shadow");
            source.quality = HoVolumeLightQuality.HD; wall.SetActive(false);
            light.shadows = LightShadows.None; Require(ImageError(Render(), open) < .001f, "Light shadow switch did not sync"); light.shadows = LightShadows.Hard;
            var holes = Quad("Local lilToon Cutout", new Vector3(0, 2, -.5f), new Vector3(30, 30, 1));
            holes.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            var lil = Mat("Hidden/lilToonCutout"); lil.SetFloat("_Cutoff", .5f); lil.SetFloat("_Cull", 0); lil.SetFloat("_AlphaToMask", 0); lil.SetFloat("_ZWrite", 1); lil.renderQueue = 2450;
            var alpha = Keep(new Texture2D(2, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
            alpha.SetPixels(new[] { Color.clear, Color.white }); alpha.Apply(); lil.SetTexture("_MainTex", alpha); holes.GetComponent<Renderer>().sharedMaterial = lil;
            yield return null;
            var cutout = Render(); Save(cutout, "local-spot-cutout");
            double left = 0, right = 0;
            for (int y = 20; y < 140; ++y) for (int x = 20; x < 70; ++x) { left += cutout[x + y*160].r; right += cutout[159-x + y*160].r; }
            localReport.cutoutContrast = (float)(Math.Abs(left-right)/Math.Max(.00001, left+right));
            Require(localReport.cutoutContrast > .8f && left+right > 1, "lilToon alpha did not shape local volume"); holes.SetActive(false);
            var foreground = Quad("Foreground local cutoff", new Vector3(0, 2, -3), new Vector3(30, 30, 1));
            yield return null;
            // Lit foreground surfaces may have specular contribution even with black
            // albedo. Compare the effect delta, retaining identical surface lighting.
            var blocked = Render(); source.enabled = false; var surfaceOnly = Render(); source.enabled = true; localReport.depthLeak = ImageError(blocked, surfaceOnly);
            Require(localReport.depthLeak < .0001f, "local leaked through foreground"); foreground.SetActive(false);
            source.enabled = false; Require(Mean(Render()) < .00001f, "disabled source retained local volume"); source.enabled = true;
            light.enabled = false; Require(Mean(Render()) < .00001f, "disabled Light retained local volume"); light.enabled = true;
            light.range = 5; localReport.rangeRatio = Mean(Render()) / Mean(open); Require(localReport.rangeRatio < .8f, "Light range not synced"); light.range = 18;
            region.volumeLightWeight = 0; Require(Mean(Render()) < .00001f, "SDF off retained local volume"); region.volumeLightWeight = 1;
            var observerPosition = camera.transform.position;
            camera.transform.position = new Vector3(0, 2, 4); Require(Mean(Render()) > .001f, "camera inside cone lost local volume"); camera.transform.position = observerPosition;
            camera.orthographic = true; camera.orthographicSize = 6; Require(Mean(Render()) > .001f, "orthographic local ray failed"); camera.orthographic = false;
            pipeline.renderScale = .5f; Require(Mean(Render()) > .001f, "half-scale local ray failed"); pipeline.renderScale = 1;
            pipeline.msaaSampleCount = 4; Require(Mean(Render()) > .001f, "MSAA local depth failed"); pipeline.msaaSampleCount = 1;
            light.color = Color.blue; var blue = Render(); foreach (var pixel in blue) Require(pixel.r < .0001f && pixel.g < .0001f, "Light color not synced");
            light.color = Color.white; light.type = LightType.Point; light.range = 10; light.transform.position = new Vector3(0, 2, 3);
            localReport.pointMean = Mean(Render()); Require(localReport.pointMean > .001f, "point produced no volume");
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            for (int face = 0; face < 6; ++face)
            {
                Vector3 axis = axes[face], center = light.transform.position;
                region.transform.position = center + axis*5; region.size = Vector3.one*4;
                camera.transform.position = center + axis*11; camera.transform.rotation = Quaternion.LookRotation(-axis, Mathf.Abs(axis.y) > .5f ? Vector3.forward : Vector3.up);
                var clear = Render();
                var caster = Quad("Point face caster " + face, center + axis*2, new Vector3(8, 8, 1));
                caster.transform.rotation = Quaternion.LookRotation(axis); caster.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                yield return null;
                var dark = Render(); localReport.pointFaceReduction[face] = 1-Mean(dark)/Mathf.Max(.000001f, Mean(clear));
                Require(Mean(clear) > .0001f && localReport.pointFaceReduction[face] > .8f, "point face " + face + " has incorrect visibility: " + localReport.pointFaceReduction[face]);
                caster.SetActive(false);
            }
            // Artist-facing side view: real cone, local cutout stripes, foreground geometry.
            light.type = LightType.Spot; light.range = 20; light.spotAngle = 42; light.innerSpotAngle = 30; light.intensity = 50;
            light.transform.SetPositionAndRotation(new Vector3(-2, 3, -2), Quaternion.LookRotation(new Vector3(.25f, -.1f, 1)));
            region.transform.position = new Vector3(0, 2, 6); region.size = new Vector3(18, 12, 18);
            camera.transform.position = new Vector3(9, 4, -7); camera.transform.LookAt(new Vector3(0, 2, 5));
            target = Keep(new RenderTexture(960, 640, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)); target.Create(); camera.aspect = 1.5f;
            source.stepCount = 192; source.jitter = true;
            var slats = new Color[16]; for (int i = 0; i < slats.Length; ++i) slats[i] = i%3 == 0 ? Color.white : Color.clear;
            var stripes = Keep(new Texture2D(16, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point }); stripes.SetPixels(slats); stripes.Apply(); lil.SetTexture("_MainTex", stripes);
            holes.transform.SetPositionAndRotation(new Vector3(-1.6f, 2.8f, -.5f), Quaternion.identity); holes.transform.localScale = new Vector3(7, 7, 1); holes.SetActive(true);
            yield return null;
            var sideCutout = Render(); Debug.Log("[HoLocalVolumeValidation] SIDE cut="+Mean(sideCutout)+" "+source.Status); Save(sideCutout, "local-spot-side-cutout"); holes.SetActive(false);
            yield return null;
            var sideOpen = Render(); Save(sideOpen, "local-spot-side-open");
            Debug.Log("[HoLocalVolumeValidation] SIDE open="+Mean(sideOpen)+" "+source.Status);
            Require(ImageError(sideCutout, sideOpen) > .0001f && Mean(sideCutout) < Mean(sideOpen) * .99f,
                "side view Cutout did not affect local volume");
            region.densityTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture3D>("Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/Atmosphere/Textures/HoAtmosphereWisps64.asset");
            region.noiseStrength = .8f; region.noiseWorldSize = 4; holes.SetActive(true);
            yield return null; Save(Render(), "local-spot-side-flow");
            light.type = LightType.Point; light.transform.position = new Vector3(0, 2, 5); light.range = 7; holes.SetActive(false);
            yield return null; Save(Render(), "local-point-side");
            localReport.passed = true; Debug.Log("[HoLocalVolumeValidation] PASS " + JsonUtility.ToJson(localReport));
        }
    }
}
