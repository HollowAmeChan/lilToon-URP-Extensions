using System;
using System.Collections.Generic;
using System.Reflection;
using lilToon.URP.Extensions.CharacterShadow;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.CharacterShadow
{
    public static class HoCharacterShadowValidation
    {
        [MenuItem("HoLil/Validation/Validate Ho-CharacterShadow")]
        public static void Validate()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("CS Validation");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            try
            {
                var group = root.AddComponent<HoObjectBufferGroup>();
                var cs = root.AddComponent<HoCharacterShadow>();
                cs.objectGroup = group;
                cs.center = Vector3.zero;
                cs.size = Vector3.one * 2;
                var sunObject = new GameObject("Sun");
                sunObject.transform.SetParent(root.transform);
                var sun = sunObject.AddComponent<Light>(); sun.type = LightType.Directional;
                Vector3[] corners = new Vector3[8]; cs.GetWorldCorners(corners);
                Type projection = typeof(HoCharacterShadow).Assembly.GetType("lilToon.URP.Extensions.CharacterShadow.HoCharacterShadowProjection");
                MethodInfo build = projection.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic);
                object baseline = build.Invoke(null, new object[] { cs, sun, corners, new List<Bounds>(), 2048, 1f, 1f, 1f });
                var casters = new List<Bounds> { new Bounds(new Vector3(0, 0, -100), Vector3.one * 4) };
                object upstream = build.Invoke(null, new object[] { cs, sun, corners, casters, 2048, 1f, 1f, 1f });
                Matrix4x4 a = Field<Matrix4x4>(baseline, "projection"), b = Field<Matrix4x4>(upstream, "projection");
                Require(Mathf.Approximately(a.m00, b.m00) && Mathf.Approximately(a.m11, b.m11), "Upstream casters changed XY precision");
                Require(Field<float>(upstream, "farPlane") > Field<float>(baseline, "farPlane") + 90, "Off-screen upstream caster not covered");
                casters.Add(new Bounds(new Vector3(10000, 0, -10000), Vector3.one));
                object unrelated = build.Invoke(null, new object[] { cs, sun, corners, casters, 2048, 1f, 1f, 1f });
                Require(Mathf.Approximately(Field<float>(upstream, "farPlane"), Field<float>(unrelated, "farPlane")), "Unrelated caster expanded depth range");
                Matrix4x4 worldToShadow = Field<Matrix4x4>(upstream, "worldToShadow");
                foreach (Vector3 corner in corners)
                {
                    Vector3 p = worldToShadow.MultiplyPoint3x4(corner);
                    Require(p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1 && p.z >= 0 && p.z <= 1, "Receiver escaped shadow volume");
                }
                Shader shader = Shader.Find("Hidden/Ho-CharacterShadow/Debug");
                Require(shader != null && !ShaderUtil.ShaderHasError(shader), "CS debug shader missing or failed");
                Debug.Log("[Ho-CS Validation] PASS: fixed XY, upstream coverage, unrelated-caster rejection, receiver coverage, debug shader.");
            }
            finally { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("[Ho-CS Validation] " + message); }

        // Distance sweep: reproduces the "shadows vanish when the camera is far" report.
        // Mirrors the PTP test scene: perspective 18 deg, far 10000, URP shadow distance 50,
        // 4 cascades, a room-sized ground caster and a character-sized receiver box.
        // Batch entry point; run with a graphics device, not -nographics.
        public static void ValidateDistanceRendering()
        {
            Require(Application.isBatchMode, "Distance validation runs in a separate batch editor only");
            RenderPipelineAsset previousGraphics = GraphicsSettings.defaultRenderPipeline;
            RenderPipelineAsset previousQuality = QualitySettings.renderPipeline;
            Light previousSun = RenderSettings.sun;
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            var created = new List<Object>();
            RenderTexture target = null;
            var previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); created.Add(renderer);
                var feature = ScriptableObject.CreateInstance<HoCharacterShadowRendererFeature>(); created.Add(feature);
                feature.Settings.resolution = HoCharacterShadowResolution.R1024;
                feature.Settings.filterRadius = 1;
                renderer.rendererFeatures.Add(feature);
                var pipeline = UniversalRenderPipelineAsset.Create(renderer); created.Add(pipeline);
                pipeline.shadowDistance = 50;
                pipeline.shadowCascadeCount = 4;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;

                var sunObject = new GameObject("CS Sweep Sun"); created.Add(sunObject);
                Light sun = sunObject.AddComponent<Light>(); sun.type = LightType.Directional;
                sun.shadows = LightShadows.Hard; sun.intensity = 1; RenderSettings.sun = sun;

                var cameraObject = new GameObject("CS Sweep Camera"); created.Add(cameraObject);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false; camera.orthographic = false; camera.fieldOfView = 18;
                camera.nearClipPlane = 0.5f; camera.farClipPlane = 10000;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

                // Room-sized scene caster so the light-space depth search must reject far geometry.
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); created.Add(ground);
                ground.transform.position = new Vector3(0, -1, 0);
                ground.transform.localScale = Vector3.one * 40;
                ground.GetComponent<MeshRenderer>().sharedMaterial =
                    new Material(Shader.Find("Universal Render Pipeline/Lit"));

                var receiver = GameObject.CreatePrimitive(PrimitiveType.Quad); created.Add(receiver);
                receiver.transform.localScale = Vector3.one * 2;
                receiver.transform.position = new Vector3(0, 0, 0);
                var receiverRenderer = receiver.GetComponent<MeshRenderer>();
                receiverRenderer.shadowCastingMode = ShadowCastingMode.Off;
                var probeMaterial = new Material(Shader.Find("Hidden/Ho-CharacterShadow/ValidationProbe")); created.Add(probeMaterial);
                receiverRenderer.sharedMaterial = probeMaterial;
                var group = receiver.AddComponent<HoObjectBufferGroup>();
                group.parts.Add(new HoObjectBufferPartEntry { name = "Receiver", includeChildren = false, renderers = new Object[] { receiverRenderer } });
                group.Apply();
                var cs = receiver.AddComponent<HoCharacterShadow>(); cs.objectGroup = group;
                cs.center = Vector3.zero; cs.size = new Vector3(1.25f, 1.25f, 0.5f);

                // Same occluder placement as the reference test: known to darken the centre pixel.
                var caster = GameObject.CreatePrimitive(PrimitiveType.Cube); created.Add(caster);
                caster.transform.position = new Vector3(0, 0, -5);
                caster.transform.localScale = new Vector3(1, 1, 0.3f);
                var casterRenderer = caster.GetComponent<MeshRenderer>();
                casterRenderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                casterRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

                target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32); target.Create();
                Color RenderPixel(string label)
                {
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    var prior = RenderTexture.active; RenderTexture.active = target;
                    var image = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); image.Apply();
                    Color result = image.GetPixel(128, 128);
                    string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HoCSValidation");
                    System.IO.Directory.CreateDirectory(directory);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, label + ".png"), image.EncodeToPNG());
                    Object.DestroyImmediate(image); RenderTexture.active = prior;
                    return result;
                }
                float RenderCenter(string label) { return RenderPixel(label).r; }

                float[] distances = { 240f, 120f, 60f, 50f, 37f, 20f, 8f };
                var results = new List<string>();
                var failures = new List<string>();
                ground.SetActive(false);
                camera.orthographic = false;
                camera.fieldOfView = 18;
                // Cascade count is the variable that currently decides whether the local atlas
                // gets drawn at all: >1 empties it whenever the camera is not inside its own
                // cascade 0. Both counts are swept so a fix can show up here.
                foreach (int cascades in new[] { 1, 4 })
                {
                    pipeline.shadowCascadeCount = cascades;
                    foreach (float distance in distances)
                    {
                        // Keep the subject centred in the 18 degree frustum: offset x/z proportionally.
                        camera.transform.position = new Vector3(distance * 0.32f, distance * 0.16f, -distance * 0.93f);
                        camera.transform.LookAt(Vector3.zero);
                        float value = RenderCenter("cs-distance-" + distance.ToString("F0"));
                        // Read the atlas tile itself: 0 = cleared far (empty tile), >0 = caster stored.
                        // Debug views are gated per view type, so the Game View switch has to be on
                        // (there is no Volume in this test scene, so the feature fallback applies).
                        feature.Settings.debugInGameView = true;
                        feature.Settings.debugMode = HoCharacterShadowDebugMode.Character;
                        feature.Settings.debugCharacter = 0;
                        float atlasTile = RenderCenter("cs-atlas-tile-" + distance.ToString("F0"));
                        feature.Settings.debugMode = HoCharacterShadowDebugMode.Off;
                        string tag = "casc" + cascades + " " + distance.ToString("F0") + "m";
                        results.Add($"{tag} vis={value:F3} atlas={atlasTile:F3}");
                        if (value > 0.2f) failures.Add($"{tag} value={value:F3}, atlas={atlasTile:F3}");                    }
                }
                Debug.Log("[Ho-CS Distance] " + string.Join(" | ", results));
                Require(failures.Count == 0, "Distant cameras lost CS: " + string.Join(" ; ", failures)
                    + " | status=" + HoCharacterShadowRendererFeature.LastCullStatus + " | cs=" + cs.status);
                Debug.Log("[Ho-CS Distance] PASS");
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = previousGraphics;
                QualitySettings.renderPipeline = previousQuality;
                RenderSettings.sun = previousSun;
                ShaderUtil.allowAsyncCompilation = previousAsync;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                for (int i = created.Count - 1; i >= 0; i--) if (created[i] != null) Object.DestroyImmediate(created[i]);
                HoObjectBufferRegistry.Release();
                HoObjectBufferRegistry.MarkDirty();
                if (previousScene.IsValid() && previousScene.isLoaded)
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousScene);
            }
        }

        // Batch entry point: uses transient objects/assets and restores project pipeline settings.
        // Run with a graphics device, not -nographics.
        public static void ValidateRendering()
        {
            Require(Application.isBatchMode, "Rendering validation runs in a separate batch editor only");
            Validate();
            RenderPipelineAsset previousGraphics = GraphicsSettings.defaultRenderPipeline;
            RenderPipelineAsset previousQuality = QualitySettings.renderPipeline;
            Light previousSun = RenderSettings.sun;
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            var created = new List<Object>();
            RenderTexture target = null;
            var previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); created.Add(renderer);
                var feature = ScriptableObject.CreateInstance<HoCharacterShadowRendererFeature>(); created.Add(feature);
                feature.Settings.resolution = HoCharacterShadowResolution.R1024;
                feature.Settings.filterRadius = 0;
                renderer.rendererFeatures.Add(feature);
                var pipeline = UniversalRenderPipelineAsset.Create(renderer); created.Add(pipeline);
                pipeline.shadowDistance = 50;
                pipeline.shadowCascadeCount = 1;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;

                var sunObject = new GameObject("CS Test Sun"); created.Add(sunObject);
                Light sun = sunObject.AddComponent<Light>(); sun.type = LightType.Directional;
                sun.shadows = LightShadows.Hard; sun.intensity = 1; RenderSettings.sun = sun;

                var cameraObject = new GameObject("CS Test Camera"); created.Add(cameraObject);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1.6f; camera.aspect = 1;
                camera.transform.position = new Vector3(4, 2, -6);
                camera.transform.LookAt(Vector3.zero);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

                var receiver = GameObject.CreatePrimitive(PrimitiveType.Quad); created.Add(receiver);
                receiver.transform.localScale = Vector3.one * 2;
                var receiverRenderer = receiver.GetComponent<MeshRenderer>();
                receiverRenderer.shadowCastingMode = ShadowCastingMode.Off;
                var probeMaterial = new Material(Shader.Find("Hidden/Ho-CharacterShadow/ValidationProbe")); created.Add(probeMaterial);
                receiverRenderer.sharedMaterial = probeMaterial;
                var group = receiver.AddComponent<HoObjectBufferGroup>();
                group.parts.Add(new HoObjectBufferPartEntry { name = "Receiver", includeChildren = false, renderers = new Object[] { receiverRenderer } });
                group.Apply();
                var cs = receiver.AddComponent<HoCharacterShadow>(); cs.objectGroup = group;
                cs.center = Vector3.zero; cs.size = new Vector3(1.25f, 1.25f, 0.5f);

                var caster = GameObject.CreatePrimitive(PrimitiveType.Cube); created.Add(caster);
                caster.transform.position = new Vector3(0, 0, -5);
                caster.transform.localScale = new Vector3(1, 1, 0.3f);
                var casterRenderer = caster.GetComponent<MeshRenderer>();
                var casterMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); created.Add(casterMaterial);
                casterRenderer.sharedMaterial = casterMaterial;
                casterRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                Require(!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), casterRenderer.bounds), "Test caster is not off-screen");

                target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32); target.Create();
                float RenderCenter(string label = null)
                {
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    var prior = RenderTexture.active; RenderTexture.active = target;
                    var image = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); image.Apply();
                    float result = image.GetPixel(128, 128).r;
                    if (label != null)
                    {
                        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HoCSValidation");
                        System.IO.Directory.CreateDirectory(directory);
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, label + ".png"), image.EncodeToPNG());
                    }
                    Object.DestroyImmediate(image); RenderTexture.active = prior;
                    return result;
                }
                RenderCenter(); // pipeline initialization / shader warmup
                float shadow = RenderCenter("cs-shadow");
                Require(Shader.GetGlobalFloat("_HoCSActive") > 0.5f, "CS did not publish data: " + cs.status);
                Require(shadow < 0.2f, "Off-screen ShadowsOnly caster missing, visibility=" + shadow);
                casterRenderer.shadowCastingMode = ShadowCastingMode.Off;
                float noCaster = RenderCenter("cs-caster-off");
                Require(noCaster > 0.8f, "ShadowCastingMode.Off still casts: " + noCaster);
                casterRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                cs.enabled = false;
                float fallback = RenderCenter();
                Require(fallback > 0.8f, "Disabled CS did not fall back: " + fallback);
                cs.enabled = true;
                cs.receiverParts.Add("NotAReceiver");
                Require(RenderCenter() > 0.8f, "Unselected identity received CS");
                cs.receiverParts.Clear();
                feature.SetActive(false);
                Require(RenderCenter() > 0.8f, "Inactive feature leaked previous camera data");
                feature.SetActive(true);
                Require(RenderCenter() < 0.2f, "Re-enabled feature did not restore CS");
                Vector3 nearCameraPosition = camera.transform.position;
                camera.transform.position *= 5;
                camera.transform.LookAt(Vector3.zero);
                float distantShadow = RenderCenter("cs-distant-camera");
                Require(distantShadow < 0.2f, $"Distant camera lost CS: {distantShadow:F3}, {cs.status}, depth={cs.depthRange:F3}");
                camera.orthographic = false;
                camera.fieldOfView = 35;
                float perspectiveShadow = RenderCenter("cs-distant-perspective");
                Require(perspectiveShadow < 0.2f, $"Distant perspective camera lost CS: {perspectiveShadow:F3}");
                camera.orthographic = true;
                camera.transform.position = nearCameraPosition;
                camera.transform.LookAt(Vector3.zero);
                feature.Settings.debugInGameView = true;
                feature.Settings.debugMode = HoCharacterShadowDebugMode.Character;
                float atlasDepth = RenderCenter("cs-atlas-debug");
                Require(atlasDepth > 0.5f, "Atlas debug did not display caster depth");
                feature.Settings.debugMode = HoCharacterShadowDebugMode.Off;
                feature.Settings.debugInGameView = false;

                // Volume overrides (the UI contract): 启用 / 单角色分辨率 are per-camera and have to reach the
                // renderer through Ho-CharacterShadowVolume, while an un-overridden field keeps the feature
                // fallback. SingleCameraRequest does not run the volume framework, so the stack is updated by
                // hand here exactly like URP does once per camera. The main stack is mutated in place: a
                // hand-made stack misses URP's component set and breaks ForwardLights.
                var profile = ScriptableObject.CreateInstance<VolumeProfile>(); created.Add(profile);
                HoCharacterShadowVolume volumeComponent = profile.Add<HoCharacterShadowVolume>();
                var volumeObject = new GameObject("CS Test Volume"); created.Add(volumeObject);
                var volume = volumeObject.AddComponent<Volume>();
                volume.isGlobal = true; volume.profile = profile;
                UniversalAdditionalCameraData additionalData = camera.GetUniversalAdditionalCameraData();
                VolumeStack volumeStack = VolumeManager.instance.stack;
                string volumeResult = "volume=skipped(no stack)";
                try
                {
                    if (volumeStack != null)
                    {
                        volumeComponent.enable.overrideState = true;
                        volumeComponent.enable.value = false;
                        VolumeManager.instance.Update(volumeStack, camera.transform, additionalData.volumeLayerMask);
                        float volumeDisabled = RenderCenter("cs-volume-disabled");
                        Require(volumeDisabled > 0.8f, $"Volume 覆盖的启用没有生效（关闭后仍走 CS）: {volumeDisabled:F3}");

                        volumeComponent.enable.value = true;
                        volumeComponent.resolution.overrideState = true;
                        volumeComponent.resolution.value = HoCharacterShadowResolution.R512;
                        VolumeManager.instance.Update(volumeStack, camera.transform, additionalData.volumeLayerMask);
                        RenderCenter();
                        float atlasSize = Shader.GetGlobalVector("_HoCSAtlasSize").z;
                        Require(Mathf.Approximately(atlasSize, 512f), $"Volume 覆盖的单角色分辨率没有生效: {atlasSize:F0}");
                        volumeResult = $"volume={volumeDisabled:F3}/atlas {atlasSize:F0}";
                    }
                    else
                    {
                        Debug.LogWarning("[Ho-CS Rendering] 没有可用的 volume stack，跳过量覆盖检查。");
                    }
                }
                finally
                {
                    volumeComponent.enable.overrideState = false;
                    volumeComponent.resolution.overrideState = false;
                    volume.profile = null;
                    Object.DestroyImmediate(volumeObject);
                    created.Remove(volumeObject);
                    VolumeManager.instance.ResetMainStack();
                    if (VolumeManager.instance.stack != null)
                        VolumeManager.instance.Update(VolumeManager.instance.stack, camera.transform, additionalData.volumeLayerMask);
                }

                Shader toonShader = Shader.Find("lilToon");
                Require(toonShader != null, "lilToon shader unavailable");
                var toon = new Material(toonShader); created.Add(toon);
                toon.SetFloat("_UseShadow", 1); toon.SetFloat("_ShadowReceive", 1);
                toon.SetFloat("_ShadowStrength", 1); toon.SetFloat("_LightMinLimit", 0);
                toon.SetColor("_ShadowColor", Color.black);
                receiverRenderer.sharedMaterial = toon;
                float toonShadow = RenderCenter("liltoon-shadow");
                casterRenderer.shadowCastingMode = ShadowCastingMode.Off;
                float toonLit = RenderCenter("liltoon-lit");
                Require(!ShaderUtil.ShaderHasError(toonShader), "lilToon has shader compile errors");
                // Existing NPR ambient/ramp response is intentionally retained; unlike the probe,
                // the final material is not expected to switch between pure black and white.
                Require(toonLit > toonShadow + 0.05f, $"lilToon reception did not respond: {toonShadow:F3} / {toonLit:F3}");
                Debug.Log($"[Ho-CS Rendering] PASS: off-screen caster={shadow:F3}, distant={distantShadow:F3}, perspective={perspectiveShadow:F3}, cast-off={noCaster:F3}, disabled fallback={fallback:F3}, identity/feature reset, atlas={atlasDepth:F3}, {volumeResult}, lilToon={toonShadow:F3}/{toonLit:F3}.");
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = previousGraphics;
                QualitySettings.renderPipeline = previousQuality;
                RenderSettings.sun = previousSun;
                ShaderUtil.allowAsyncCompilation = previousAsync;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                for (int i = created.Count - 1; i >= 0; i--) if (created[i] != null) Object.DestroyImmediate(created[i]);
                HoObjectBufferRegistry.Release();
                HoObjectBufferRegistry.MarkDirty();
                if (previousScene.IsValid() && previousScene.isLoaded)
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousScene);
            }
        }

        // Regression for the report "turning CS on takes away the normal main-light shadows of every
        // other object in the scene". The CS pass runs at BeforeRenderingShadows, i.e. *before* URP
        // builds this camera's shadow map, so any per-light shadow state it leaves behind lands there.
        // The receiver is URP/Lit (so the measurement is URP's own shadow map, not CS) and no caster
        // ever sits inside the CS box (so CS itself contributes no darkening).
        // The lilToon caster pass additionally covers the OB palette SRV: on D3D12 a lilToon draw with
        // "_HoObjectBufferEntries" unbound is skipped, which also loses shadows.
        // Run on D3D11 and (with -force-d3d12) on D3D12.
        public static void ValidateSceneShadows()
        {
            Require(Application.isBatchMode, "Scene shadow validation runs in a separate batch editor only");
            Validate();
            RenderPipelineAsset previousGraphics = GraphicsSettings.defaultRenderPipeline;
            RenderPipelineAsset previousQuality = QualitySettings.renderPipeline;
            Light previousSun = RenderSettings.sun;
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            AmbientMode previousAmbientMode = RenderSettings.ambientMode;
            Color previousAmbientColor = RenderSettings.ambientLight;
            var created = new List<Object>();
            var skippedDraws = new List<string>();
            Application.LogCallback logCallback = null;
            RenderTexture target = null;
            var previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                logCallback = (condition, stackTrace, type) =>
                {
                    if (!string.IsNullOrEmpty(condition)
                        && (condition.Contains("requires a buffer") || condition.Contains("Skipping draw calls")))
                    {
                        skippedDraws.Add(condition);
                    }
                };
                Application.logMessageReceived += logCallback;

                // Flat black ambient: an unshadowed white ground is ~1.0 and a shadowed one ~0.0, so the
                // measurement does not depend on the project's lighting environment.
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;

                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); created.Add(renderer);
                var feature = ScriptableObject.CreateInstance<HoCharacterShadowRendererFeature>(); created.Add(feature);
                feature.Settings.resolution = HoCharacterShadowResolution.R1024;
                feature.Settings.filterRadius = 0;
                renderer.rendererFeatures.Add(feature);
                var pipeline = UniversalRenderPipelineAsset.Create(renderer); created.Add(pipeline);
                pipeline.shadowDistance = 50;
                pipeline.shadowCascadeCount = 4;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;

                var sunObject = new GameObject("Scene Shadows Sun"); created.Add(sunObject);
                sunObject.transform.rotation = Quaternion.Euler(55, 25, 0);
                Light sun = sunObject.AddComponent<Light>(); sun.type = LightType.Directional;
                sun.shadows = LightShadows.Hard; sun.intensity = 1; sun.shadowStrength = 1;
                RenderSettings.sun = sun;

                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); created.Add(ground);
                ground.transform.position = Vector3.zero;
                ground.transform.localScale = Vector3.one * 20;
                var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); created.Add(groundMaterial);
                groundMaterial.SetColor("_BaseColor", Color.white);
                groundMaterial.SetFloat("_Smoothness", 0);
                ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

                // The caster floats above the ground away from the CS box, so its shadow can only come
                // from URP's camera shadow map.
                var caster = GameObject.CreatePrimitive(PrimitiveType.Cube); created.Add(caster);
                Vector3 casterCenter = new Vector3(0, 2.5f, 0);
                caster.transform.position = casterCenter;
                var casterRenderer = caster.GetComponent<MeshRenderer>();
                var litCasterMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); created.Add(litCasterMaterial);
                litCasterMaterial.SetColor("_BaseColor", Color.gray);
                casterRenderer.sharedMaterial = litCasterMaterial;

                Vector3 lightTravel = -sun.transform.forward;
                Vector3 landing = casterCenter + lightTravel * (-casterCenter.y / lightTravel.y);

                var cameraObject = new GameObject("Scene Shadows Camera"); created.Add(cameraObject);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false; camera.orthographic = false; camera.fieldOfView = 40;
                camera.nearClipPlane = 0.5f; camera.farClipPlane = 1000;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                Vector3 lookAt = new Vector3((landing.x + casterCenter.x) * 0.5f, 0, (landing.z + casterCenter.z) * 0.5f);
                camera.transform.position = lookAt + new Vector3(5.5f, 7.5f, -10.5f);
                camera.transform.LookAt(lookAt);

                // The CS receiver: in view, in its own OB group, with an empty box (no CS caster).
                var receiver = GameObject.CreatePrimitive(PrimitiveType.Quad); created.Add(receiver);
                receiver.transform.localScale = Vector3.one * 1.5f;
                receiver.transform.position = landing + new Vector3(3.0f, 1.0f, 0.5f);
                receiver.transform.rotation = Quaternion.LookRotation((camera.transform.position - receiver.transform.position).normalized, Vector3.up);
                var receiverRenderer = receiver.GetComponent<MeshRenderer>();
                receiverRenderer.shadowCastingMode = ShadowCastingMode.Off;
                var probeMaterial = new Material(Shader.Find("Hidden/Ho-CharacterShadow/ValidationProbe")); created.Add(probeMaterial);
                receiverRenderer.sharedMaterial = probeMaterial;
                var group = receiver.AddComponent<HoObjectBufferGroup>();
                group.parts.Add(new HoObjectBufferPartEntry { name = "Receiver", includeChildren = false, renderers = new Object[] { receiverRenderer } });
                group.Apply();
                var cs = receiver.AddComponent<HoCharacterShadow>(); cs.objectGroup = group;
                cs.center = Vector3.zero; cs.size = new Vector3(1.25f, 1.25f, 0.5f);
                HoObjectBufferRegistry.EnsureBuilt();

                target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32); target.Create();
                float GroundAt(Vector3 world)
                {
                    Vector3 viewport = camera.WorldToViewportPoint(world);
                    Require(viewport.z > 0 && viewport.x > 0.05f && viewport.x < 0.95f && viewport.y > 0.05f && viewport.y < 0.95f,
                        $"Shadow sample point is off screen ({viewport.x:F2}, {viewport.y:F2}, {viewport.z:F2})");
                    int cx = Mathf.RoundToInt(viewport.x * 256.0f);
                    int cy = Mathf.RoundToInt(viewport.y * 256.0f);
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    var prior = RenderTexture.active; RenderTexture.active = target;
                    var image = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); image.Apply();
                    RenderTexture.active = prior;
                    float sum = 0;
                    for (int y = -2; y <= 2; y++)
                        for (int x = -2; x <= 2; x++)
                            sum += image.GetPixel(cx + x, cy + y).grayscale;
                    Object.DestroyImmediate(image);
                    return sum / 25.0f;
                }

                Vector3 samplePoint = landing + new Vector3(0, 0.02f, 0);
                GroundAt(samplePoint); // pipeline initialization / shader warmup

                casterRenderer.shadowCastingMode = ShadowCastingMode.Off;
                float lit = GroundAt(samplePoint);
                casterRenderer.shadowCastingMode = ShadowCastingMode.On;
                feature.SetActive(false);
                float shadowWithoutCs = GroundAt(samplePoint);
                Require(lit - shadowWithoutCs > 0.15f,
                    $"The URP/Lit caster casts no normal shadow even without CS: lit={lit:F3}, shadow={shadowWithoutCs:F3}");
                Require(cs.atlasSlice >= 0, $"CS receiver is not in the camera frustum: {cs.status}");

                feature.SetActive(true);
                float shadowWithCs = GroundAt(samplePoint);
                Require(Shader.GetGlobalFloat("_HoCSActive") > 0.5f, "CS did not publish data while enabled");
                Require(shadowWithCs <= shadowWithoutCs + 0.05f,
                    $"Enabling CS took away another object's normal shadow: without CS={shadowWithoutCs:F3}, with CS={shadowWithCs:F3}, lit={lit:F3}");

                // Same measurement with a lilToon caster: its ShadowCaster pass needs the OB palette SRV
                // to be bound on D3D12, otherwise the draw (and therefore the shadow) is skipped.
                var toonMaterial = new Material(Shader.Find("lilToon")); created.Add(toonMaterial);
                casterRenderer.sharedMaterial = toonMaterial;
                float toonShadowWithoutCs = GroundAt(samplePoint);
                Require(lit - toonShadowWithoutCs > 0.15f,
                    $"The lilToon caster casts no normal shadow without CS: lit={lit:F3}, shadow={toonShadowWithoutCs:F3}");
                feature.SetActive(false);
                HoObjectBufferRegistry.Release();
                GroundAt(samplePoint); // a frame with no palette bound at all: the D3D12 startup race
                Require(skippedDraws.Count == 0,
                    "lilToon draws were skipped for a missing buffer while the OB palette was unbound: " + string.Join(" | ", skippedDraws));
                HoObjectBufferRegistry.MarkDirty();
                HoObjectBufferRegistry.EnsureBuilt();
                feature.SetActive(true);
                float toonShadowWithCs = GroundAt(samplePoint);
                Require(toonShadowWithCs <= toonShadowWithoutCs + 0.05f,
                    $"Enabling CS took away a lilToon caster's normal shadow: without CS={toonShadowWithoutCs:F3}, with CS={toonShadowWithCs:F3}");

                // Object lifetime: in the editor Create() runs from SerializedObject.ApplyModifiedProperties,
                // and its Dispose() must therefore avoid both Object.Destroy ("Destroy may not be called from
                // edit mode!") and DestroyImmediate ("... not permitted during rendering callbacks").
                int lifetimeWarnings = 0;
                Application.LogCallback lifetimeHandler = (condition, stackTrace, type) =>
                {
                    if (!string.IsNullOrEmpty(condition)
                        && (condition.Contains("Destroy may not be called")
                            || condition.Contains("Destroying GameObjects immediately is not permitted")
                            || condition.Contains("Destroying an object in edit mode")))
                    {
                        lifetimeWarnings++;
                    }
                };
                Application.logMessageReceived += lifetimeHandler;
                try
                {
                    feature.Create();
                    float afterRecreate = GroundAt(samplePoint);
                    Require(Shader.GetGlobalFloat("_HoCSActive") > 0.5f && afterRecreate <= shadowWithoutCs + 0.05f,
                        $"CS stopped working after the feature was recreated: {afterRecreate:F3}");
                    feature.Create();
                }
                finally
                {
                    Application.logMessageReceived -= lifetimeHandler;
                }
                Require(lifetimeWarnings == 0,
                    $"Object lifetime warnings while the feature was recreated (Destroy/DestroyImmediate in the wrong context): {lifetimeWarnings}");

                Debug.Log($"[Ho-CS Scene Shadows] PASS: lit={lit:F3}, URP shadow={shadowWithoutCs:F3}/{shadowWithCs:F3} (CS off/on), "
                    + $"lilToon shadow={toonShadowWithoutCs:F3}/{toonShadowWithCs:F3}, skipped draws={skippedDraws.Count}, graphics={SystemInfo.graphicsDeviceType}.");
            }
            finally
            {
                if (logCallback != null) Application.logMessageReceived -= logCallback;
                GraphicsSettings.defaultRenderPipeline = previousGraphics;
                QualitySettings.renderPipeline = previousQuality;
                RenderSettings.sun = previousSun;
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientColor;
                ShaderUtil.allowAsyncCompilation = previousAsync;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                for (int i = created.Count - 1; i >= 0; i--) if (created[i] != null) Object.DestroyImmediate(created[i]);
                HoObjectBufferRegistry.Release();
                HoObjectBufferRegistry.MarkDirty();
                if (previousScene.IsValid() && previousScene.isLoaded)
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousScene);
            }
        }
    }
}
