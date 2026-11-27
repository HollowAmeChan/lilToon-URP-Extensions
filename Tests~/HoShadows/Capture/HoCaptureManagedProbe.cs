using System;
using System.Collections.Generic;
using System.IO;
using HoShader.SkinStylizedURP.Editor;
using lilToon.URP.Extensions.CharacterSpecialization;
using lilToon.URP.Extensions.Shadows;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class HoCaptureManagedProbe
{
    private const int Width = 128, Height = 96;
    private const int Group = 9;

    private struct Part
    {
        public uint owner, name, tags, canonical;
        public Vector4 surface, properties, color;
    }

    private struct GroupRow
    {
        public uint row, count, tags, reserved;
    }

    [Serializable] private sealed class Report
    {
        public string unity, graphics, error;
        public bool passed;
        public List<Row> cases = new();
    }

    [Serializable] private sealed class Row
    {
        public string name;
        public float faceBaseline, faceShadow, eyeBaseline, eyeShadow;
        public float faceLitError, eyeLitError, opticalError, sourceLostError, disabledOrMainError;
        public int changedPixels;
    }

    public static void Run()
    {
        string output = Environment.GetEnvironmentVariable("HO_CAPTURE_MANAGED_OUTPUT");
        Require(Application.isBatchMode && Directory.Exists(output) && !File.Exists(Path.Combine(output, "capture-managed-rgb.json")),
            "Run in a disposable batch-mode project with a fresh, existing HO_CAPTURE_MANAGED_OUTPUT directory.");
        var report = new Report { unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString() };
        bool previousAsync = ShaderUtil.allowAsyncCompilation;
        try
        {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "A graphics device is required.");
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Check(report);
            report.passed = true;
        }
        catch (Exception error) { report.error = error.ToString(); Debug.LogException(error); }
        finally
        {
            ShaderUtil.allowAsyncCompilation = previousAsync;
            File.WriteAllText(Path.Combine(output, "capture-managed-rgb.json"), JsonUtility.ToJson(report, true));
        }

        bool bytecode = HoCaptureManagedBytecode.Compile(output);
        Require(report.passed && bytecode, "See the Capture RGB and fragment reports.");
        Debug.Log("HO_CAPTURE_MANAGED_OK");
    }

    private static void Check(Report report)
    {
        var oldPipeline = GraphicsSettings.defaultRenderPipeline;
        var oldQuality = QualitySettings.renderPipeline;
        var owned = new List<Object>();
        T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        var renderer = Own(ScriptableObject.CreateInstance<UniversalRendererData>());
        renderer.renderingMode = RenderingMode.Forward;
        renderer.opaqueLayerMask = 0;
        renderer.transparentLayerMask = 0;
        var capture = Own(ScriptableObject.CreateInstance<HoCharacterSpecializationRendererFeature>());
        Require(!capture.CaptureShadowInputsEnabled, "Capture inputs must start disabled.");
        capture.CaptureShadowInputsEnabled = true;
        capture.Settings.renderScale = HoCharacterRenderScale.Half;
        var replay = Own(ScriptableObject.CreateInstance<HoCaptureManagedProbeFeature>());
        replay.Capture = capture;
        renderer.rendererFeatures.Add(Own(ScriptableObject.CreateInstance<HoShadowsRendererFeature>()));
        renderer.rendererFeatures.Add(replay);
        var pipeline = Own(UniversalRenderPipelineAsset.Create(renderer));
        pipeline.msaaSampleCount = 1;
        pipeline.shadowDistance = 16;
        pipeline.shadowCascadeCount = 1;
        var settings = new SerializedObject(pipeline);
        settings.FindProperty("m_MainLightShadowsSupported").boolValue = true;
        settings.FindProperty("m_MainLightShadowmapResolution").intValue = 256;
        settings.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
        settings.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
        settings.ApplyModifiedPropertiesWithoutUndo();

        using var parts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 64);
        parts.SetData(new[] { new Part { owner = Group * 256, tags = 2 }, new Part { owner = Group * 256 + 1, tags = 8 } });
        using var groups = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 256, 16);
        var groupRows = new GroupRow[256];
        groupRows[Group] = new GroupRow { count = 2 };
        groups.SetData(groupRows);

        var color = Own(Target(Width / 2, Height / 2, 0));
        var optical = Own(Target(Width / 2, Height / 2, 0));
        var destination = Own(Target(Width, Height, 24));
        var colorHandle = RTHandles.Alloc(color);
        var opticalHandle = RTHandles.Alloc(optical);
        try
        {
            replay.Color = colorHandle;
            replay.Optical = opticalHandle;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            Shader.SetGlobalBuffer("_HoObjectBufferEntries", parts);
            Shader.SetGlobalBuffer("_HoObjectBufferGroups", groups);
            Shader.SetGlobalFloat("_HoObjectBufferPartCount", 2);

            var face = Own(MakeMaterial("Ho/Skin/URP/Stylized", new Color(.81f, .68f, .57f)));
            face.SetFloat("_StencilPass", (float)StencilOp.Replace);
            face.SetFloat("_ReceiveCastShadows", 1);
            SkinMaterialUtility.SyncMaterial(face);
            var eye = Own(MakeMaterial("Ho/Skin/URP/Eye", new Color(.67f, .31f, .14f)));
            eye.SetFloat("_SurfaceMode", 2);
            eye.SetFloat("_StencilComp", (float)CompareFunction.Equal);
            eye.SetFloat("_HoCharacterCaptureOpacity", 1);
            EyeMaterialUtility.SyncMaterial(eye);
            Own(Quad("Face", face, Group * 256, Vector3.zero, new Vector2(2.8f, 2.3f)));
            Own(Quad("Eye", eye, Group * 256 + 1, new Vector3(.15f, 0, -.26f), new Vector2(2.7f, .54f)));

            var cube = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
            cube.transform.position = new Vector3(.38f, -.04f, -1.28f);
            cube.transform.localScale = new Vector3(.85f, .72f, .28f);
            var blocker = cube.GetComponent<MeshRenderer>();
            blocker.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            blocker.sharedMaterial = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));

            var camera = Own(new GameObject("Managed Capture RGB camera")).AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(0, 0, -6);
            camera.orthographic = true;
            camera.orthographicSize = 2;
            camera.aspect = Width / (float)Height;
            camera.nearClipPlane = .25f;
            camera.farClipPlane = 25;
            camera.allowMSAA = false;
            var sun = Own(new GameObject("Capture directional witness")).AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(.2f, .2f, 1));
            sun.shadowStrength = .71f;
            sun.shadowBias = .015f;
            sun.shadowNormalBias = 0;
            RenderSettings.sun = sun;
            Case("native-directional", sun, true, new Vector2(.63f, .38f), new Vector2(.63f, .1f));

            sun.enabled = false;
            RenderSettings.sun = null;
            var spot = Own(new GameObject("Capture additional Spot witness")).AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.transform.position = new Vector3(.45f, .05f, -4);
            spot.range = 10;
            spot.spotAngle = 75;
            spot.intensity = 4;
            spot.shadowStrength = .63f;
            spot.shadowBias = .015f;
            spot.shadowNormalBias = 0;
            Case("full-additional-spot-without-main", spot, false, new Vector2(.43f, -.36f), new Vector2(.43f, .02f));
            Require(!ShaderUtil.ShaderHasError(face.shader) && !ShaderUtil.ShaderHasError(eye.shader), "Production shader import errors.");

            void Render(bool main, bool additional)
            {
                replay.ExpectMain = main;
                replay.ExpectAdditional = additional;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = destination });
            }

            void Case(string name, Light light, bool main, Vector2 facePoint, Vector2 eyePoint)
            {
                var row = new Row { name = name };
                report.cases.Add(row);
                SetCastScope(main ? 1 : 2);
                light.shadows = LightShadows.None;
                Render(false, false);
                Render(false, false);
                var baseline = Read(color);
                var baselineOptical = Read(optical);
                int faceWitness = Pixel(camera, color, facePoint);
                int eyeWitness = Pixel(camera, color, eyePoint);
                int faceControl = Pixel(camera, color, new Vector2(1.23f, facePoint.y));
                int eyeControl = Pixel(camera, color, new Vector2(1.23f, eyePoint.y));
                Require(baselineOptical[faceWitness].r == 0 && baselineOptical[eyeWitness].r > .99f,
                    name + " must witness Face alone and a fully opaque Eye contribution.");

                light.shadows = LightShadows.Hard;
                Render(main, !main);
                var shadowed = Read(color);
                row.faceBaseline = baseline[faceWitness].r;
                row.faceShadow = shadowed[faceWitness].r;
                row.eyeBaseline = baseline[eyeWitness].r;
                row.eyeShadow = shadowed[eyeWitness].r;
                row.faceLitError = Delta(baseline[faceControl], shadowed[faceControl]);
                row.eyeLitError = Delta(baseline[eyeControl], shadowed[eyeControl]);
                row.opticalError = MaxDelta(baselineOptical, Read(optical));
                for (int i = 0; i < baseline.Length; i++)
                    if (Delta(baseline[i], shadowed[i]) > .02f) row.changedPixels++;
                Debug.Log($"CAPTURE_MANAGED {name} Face {row.faceBaseline:F5}->{row.faceShadow:F5}, Eye {row.eyeBaseline:F5}->{row.eyeShadow:F5}, controls {row.faceLitError:F5}/{row.eyeLitError:F5}, pixels {row.changedPixels}");
                Require(row.faceBaseline - row.faceShadow > .05f && row.eyeBaseline - row.eyeShadow > .045f
                    && row.faceLitError < .015f && row.eyeLitError < .015f && row.changedPixels > 45 && row.opticalError < .0002f,
                    name + " both real Capture fragments must shade the occluded samples and retain lit/optical controls.");

                if (main) capture.CaptureShadowInputsEnabled = false;
                else SetCastScope(1);
                Render(false, !main);
                row.disabledOrMainError = MaxDelta(baseline, Read(color));
                Require(row.disabledOrMainError < .002f, name + " preview-off or Main mode must agree with the unshadowed capture.");
                capture.CaptureShadowInputsEnabled = true;
                SetCastScope(main ? 1 : 2);
                light.shadows = LightShadows.None;
                Render(false, false);
                row.sourceLostError = MaxDelta(baseline, Read(color));
                Require(row.sourceLostError < .002f, name + " previous Capture bindings must not affect a frame with no atlas.");
            }

            void SetCastScope(int mode)
            {
                face.SetFloat("_HoRPShadowMode", mode);
                eye.SetFloat("_HoRPShadowMode", mode);
            }
        }
        finally
        {
            Shader.SetGlobalFloat("_HoObjectBufferPartCount", 0);
            GraphicsSettings.defaultRenderPipeline = oldPipeline;
            QualitySettings.renderPipeline = oldQuality;
            colorHandle.Release();
            opticalHandle.Release();
            for (int i = owned.Count - 1; i >= 0; i--) Object.DestroyImmediate(owned[i]);
        }
    }

    private static Material MakeMaterial(string name, Color color)
    {
        var shader = Shader.Find(name);
        Require(shader != null, name);
        var material = new Material(shader);
        material.SetKeyword(new LocalKeyword(shader, "_HO_SHADOWS_SCREEN_PREVIEW"), true);
        material.SetColor("_BaseColor", color);
        material.SetColor("_ShadowTint", new Color(.11f, .14f, .18f));
        material.SetFloat("_UseShadow", 1);
        material.SetFloat("_ReceiveShadows", 1);
        material.SetFloat("_ShadowMode", 0);
        material.SetFloat("_UseSpecular", 0);
        material.SetFloat("_UseEnvironmentLighting", 0);
        material.SetFloat("_Cull", 0);
        material.SetFloat("_StencilRef", 1);
        material.SetFloat("_StencilReadMask", 1);
        material.SetFloat("_StencilWriteMask", 1);
        return material;
    }

    private static GameObject Quad(string name, Material material, uint owner, Vector3 position, Vector2 size)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.transform.position = position;
        quad.transform.localScale = new Vector3(size.x, size.y, 1);
        var renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.SetShaderUserValue(0xBEEF0000u | owner);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return quad;
    }

    private static RenderTexture Target(int width, int height, int depth)
    {
        var target = new RenderTexture(width, height, depth, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        target.Create();
        return target;
    }

    private static Color[] Read(RenderTexture target)
    {
        var previous = RenderTexture.active;
        var cpu = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
        try
        {
            RenderTexture.active = target;
            cpu.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            cpu.Apply(false);
            return cpu.GetPixels();
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(cpu);
        }
    }

    private static int Pixel(Camera camera, RenderTexture target, Vector2 point)
    {
        Vector3 uv = camera.WorldToViewportPoint(point);
        return Mathf.FloorToInt(uv.y * target.height) * target.width + Mathf.FloorToInt(uv.x * target.width);
    }

    private static float Delta(Color a, Color b) =>
        Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a));

    private static float MaxDelta(Color[] a, Color[] b)
    {
        float maximum = 0;
        for (int i = 0; i < a.Length; i++)
            maximum = Mathf.Max(maximum, Delta(a[i], b[i]));
        return maximum;
    }

    internal static void Require(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
    }
}
