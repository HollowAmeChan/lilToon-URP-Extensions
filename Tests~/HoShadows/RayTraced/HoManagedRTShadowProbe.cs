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
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class HoManagedRTShadowProbe
{
    const int Size = 64;
    const int CameraOnlyLayer = 27;
    const BindingFlags InstanceInternal = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags StaticInternal = BindingFlags.Static | BindingFlags.NonPublic;

    [Serializable]
    sealed class LocalWitness
    {
        public int shaderIndex = -1;
        public int p2Slot = -1;
        public int raySlot;
        public Color raw;
        public Color candidate;
    }

    [Serializable]
    sealed class Case
    {
        public string name;
        public int hostViews;
        public int hostRequests;
        public bool unifiedOwnsRequest;
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
        public float rawVisibility;
        public float foreignRawStatus;
        public float foreignRawOwner;
        public int candidateActive;
        public int mainCandidateSlot;
        public Color candidateCenter;
        public Color foreignCandidate;
        public LocalWitness point = new();
        public LocalWitness spot = new();
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
        RTHandle candidateTarget = null;
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
            var candidateProbe = Keep(ScriptableObject.CreateInstance<CandidateProbeFeature>());
            var productProbe = Keep(ScriptableObject.CreateInstance<ProductProbeFeature>());
            geometry.Settings.layerMask = ~(1 << CameraOnlyLayer);
            surface.Settings.enableSemanticLanes = false;
            surface.Settings.enableCorrelatedSemantics = false;
            data.renderingMode = RenderingMode.Forward;
            data.rendererFeatures.Add(host);
            data.rendererFeatures.Add(companion);
            data.rendererFeatures.Add(geometry);
            data.rendererFeatures.Add(surface);
            data.rendererFeatures.Add(shadows);
            data.rendererFeatures.Add(candidateProbe);
            data.rendererFeatures.Add(productProbe);
            candidateTarget = RTHandles.Alloc(new RenderTextureDescriptor(Size, Size)
            {
                graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat,
                depthBufferBits = 0, msaaSamples = 1
            }, FilterMode.Point, TextureWrapMode.Clamp, name: "Ho RT candidate readback");
            candidateProbe.Target = candidateTarget;

            var pipeline = Keep(UniversalRenderPipelineAsset.Create(data));
            pipeline.msaaSampleCount = 1;
            pipeline.shadowDistance = 16;
            var serialized = new SerializedObject(pipeline);
            serialized.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
            serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
            serialized.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = 1024;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;

            var camera = Keep(new GameObject("Managed Shadow camera")).AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 1.5f;
            camera.aspect = 1;
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
            var firstPart = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)).GetComponent<Renderer>();
            firstPart.transform.position = new Vector3(20, 0, 0);
            firstPart.sharedMaterial = receiverMaterial;
            firstPart.shadowCastingMode = ShadowCastingMode.Off;
            group.parts.Add(new HoObjectBufferPartEntry { name = "outside-view", renderers = new Object[] { firstPart } });
            group.parts.Add(new HoObjectBufferPartEntry { name = "surface", renderers = new Object[] { receiverMesh } });
            group.Apply();
            uint owner = HoObjectBufferRegistry.GetPartId(group.groupId, "surface");
            Require(owner == 6145, "Receiver must exercise an exact 16-bit owner above the half precision integer limit.");

            var otherMesh = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)).GetComponent<Renderer>();
            otherMesh.transform.position = new Vector3(.9f, 0, -.08f);
            otherMesh.transform.localScale = new Vector3(.42f, .6f, 1);
            otherMesh.sharedMaterial = receiverMaterial;
            otherMesh.shadowCastingMode = ShadowCastingMode.Off;
            var otherGroup = Keep(new GameObject("Unregistered RT receiver")).AddComponent<HoObjectBufferGroup>();
            otherGroup.groupId = 25;
            otherGroup.parts.Add(new HoObjectBufferPartEntry { name = "other", renderers = new Object[] { otherMesh } });
            otherGroup.Apply();
            uint otherOwner = HoObjectBufferRegistry.GetPartId(otherGroup.groupId, "other");
            Require(otherOwner != 0 && otherOwner != owner, "Other SurfaceBuffer writer must have a different owner.");
            int foreignX = Mathf.FloorToInt((.5f + .9f / (2 * camera.orthographicSize)) * Size);

            var front = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)).GetComponent<Renderer>();
            front.gameObject.layer = CameraOnlyLayer;
            front.transform.position = new Vector3(0, 0, -.45f);
            front.transform.localScale = new Vector3(.6f, .6f, 1);
            front.sharedMaterial = casterMaterial;
            front.shadowCastingMode = ShadowCastingMode.Off;
            front.gameObject.SetActive(false);

            Case Render(string name, bool expectProducer, bool expectOutput, bool expectNoReceivers = false,
                bool expectDepthRejection = false, bool expectLocal = false)
            {
                var sample = new Case { name = name };
                report.cases.Add(sample);
                productProbe.Current = sample;
                candidateProbe.Current = sample;
                var request = new RenderPipeline.StandardRequest { destination = target };
                Require(RenderPipeline.SupportsRenderRequest(camera, request), "StandardRequest unsupported.");
                RenderPipeline.SubmitRenderRequest(camera, request);
                productProbe.Current = null;
                candidateProbe.Current = null;
                object producer = typeof(HoShadowsRendererFeature).GetField("rayProducer", InstanceInternal).GetValue(shadows);
                Type producerType = producer?.GetType();
                sample.hostViews = (int)typeof(HoRTRendererFeature).GetProperty("ViewCount", InstanceInternal).GetValue(host);
                sample.hostRequests = HostRequestCount(host, camera, shadows, out sample.unifiedOwnsRequest);
                sample.companionAcquired = companion.Acquired;
                sample.companionLifetime = companion.Lifetime;
                sample.hasProducer = producer != null;
                sample.outputs = producer == null ? 0 : (int)producerType.GetProperty("ActiveOutputCount", InstanceInternal).GetValue(producer);
                sample.scheduled = producer == null ? 0 : (int)producerType.GetProperty("ScheduledLightCount", InstanceInternal).GetValue(producer);
                sample.legacyAlias = Shader.GetGlobalFloat("_HoRTShadowActive");
                Color header = ReadPixel(candidateTarget.rt, 0, 0);
                sample.candidateActive = Mathf.RoundToInt(header.r);
                sample.mainCandidateSlot = Mathf.RoundToInt(header.g);
                sample.candidateCenter = ReadPixel(candidateTarget.rt, Size / 2, Size / 2);
                sample.foreignCandidate = ReadPixel(candidateTarget.rt, foreignX, Size / 2);
                sample.point.raySlot = Mathf.RoundToInt(header.b);
                sample.spot.raySlot = Mathf.RoundToInt(header.a);
                sample.point.candidate = ReadPixel(candidateTarget.rt, 1, 0);
                sample.spot.candidate = ReadPixel(candidateTarget.rt, 2, 0);
                int expectedClients = expectOutput ? 2 : 1;
                Require(sample.companionAcquired && sample.companionLifetime != 0 && sample.hostViews == expectedClients
                    && sample.hostRequests == expectedClients, name + " companion/Shadow host requests and views.");
                Require(!(producer is Object) && sample.unifiedOwnsRequest == expectOutput,
                    name + " plain RT producer must key the host request by its unified renderer feature.");
                int expectedOutputs = expectLocal ? 3 : expectOutput ? 1 : 0;
                Require(sample.hasProducer == expectProducer && sample.outputs == expectedOutputs
                    && sample.scheduled == expectedOutputs, name + " outputs and scheduling.");
                Require(sample.available == expectedOutputs
                    && sample.withoutReceivers == (expectNoReceivers ? 1 : 0), name + " available and rejected products.");
                Require(sample.legacyAlias == 0, name + " managed producer must not publish the legacy main alias.");
                Require(sample.candidateActive == (expectOutput ? 1 : 0) && (!expectOutput || sample.mainCandidateSlot == 1),
                    name + " globals must select the current main candidate only when present.");
                Require(Neutral(sample.foreignCandidate), name + " nonreceiver pixel must remain neutral.");

                if (expectOutput)
                {
                    var result = producerType.GetProperty("LastOutput", InstanceInternal).GetValue(producer);
                    Type type = result.GetType();
                    long submitted = (long)type.GetField("producedSerial", InstanceInternal).GetValue(result);
                    long serial = (long)type.GetField("renderSerial", InstanceInternal).GetValue(result);
                    sample.submitted = submitted != 0 && submitted == serial;
                    var raw = (RTHandle)type.GetField("raw", InstanceInternal).GetValue(result);
                    Color pixel = ReadPixel(raw.rt, Size / 2, Size / 2);
                    Color foreignPixel = ReadPixel(raw.rt, foreignX, Size / 2);
                    sample.pixelStatus = pixel.b;
                    sample.pixelOwner = pixel.a;
                    sample.rawVisibility = pixel.r;
                    sample.foreignRawStatus = foreignPixel.b;
                    sample.foreignRawOwner = foreignPixel.a;
                    Require(sample.submitted && sample.exactDepth && Mathf.Abs(sample.capturedStrength - .63f) < .0001f,
                        name + " real dispatch, SourceDepth, and LightShadowStrength.");
                    Require(pixel.b == 1 && pixel.a == owner, name + $" raw center must be a valid receiver, got {pixel}.");
                    Require(foreignPixel.b == 0 && foreignPixel.a == 0, name + " raw nonreceiver must be unrequested.");
                    if (expectDepthRejection)
                        Require(Neutral(sample.candidateCenter), name + " different camera depth must reject a valid Raw pixel.");
                    else
                    {
                        float expectedCast = Mathf.Lerp(1, pixel.r, sample.capturedStrength);
                        Require(pixel.r == 0 && sample.candidateCenter.g == 1 && sample.candidateCenter.b == (owner >> 8)
                            && sample.candidateCenter.a == (owner & 255)
                            && Mathf.Abs(sample.candidateCenter.r - expectedCast) < .001f,
                            name + $" candidate must retain owner {owner} and apply strength once, got {sample.candidateCenter}.");
                    }
                }
                else Require(Neutral(sample.candidateCenter), name + " unavailable RT must read neutral even if a slot was bound previously.");

                if (expectLocal)
                {
                    Require(sample.point.p2Slot >= 0 && sample.spot.p2Slot >= 0 && sample.point.p2Slot != sample.spot.p2Slot
                        && sample.point.shaderIndex != sample.spot.shaderIndex && sample.point.raySlot > 1 && sample.spot.raySlot > 1
                        && sample.point.raySlot != sample.spot.raySlot, name + " independent P2 and RT slot pairs.");
                    VerifyLocal(sample.point, producer, candidateProbe.Point, owner, 1);
                    VerifyLocal(sample.spot, producer, candidateProbe.Spot, owner, 0);
                }
                else if (!expectOutput)
                    Require(Neutral(sample.point.candidate) && Neutral(sample.spot.candidate), name + " inactive local slots must be neutral.");

                Debug.Log($"P4a/P4b {name} hostRequests={sample.hostRequests} hostViews={sample.hostViews} companion={sample.companionLifetime} " +
                    $"outputs={sample.outputs} products={sample.available} missingReceivers={sample.withoutReceivers} submitted={sample.submitted} " +
                    $"slot={sample.mainCandidateSlot} active={sample.candidateActive} center={sample.candidateCenter} rawStatus={sample.pixelStatus}");
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
            front.gameObject.SetActive(true);
            Render("foreground-camera-depth-rejects-valid-raw", true, true, expectDepthRejection: true);
            front.gameObject.SetActive(false);
            overrideVolume.enable.Override(false);
            Render("volume-off-with-master-on", true, false);
            overrideVolume.enable.Override(true);
            Render("managed-opt-in-restored", true, true);
            var point = Keep(new GameObject("Independent RT point")).AddComponent<Light>();
            ConfigureLocalLight(point, LightType.Point, new Vector3(1.05f, 0, -3), .42f);
            var spot = Keep(new GameObject("Independent RT spot")).AddComponent<Light>();
            ConfigureLocalLight(spot, LightType.Spot, new Vector3(0, 0, -3), .78f);
            spot.spotAngle = 98;
            candidateProbe.Point = point;
            candidateProbe.Spot = spot;
            shadows.RayTracedShadowSettings.lightSelection = HoRTShadowLightSelection.AllSupported;
            Render("main-point-spot-native-source-indices", true, true, expectLocal: true);
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
            candidateTarget?.Release();
            for (int i = owned.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(owned[i]);
        }
    }

    static void ConfigureLocalLight(Light light, LightType type, Vector3 position, float strength)
    {
        light.type = type;
        light.transform.position = position;
        light.transform.rotation = Quaternion.LookRotation(Vector3.forward);
        light.range = 8;
        light.intensity = 3;
        light.shadows = LightShadows.Hard;
        light.shadowStrength = strength;
    }

    static void VerifyLocal(LocalWitness result, object producer, Light light, uint owner, float visibility)
    {
        object output = producer.GetType().GetMethod("OutputForLight", InstanceInternal).Invoke(producer, new object[] { light });
        Require(output != null, light.name + " must have a per-light RT output.");
        var texture = (RTHandle)output.GetType().GetField("raw", InstanceInternal).GetValue(output);
        result.raw = ReadPixel(texture.rt, Size / 2, Size / 2);
        Require(result.raw.b == 1 && result.raw.a == owner && result.raw.r == visibility, light.name + " independent Raw visibility and owner.");
        Require(result.candidate.g == 1 && result.candidate.b == (owner >> 8) && result.candidate.a == (owner & 255)
            && Mathf.Abs(result.candidate.r - Mathf.Lerp(1, result.raw.r, light.shadowStrength)) < .001f,
            light.name + " candidate chosen by P2 shader index must preserve the independently traced result.");
    }

    static bool Neutral(Color value) => value.r == 1 && value.g == 0 && value.b == 0 && value.a == 0;

    static Color ReadPixel(RenderTexture source, int x, int y)
    {
        RenderTexture previous = RenderTexture.active;
        var image = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        try
        {
            RenderTexture.active = source;
            image.ReadPixels(new Rect(x, y, 1, 1), 0, 0);
            return image.GetPixel(0, 0);
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(image);
        }
    }

    static int HostRequestCount(HoRTRendererFeature host, Camera camera, Object unified, out bool unifiedOwnsRequest)
    {
        unifiedOwnsRequest = false;
        var domains = (IDictionary)typeof(HoRTRendererFeature).GetField("domains", InstanceInternal).GetValue(host);
        foreach (object domain in domains.Values)
        {
            Type type = domain.GetType();
            if ((Camera)type.GetField("camera", InstanceInternal).GetValue(domain) == camera)
            {
                var requests = (IDictionary)type.GetField("requests", InstanceInternal).GetValue(domain);
                unifiedOwnsRequest = requests.Contains(unified);
                return requests.Count;
            }
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

    sealed class CandidateProbeFeature : ScriptableRendererFeature
    {
        public RTHandle Target;
        public Case Current;
        public Light Point, Spot;
        Material material;
        ProbePass pass;

        public override void Create()
        {
            CoreUtils.Destroy(material);
            Shader shader = Shader.Find("Hidden/HoShadows/RayTracedCandidateReadback");
            Require(shader != null, "Ray-traced candidate readback shader missing.");
            material = CoreUtils.CreateEngineMaterial(shader);
            pass = new ProbePass(this, material);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if (Target != null && Current != null) renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);

        sealed class ProbePass : ScriptableRenderPass
        {
            static readonly int Visibility = Shader.PropertyToID("_HoShadowsRayTracedVisibility");
            static readonly int LocalIndices = Shader.PropertyToID("_HoProbeLocalLightIndices");
            static readonly Type ProductType = typeof(HoShadowsRendererFeature).Assembly.GetType(
                "lilToon.URP.Extensions.Shadows.HoRayTracedScreenShadowFrameData", true);
            static readonly MethodInfo HasProduct = typeof(ContextContainer).GetMethod("Contains").MakeGenericMethod(ProductType);
            static readonly MethodInfo GetProduct = typeof(ContextContainer).GetMethod("Get").MakeGenericMethod(ProductType);
            static readonly PropertyInfo ProductVisibility = ProductType.GetProperty("Visibility", InstanceInternal);
            readonly CandidateProbeFeature owner;
            readonly Material material;

            sealed class PassData
            {
                public Material material;
                public Vector4 localIndices;
            }

            public ProbePass(CandidateProbeFeature owner, Material material)
            {
                this.owner = owner;
                this.material = material;
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                using var builder = graph.AddRasterRenderPass<PassData>("Ho RT candidate probe after binding", out var data);
                if ((bool)HasProduct.Invoke(frameData, null))
                {
                    object product = GetProduct.Invoke(frameData, null);
                    if (((TextureHandle)ProductVisibility.GetValue(product)).IsValid())
                        builder.UseGlobalTexture(Visibility);
                }
                data.material = material;
                ResolveLocal(frameData, owner.Point, owner.Current.point);
                ResolveLocal(frameData, owner.Spot, owner.Current.spot);
                data.localIndices = new Vector4(owner.Current.point.shaderIndex, owner.Current.spot.shaderIndex, 0, 0);
                builder.SetRenderAttachment(graph.ImportTexture(owner.Target), 0, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
                {
                    var properties = context.renderGraphPool.GetTempMaterialPropertyBlock();
                    properties.SetVector(LocalIndices, pass.localIndices);
                    CoreUtils.DrawFullScreen(context.cmd, pass.material, properties);
                });
            }

            static void ResolveLocal(ContextContainer frameData, Light light, LocalWitness result)
            {
                if (light == null || !frameData.Contains<AdditionalLightShadowAtlasData>()
                    || !frameData.Contains<HoAdditionalShadowFrameData>())
                    return;

                var native = frameData.Get<AdditionalLightShadowAtlasData>();
                var additional = frameData.Get<HoAdditionalShadowFrameData>();
                var lights = frameData.Get<UniversalLightData>().visibleLights;
                for (int slot = 0; slot < additional.Count; slot++)
                {
                    int shaderIndex = additional.GetShaderLightIndex(slot);
                    if (!native.TryGetByShaderLightIndex(shaderIndex, out var entry) || lights[entry.VisibleLightIndex].light != light)
                        continue;
                    if (additional.TryGetSlice(shaderIndex, out int resolvedSlot) && resolvedSlot == slot)
                    {
                        result.shaderIndex = shaderIndex;
                        result.p2Slot = slot;
                    }
                    return;
                }
            }
        }
    }
}
