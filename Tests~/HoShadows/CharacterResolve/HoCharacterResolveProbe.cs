using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.CharacterShadow;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.Shadows;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class HoCharacterResolveProbe
{
    const string SelectedPart = "Selected";
    const string UnselectedPart = "Unselected";
    const string OtherPart = "Other";
    const string ReportName = "character-resolve-report.json";
    static readonly Vector3 DarkPoint = new(-1.12f, 0, 0);
    static readonly Vector3 LitPoint = new(-.29f, 0, 0);
    static readonly Vector3 ForegroundPoint = new(-.57f, 0, 0);
    static readonly Vector3 UnselectedPoint = new(.25f, 0, 0);
    static readonly Vector3 OtherPoint = new(.95f, 0, 0);

    [Serializable]
    sealed class Sample
    {
        public string name, format, producerStatus;
        public int width, height, cameraId, expectedOwner;
        public bool surfaceDepth, characterProduct;
        public int accepted, neutral, dark, lit, partial, border;
        public Color darkProbe, litProbe, foregroundProbe, unselectedProbe, otherProbe;
    }

    [Serializable]
    sealed class Report
    {
        public string unity, graphics, error;
        public bool passed;
        public List<Sample> samples = new();
    }

    public static void Run()
    {
        string output = Environment.GetEnvironmentVariable("HO_SHADOWS_CHARACTER_RESOLVE_OUTPUT");
        if (!Directory.Exists(output) || File.Exists(Path.Combine(output, ReportName)))
            throw new InvalidOperationException("Use a fresh isolated output directory.");

        var report = new Report { unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString() };
        bool previousAsync = ShaderUtil.allowAsyncCompilation;
        try
        {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "A graphics device is required.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ShaderUtil.allowAsyncCompilation = false;
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
            ShaderUtil.allowAsyncCompilation = previousAsync;
            File.WriteAllText(Path.Combine(output, ReportName), JsonUtility.ToJson(report, true));
        }

        Debug.Log("HO_SHADOWS_P1C_OK");
    }

    static void Verify(Report report, string output)
    {
        var surfaceShader = Shader.Find("Ho/Environment/URP/Lit");
        var litShader = Shader.Find("Universal Render Pipeline/Lit");
        Require(surfaceShader != null && litShader != null && Shader.Find("Hidden/HoShadows/CharacterResolveReadback") != null,
            "Required receiver, caster, or probe shader is unavailable.");
        var receiverMaterial = new Material(surfaceShader);
        var litMaterial = new Material(litShader);
        var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
        var character = ScriptableObject.CreateInstance<HoCharacterShadowRendererFeature>();
        var surface = ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>();
        var resolve = ScriptableObject.CreateInstance<HoShadowsRendererFeature>();
        var probe = ScriptableObject.CreateInstance<ProbeFeature>();
        character.Settings.resolution = HoCharacterShadowResolution.R512;
        character.Settings.maxCharacters = 2;
        character.Settings.depthBias = 0f;
        character.Settings.normalBias = 0f;
        character.Settings.pcssEnabled = false;
        surface.Settings.enableSemanticLanes = false;
        surface.Settings.enableCorrelatedSemantics = false;
        renderer.renderingMode = RenderingMode.Forward;
        renderer.rendererFeatures.Add(character);
        renderer.rendererFeatures.Add(surface);
        renderer.rendererFeatures.Add(resolve);
        renderer.rendererFeatures.Add(probe);
        var pipeline = UniversalRenderPipelineAsset.Create(renderer);
        pipeline.msaaSampleCount = 1;
        pipeline.shadowDistance = 20f;
        pipeline.shadowCascadeCount = 1;
        var serialized = new SerializedObject(pipeline);
        serialized.FindProperty("m_MainLightShadowsSupported").boolValue = true;
        serialized.FindProperty("m_SoftShadowsSupported").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        var previousGraphics = GraphicsSettings.defaultRenderPipeline;
        var previousQuality = QualitySettings.renderPipeline;
        var previousSun = RenderSettings.sun;
        var root = new GameObject("P1c Character Screen Shadow Witness");
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        try
        {
            var selected = MakeMesh(root, "Selected receiver", PrimitiveType.Quad, new Vector3(-.86f, 0, 0),
                new Vector3(1.41f, 1.65f, 1), receiverMaterial, ShadowCastingMode.Off);
            var unselected = MakeMesh(root, "Unselected receiver", PrimitiveType.Quad, UnselectedPoint,
                new Vector3(.53f, 1.55f, 1), receiverMaterial, ShadowCastingMode.Off);
            var other = MakeMesh(root, "Other group receiver", PrimitiveType.Quad, OtherPoint,
                new Vector3(.55f, 1.55f, 1), receiverMaterial, ShadowCastingMode.Off);
            var foreground = MakeMesh(root, "Foreground without Ho Surface pass", PrimitiveType.Quad,
                ForegroundPoint - new Vector3(0, 0, .34f), new Vector3(.18f, .25f, 1), litMaterial, ShadowCastingMode.Off);
            MakeMesh(root, "Shadows-only blocker", PrimitiveType.Cube, new Vector3(-1.09f, 0, -1.4f),
                new Vector3(.65f, .73f, .3f), litMaterial, ShadowCastingMode.ShadowsOnly);

            var group = MakeObject(root, "Subject owner").AddComponent<HoObjectBufferGroup>();
            group.groupId = 31;
            group.parts.Add(MakePart(UnselectedPart, unselected));
            group.parts.Add(MakePart(SelectedPart, selected));
            group.Apply();
            var otherGroup = MakeObject(root, "Other owner").AddComponent<HoObjectBufferGroup>();
            otherGroup.groupId = 77;
            otherGroup.parts.Add(MakePart(OtherPart, other));
            otherGroup.Apply();
            int selectedOwner = (int)HoObjectBufferRegistry.GetPartId(group.groupId, SelectedPart);
            int unselectedOwner = (int)HoObjectBufferRegistry.GetPartId(group.groupId, UnselectedPart);
            int otherOwner = (int)HoObjectBufferRegistry.GetPartId(otherGroup.groupId, OtherPart);
            Require(selectedOwner >= 256 && unselectedOwner >= 256 && otherOwner >= 256 && unselectedOwner != selectedOwner
                && (otherOwner >> 8) != (selectedOwner >> 8),
                "ObjectBuffer did not assign distinct owners for the fixture parts.");

            var subject = group.gameObject.AddComponent<HoCharacterShadow>();
            subject.objectGroup = group;
            subject.receiveWholeGroup = false;
            subject.receiverParts.Add(SelectedPart);
            subject.center = new Vector3(-.25f, 0, 0);
            subject.size = new Vector3(3f, 1.32f, 1.4f);
            subject.edgeBlend = .18f;

            var sun = MakeObject(root, "Main directional light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(.05f, 0, 1));
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 1;
            sun.shadowBias = 0;
            sun.shadowNormalBias = 0;
            RenderSettings.sun = sun;

            var camera = MakeCamera(root, "Primary", new Vector3(0, 0, -6));
            var alternate = MakeCamera(root, "Different view and raster", new Vector3(-.18f, 0, -6));
            Sample Capture(string name, int expectedOwner, bool expectedProduct = true) =>
                Read(report, output, probe, camera, name, 320, 256, expectedOwner, expectedProduct);

            var hard = Capture("hard", selectedOwner);
            Require(hard.dark > 50 && hard.lit > 50 && hard.border > 20, "Hard shadow, uncovered area, and local border must all appear.");
            Require(hard.darkProbe.r < .08f && hard.litProbe.r > .92f, "Hard-shadow witness locations.");
            Require(IsNeutral(hard.foregroundProbe) && IsNeutral(hard.unselectedProbe) && IsNeutral(hard.otherProbe),
                "The foreground depth, rejected part, and other owner must yield neutral output.");

            foreground.gameObject.SetActive(false);
            var revealed = Capture("foreground-disabled", selectedOwner);
            Require(Owner(revealed.foregroundProbe) == selectedOwner && revealed.accepted > hard.accepted + 20,
                "Removing the foreground must reveal the selected owner at the same pixel.");
            foreground.gameObject.SetActive(true);

            sun.shadows = LightShadows.Soft;
            var soft = Capture("soft", selectedOwner);
            Require(soft.dark > 50 && soft.lit > 50 && soft.partial > hard.partial,
                "Soft PCF must widen the transition while preserving dark and lit pixels.");
            sun.shadowStrength = .35f;
            var strength = Capture("soft-strength-035", selectedOwner);
            Require(Mathf.Abs(strength.darkProbe.r - .65f) < .03f && strength.darkProbe.g > .9f,
                "Output R must include light strength, and G must retain its independent local border blend.");
            sun.shadowStrength = 1;

            subject.receiverParts[0] = UnselectedPart;
            var moved = Capture("selected-part-changed", unselectedOwner);
            Require(IsNeutral(moved.darkProbe) && Owner(moved.unselectedProbe) == unselectedOwner,
                "Changing the whitelist must switch the accepted owner to the other part.");
            subject.receiverParts[0] = SelectedPart;

            subject.enabled = false;
            var otherSubject = otherGroup.gameObject.AddComponent<HoCharacterShadow>();
            otherSubject.objectGroup = otherGroup;
            otherSubject.center = subject.center;
            otherSubject.size = subject.size;
            otherSubject.edgeBlend = subject.edgeBlend;
            var changedGroup = Capture("different-active-group", otherOwner);
            Require(IsNeutral(changedGroup.darkProbe) && Owner(changedGroup.otherProbe) == otherOwner,
                "The other owner is present in SurfaceBuffer and resolves only when its own group is active.");
            otherSubject.enabled = false;
            subject.enabled = true;

            surface.SetActive(false);
            var noSurface = Capture("surface-feature-disabled", 0, false);
            Require(noSurface.surfaceDepth, "Missing SurfaceBuffer must leave the native main depth available.");
            surface.SetActive(true);
            character.SetActive(false);
            var noAtlas = Capture("character-feature-disabled", 0, false);
            Require(noAtlas.surfaceDepth, "Missing the Character atlas must leave the native main depth available.");
            character.SetActive(true);

            Read(report, output, probe, alternate, "alternate-camera", 224, 176, selectedOwner, true);
            alternate.cullingMask = 0;
            Read(report, output, probe, alternate, "alternate-camera-empty", 224, 176, 0, false);
            alternate.cullingMask = camera.cullingMask;
            sun.shadows = LightShadows.None;
            var noShadow = Read(report, output, probe, alternate, "alternate-camera-no-main-shadow", 224, 176, 0, false);
            Require(noShadow.cameraId == 0 && !noShadow.surfaceDepth, "The unshadowed view cannot inherit the previous frame product.");
            sun.shadows = LightShadows.Soft;
            var restored = Capture("primary-restored", selectedOwner);
            Require(restored.accepted == soft.accepted && restored.dark == soft.dark && restored.partial == soft.partial,
                "The primary camera must resume the same result after views without a character product.");

            character.SetActive(false);
            resolve.CharacterSettings.resolution = HoCharacterShadowResolution.R512;
            resolve.CharacterSettings.maxCharacters = 2;
            resolve.CharacterSettings.depthBias = 0;
            resolve.CharacterSettings.normalBias = 0;
            resolve.ManagedCharacterShadowsEnabled = true;
            var managed = Capture("managed-character-without-legacy-feature", selectedOwner);
            Require(managed.dark > 50 && managed.lit > 50 && managed.partial > 0 && managed.producerStatus.Contains("fixed PCF"),
                "Ho Shadows must own the atlas and fixed filter when the standalone feature is disabled.");
            resolve.ManagedCharacterShadowsEnabled = false;
            Capture("managed-character-turned-off", 0, false);
        }
        finally
        {
            RenderSettings.sun = previousSun;
            GraphicsSettings.defaultRenderPipeline = previousGraphics;
            QualitySettings.renderPipeline = previousQuality;
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(receiverMaterial);
            Object.DestroyImmediate(litMaterial);
            Object.DestroyImmediate(pipeline);
            Object.DestroyImmediate(renderer);
            Object.DestroyImmediate(character);
            Object.DestroyImmediate(surface);
            Object.DestroyImmediate(resolve);
            Object.DestroyImmediate(probe);
        }
    }

    static HoObjectBufferPartEntry MakePart(string name, Renderer mesh) =>
        new() { name = name, includeChildren = false, renderers = new Object[] { mesh } };

    static GameObject MakeObject(GameObject root, string name)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(root.transform, false);
        return obj;
    }

    static Renderer MakeMesh(GameObject root, string name, PrimitiveType shape, Vector3 position, Vector3 scale,
        Material material, ShadowCastingMode castsShadows)
    {
        var obj = GameObject.CreatePrimitive(shape);
        obj.transform.SetParent(root.transform, false);
        obj.name = name;
        obj.transform.SetPositionAndRotation(position, Quaternion.identity);
        obj.transform.localScale = scale;
        var mesh = obj.GetComponent<Renderer>();
        mesh.sharedMaterial = material;
        mesh.shadowCastingMode = castsShadows;
        return mesh;
    }

    static Camera MakeCamera(GameObject root, string name, Vector3 position)
    {
        var camera = MakeObject(root, name).AddComponent<Camera>();
        camera.transform.position = position;
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 1.19f;
        camera.nearClipPlane = .25f;
        camera.farClipPlane = 25;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.allowMSAA = false;
        return camera;
    }

    static Sample Read(Report report, string output, ProbeFeature probe, Camera camera, string name,
        int width, int height, int expectedOwner, bool expectedProduct)
    {
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var cpu = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        var previous = RenderTexture.active;
        var sample = new Sample { name = name, width = width, height = height, expectedOwner = expectedOwner };
        report.samples.Add(sample);
        try
        {
            probe.sample = sample;
            camera.aspect = (float)width / height;
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "Single-camera rendering is unavailable.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            sample.producerStatus = HoCharacterShadowRendererFeature.LastCullStatus;
            RenderTexture.active = target;
            cpu.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            cpu.Apply(false);
            File.WriteAllBytes(Path.Combine(output, name + ".exr"), cpu.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            sample.darkProbe = At(DarkPoint);
            sample.litProbe = At(LitPoint);
            sample.foregroundProbe = At(ForegroundPoint);
            sample.unselectedProbe = At(UnselectedPoint);
            sample.otherProbe = At(OtherPoint);

            Require(sample.characterProduct == expectedProduct, name + " product availability. " + sample.producerStatus);
            if (expectedProduct)
                Require(sample.cameraId == camera.GetInstanceID() && sample.surfaceDepth
                    && sample.format == GraphicsFormat.R16G16B16A16_SFloat.ToString(), name + " camera, depth or format.");

            foreach (Color pixel in cpu.GetPixels())
            {
                if (IsNeutral(pixel)) { sample.neutral++; continue; }
                Require(expectedOwner != 0 && Owner(pixel) == expectedOwner
                    && Mathf.Abs(pixel.b - Mathf.Round(pixel.b)) < .002f && Mathf.Abs(pixel.a - Mathf.Round(pixel.a)) < .002f
                    && pixel.r >= 0 && pixel.r <= 1 && pixel.g >= 0 && pixel.g <= 1,
                    name + " unexpected RGBA tuple " + pixel + ".");
                sample.accepted++;
                if (pixel.r < .08f) sample.dark++;
                else if (pixel.r > .92f) sample.lit++;
                else sample.partial++;
                if (pixel.g > .02f && pixel.g < .98f) sample.border++;
            }
            Require(!expectedProduct || (sample.accepted > 50 && sample.neutral > 50), name + " product coverage.");

            Color At(Vector3 world)
            {
                Vector3 uv = camera.WorldToViewportPoint(world);
                return cpu.GetPixel(Mathf.FloorToInt(uv.x * width), Mathf.FloorToInt(uv.y * height));
            }
        }
        finally
        {
            probe.sample = null;
            RenderTexture.active = previous;
            Object.DestroyImmediate(cpu);
            Object.DestroyImmediate(target);
        }

        Debug.Log($"P1c {name} product={sample.characterProduct} owner={sample.expectedOwner} accepted={sample.accepted} dark={sample.dark} lit={sample.lit} partial={sample.partial} border={sample.border}");
        return sample;
    }

    static int Owner(Color pixel) => (Mathf.RoundToInt(pixel.b) << 8) | Mathf.RoundToInt(pixel.a);

    static bool IsNeutral(Color pixel) => Mathf.Abs(pixel.r - 1) < .002f
        && Mathf.Abs(pixel.g) < .002f && Mathf.Abs(pixel.b) < .002f && Mathf.Abs(pixel.a) < .002f;

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    sealed class ProbeFeature : ScriptableRendererFeature
    {
        public Sample sample;
        Material material;

        public override void Create()
        {
            CoreUtils.Destroy(material);
            material = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/HoShadows/CharacterResolveReadback"));
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (sample != null) renderer.EnqueuePass(new ProbePass(sample, material));
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);
    }

    sealed class ProbePass : ScriptableRenderPass
    {
        static readonly int Product = Shader.PropertyToID("_HoCharacterResolveProbeTexture");
        static readonly int Valid = Shader.PropertyToID("_HoCharacterResolveProbeValid");
        readonly Sample sample;
        readonly Material material;

        sealed class PassData
        {
            public Material material;
            public TextureHandle product;
        }

        public ProbePass(Sample sample, Material material)
        {
            this.sample = sample;
            this.material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var product = frameData.GetOrCreate<HoNativeMainShadowFrameData>();
            var resources = frameData.Get<UniversalResourceData>();
            sample.cameraId = product.CameraInstanceId;
            sample.surfaceDepth = product.SurfaceDepth.IsValid();
            sample.characterProduct = product.CharacterVisibility.IsValid();
            using var builder = graph.AddRasterRenderPass<PassData>("Ho P1c character product readback", out var data);
            data.material = material;
            data.product = product.CharacterVisibility;
            if (sample.characterProduct)
            {
                sample.format = data.product.GetDescriptor(graph).colorFormat.ToString();
                builder.UseTexture(data.product, AccessFlags.Read);
            }
            builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.WriteAll);
            builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
            {
                var properties = context.renderGraphPool.GetTempMaterialPropertyBlock();
                bool valid = pass.product.IsValid();
                properties.SetFloat(Valid, valid ? 1 : 0);
                if (valid) properties.SetTexture(Product, (Texture)pass.product);
                CoreUtils.DrawFullScreen(context.cmd, pass.material, properties);
            });
        }
    }
}
