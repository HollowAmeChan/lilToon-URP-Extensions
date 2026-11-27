using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HoShader.EnvironmentURP.Editor;
using lilToon.URP.Extensions.ShadowCast;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class HoShadowCastP0Probe
{
    private const int Size = 128;
    private const float DifferenceThreshold = 0.002f;
    private static string outputDirectory;

    [Serializable]
    private sealed class Frame
    {
        public string name, image, linearImage, diagnosticsPath, diagnosticsSource;
        public int invocation, engineFrame, diagnosticsFrame, visibleLights, punctualSlices, secondDirectionalSlices;
        public bool featureEnabled, nativeAdditionalShadowKeyword, punctualAtlasActive, secondDirectionalAtlasActive;
        public float characterActive, rtActive;
        public string[] accepted, skipped;
        public Color center;
    }

    [Serializable]
    private sealed class Comparison
    {
        public string scene, scope;
        public Frame off, on, offAgain;
        public float maxAbsRgb, meanAbsRgb, initialResetMaxAbsRgb;
        public int changedPixels, pixels;
        public Color onMinusOffCenter;
    }

    [Serializable]
    private sealed class Report
    {
        public string unity, graphics, gpu, shader, fixture, assertion;
        public int width = Size, height = Size;
        public float threshold = DifferenceThreshold;
        public bool success;
        public List<Comparison> comparisons = new();
        public List<string> configuration = new();
    }

    public static void Run()
    {
        outputDirectory = Environment.GetEnvironmentVariable("HO_SHADOWS_P0_OUTPUT");
        string expectedProject = Environment.GetEnvironmentVariable("HO_SHADOWS_P0_PROJECT");
        if (string.IsNullOrWhiteSpace(expectedProject) || !Directory.Exists(outputDirectory)
            || !string.Equals(Path.GetFullPath(expectedProject).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(Path.GetDirectoryName(Application.dataPath)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Set HO_SHADOWS_P0_PROJECT to the physical test project and HO_SHADOWS_P0_OUTPUT to a fresh evidence directory.");
        if (File.Exists(Path.Combine(outputDirectory, "render-ab.json")) || QualitySettings.activeColorSpace != ColorSpace.Linear)
            throw new InvalidOperationException("Use a clean result directory and Linear rendering.");

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var feature = ScriptableObject.CreateInstance<HoShadowCastRendererFeature>();
        feature.Settings.atlasSize = 1024;
        feature.Settings.pointFaceResolution = 128;
        feature.Settings.spotResolution = 128;
        feature.Settings.secondDirectionalAtlasSize = 512;
        feature.Settings.secondDirectionalCascadeCount = 4;
        feature.Settings.secondDirectionalMaxDistance = 18;
        feature.Settings.secondDirectionalShadowDepth = 35;
        feature.Settings.secondDirectionalShadowStrength = 1;
        feature.Settings.punctualShadowStrength = 0.4f;
        feature.Settings.punctualShadowFadeSpeed = 0.35f;
        feature.Settings.pcssEnabled = false;

        var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
        renderer.renderingMode = RenderingMode.ForwardPlus;
        renderer.rendererFeatures.Add(feature);
        var pipeline = UniversalRenderPipelineAsset.Create(renderer);
        pipeline.msaaSampleCount = 1;
        pipeline.shadowDistance = 20;
        pipeline.shadowCascadeCount = 1;
        using (var serialized = new SerializedObject(pipeline))
        {
            serialized.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
            serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        RenderPipelineAsset oldDefault = GraphicsSettings.defaultRenderPipeline;
        RenderPipelineAsset oldQuality = QualitySettings.renderPipeline;
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.ambientIntensity = 0;
        RenderSettings.reflectionIntensity = 0;
        RenderSettings.fog = false;

        var report = new Report {
            unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString(),
            gpu = SystemInfo.graphicsDeviceName, shader = "Ho/Environment/URP/Lit",
            fixture = "Synthetic camera via URP SingleCameraRequest. One production HoShadowCastRendererFeature, no mocked globals.",
            configuration = new List<string> {
                "ForwardPlus/PerPixel, main and additional native shadows supported, HDR Linear, MSAA 1",
                "Character and RT Features absent throughout, ambient/fog/reflections/specular off",
                "Cast atlas 1024, point/spot 128, secondary directional 512 at four cascades, PCSS off",
                "Cast-only A/B: feature.Settings.enabled=false,true,false with lights, geometry and scope fixed"
            }
        };
        int invocation = 0;
        try
        {
            Shader shader = Shader.Find(report.shader);
            Require(shader && shader.isSupported, "Actual Environment shader must resolve.");
            RunCase(report, feature, shader, "native-point-spot", false, false, ref invocation);
            RunCase(report, feature, shader, "ordinary-second-directional", true, false, ref invocation);
            RunCase(report, feature, shader, "declared-second-directional", true, true, ref invocation);
            Require(!ShaderUtil.ShaderHasError(shader), "Environment shader compiled without errors.");
            Require(report.comparisons.All(pair => pair.initialResetMaxAbsRgb <= DifferenceThreshold
                && !pair.off.punctualAtlasActive && !pair.off.secondDirectionalAtlasActive
                && !pair.offAgain.punctualAtlasActive && !pair.offAgain.secondDirectionalAtlasActive
                && pair.on.diagnosticsPath == "RenderGraph"), "Feature off repeat and real RenderGraph publication.");
            Require(report.comparisons.Single(pair => pair.scene == "declared-second-directional" && pair.scope == "Main")
                .changedPixels > 4, "Declared main-scope control should alter rendered pixels.");
            report.success = true;
        }
        catch (Exception error)
        {
            report.assertion = error.ToString();
            throw;
        }
        finally
        {
            File.WriteAllText(Path.Combine(outputDirectory, "render-ab.json"), JsonUtility.ToJson(report, true));
            GraphicsSettings.defaultRenderPipeline = oldDefault;
            QualitySettings.renderPipeline = oldQuality;
            Object.DestroyImmediate(pipeline);
            Object.DestroyImmediate(renderer);
            Object.DestroyImmediate(feature);
        }
    }

    private static void RunCase(Report report, HoShadowCastRendererFeature feature, Shader shader, string scene,
        bool directional, bool declaration, ref int invocation)
    {
        var root = new GameObject(scene);
        var receiver = new Material(shader);
        var caster = new Material(shader);
        try
        {
            receiver.SetFloat("_UseShadow", 1);
            receiver.SetFloat("_ReceiveShadows", 1);
            receiver.SetFloat("_SpecularHighlights", 0);
            receiver.SetFloat("_UseEnvironmentLighting", 0);
            receiver.SetColor("_ShadowColor", Color.black);
            receiver.SetColor("_Shadow2ndColor", Color.clear);
            receiver.SetFloat("_ShadowReceive", 1);
            caster.SetFloat("_Cull", (float)CullMode.Off);
            EnvironmentMaterialUtility.SyncMaterial(caster);
            Primitive(root, PrimitiveType.Quad, "receiver", Vector3.zero, new Vector3(4, 4, 1),
                receiver, ShadowCastingMode.Off);
            Primitive(root, PrimitiveType.Cube, "left blocker", new Vector3(-0.75f, 0, -1),
                new Vector3(0.65f, 0.7f, 0.2f), caster, ShadowCastingMode.On);
            var sun = CreateLight(root, "URP main blue", LightType.Directional, Color.blue, 0.8f, Vector3.zero,
                Quaternion.identity, LightShadows.None);
            RenderSettings.sun = sun;
            if (directional)
            {
                var red = CreateLight(root, "red second directional", LightType.Directional, Color.red, 1,
                    Vector3.zero, Quaternion.LookRotation(new Vector3(.75f, 0, 1)), LightShadows.Hard);
                if (declaration)
                    red.gameObject.AddComponent<HoShadowCastLight>().includeInMainShadow = true;
                CreateLight(root, "native green point anchor", LightType.Point, Color.green, .4f,
                    new Vector3(1.5f, 0, -2), Quaternion.identity, LightShadows.Hard);
            }
            else
            {
                Primitive(root, PrimitiveType.Cube, "right blocker", new Vector3(0.75f, 0, -1),
                    new Vector3(0.65f, 0.7f, 0.2f), caster, ShadowCastingMode.On);
                CreateLight(root, "native red point", LightType.Point, Color.red, 8, new Vector3(-1.5f, 0, -2),
                    Quaternion.identity, LightShadows.Hard);
                var spot = CreateLight(root, "native green spot", LightType.Spot, Color.green, 8,
                    new Vector3(1.5f, 0, -2), Quaternion.LookRotation(new Vector3(-.75f, 0, 1)), LightShadows.Hard);
                spot.spotAngle = 60;
            }

            var camera = new GameObject(scene + " camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            camera.transform.position = new Vector3(0, 0, -4);
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.farClipPlane = 25;
            ReadCamera(camera, null, out _);

            foreach (string scope in new[] { "Main", "Full" })
            {
                receiver.SetFloat("_HoRPShadowMode", scope == "Main" ? 1 : 2);
                EnvironmentMaterialUtility.SyncMaterial(receiver);
                var off = Render(feature, camera, scene, scope, "off", false, ++invocation, out var pixelsOff);
                var on = Render(feature, camera, scene, scope, "on", true, ++invocation, out var pixelsOn);
                var offAgain = Render(feature, camera, scene, scope, "off-again", false, ++invocation, out var pixelsOffAgain);
                Require(off.characterActive == 0f && on.characterActive == 0f && offAgain.characterActive == 0f
                    && off.rtActive == 0f && on.rtActive == 0f && offAgain.rtActive == 0f, "Character and RT stay fixed off.");
                var stats = new Comparison { scene = scene, scope = scope, off = off, on = on, offAgain = offAgain,
                    pixels = pixelsOff.Length, onMinusOffCenter = on.center - off.center };
                for (int i = 0; i < pixelsOff.Length; i++)
                {
                    Color delta = pixelsOn[i] - pixelsOff[i];
                    float change = MaxRgbDelta(delta);
                    stats.maxAbsRgb = Mathf.Max(stats.maxAbsRgb, change);
                    stats.meanAbsRgb += (Mathf.Abs(delta.r) + Mathf.Abs(delta.g) + Mathf.Abs(delta.b)) / 3f;
                    stats.initialResetMaxAbsRgb = Mathf.Max(stats.initialResetMaxAbsRgb, MaxRgbDelta(pixelsOffAgain[i] - pixelsOff[i]));
                    if (change > DifferenceThreshold)
                        stats.changedPixels++;
                }
                stats.meanAbsRgb /= stats.pixels;
                report.comparisons.Add(stats);
                File.WriteAllText(Path.Combine(outputDirectory, "render-ab-progress.json"), JsonUtility.ToJson(report, true));
                Debug.Log($"HO_SHADOWCAST_P0 {scene} {scope} max={stats.maxAbsRgb:F6} mean={stats.meanAbsRgb:F6} changed={stats.changedPixels}/{stats.pixels} reset={stats.initialResetMaxAbsRgb:F6} delta={stats.onMinusOffCenter} producer={on.diagnosticsPath}/{on.punctualSlices}/{on.secondDirectionalSlices}");
            }
        }
        finally
        {
            feature.Settings.enabled = false;
            RenderSettings.sun = null;
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(receiver);
            Object.DestroyImmediate(caster);
        }
    }

    private static Frame Render(HoShadowCastRendererFeature feature, Camera camera, string scene, string scope,
        string label, bool enabled, int invocation, out Color[] pixels)
    {
        feature.Settings.enabled = enabled;
        string stem = scene + "-" + scope.ToLowerInvariant() + "-" + label;
        Color center = ReadCamera(camera, Path.Combine(outputDirectory, stem), out pixels);
        var snapshot = HoShadowCastRuntimeDiagnostics.CurrentSnapshot;
        return new Frame {
            name = stem, image = stem + ".png", linearImage = stem + ".exr", invocation = invocation,
            engineFrame = Time.frameCount, center = center, featureEnabled = enabled,
            diagnosticsFrame = snapshot.FrameCount, diagnosticsPath = snapshot.Path, diagnosticsSource = snapshot.Source,
            visibleLights = snapshot.VisibleLightCount, punctualSlices = snapshot.SliceCount,
            secondDirectionalSlices = snapshot.SecondDirectionalSliceCount,
            nativeAdditionalShadowKeyword = Shader.IsKeywordEnabled("_ADDITIONAL_LIGHT_SHADOWS"),
            punctualAtlasActive = Shader.GetGlobalFloat("_HoShadowCastActive") > 0.5f,
            secondDirectionalAtlasActive = Shader.GetGlobalVector("_HoShadowCastSecondDirectionalParams").x > 0.5f,
            characterActive = Shader.GetGlobalFloat("_HoCSActive"), rtActive = Shader.GetGlobalFloat("_HoRTShadowActive"),
            accepted = snapshot.AcceptedLights.Select(l => $"{l.Name}:{l.Type}:{l.Stage}:{l.SliceCount}").ToArray(),
            skipped = snapshot.SkippedLights.Select(l => $"{l.Name}:{l.Type}:{l.Stage}:{l.Reason}").ToArray()
        };
    }

    private static Color ReadCamera(Camera camera, string path, out Color[] pixels)
    {
        var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var readback = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        var previous = RenderTexture.active;
        try
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "SingleCameraRequest must be supported.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            readback.Apply(false);
            pixels = readback.GetPixels();
            if (path != null)
            {
                File.WriteAllBytes(path + ".exr", readback.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false);
                try
                {
                    image.SetPixels(Array.ConvertAll(pixels, pixel => pixel.gamma));
                    image.Apply(false);
                    File.WriteAllBytes(path + ".png", image.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(image); }
            }
            return pixels[(Size / 2) * Size + Size / 2];
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(readback);
            Object.DestroyImmediate(target);
        }
    }

    private static Light CreateLight(GameObject root, string name, LightType type, Color color, float intensity,
        Vector3 position, Quaternion rotation, LightShadows shadows)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.transform.SetParent(root.transform, false);
        light.transform.SetPositionAndRotation(position, rotation);
        light.type = type; light.color = color; light.intensity = intensity; light.shadows = shadows;
        light.range = 8; light.shadowStrength = 1; light.shadowBias = .02f; light.shadowNormalBias = 0;
        return light;
    }

    private static void Primitive(GameObject root, PrimitiveType type, string name, Vector3 position,
        Vector3 scale, Material material, ShadowCastingMode mode)
    {
        var primitive = GameObject.CreatePrimitive(type);
        primitive.name = name;
        primitive.transform.SetParent(root.transform, false);
        primitive.transform.position = position;
        primitive.transform.localScale = scale;
        var renderer = primitive.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = mode;
    }

    private static float MaxRgbDelta(Color delta) => Mathf.Max(Mathf.Abs(delta.r), Mathf.Abs(delta.g), Mathf.Abs(delta.b));
    private static void Require(bool check, string message)
    {
        if (!check) throw new InvalidOperationException(message);
    }
}
