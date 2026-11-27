using System;
using System.Collections.Generic;
using System.IO;
using HoShader.ClothURP.Editor;
using lilToon.URP.Extensions.OIT;
using lilToon.URP.Extensions.Shadows;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class HoWeightedOitIntegrationProbe
{
    const int Size = 192;
    const int FrontOwner = (31 << 8) | 9;
    const int BackOwner = (77 << 8) | 3;

    [Serializable] sealed class Case
    {
        public string name;
        public Color shadow, lit;
        public float deltaFromBaseline;
        [NonSerialized] public Color[] pixels;
    }

    [Serializable] sealed class Report
    {
        public string unity, graphics, device, error;
        public bool passed;
        public List<Case> cases = new();
    }

    public static void Run()
    {
        string output = Environment.GetEnvironmentVariable("HO_OIT_INTEGRATION_OUTPUT");
        Require(Application.isBatchMode && Directory.Exists(output) && !File.Exists(Path.Combine(output, "oit-integration.json")),
            "Use a private Unity batch project and a fresh output directory.");
        var report = new Report
        {
            unity = Application.unityVersion,
            graphics = SystemInfo.graphicsDeviceType.ToString(),
            device = SystemInfo.graphicsDeviceName
        };
        bool previous = ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Verify(report, output);
            report.passed = true;
        }
        catch (Exception error)
        {
            report.error = error.ToString();
            throw;
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = previous;
            File.WriteAllText(Path.Combine(output, "oit-integration.json"), JsonUtility.ToJson(report, true));
        }
        Debug.Log("HO_OIT_INTEGRATION_OK");
    }

    static void Verify(Report report, string output)
    {
        var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
        renderer.renderingMode = RenderingMode.Forward;
        renderer.copyDepthMode = CopyDepthMode.AfterOpaques;
        var oit = ScriptableObject.CreateInstance<WeightedOITRendererFeature>();
        oit.Settings.alphaClipThreshold = .12f;
        var shadows = ScriptableObject.CreateInstance<HoShadowsRendererFeature>();
        shadows.WeightedOitShadowsEnabled = true;
        renderer.rendererFeatures.Add(oit);
        renderer.rendererFeatures.Add(shadows);
        var pipeline = UniversalRenderPipelineAsset.Create(renderer);
        pipeline.supportsCameraDepthTexture = true;
        pipeline.msaaSampleCount = 1;
        pipeline.shadowDistance = 25;
        pipeline.shadowCascadeCount = 1;
        var serialized = new SerializedObject(pipeline);
        serialized.FindProperty("m_MainLightShadowsSupported").boolValue = true;
        serialized.FindProperty("m_MainLightShadowmapResolution").intValue = 512;
        serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
        serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
        serialized.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = 1024;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        RenderPipelineAsset previousGraphics = GraphicsSettings.defaultRenderPipeline;
        RenderPipelineAsset previousQuality = QualitySettings.renderPipeline;
        Light previousSun = RenderSettings.sun;
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;

        var root = new GameObject("Weighted OIT production feature isolation");
        Shader cloth = Shader.Find("Ho/Cloth/URP/Cloth");
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        Require(cloth != null && lit != null && unlit != null, "The physical integration host needs Cloth and URP shaders.");
        var frontMaterial = new Material(cloth);
        var backMaterial = new Material(cloth);
        var blockerMaterial = new Material(lit);
        var background = new Material(unlit);
        try
        {
            SetCloth(frontMaterial, new Color(1, .58f, .20f, .75f), -10);
            SetCloth(backMaterial, new Color(.25f, .62f, 1, .6f), 10);
            background.SetColor("_BaseColor", new Color(.025f, .025f, .025f, 1));
            var front = MakeMesh(root, "front", PrimitiveType.Quad, new Vector3(0, 0, 0), new Vector3(1.65f, 1.65f, 1), frontMaterial, FrontOwner);
            var back = MakeMesh(root, "back", PrimitiveType.Quad, new Vector3(.14f, 0, .42f), new Vector3(1.65f, 1.65f, 1), backMaterial, BackOwner);
            MakeMesh(root, "Backdrop", PrimitiveType.Quad, new Vector3(0, 0, 1), new Vector3(4, 4, 1), background, 0);
            var solid = MakeMesh(root, "ShadowsOnly blocker", PrimitiveType.Cube, new Vector3(-.43f, 0, -1.4f),
                new Vector3(.63f, 1.5f, .28f), blockerMaterial, 0);
            solid.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            var sun = new GameObject("Main directional").AddComponent<Light>();
            sun.transform.SetParent(root.transform);
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(.07f, 0, 1));
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 1;
            sun.shadowBias = .012f;
            sun.shadowNormalBias = 0;
            RenderSettings.sun = sun;

            var camera = new GameObject("Production feature camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.transform.position = new Vector3(0, 0, -6);
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.farClipPlane = 25;
            camera.enabled = false;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;

            var baseline = Capture(report, output, camera, "feature-layered", null);
            Require(Luma(baseline.lit) > Luma(baseline.shadow) + .025f, "Real production OIT shadow must visibly darken registered surfaces.");
            SetQueue(frontMaterial, 10);
            SetQueue(backMaterial, -10);
            var reversed = Capture(report, output, camera, "reversed-queue-layered", baseline);
            Require(reversed.deltaFromBaseline <= .006f, "Reverse queues must preserve the result of actual OIT blending.");

            front.SetShaderUserValue(0);
            back.SetShaderUserValue(0);
            var missing = Capture(report, output, camera, "unregistered-receivers", baseline);
            Require(Luma(missing.shadow) > Luma(baseline.shadow) + .025f && Mathf.Abs(Luma(missing.lit) - Luma(baseline.lit)) < .01f,
                "An active production scope must show the neutral miss only where shadow was present.");
            front.SetShaderUserValue(FrontOwner);
            back.SetShaderUserValue(BackOwner);
            shadows.WeightedOitShadowsEnabled = false;
            var disabled = Capture(report, output, camera, "layered-feature-disabled", missing);
            Require(disabled.deltaFromBaseline <= .006f, "Disabling layered OIT shadows must clear the previous camera result.");

            shadows.WeightedOitShadowsEnabled = true;
            RenderSettings.sun = null;
            sun.enabled = false;
            frontMaterial.SetFloat("_HoRPShadowMode", 2);
            backMaterial.SetFloat("_HoRPShadowMode", 2);
            VerifyPunctual(report, output, camera, root.transform, front, back);
            Require(!ShaderUtil.ShaderHasError(cloth), "The production Cloth variant must be compiled without shader errors.");
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = previousGraphics;
            QualitySettings.renderPipeline = previousQuality;
            RenderSettings.sun = previousSun;
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(frontMaterial);
            Object.DestroyImmediate(backMaterial);
            Object.DestroyImmediate(blockerMaterial);
            Object.DestroyImmediate(background);
            Object.DestroyImmediate(pipeline);
            Object.DestroyImmediate(renderer);
            Object.DestroyImmediate(shadows);
            Object.DestroyImmediate(oit);
        }
    }

    static void VerifyPunctual(Report report, string output, Camera camera, Transform root, MeshRenderer front, MeshRenderer back)
    {
        var light = new GameObject("Full punctual shadow witness").AddComponent<Light>();
        light.transform.SetParent(root);
        light.transform.position = new Vector3(-.48f, 0, -3.7f);
        light.range = 10;
        light.intensity = 6;
        light.renderMode = LightRenderMode.ForcePixel;
        light.shadows = LightShadows.Hard;
        light.shadowStrength = 1;
        light.shadowBias = .015f;
        light.shadowNormalBias = 0;
        light.spotAngle = 95;

        foreach (LightType type in new[] { LightType.Point, LightType.Spot })
        {
            light.type = type;
            string label = "full-" + type.ToString().ToLowerInvariant();
            var registered = Capture(report, output, camera, label + "-registered", null);
            Require(Luma(registered.lit) > Luma(registered.shadow) + .025f, label + " must visibly shadow the registered layers.");
            front.SetShaderUserValue(0);
            back.SetShaderUserValue(0);
            var missing = Capture(report, output, camera, label + "-unregistered", registered);
            Require(Luma(missing.shadow) > Luma(registered.shadow) + .025f
                && Mathf.Abs(Luma(missing.lit) - Luma(registered.lit)) < .01f,
                label + " missing owner must remain neutral while preserving the unoccluded region.");
            front.SetShaderUserValue(FrontOwner);
            back.SetShaderUserValue(BackOwner);
        }
    }

    static void SetCloth(Material material, Color color, int offset)
    {
        material.SetFloat("_UseHoOIT", 1);
        material.SetFloat("_SurfaceMode", 2);
        material.SetFloat("_UseShadow", 1);
        material.SetFloat("_ReceiveShadows", 1);
        material.SetFloat("_HoRPShadowMode", 0);
        material.SetFloat("_UseEnvironmentLighting", 0);
        material.SetFloat("_UseSpecular", 0);
        material.SetFloat("_ShadowStrength", 1);
        material.SetFloat("_ShadowReceive", 1);
        material.SetColor("_BaseColor", color);
        material.SetColor("_ShadowColor", new Color(.05f, .05f, .05f, 1));
        material.SetColor("_Shadow2ndColor", Color.clear);
        SetQueue(material, offset);
    }

    static void SetQueue(Material material, int offset)
    {
        material.SetFloat("_QueueOffset", offset);
        ClothMaterialUtility.SyncMaterial(material);
    }

    static MeshRenderer MakeMesh(GameObject root, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, int owner)
    {
        var obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.SetParent(root.transform);
        obj.transform.localPosition = pos;
        obj.transform.localScale = scale;
        var mesh = obj.GetComponent<MeshRenderer>();
        mesh.sharedMaterial = mat;
        mesh.SetShaderUserValue((uint)owner);
        mesh.shadowCastingMode = ShadowCastingMode.Off;
        return mesh;
    }

    static Case Capture(Report report, string output, Camera camera, string label, Case baseline)
    {
        var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        var previous = RenderTexture.active;
        try
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "Single camera GPU capture is unavailable.");
            Debug.Log("OIT integration capture " + label);
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply(false);
            var result = new Case { name = label, shadow = image.GetPixel(60, 96), lit = image.GetPixel(134, 96), pixels = image.GetPixels() };
            if (baseline != null)
            {
                for (int i = 0; i < result.pixels.Length; ++i)
                {
                    Color change = result.pixels[i] - baseline.pixels[i];
                    result.deltaFromBaseline = Mathf.Max(result.deltaFromBaseline, Mathf.Abs(change.r), Mathf.Abs(change.g), Mathf.Abs(change.b));
                }
            }
            report.cases.Add(result);
            File.WriteAllBytes(Path.Combine(output, label + ".exr"), image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            var png = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false);
            try
            {
                var display = (Color[])result.pixels.Clone();
                for (int i = 0; i < display.Length; ++i) display[i] = display[i].gamma;
                png.SetPixels(display);
                png.Apply(false);
                File.WriteAllBytes(Path.Combine(output, label + ".png"), png.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(png); }
            Debug.Log($"OIT integrated {label} shadow={result.shadow} lit={result.lit} maxDelta={result.deltaFromBaseline}");
            return result;
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
        }
    }

    static float Luma(Color color) => Vector3.Dot(new Vector3(color.r, color.g, color.b), new Vector3(.2126f, .7152f, .0722f));
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
