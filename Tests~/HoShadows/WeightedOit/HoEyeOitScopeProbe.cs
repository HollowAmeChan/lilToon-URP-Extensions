using System;
using System.Collections.Generic;
using System.IO;
using HoShader.ClothURP.Editor;
using HoShader.SkinStylizedURP.Editor;
using lilToon.URP.Extensions.OIT;
using lilToon.URP.Extensions.Shadows;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class HoEyeOitScopeProbe
{
    const int Size = 192;
    const int EyeRow = 142;
    const int OitRow = 49;
    const int ShadowColumn = 60;
    const int LitColumn = 134;
    const float Tolerance = .004f;
    const string ReportName = "eye-oit-scopes.json";

    [Serializable] sealed class Sample
    {
        public string name;
        public Color eyeShadow, eyeLit, oitShadow, oitLit;
        public float otherScopeError;
        [NonSerialized] public Color[] pixels;
    }

    [Serializable] sealed class Report
    {
        public string unity, graphics, device, error;
        public bool passed;
        public float bothOrderMaximumError, eyeOnlyOrderMaximumError, oitOnlyOrderMaximumError;
        public List<Sample> samples = new();
    }

    public static void Run()
    {
        string output = Environment.GetEnvironmentVariable("HO_EYE_OIT_SCOPE_OUTPUT");
        Require(Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null
            && QualitySettings.activeColorSpace == ColorSpace.Linear && Directory.Exists(output)
            && !File.Exists(Path.Combine(output, ReportName)), "Use a private Linear batch project, a GPU and a fresh output directory.");
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
            File.WriteAllText(Path.Combine(output, ReportName), JsonUtility.ToJson(report, true));
        }
        Debug.Log("HO_EYE_OIT_SCOPES_OK");
    }

    static void Verify(Report report, string output)
    {
        var objects = new List<Object>();
        T Keep<T>(T obj) where T : Object { objects.Add(obj); return obj; }
        Material MaterialFor(string name)
        {
            var shader = Shader.Find(name);
            Require(shader != null, "Missing shader " + name);
            return Keep(new Material(shader));
        }
        var root = Keep(new GameObject("Mixed Eye and OIT scope witness"));
        var eye = MaterialFor("Ho/Skin/URP/Eye");
        var cloth = MaterialFor("Ho/Cloth/URP/Cloth");
        var lit = MaterialFor("Universal Render Pipeline/Lit");
        var backdrop = MaterialFor("Universal Render Pipeline/Unlit");
        var previousSun = RenderSettings.sun;
        try
        {
            PrepareReceiver(eye, new Color(.23f, .58f, 1, .82f));
            eye.SetFloat("_ShadowMode", 1);
            eye.SetFloat("_TransparentZWrite", 0);
            EyeMaterialUtility.SyncMaterial(eye);
            PrepareReceiver(cloth, new Color(1, .60f, .22f, .8f));
            cloth.SetFloat("_UseHoOIT", 1);
            ClothMaterialUtility.SyncMaterial(cloth);
            backdrop.SetColor("_BaseColor", new Color(.025f, .025f, .025f, 1));

            MakePrimitive(root.transform, PrimitiveType.Quad, new Vector3(0, .48f, 0), new Vector3(1.7f, .62f, 1), eye, 24 << 8 | 3);
            MakePrimitive(root.transform, PrimitiveType.Quad, new Vector3(0, -.48f, .18f), new Vector3(1.7f, .62f, 1), cloth, 38 << 8 | 11);
            MakePrimitive(root.transform, PrimitiveType.Quad, new Vector3(0, 0, 1), new Vector3(4, 4, 1), backdrop, 0);
            var blocker = MakePrimitive(root.transform, PrimitiveType.Cube, new Vector3(-.43f, 0, -1.4f),
                new Vector3(.63f, 1.9f, .28f), lit, 0);
            blocker.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            var sun = new GameObject("Shared native directional").AddComponent<Light>();
            sun.transform.SetParent(root.transform);
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(.07f, 0, 1));
            sun.shadows = LightShadows.Hard;
            sun.shadowBias = .012f;
            sun.shadowNormalBias = 0;
            RenderSettings.sun = sun;

            var camera = new GameObject("Shared camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.transform.position = new Vector3(0, 0, -6);
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.farClipPlane = 25;
            camera.enabled = false;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;

            var forward = VerifyOrder(report, output, camera, true);
            var reverse = VerifyOrder(report, output, camera, false);
            report.bothOrderMaximumError = Difference(forward.both.pixels, reverse.both.pixels);
            report.eyeOnlyOrderMaximumError = Difference(forward.eyeOnly.pixels, reverse.eyeOnly.pixels);
            report.oitOnlyOrderMaximumError = Difference(forward.oitOnly.pixels, reverse.oitOnly.pixels);
            Require(report.bothOrderMaximumError <= Tolerance && report.eyeOnlyOrderMaximumError <= Tolerance
                && report.oitOnlyOrderMaximumError <= Tolerance, "Exchanging RendererFeature declaration order changed a camera result.");
            Require(!ShaderUtil.ShaderHasError(eye.shader) && !ShaderUtil.ShaderHasError(cloth.shader), "Real receiver shader variants must compile.");
        }
        finally
        {
            RenderSettings.sun = previousSun;
            for (int i = objects.Count - 1; i >= 0; --i) Object.DestroyImmediate(objects[i]);
        }
    }

    static (Sample both, Sample eyeOnly, Sample oitOnly) VerifyOrder(Report report, string output, Camera camera, bool oitFirst)
    {
        var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
        renderer.renderingMode = RenderingMode.Forward;
        renderer.copyDepthMode = CopyDepthMode.AfterOpaques;
        var oit = ScriptableObject.CreateInstance<WeightedOITRendererFeature>();
        oit.Settings.alphaClipThreshold = .12f;
        var shadows = ScriptableObject.CreateInstance<HoShadowsRendererFeature>();
        shadows.TransparentEyeShadowsEnabled = true;
        shadows.WeightedOitShadowsEnabled = true;
        renderer.rendererFeatures.Add(oitFirst ? (ScriptableRendererFeature)oit : shadows);
        renderer.rendererFeatures.Add(oitFirst ? (ScriptableRendererFeature)shadows : oit);
        var pipeline = UniversalRenderPipelineAsset.Create(renderer);
        pipeline.supportsCameraDepthTexture = true;
        pipeline.msaaSampleCount = 1;
        pipeline.shadowDistance = 25;
        pipeline.shadowCascadeCount = 1;
        var serialized = new SerializedObject(pipeline);
        serialized.FindProperty("m_MainLightShadowsSupported").boolValue = true;
        serialized.FindProperty("m_MainLightShadowmapResolution").intValue = 512;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        var previousGraphics = GraphicsSettings.defaultRenderPipeline;
        var previousQuality = QualitySettings.renderPipeline;
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        string prefix = oitFirst ? "oit-feature-first" : "shadows-feature-first";
        try
        {
            var both = Capture(report, output, camera, prefix + "-both");
            Require(Luma(both.eyeLit) > Luma(both.eyeShadow) + .05f, prefix + " Eye shadow missing with both features enabled.");
            Require(Luma(both.oitLit) > Luma(both.oitShadow) + .05f, prefix + " OIT shadow missing with both features enabled.");

            shadows.WeightedOitShadowsEnabled = false;
            var eyeOnly = Capture(report, output, camera, prefix + "-eye-only");
            eyeOnly.otherScopeError = Difference(both.pixels, eyeOnly.pixels, Size / 2, Size);
            Require(eyeOnly.otherScopeError <= Tolerance && Luma(eyeOnly.oitShadow) > Luma(both.oitShadow) + .05f,
                prefix + " disabling OIT shadows must leave the Eye region unchanged and only neutralize the OIT shadow.");

            shadows.WeightedOitShadowsEnabled = true;
            shadows.TransparentEyeShadowsEnabled = false;
            var oitOnly = Capture(report, output, camera, prefix + "-oit-only");
            oitOnly.otherScopeError = Difference(both.pixels, oitOnly.pixels, 0, Size / 2);
            Require(oitOnly.otherScopeError <= Tolerance && Luma(oitOnly.eyeShadow) > Luma(both.eyeShadow) + .05f,
                prefix + " disabling Eye shadows must leave the OIT region unchanged and only neutralize the Eye shadow.");
            return (both, eyeOnly, oitOnly);
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = previousGraphics;
            QualitySettings.renderPipeline = previousQuality;
            Object.DestroyImmediate(pipeline);
            Object.DestroyImmediate(renderer);
            Object.DestroyImmediate(shadows);
            Object.DestroyImmediate(oit);
        }
    }

    static void PrepareReceiver(Material material, Color baseColor)
    {
        material.SetFloat("_SurfaceMode", 2);
        material.SetFloat("_UseShadow", 1);
        material.SetFloat("_ReceiveShadows", 1);
        material.SetFloat("_HoRPShadowMode", 1);
        material.SetFloat("_UseSpecular", 0);
        material.SetFloat("_UseEnvironmentLighting", 0);
        material.SetFloat("_ShadowStrength", 1);
        material.SetFloat("_ShadowReceive", 1);
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_ShadowColor", new Color(.05f, .05f, .05f, 1));
        material.SetColor("_Shadow2ndColor", Color.clear);
        var keyword = new LocalKeyword(material.shader, "_HO_SHADOWS_SCREEN_PREVIEW");
        Require(keyword.isValid, "Material shader lacks screen shadow preview: " + material.shader.name);
        material.SetKeyword(keyword, true);
    }

    static MeshRenderer MakePrimitive(Transform root, PrimitiveType shape, Vector3 position, Vector3 size, Material material, int owner)
    {
        var obj = GameObject.CreatePrimitive(shape);
        obj.transform.SetParent(root, false);
        obj.transform.localPosition = position;
        obj.transform.localScale = size;
        var mesh = obj.GetComponent<MeshRenderer>();
        mesh.sharedMaterial = material;
        mesh.SetShaderUserValue((uint)owner);
        mesh.shadowCastingMode = ShadowCastingMode.Off;
        return mesh;
    }

    static Sample Capture(Report report, string output, Camera camera, string name)
    {
        var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        var png = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false);
        var previous = RenderTexture.active;
        try
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "Single camera readback is unavailable.");
            Debug.Log("Eye + OIT scope capture " + name);
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply(false);
            var sample = new Sample
            {
                name = name,
                eyeShadow = image.GetPixel(ShadowColumn, EyeRow),
                eyeLit = image.GetPixel(LitColumn, EyeRow),
                oitShadow = image.GetPixel(ShadowColumn, OitRow),
                oitLit = image.GetPixel(LitColumn, OitRow),
                pixels = image.GetPixels()
            };
            report.samples.Add(sample);
            File.WriteAllBytes(Path.Combine(output, name + ".exr"), image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            var display = new Color[sample.pixels.Length];
            for (int i = 0; i < display.Length; ++i) display[i] = sample.pixels[i].gamma;
            png.SetPixels(display);
            png.Apply(false);
            File.WriteAllBytes(Path.Combine(output, name + ".png"), png.EncodeToPNG());
            Debug.Log($"Eye + OIT {name} Eye shadow/lit={sample.eyeShadow}/{sample.eyeLit} OIT shadow/lit={sample.oitShadow}/{sample.oitLit}");
            return sample;
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(png);
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
        }
    }

    static float Difference(Color[] first, Color[] second, int fromRow = 0, int toRow = Size)
    {
        float maximum = 0;
        for (int i = fromRow * Size; i < toRow * Size; ++i)
        {
            Color d = first[i] - second[i];
            maximum = Mathf.Max(maximum, Mathf.Abs(d.r), Mathf.Abs(d.g), Mathf.Abs(d.b));
        }
        return maximum;
    }

    static float Luma(Color color) => .2126f * color.r + .7152f * color.g + .0722f * color.b;
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
