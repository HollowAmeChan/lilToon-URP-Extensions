using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTShadow;
using lilToon.URP.Extensions.Shadows;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class HoManagedRTShadowProbe
{
    const int Size = 64;
    const BindingFlags InstanceInternal = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags StaticInternal = BindingFlags.Static | BindingFlags.NonPublic;

    [Serializable]
    sealed class Case
    {
        public string name;
        public int hostViews;
        public int hostRequests;
        public long companionLifetime;
        public bool companionAcquired;
        public bool hasProducer;
        public int outputs;
        public int scheduled;
        public int available;
        public int withoutReceivers;
        public bool exactDepth;
        public float capturedStrength;
        public bool submitted;
        public float pixelStatus;
        public float pixelOwner;
        public float legacyAlias;
    }

    [Serializable]
    sealed class Report
    {
        public string unity;
        public string graphics;
        public string device;
        public bool passed;
        public string error;
        public List<Case> cases = new();
    }

    public static void Run()
    {
        var folder = Environment.GetEnvironmentVariable("HO_SHADOWS_MANAGED_RT_OUTPUT");
        Require(Directory.Exists(folder), "Create an isolated output directory and set HO_SHADOWS_MANAGED_RT_OUTPUT.");
        string output = Path.Combine(folder, "managed-rt-shadow-report.json");
        Require(!File.Exists(output), "The output directory already has a report.");
        var report = new Report { unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString(), device = SystemInfo.graphicsDeviceName };
        try
        {
            Require(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 && SystemInfo.supportsRayTracing
                && SystemInfo.supportsInlineRayTracing && SystemInfo.supportsGraphicsFence, "D3D12 native and inline RT with a graphics fence required.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Verify(report);
            report.passed = true;
        }
        catch (Exception error)
        {
            report.error = error.ToString();
            throw;
        }
        finally { File.WriteAllText(output, JsonUtility.ToJson(report, true)); }
        Debug.Log("HO_SHADOWS_P4A_MANAGED_OK");
    }

    static void Verify(Report report)
    {
        var oldGraphics = GraphicsSettings.defaultRenderPipeline;
        var oldQuality = QualitySettings.renderPipeline;
        var oldSun = RenderSettings.sun;
        bool oldAsync = ShaderUtil.allowAsyncCompilation;
        var owned = new List<Object>();
        T Keep<T>(T item) where T : Object { owned.Add(item); return item; }
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            var data = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
            var host = Keep(ScriptableObject.CreateInstance<HoRTRendererFeature>());
            var companion = Keep(ScriptableObject.CreateInstance<CompanionFeature>());
            var geometry = Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());
            var surface = Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());
            var shadows = Keep(ScriptableObject.CreateInstance<HoShadowsRendererFeature>());
            var productProbe = Keep(ScriptableObject.CreateInstance<ProductProbeFeature>());
            surface.Settings.enableSemanticLanes = false;
            surface.Settings.enableCorrelatedSemantics = false;
            data.renderingMode = RenderingMode.Forward;
            data.rendererFeatures.Add(host);
            data.rendererFeatures.Add(companion);
            data.rendererFeatures.Add(geometry);
            data.rendererFeatures.Add(surface);
            data.rendererFeatures.Add(shadows);
            data.rendererFeatures.Add(productProbe);

            var pipeline = Keep(UniversalRenderPipelineAsset.Create(data));
            pipeline.msaaSampleCount = 1;
            pipeline.shadowDistance = 16;
            var serialized = new SerializedObject(pipeline);
            serialized.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;

            var camera = Keep(new GameObject("Managed Shadow camera")).AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 1.5f;
            camera.transform.position = new Vector3(0, 0, -4);
            camera.nearClipPlane = .2f;
            camera.farClipPlane = 14;
            camera.allowMSAA = false;
            camera.GetUniversalAdditionalCameraData().volumeLayerMask = 1;
            var target = Keep(new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
            target.Create();

            var volume = Keep(new GameObject("Managed Shadow Volume")).AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = Keep(ScriptableObject.CreateInstance<VolumeProfile>());
            volume.sharedProfile = profile;
            profile.Add<HoSurfaceBufferVolume>(true);
            var overrideVolume = profile.Add<HoRTShadowVolume>(true);
            overrideVolume.active = false;

            var sun = Keep(new GameObject("Managed Shadow sunlight")).AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = .63f;
            RenderSettings.sun = sun;
            productProbe.LightId = HoRTLightIdentity.Get(sun);

            Shader receiverShader = Shader.Find("Ho/Environment/URP/Lit");
            Shader casterShader = Shader.Find("Universal Render Pipeline/Lit");
            Require(receiverShader != null && casterShader != null, "Environment and URP Lit shader imports required.");
            var receiverMaterial = Keep(new Material(receiverShader));
            var casterMaterial = Keep(new Material(casterShader));
            var receiverMesh = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)).GetComponent<Renderer>();
            receiverMesh.transform.localScale = new Vector3(2.5f, 2.5f, 1);
            receiverMesh.sharedMaterial = receiverMaterial;
            receiverMesh.shadowCastingMode = ShadowCastingMode.Off;
            var caster = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube)).GetComponent<Renderer>();
            caster.transform.position = new Vector3(0, 0, -1.2f);
            caster.transform.localScale = new Vector3(.6f, .6f, .3f);
            caster.sharedMaterial = casterMaterial;
            caster.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            var group = Keep(new GameObject("Managed Shadow receiver identity")).AddComponent<HoObjectBufferGroup>();
            group.groupId = 24;
            group.parts.Add(new HoObjectBufferPartEntry { name = "surface", renderers = new Object[] { receiverMesh } });
            group.Apply();
            uint owner = HoObjectBufferRegistry.GetPartId(group.groupId, "surface");
            Require(owner != 0, "Receiver owner was not allocated.");

            Case Render(string name, bool expectProducer, bool expectOutput, bool expectNoReceivers = false)
            {
                var sample = new Case { name = name };
                report.cases.Add(sample);
                productProbe.Current = sample;
                var request = new RenderPipeline.StandardRequest { destination = target };
                Require(RenderPipeline.SupportsRenderRequest(camera, request), "StandardRequest unsupported.");
                RenderPipeline.SubmitRenderRequest(camera, request);
                productProbe.Current = null;
                var producer = (HoRTShadowRendererFeature)typeof(HoShadowsRendererFeature)
                    .GetField("rayProducer", InstanceInternal).GetValue(shadows);
                sample.hostViews = (int)typeof(HoRTRendererFeature).GetProperty("ViewCount", InstanceInternal).GetValue(host);
                sample.hostRequests = HostRequestCount(host, camera);
                sample.companionAcquired = companion.Acquired;
                sample.companionLifetime = companion.Lifetime;
                sample.hasProducer = producer != null;
                sample.outputs = producer == null ? 0 : (int)typeof(HoRTShadowRendererFeature).GetProperty("ActiveOutputCount", InstanceInternal).GetValue(producer);
                sample.scheduled = producer == null ? 0 : (int)typeof(HoRTShadowRendererFeature).GetProperty("ScheduledLightCount", InstanceInternal).GetValue(producer);
                sample.legacyAlias = Shader.GetGlobalFloat("_HoRTShadowActive");
                int expectedClients = expectOutput ? 2 : 1;
                Require(sample.companionAcquired && sample.companionLifetime != 0 && sample.hostViews == expectedClients
                    && sample.hostRequests == expectedClients, name + " companion/Shadow host requests and views.");
                Require(sample.hasProducer == expectProducer && sample.outputs == (expectOutput ? 1 : 0)
                    && sample.scheduled == (expectOutput ? 1 : 0), name + " outputs and scheduling.");
                Require(sample.available == (expectOutput ? 1 : 0)
                    && sample.withoutReceivers == (expectNoReceivers ? 1 : 0), name + " available and rejected products.");
                Require(sample.legacyAlias == 0, name + " managed producer must not publish the legacy main alias.");

                if (expectOutput)
                {
                    var result = typeof(HoRTShadowRendererFeature).GetProperty("LastOutput", InstanceInternal).GetValue(producer);
                    Type type = result.GetType();
                    long submitted = (long)type.GetField("producedSerial", InstanceInternal).GetValue(result);
                    long serial = (long)type.GetField("renderSerial", InstanceInternal).GetValue(result);
                    sample.submitted = submitted != 0 && submitted == serial;
                    var raw = (RTHandle)type.GetField("raw", InstanceInternal).GetValue(result);
                    Color pixel = ReadCenter(raw.rt);
                    sample.pixelStatus = pixel.b;
                    sample.pixelOwner = pixel.a;
                    Require(sample.submitted && sample.exactDepth && Mathf.Abs(sample.capturedStrength - .63f) < .0001f,
                        name + " real dispatch, SourceDepth, and LightShadowStrength.");
                    Require(pixel.b == 1 && pixel.a == owner, name + $" raw center must be a valid receiver, got {pixel}.");
                }

                Debug.Log($"P4a {name} hostRequests={sample.hostRequests} hostViews={sample.hostViews} companion={sample.companionLifetime} outputs={sample.outputs} products={sample.available} missingReceivers={sample.withoutReceivers} submitted={sample.submitted}");
                return sample;
            }

            Require(!shadows.RayTracedShadowsEnabled, "Unified renderer must start with RT Shadow off.");
            Case baseline = Render("default-off", false, false);
            overrideVolume.active = true;
            overrideVolume.enable.Override(true);
            Render("volume-on-with-master-off", false, false);
            shadows.RayTracedShadowsEnabled = true;
            Render("opt-in-without-receivers", true, false, true);
            var receiver = group.gameObject.AddComponent<HoRTShadowReceiver>();
            receiver.objectGroup = group;
            Render("managed-opt-in", true, true);
            overrideVolume.enable.Override(false);
            Render("volume-off-with-master-on", true, false);
            overrideVolume.enable.Override(true);
            Render("managed-opt-in-restored", true, true);
            shadows.RayTracedShadowsEnabled = false;
            Render("master-off-retires-shadow", false, false);
            foreach (Case sample in report.cases)
                Require(sample.companionLifetime == baseline.companionLifetime, sample.name + " companion view identity changed.");
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = oldGraphics;
            QualitySettings.renderPipeline = oldQuality;
            RenderSettings.sun = oldSun;
            ShaderUtil.allowAsyncCompilation = oldAsync;
            for (int i = owned.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(owned[i]);
        }
    }

    static Color ReadCenter(RenderTexture source)
    {
        RenderTexture previous = RenderTexture.active;
        var image = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        try
        {
            RenderTexture.active = source;
            image.ReadPixels(new Rect(Size / 2, Size / 2, 1, 1), 0, 0);
            return image.GetPixel(0, 0);
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(image);
        }
    }

    static int HostRequestCount(HoRTRendererFeature host, Camera camera)
    {
        var domains = (IDictionary)typeof(HoRTRendererFeature).GetField("domains", InstanceInternal).GetValue(host);
        foreach (object domain in domains.Values)
        {
            Type type = domain.GetType();
            if ((Camera)type.GetField("camera", InstanceInternal).GetValue(domain) == camera)
                return ((IDictionary)type.GetField("requests", InstanceInternal).GetValue(domain)).Count;
        }
        return 0;
    }

    static void Require(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message);
    }

    sealed class CompanionFeature : ScriptableRendererFeature
    {
        static readonly Type HostType = typeof(HoRTRendererFeature);
        static readonly MethodInfo Declare = HostType.GetMethod("Declare", StaticInternal);
        static readonly MethodInfo TryAcquire = HostType.GetMethod("TryAcquire", StaticInternal);
        object request;
        public bool Acquired;
        public long Lifetime;

        public override void Create()
        {
            Type requestType = HostType.Assembly.GetType("lilToon.URP.Extensions.RT.HoRTViewRequest", true);
            ConstructorInfo constructor = requestType.GetConstructors(InstanceInternal)[0];
            var parameters = constructor.GetParameters();
            var arguments = new object[parameters.Length];
            arguments[0] = -1;
            for (int i = 1; i < arguments.Length; i++)
                arguments[i] = parameters[i].DefaultValue;
            request = constructor.Invoke(arguments);
        }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData data)
        {
            Acquired = false;
            Lifetime = 0;
            Declare.Invoke(null, new object[] { renderer, data.camera, this, request, null });
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            var arguments = new object[] { renderer, data.cameraData.camera, this, null, null };
            Acquired = (bool)TryAcquire.Invoke(null, arguments);
            if (!Acquired) return;
            object scene = arguments[3].GetType().GetProperty("Scene", InstanceInternal).GetValue(arguments[3]);
            Lifetime = (long)scene.GetType().GetProperty("Lifetime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(scene);
        }
    }

    sealed class ProductProbeFeature : ScriptableRendererFeature
    {
        public long LightId;
        public Case Current;
        ProbePass pass;

        public override void Create() => pass = new ProbePass(this);

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if (Current != null) renderer.EnqueuePass(pass);
        }

        sealed class ProbePass : ScriptableRenderPass
        {
            readonly ProductProbeFeature owner;
            public ProbePass(ProductProbeFeature owner)
            {
                this.owner = owner;
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                if (!frameData.Contains<HoRTShadowRenderGraphResources>()) return;
                var resources = frameData.Get<HoRTShadowRenderGraphResources>();
                foreach (var product in resources.Products)
                {
                    if (product.Status == HoRTShadowProductStatus.Available) owner.Current.available++;
                    if (product.Status == HoRTShadowProductStatus.NoReceivers) owner.Current.withoutReceivers++;
                }
                if (resources.TryGet(owner.LightId, out var current) && current.Status == HoRTShadowProductStatus.Available)
                {
                    Type surfaceResource = typeof(HoSurfaceBufferRendererFeature).Assembly.GetType(
                        "lilToon.URP.Extensions.SurfaceBuffer.HoSurfaceBufferRenderGraphResources", true);
                    object surfaces = typeof(ContextContainer).GetMethod("Get").MakeGenericMethod(surfaceResource).Invoke(frameData, null);
                    var surfaceDepth = (TextureHandle)surfaceResource.GetField("depthTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(surfaces);
                    owner.Current.exactDepth = current.SourceDepth.IsValid() && current.SourceDepth.Equals(surfaceDepth);
                    owner.Current.capturedStrength = current.LightShadowStrength;
                }
            }
        }
    }
}
