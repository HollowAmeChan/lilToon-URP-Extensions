using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.Shadows;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class HoNativeMainShadowProbe
{
    [Serializable]
    sealed class Case
    {
        public string name;
        public int width;
        public int height;
        public int cameraId;
        public float maximumError;
        public float meanError;
        public int shadowed;
        public int lit;
        public int partial;
        public int observedCascades;
        public bool comparedStock;
    }

    [Serializable]
    sealed class Report
    {
        public string unity;
        public string graphics;
        public bool passed;
        public string error;
        public List<Case> cases = new();
    }

    public static void Run()
    {
        string output = Environment.GetEnvironmentVariable("HO_SHADOWS_NATIVE_MAIN_OUTPUT");
        if (!Directory.Exists(output) || File.Exists(Path.Combine(output, "native-main-report.json")))
            throw new InvalidOperationException("Use a fresh isolated output directory.");

        var report = new Report { unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString() };
        try
        {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "A graphics device is required.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Verify(report);
            report.passed = true;
        }
        catch (Exception error)
        {
            report.error = error.ToString();
            throw;
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "native-main-report.json"),
                JsonUtility.ToJson(report, true));
        }
        Debug.Log("HO_SHADOWS_P1A_OK");
    }

    static void Verify(Report report)
    {
        var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
        var sourceType = typeof(UniversalRenderer).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceShadows");
        Require(sourceType != null, "Stock ScreenSpaceShadows is missing.");
        var stock = (ScriptableRendererFeature)ScriptableObject.CreateInstance(sourceType);
        var feature = ScriptableObject.CreateInstance<HoShadowsRendererFeature>();
        var probe = ScriptableObject.CreateInstance<ProbeFeature>();
        renderer.renderingMode = RenderingMode.Forward;
        renderer.rendererFeatures.Add(stock);
        renderer.rendererFeatures.Add(feature);
        renderer.rendererFeatures.Add(probe);
        var pipeline = UniversalRenderPipelineAsset.Create(renderer);
        pipeline.msaaSampleCount = 1;
        pipeline.renderScale = 1;
        pipeline.shadowDistance = 16;
        pipeline.shadowCascadeCount = 1;
        pipeline.cascade4Split = new Vector3(.20f, .35f, .60f);
        var serialized = new SerializedObject(pipeline);
        serialized.FindProperty("m_MainLightShadowsSupported").boolValue = true;
        serialized.FindProperty("m_SoftShadowsSupported").boolValue = true;
        serialized.FindProperty("m_MainLightShadowmapResolution").intValue = 256;
        serialized.FindProperty("m_SoftShadowQuality").intValue = (int)SoftShadowQuality.High;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        RenderPipelineAsset oldGraphics = GraphicsSettings.defaultRenderPipeline;
        RenderPipelineAsset oldQuality = QualitySettings.renderPipeline;
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        var root = new GameObject("P1a Shadow Witness");
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Shader resolve = Shader.Find("Hidden/HoShadows/NativeMainResolve");
        Require(lit != null && resolve != null, "Required shaders are unavailable.");
        var material = new Material(lit);
        try
        {
            var receiver = GameObject.CreatePrimitive(PrimitiveType.Quad);
            receiver.transform.SetParent(root.transform);
            receiver.transform.localScale = new Vector3(10, 10, 1);
            receiver.GetComponent<Renderer>().sharedMaterial = material;
            receiver.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.transform.SetParent(root.transform);
            blocker.transform.position = new Vector3(-.6f, .4f, -1.4f);
            blocker.transform.localScale = new Vector3(1.1f, 1.5f, .35f);
            blocker.GetComponent<Renderer>().sharedMaterial = material;
            blocker.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            var sun = new GameObject("Main directional light").AddComponent<Light>();
            sun.transform.SetParent(root.transform);
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(.19f, .21f, 1));
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 1;
            sun.shadowBias = .015f;
            sun.shadowNormalBias = 0;
            RenderSettings.sun = sun;

            var cameraA = MakeCamera(root, "Orthographic A", new Vector3(0, 0, -6));
            var cameraB = MakeCamera(root, "Orthographic B", new Vector3(1.45f, 0, -6));
            var hard = Sample(report, probe, cameraA, "hard-one-cascade", 256, 192, true, 1);
            sun.shadows = LightShadows.Soft;
            var soft = Sample(report, probe, cameraA, "pcf-high-one-cascade", 256, 192, true, 1);
            Require(soft.partial > hard.partial, "PCF must widen the hard-shadow transition.");

            Sample(report, probe, cameraB, "different-camera-resolution", 208, 160, true, 1);
            sun.shadows = LightShadows.None;
            Sample(report, probe, cameraB, "same-camera-next-render-no-main-shadows", 208, 160, false, 0);
            sun.enabled = false;
            RenderSettings.sun = null;
            Sample(report, probe, cameraA, "prior-camera-no-visible-main", 256, 192, false, 0);
            sun.enabled = true;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            var again = Sample(report, probe, cameraA, "first-camera-after-no-main", 256, 192, true, 1);
            Require(again.shadowed == soft.shadowed && again.partial == soft.partial,
                "Camera changes must not change the first camera's restored results.");

            pipeline.shadowCascadeCount = 4;
            var cameraC = MakeCamera(root, "Perspective cascades", new Vector3(0, 0, -6));
            cameraC.orthographic = false;
            cameraC.fieldOfView = 41;
            receiver.transform.rotation = Quaternion.Euler(54, 0, 0);
            Sample(report, probe, cameraC, "pcf-high-four-cascades", 256, 192, true, 2);
            stock.SetActive(false);
            pipeline.shadowCascadeCount = 1;
            receiver.transform.rotation = Quaternion.identity;
            var independent = Sample(report, probe, cameraA, "pcf-high-with-stock-feature-disabled", 256, 192, true, 1, false);
            Require(independent.shadowed == soft.shadowed && independent.partial == soft.partial && independent.lit == soft.lit,
                "The preview must match its prior classification without the stock renderer feature.");
            Require(!ShaderUtil.ShaderHasError(resolve), "P1a shader compilation failed.");
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = oldGraphics;
            QualitySettings.renderPipeline = oldQuality;
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(pipeline);
            Object.DestroyImmediate(renderer);
            Object.DestroyImmediate(probe);
            Object.DestroyImmediate(feature);
            Object.DestroyImmediate(stock);
        }
    }

    static Camera MakeCamera(GameObject root, string name, Vector3 position)
    {
        var camera = new GameObject(name).AddComponent<Camera>();
        camera.transform.SetParent(root.transform);
        camera.transform.position = position;
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 2;
        camera.nearClipPlane = .25f;
        camera.farClipPlane = 25;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.allowMSAA = false;
        return camera;
    }

    static Case Sample(Report report, ProbeFeature probe, Camera camera, string name, int width, int height,
        bool expectedValid, int minimumObservedCascades, bool compareStock = true)
    {
        probe.ExpectStock = expectedValid && compareStock;
        probe.ProductCameraId = -1;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var cpu = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        RenderTexture saved = RenderTexture.active;
        Color[] pixels;
        try
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "SingleCameraRequest is unsupported.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            cpu.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            cpu.Apply(false);
            pixels = cpu.GetPixels();
        }
        finally
        {
            RenderTexture.active = saved;
            Object.DestroyImmediate(cpu);
            Object.DestroyImmediate(target);
        }

        var result = new Case { name = name, width = width, height = height, cameraId = probe.ProductCameraId, comparedStock = probe.ExpectStock };
        report.cases.Add(result);
        Require(probe.ProductCameraId == (expectedValid ? camera.GetInstanceID() : 0), name + " product camera ID.");
        var observedCascades = new HashSet<int>();
        int count = 0;
        foreach (Color pixel in pixels)
        {
            Require(Mathf.Abs(pixel.b - (expectedValid ? 1 : 0)) < .01f, name + " camera validity channel.");
            if (!expectedValid)
            {
                Require(pixel.r > .99f && pixel.g > .99f, name + " neutral output.");
                continue;
            }

            if (pixel.a < .01f)
                continue;
            observedCascades.Add(Mathf.RoundToInt(pixel.a * 8f) - 1);
            float error = Mathf.Abs(pixel.r - pixel.g);
            result.maximumError = Mathf.Max(result.maximumError, error);
            result.meanError += error;
            if (pixel.r < .08f) result.shadowed++;
            else if (pixel.r > .92f) result.lit++;
            else result.partial++;
            count++;
        }

        if (expectedValid)
        {
            observedCascades.Remove(4);
            result.observedCascades = observedCascades.Count;
            result.meanError /= Mathf.Max(count, 1);
            Require(result.shadowed > 12 && result.lit > 12, name + " unoccluded and occluded witnesses.");
            Require(result.observedCascades >= minimumObservedCascades, name + " coverage of distinct cascade indices.");
            // The built-in producer stores R8_UNorm, while the preview keeps R16_SFloat.
            if (compareStock) Require(result.maximumError <= .008f, name + " stock resolve maximum error " + result.maximumError);
        }

        Debug.Log($"P1a {name} valid={expectedValid} cascadeRegions={result.observedCascades} maxError={result.maximumError:F6} meanError={result.meanError:F6} shadow={result.shadowed} lit={result.lit} partial={result.partial}");
        return result;
    }

    static void Require(bool passed, string message)
    {
        if (!passed)
            throw new InvalidOperationException(message);
    }

    sealed class ProbeFeature : ScriptableRendererFeature
    {
        public bool ExpectStock;
        public int ProductCameraId;
        Material material;

        public override void Create()
        {
            CoreUtils.Destroy(material);
            material = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/HoShadows/NativeMainShadowReadback"));
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(new ProbePass(this, material));
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);
    }

    sealed class ProbePass : ScriptableRenderPass
    {
        static readonly int StockTexture = Shader.PropertyToID("_ScreenSpaceShadowmapTexture");
        static readonly int ProductTexture = Shader.PropertyToID("_HoProbeShadow");
        static readonly int CompareStock = Shader.PropertyToID("_HoProbeCompareStock");
        static readonly int Valid = Shader.PropertyToID("_HoProbeValid");
        static readonly int CascadeCount = Shader.PropertyToID("_HoProbeCascadeCount");
        readonly ProbeFeature owner;
        readonly Material material;

        sealed class PassData
        {
            public Material material;
            public TextureHandle result;
            public bool valid;
            public bool compareStock;
            public int cascadeCount;
        }

        public ProbePass(ProbeFeature owner, Material material)
        {
            this.owner = owner;
            this.material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var product = frameData.GetOrCreate<HoNativeMainShadowFrameData>();
            owner.ProductCameraId = product.CameraInstanceId;
            var resources = frameData.Get<UniversalResourceData>();
            using var builder = graph.AddRasterRenderPass<PassData>("Ho P1a comparison with stock", out var data);
            data.material = material;
            data.result = product.Visibility;
            data.valid = product.IsValid;
            data.compareStock = data.valid && owner.ExpectStock;
            data.cascadeCount = frameData.Get<UniversalShadowData>().mainLightShadowCascadesCount;
            if (data.valid)
            {
                builder.UseTexture(data.result, AccessFlags.Read);
                if (data.compareStock)
                    builder.UseGlobalTexture(StockTexture);
                builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
            }

            builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.WriteAll);
            builder.SetRenderFunc(static (PassData pass, RasterGraphContext ctx) =>
            {
                var properties = ctx.renderGraphPool.GetTempMaterialPropertyBlock();
                properties.SetFloat(Valid, pass.valid ? 1 : 0);
                properties.SetFloat(CompareStock, pass.compareStock ? 1 : 0);
                properties.SetFloat(CascadeCount, pass.cascadeCount);
                if (pass.valid)
                    properties.SetTexture(ProductTexture, pass.result);
                CoreUtils.DrawFullScreen(ctx.cmd, pass.material, properties);
            });
        }
    }
}
