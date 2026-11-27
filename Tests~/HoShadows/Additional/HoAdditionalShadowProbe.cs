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

public static class HoAdditionalShadowProbe
{
    const int Width = 240;
    const int Height = 160;

    [Serializable]
    sealed class Witness
    {
        public int shaderIndex = -1;
        public int visibleIndex = -1;
        public int metadataRank = -1;
        public int firstNativeSlice = -1;
        public int nativeSliceCount;
        public uint nativeSliceMask;
        public int screenSlot = -1;
        public float maximumError;
        public float meanError;
        public float centerVisibility;
        public float crossVisibility;
    }

    [Serializable]
    sealed class Case
    {
        public string name;
        public string mode;
        public int cameraId;
        public int productCameraId;
        public bool observed;
        public bool hasMainProduct;
        public bool usesForwardIndices;
        public int metadataCount;
        public int screenCount;
        public bool mappingsValid;
        public int incompletePointCount;
        public Witness point = new();
        public Witness spot = new();
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
        string folder = Environment.GetEnvironmentVariable("HO_SHADOWS_ADDITIONAL_OUTPUT");
        if (!Directory.Exists(folder))
            throw new InvalidOperationException("Create a fresh isolated output directory and set HO_SHADOWS_ADDITIONAL_OUTPUT.");
        string reportPath = Path.Combine(folder, "additional-shadow-report.json");
        Require(!File.Exists(reportPath), "Output directory already has an additional-shadow report.");

        var report = new Report { unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString() };
        try
        {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Graphics device required.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            VerifyMode(report, RenderingMode.Forward);
            VerifyMode(report, RenderingMode.ForwardPlus);
            report.passed = true;
        }
        catch (Exception error)
        {
            report.error = error.ToString();
            throw;
        }
        finally
        {
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
        }

        Debug.Log("HO_SHADOWS_P2B_OK");
    }

    static void VerifyMode(Report report, RenderingMode mode)
    {
        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        var feature = ScriptableObject.CreateInstance<HoShadowsRendererFeature>();
        var probe = ScriptableObject.CreateInstance<ProbeFeature>();
        rendererData.renderingMode = mode;
        rendererData.rendererFeatures.Add(feature);
        rendererData.rendererFeatures.Add(probe);
        var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
        pipeline.msaaSampleCount = 1;
        pipeline.renderScale = 1;
        pipeline.shadowDistance = 20;
        var serialized = new SerializedObject(pipeline);
        serialized.FindProperty("m_MainLightShadowsSupported").boolValue = false;
        serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
        serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
        serialized.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = 1024;
        serialized.FindProperty("m_SoftShadowsSupported").boolValue = true;
        serialized.FindProperty("m_SoftShadowQuality").intValue = (int)SoftShadowQuality.High;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        RenderPipelineAsset oldGraphics = GraphicsSettings.defaultRenderPipeline;
        RenderPipelineAsset oldQuality = QualitySettings.renderPipeline;
        Light oldSun = RenderSettings.sun;
        var root = new GameObject("Additional shadow comparison");
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Require(lit != null, "URP Lit is required for the depth and caster fixture.");
        var material = new Material(lit);
        try
        {
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            RenderSettings.sun = null;

            MakePrimitive(root.transform, PrimitiveType.Quad, new Vector3(0, 0, 0), new Vector3(12, 12, 1),
                material, ShadowCastingMode.Off);
            MakePrimitive(root.transform, PrimitiveType.Cube, new Vector3(-.9f, 0, -1.3f), new Vector3(.48f, 1.25f, .22f),
                material, ShadowCastingMode.ShadowsOnly);
            MakePrimitive(root.transform, PrimitiveType.Cube, new Vector3(.9f, 0, -1.3f), new Vector3(.48f, 1.25f, .22f),
                material, ShadowCastingMode.ShadowsOnly);

            Light point = MakeLight(root.transform, LightType.Point, new Vector3(-1.5f, 0, -3.7f), Color.red);
            Light spot = MakeLight(root.transform, LightType.Spot, new Vector3(1.5f, 0, -3.7f), Color.blue);
            spot.spotAngle = 113;
            probe.PointId = point.GetInstanceID();
            probe.SpotId = spot.GetInstanceID();

            Camera cameraA = MakeCamera(root.transform, "First camera", new Vector3(0, 0, -7));
            Camera cameraB = MakeCamera(root.transform, "Second camera", new Vector3(.26f, 0, -7));
            var hard = Sample(report, probe, cameraA, mode, "two-hard-lights-without-main", 2, true, true);
            VerifyIsolation(hard, .12f, .12f);
            Require(hard.point.shaderIndex != hard.spot.shaderIndex && hard.point.screenSlot != hard.spot.screenSlot,
                "Two native lights need distinct source indices and output slots.");

            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = .65f;
            var soft = Sample(report, probe, cameraB, mode, "pcf-and-shadow-strength-second-camera", 2, true, true);
            VerifyIsolation(soft, .12f, .62f);
            Require(soft.spot.centerVisibility > .29f && soft.spot.centerVisibility < .62f,
                "The spot's realtime value must include the native shadow strength.");
            spot.shadows = LightShadows.Hard;
            spot.shadowStrength = 1;

            bool pointFirst = hard.point.shaderIndex < hard.spot.shaderIndex;
            Light earlier = pointFirst ? point : spot;
            Light later = pointFirst ? spot : point;
            int survivorBefore = pointFirst ? hard.spot.shaderIndex : hard.point.shaderIndex;
            earlier.enabled = false;
            var reordered = Sample(report, probe, cameraA, mode, "earlier-source-disabled-survivor-reindexed", 1,
                !pointFirst, pointFirst);
            Witness surviving = pointFirst ? reordered.spot : reordered.point;
            Require(surviving.shaderIndex == 0 && surviving.shaderIndex != survivorBefore && surviving.screenSlot == 0,
                "The surviving light must acquire the new source index and slot without reading the old index.");
            Require(surviving.centerVisibility < .12f, "The reindexed light must keep its own occlusion.");

            later.shadows = LightShadows.None;
            Sample(report, probe, cameraB, mode, "visible-light-with-no-shadow", 0, false, false);
            earlier.enabled = true;
            later.shadows = LightShadows.Hard;
            feature.SetActive(false);
            var nativeOnly = Sample(report, probe, cameraA, mode, "feature-disabled-with-native-atlas", 2, false, false);
            Require(nativeOnly.point.centerVisibility < .12f && nativeOnly.spot.centerVisibility < .12f,
                "The disabled feature must leave native shadowing available.");
            feature.SetActive(true);
            var restored = Sample(report, probe, cameraA, mode, "both-lights-restored", 2, true, true);
            VerifyIsolation(restored, .12f, .12f);
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = oldGraphics;
            QualitySettings.renderPipeline = oldQuality;
            RenderSettings.sun = oldSun;
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(pipeline);
            Object.DestroyImmediate(rendererData);
            Object.DestroyImmediate(probe);
            Object.DestroyImmediate(feature);
        }
    }

    static Light MakeLight(Transform root, LightType type, Vector3 position, Color color)
    {
        var light = new GameObject(type.ToString()).AddComponent<Light>();
        light.transform.SetParent(root);
        light.transform.position = position;
        light.type = type;
        light.color = color;
        light.range = 10;
        light.intensity = 4;
        light.renderMode = LightRenderMode.ForcePixel;
        light.shadows = LightShadows.Hard;
        light.shadowStrength = 1;
        light.shadowBias = .015f;
        light.shadowNormalBias = 0;
        return light;
    }

    static void MakePrimitive(Transform root, PrimitiveType geometry, Vector3 position, Vector3 scale,
        Material material, ShadowCastingMode casting)
    {
        var primitive = GameObject.CreatePrimitive(geometry);
        primitive.transform.SetParent(root);
        primitive.transform.position = position;
        primitive.transform.localScale = scale;
        var renderer = primitive.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = casting;
    }

    static Camera MakeCamera(Transform root, string name, Vector3 position)
    {
        var camera = new GameObject(name).AddComponent<Camera>();
        camera.transform.SetParent(root);
        camera.transform.position = position;
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 1.8f;
        camera.nearClipPlane = .25f;
        camera.farClipPlane = 24;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.allowMSAA = false;
        camera.allowHDR = true;
        return camera;
    }

    static Case Sample(Report report, ProbeFeature probe, Camera camera, RenderingMode mode, string name,
        int expectedMetadata, bool expectPoint, bool expectSpot)
    {
        var result = new Case { name = name, mode = mode.ToString(), cameraId = camera.GetInstanceID() };
        report.cases.Add(result);
        probe.Current = result;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var cpu = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
        RenderTexture oldTarget = RenderTexture.active;
        try
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "Single camera request unsupported.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            cpu.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            cpu.Apply(false);
            Color[] pixels = cpu.GetPixels();

            Require(result.observed && !result.hasMainProduct, name + " must run without a main shadow product.");
            Require(result.metadataCount == expectedMetadata, name + " native metadata count.");
            Require(result.usesForwardIndices == (expectedMetadata > 0), name + " native Forward indices.");
            int expectedScreenCount = (expectPoint ? 1 : 0) + (expectSpot ? 1 : 0);
            Require(result.screenCount == expectedScreenCount && result.mappingsValid, name + " screen slot map.");
            Require(result.productCameraId == (expectedScreenCount > 0 ? result.cameraId : 0), name + " current camera ID.");
            Require((result.point.screenSlot >= 0) == expectPoint && (result.spot.screenSlot >= 0) == expectSpot,
                name + " publication for each tracked light.");
            if (expectPoint)
                Require(result.point.nativeSliceCount == 6 && result.point.nativeSliceMask == 0x3f, name + " point-face mask.");
            if (expectSpot)
                Require(result.spot.nativeSliceCount == 1 && result.spot.nativeSliceMask == 1, name + " spot-face mask.");

            Measure(result.point, pixels, 0, camera, -.58f, expectPoint);
            Measure(result.spot, pixels, 1, camera, .58f, expectSpot);
            Debug.Log($"P2b {mode}/{name} native={result.metadataCount} screen={result.screenCount} " +
                $"point={result.point.shaderIndex}:{result.point.screenSlot} ({result.point.maximumError:F6}) " +
                $"spot={result.spot.shaderIndex}:{result.spot.screenSlot} ({result.spot.maximumError:F6})");
            return result;
        }
        finally
        {
            RenderTexture.active = oldTarget;
            probe.Current = null;
            Object.DestroyImmediate(cpu);
            Object.DestroyImmediate(target);
        }
    }

    static void Measure(Witness witness, Color[] pixels, int lightChannel, Camera camera, float centerX, bool hasScreen)
    {
        int nativeChannel = lightChannel + 2;
        int sampledChannel = hasScreen ? lightChannel : nativeChannel;
        witness.centerVisibility = NeighborhoodAverage(pixels, camera, centerX, sampledChannel);
        witness.crossVisibility = NeighborhoodAverage(pixels, camera, -centerX, sampledChannel);

        foreach (Color pixel in pixels)
        {
            float error = Mathf.Abs(pixel[lightChannel] - (hasScreen ? pixel[nativeChannel] : 1));
            Require(float.IsFinite(error), "Non-finite visibility error.");
            witness.maximumError = Mathf.Max(witness.maximumError, error);
            witness.meanError += error;
        }
        witness.meanError /= pixels.Length;
        Require(witness.maximumError <= .015f && witness.meanError <= .003f,
            $"Slot {witness.screenSlot} source {witness.shaderIndex} differs from the expected visibility, " +
            $"max {witness.maximumError:F6} mean {witness.meanError:F6}.");
    }

    static float NeighborhoodAverage(Color[] pixels, Camera camera, float worldX, int channel)
    {
        // The camera has identity rotation and the sample points are midway up the image.
        float halfWidth = camera.orthographicSize * Width / Height;
        int x = Mathf.RoundToInt((.5f + (worldX - camera.transform.position.x) * .5f / halfWidth) * Width - .5f);
        float sum = 0;
        for (int y = Height / 2 - 2; y <= Height / 2 + 2; ++y)
        for (int dx = -2; dx <= 2; ++dx)
            sum += pixels[y * Width + x + dx][channel];
        return sum / 25;
    }

    static void VerifyIsolation(Case result, float pointMaximum, float spotMaximum)
    {
        Require(result.point.centerVisibility < pointMaximum && result.spot.centerVisibility < spotMaximum,
            result.name + " both authored blockers should shadow their intended neighborhoods.");
        Require(result.point.crossVisibility > .92f && result.spot.crossVisibility > .92f,
            result.name + " each other-light neighborhood should remain lit.");
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    sealed class ProbeFeature : ScriptableRendererFeature
    {
        public int PointId;
        public int SpotId;
        public Case Current;
        Material material;
        ProbePass pass;

        public override void Create()
        {
            CoreUtils.Destroy(material);
            material = null;
            pass = null;
            Shader shader = Shader.Find("Hidden/HoShadows/AdditionalShadowReadback");
            if (shader != null)
            {
                material = CoreUtils.CreateEngineMaterial(shader);
                pass = new ProbePass(this, material);
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (Current != null && pass != null)
                renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);
    }

    sealed class ProbePass : ScriptableRenderPass
    {
        static readonly int Parameters = Shader.PropertyToID("_HoAdditionalProbeIndices");
        static readonly int Visibility = Shader.PropertyToID("_HoAdditionalProbeVisibility");
        static readonly int Depth = Shader.PropertyToID("_HoAdditionalProbeDepth");
        readonly ProbeFeature owner;
        readonly Material material;

        sealed class PassData
        {
            public Material material;
            public Vector4 indices;
            public TextureHandle visibility;
            public TextureHandle depth;
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
            Case result = owner.Current;
            if (result == null)
                return;

            var resources = frameData.Get<UniversalResourceData>();
            var lights = frameData.Get<UniversalLightData>();
            var atlas = frameData.Contains<AdditionalLightShadowAtlasData>()
                ? frameData.Get<AdditionalLightShadowAtlasData>() : null;
            var product = frameData.Contains<HoAdditionalShadowFrameData>()
                ? frameData.Get<HoAdditionalShadowFrameData>() : null;
            result.observed = true;
            result.hasMainProduct = frameData.Contains<HoNativeMainShadowFrameData>()
                && frameData.Get<HoNativeMainShadowFrameData>().IsValid;
            result.usesForwardIndices = atlas?.UsesForwardLightIndices ?? false;
            result.metadataCount = atlas?.Count ?? 0;
            result.screenCount = product?.Count ?? 0;
            result.productCameraId = product?.CameraInstanceId ?? 0;
            result.mappingsValid = true;
            for (int i = 0; i < result.metadataCount; ++i)
                ObserveEntry(atlas[i], lights, product, result);
            for (int slot = 0; slot < result.screenCount; ++slot)
            {
                int shaderIndex = product.GetShaderLightIndex(slot);
                result.mappingsValid &= atlas != null && atlas.TryGetByShaderLightIndex(shaderIndex, out _)
                    && product.TryGetSlice(shaderIndex, out int roundTrip) && roundTrip == slot;
            }

            using var builder = graph.AddRasterRenderPass<PassData>("Ho additional shadow native comparison", out var data);
            data.material = material;
            data.visibility = product?.Visibility ?? TextureHandle.nullHandle;
            data.depth = result.screenCount > 0 ? product.SurfaceDepth : resources.cameraDepthTexture;
            data.indices = new Vector4(result.point.screenSlot, result.spot.screenSlot,
                result.point.shaderIndex, result.spot.shaderIndex);

            if (data.visibility.IsValid())
                builder.UseTexture(data.visibility, AccessFlags.Read);
            if (data.depth.IsValid())
                builder.UseTexture(data.depth, AccessFlags.Read);
            if (result.metadataCount > 0 && resources.additionalShadowsTexture.IsValid())
                builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);
            builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.WriteAll);
            builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
            {
                if (pass.depth.IsValid() || pass.indices.z < 0 && pass.indices.w < 0)
                {
                    var properties = context.renderGraphPool.GetTempMaterialPropertyBlock();
                    properties.SetVector(Parameters, pass.indices);
                    if (pass.visibility.IsValid())
                        properties.SetTexture(Visibility, pass.visibility);
                    if (pass.depth.IsValid())
                        properties.SetTexture(Depth, pass.depth);
                    CoreUtils.DrawFullScreen(context.cmd, pass.material, properties);
                }
            });
        }

        void ObserveEntry(AdditionalLightShadowAtlasData.Entry entry, UniversalLightData lights,
            HoAdditionalShadowFrameData product, Case result)
        {
            if ((uint)entry.VisibleLightIndex >= (uint)lights.visibleLights.Length)
            {
                result.mappingsValid = false;
                return;
            }

            VisibleLight visible = lights.visibleLights[entry.VisibleLightIndex];
            Light source = visible.light;
            int id = source == null ? 0 : source.GetInstanceID();
            Witness witness = id == owner.PointId ? result.point : id == owner.SpotId ? result.spot : null;
            if (witness == null)
            {
                result.mappingsValid = false;
                return;
            }

            witness.shaderIndex = entry.ShaderLightIndex;
            witness.visibleIndex = entry.VisibleLightIndex;
            witness.metadataRank = entry.Rank;
            witness.firstNativeSlice = entry.FirstShadowSlice;
            witness.nativeSliceCount = entry.ShadowSliceCount;
            witness.nativeSliceMask = entry.ShadowSliceMask;
            if (product != null && product.TryGetSlice(entry.ShaderLightIndex, out int slot))
                witness.screenSlot = slot;
            if (visible.lightType == LightType.Point && entry.ShadowSliceMask != 0x3f)
            {
                result.incompletePointCount++;
                result.mappingsValid &= witness.screenSlot < 0;
            }
        }
    }
}
