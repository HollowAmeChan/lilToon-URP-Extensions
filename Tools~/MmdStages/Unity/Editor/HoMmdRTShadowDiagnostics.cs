using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTShadow;
using lilToon.URP.Extensions.Shadows;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Current-scene RT Cast proof; temporary settings are never saved.</summary>
public static class HoMmdRTShadowDiagnostics
{
    const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.NonPublic;
    private delegate bool TryReadProduct(Camera camera, Light light, out HoRTShadowCompatibilityProduct product);

    public static string ConfigureStageReceivers(HoMmdVideoWorkshop.Request request)
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != request.scene || EditorApplication.isPlaying)
            throw new InvalidOperationException("Configure only the current edit-mode scene.");
        bool hadUnsavedChanges = scene.isDirty;
        var roots = scene.GetRootGameObjects();
        var stage = roots.Single(x => x.GetComponentsInChildren<Volume>(true).Any(v => v.sharedProfile != null &&
            AssetDatabase.GetAssetPath(v.sharedProfile).StartsWith(request.folder.TrimEnd('/') + "/Rendering/", StringComparison.Ordinal)));
        var candidates = stage.GetComponentsInChildren<Renderer>(true).Where(r => (r is MeshRenderer || r is SkinnedMeshRenderer) &&
            r.shadowCastingMode != ShadowCastingMode.ShadowsOnly &&
            r.sharedMaterials.Any(m => m != null && m.shader.name.IndexOf("lilToon", StringComparison.OrdinalIgnoreCase) >= 0 &&
                m.renderQueue <= 2500 && m.HasProperty("_ShadowReceive"))).ToArray();
        if (candidates.Length == 0) throw new InvalidOperationException("No opaque Ho lilToon stage surfaces found.");
        var candidateSet = new HashSet<Renderer>(candidates);
        var assigned = new HashSet<Renderer>();
        var log = new List<string>();
        var oldGroups = roots.SelectMany(x => x.GetComponentsInChildren<HoObjectBufferGroup>(true)).ToArray();
        if (oldGroups.Length > 0) oldGroups[0].Apply();
        foreach (var existing in oldGroups.Where(x => x.isActiveAndEnabled))
        {
            var members = new List<Renderer>();
            existing.GetAssignedRenderers(members);
            if (!members.Any(candidateSet.Contains)) continue;
            foreach (var member in members.Where(candidateSet.Contains)) assigned.Add(member);
            var receiver = existing.GetComponent<HoRTShadowReceiver>();
            bool added = receiver == null;
            if (added) receiver = Undo.AddComponent<HoRTShadowReceiver>(existing.gameObject);
            if (receiver.objectGroup != null && receiver.objectGroup != existing)
                throw new InvalidOperationException("Existing receiver targets a different group: " + PathOf(existing.transform));
            Undo.RecordObject(receiver, "接入舞台 RTShadow 接收");
            receiver.objectGroup = existing;
            if (added) { receiver.receiveWholeGroup = true; receiver.receiveAllLights = true; }
            log.Add("Existing group: " + PathOf(existing.transform));
        }
        var missing = candidates.Where(x => !assigned.Contains(x)).ToArray();
        if (missing.Length > 0)
        {
            GameObject holder = stage;
            if (holder.GetComponent<HoObjectBufferGroup>() != null)
            {
                holder = new GameObject("RTShadow 场景接收");
                Undo.RegisterCreatedObjectUndo(holder, "接入舞台 RTShadow 接收");
                holder.transform.SetParent(stage.transform, false);
            }
            var group = Undo.AddComponent<HoObjectBufferGroup>(holder);
            group.parts = missing.Select((r, i) => new HoObjectBufferPartEntry {
                name = $"{i + 1:00} · {r.name}", renderers = new Object[] { r }
            }).ToList();
            group.Apply();
            var receiver = Undo.AddComponent<HoRTShadowReceiver>(holder);
            receiver.objectGroup = group;
            receiver.receiveWholeGroup = true;
            receiver.receiveAllLights = true;
            EditorUtility.SetDirty(group); EditorUtility.SetDirty(receiver);
            log.Add($"Added group {group.groupId}, parts={missing.Length}: " + PathOf(holder.transform));
        }
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = !hadUnsavedChanges && EditorSceneManager.SaveScene(scene);
        log.Add("Saved=" + saved + " (pre-existing unsaved edits=" + hadUnsavedChanges + ")");
        log.Add("Lighting, materials, filters and character hierarchy were not changed.");
        string output = Path.GetFullPath("Library/HoMmdVideo/Diagnostics/RTShadow-Stage-Receivers-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllLines(output, log);
        EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll();
        return output;
    }

    public static string Run(HoMmdVideoWorkshop.Request request)
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != request.scene) throw new InvalidOperationException("Only inspect the loaded scene.");
        string folder = string.IsNullOrEmpty(request.output) ? Path.GetFullPath(
            "Library/HoMmdVideo/Diagnostics/RTShadow-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")) : Path.GetFullPath(request.output);
        var roots = scene.GetRootGameObjects();
        var cameras = roots.SelectMany(x => x.GetComponentsInChildren<Camera>(true)).ToArray();
        var camera = request.cameraName == "$scene-view" ? SceneView.lastActiveSceneView?.camera :
            string.IsNullOrEmpty(request.cameraName) ? cameras.Single(x => x.CompareTag("MainCamera")) :
            cameras.Single(x => x.name == request.cameraName);
        if (camera == null) throw new InvalidOperationException("Requested camera is missing.");
        var data = camera.GetComponent<UniversalAdditionalCameraData>();
        var cameraRenderer = (camera.cameraType == CameraType.SceneView ? null : data?.scriptableRenderer)
            ?? UniversalRenderPipeline.asset?.scriptableRenderer;
        var cameraFeatures = cameraRenderer == null ? null : typeof(ScriptableRenderer)
            .GetProperty("rendererFeatures", InstanceMembers)?.GetValue(cameraRenderer) as List<ScriptableRendererFeature>;
        var features = cameraFeatures;
        if (features == null)
        {
            var renderer = string.IsNullOrEmpty(request.folder) ? null : HoMmdStageBuilder.ResolveRenderer(request.folder.TrimEnd('/'));
            if (renderer == null) renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(HoMmdSharedRenderAssets.RendererPath);
            features = renderer?.rendererFeatures;
            if (HoMmdSharedRenderAssets.ActiveHoShadows(renderer) != null)
                return WriteUnavailableReport(folder, scene, features, "managed", "The selected camera's active renderer could not be inspected.");
        }
        var unified = cameraFeatures?.OfType<HoShadowsRendererFeature>().FirstOrDefault(item => item.isActive);
        bool managed = unified != null;
        HoRTShadowRendererFeature feature = null;
        if (!managed)
        {
            var candidates = features?.OfType<HoRTShadowRendererFeature>().Where(item => item.isActive).Take(2).ToArray();
            if (candidates == null || candidates.Length != 1)
                return WriteUnavailableReport(folder, scene, features, "legacy", "Expected exactly one active legacy RT Shadow feature on the selected renderer.");
            feature = candidates[0];
        }
        else if (data != null && data.renderType != CameraRenderType.Base)
            return WriteUnavailableReport(folder, scene, features, "managed", "Managed RT capture requires a Base camera.");

        var host = features.OfType<HoRTRendererFeature>().FirstOrDefault(item => item.isActive);
        bool previousManagedRt = managed && unified.RayTracedShadowsEnabled;
        var previousTarget = camera.targetTexture;
        var previousAspect = camera.aspect;
        var previousAA = data != null ? data.antialiasing : AntialiasingMode.None;
        bool previousRenderShadows = data == null || data.renderShadows;
        bool previousAsync = ShaderUtil.allowAsyncCompilation;
        var copies = new List<ProfileCopy>();
        var frames = new List<Frame>();
        Directory.CreateDirectory(folder);
        var target = new RenderTexture(request.width, request.height, 24, RenderTextureFormat.ARGBHalf) {
            name = "Ho RTShadow proof", antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave
        };
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            if (!managed)
                foreach (var volume in roots.SelectMany(x => x.GetComponentsInChildren<Volume>(true))
                             .Where(x => x.isActiveAndEnabled && x.sharedProfile != null))
                    copies.Add(new ProfileCopy(volume));
            target.Create();
            camera.targetTexture = target;
            camera.aspect = request.width / (float)request.height;
            if (data != null && data.antialiasing == AntialiasingMode.TemporalAntiAliasing)
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var standardRequest = new RenderPipeline.StandardRequest { destination = target };
            if (managed && !RenderPipeline.SupportsRenderRequest(camera, standardRequest))
                return WriteUnavailableReport(folder, scene, features, "managed", "The pipeline does not support the camera capture request.");
            var modes = managed ? new[] { "authored", "rtshadow-off" }
                : new[] { "authored", "rtshadow-off", "consumer-status", "force-lit", "force-shadow" };
            foreach (string mode in modes)
            {
                if (managed) unified.RayTracedShadowsEnabled = previousManagedRt && mode == "authored";
                else foreach (var copy in copies) copy.Mode(mode);

                for (int warmup = 0; warmup < 3; ++warmup)
                    RenderPipeline.SubmitRenderRequest(camera, standardRequest);
                Frame frame;
                if (managed) frame = CaptureManagedFrame(standardRequest, mode, unified, host, cameraRenderer, camera);
                else
                {
                    RenderPipeline.SubmitRenderRequest(camera, standardRequest);
                    frame = DescribeFrame(mode, host, camera);
                    ReadLegacyFrame(frame, feature, camera, cameraFeatures != null);
                }
                frames.Add(frame);
                File.WriteAllText(Path.Combine(folder, mode + ".json"), JsonUtility.ToJson(frame, true));
                SavePng(target, Path.Combine(folder, mode + ".png"));
            }
            var receivers = roots.SelectMany(x => x.GetComponentsInChildren<HoRTShadowReceiver>(true)).ToArray();
            var report = new Report {
                scene = scene.path, hardwareRayTracing = SystemInfo.supportsRayTracing, device = SystemInfo.graphicsDeviceName,
                shadowBackend = managed ? "managed" : "legacy", features = DescribeFeatures(features),
                rendererSource = cameraFeatures != null ? "Selected camera runtime renderer" : "Stage renderer asset fallback",
                capturePath = managed ? "Camera stack image with Base camera RT diagnostics" : "Camera stack",
                consumerWitness = managed ? "Legacy consumer-status and force controls are unavailable in managed mode." : "Legacy consumer debug globals",
                perLightScreenVisibility = managed ? "Unavailable after RenderGraph execution. Product statistics are from the producer's Raw texture." : null,
                frames = frames.ToArray(),
                lights = roots.SelectMany(x => x.GetComponentsInChildren<Light>(true)).Select(x =>
                    $"{PathOf(x.transform)}: active={x.isActiveAndEnabled} type={x.type} intensity={x.intensity} " +
                    $"shadows={x.shadows} strength={x.shadowStrength} mask={x.cullingMask} layers={x.renderingLayerMask} " +
                    $"cookie={x.cookie != null} bake={x.lightmapBakeType}").ToArray(),
                receivers = receivers.Select(DescribeReceiver).ToArray(),
                rendererCasting = roots.SelectMany(x => x.GetComponentsInChildren<Renderer>(true))
                    .GroupBy(x => x.shadowCastingMode + " / active=" + (x.enabled && x.gameObject.activeInHierarchy))
                    .Select(x => x.Key + ": " + x.Count()).ToArray()
            };
            File.WriteAllText(Path.Combine(folder, "report.json"), JsonUtility.ToJson(report, true));
            return folder;
        }
        finally
        {
            if (managed) unified.RayTracedShadowsEnabled = previousManagedRt;
            camera.targetTexture = previousTarget; camera.aspect = previousAspect;
            if (data != null) data.antialiasing = previousAA;
            if (data != null) data.renderShadows = previousRenderShadows;
            ShaderUtil.allowAsyncCompilation = previousAsync;
            foreach (var copy in copies) copy.Dispose();
            target.Release(); Object.DestroyImmediate(target);
            VolumeManager.instance.ResetMainStack();
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }

    static Frame DescribeFrame(string mode, HoRTRendererFeature host, Camera camera) => new Frame
    {
        mode = mode, camera = camera.name, host = host != null ? host.Status : "Missing",
        ordinaryMainMaps = Shader.IsKeywordEnabled("_MAIN_LIGHT_SHADOWS") ||
            Shader.IsKeywordEnabled("_MAIN_LIGHT_SHADOWS_CASCADE") || Shader.IsKeywordEnabled("_MAIN_LIGHT_SHADOWS_SCREEN"),
        characterCast = Shader.GetGlobalFloat("_HoCSActive"), legacyCast = Shader.GetGlobalFloat("_HoShadowCastActive")
    };

    static Frame CaptureManagedFrame(RenderPipeline.StandardRequest request, string mode, HoShadowsRendererFeature feature,
        HoRTRendererFeature host, ScriptableRenderer renderer, Camera camera)
    {
        Frame frame = null;
        var readbacks = new List<(Product product, RenderTexture raw)>();
        void CameraRendered(ScriptableRenderContext _, Camera rendered)
        {
            if (rendered != camera || frame != null) return;
            frame = DescribeFrame(mode, host, camera);
            ReadManagedFrame(frame, feature, renderer, camera, readbacks);
        }

        // Each camera resets the producer's public observations. Take the Base snapshot before any overlay begins.
        // The producer's Raw texture is an imported RTHandle, so read it after the entire request has been submitted.
        RenderPipelineManager.endCameraRendering += CameraRendered;
        try { RenderPipeline.SubmitRenderRequest(camera, request); }
        finally { RenderPipelineManager.endCameraRendering -= CameraRendered; }
        foreach (var (product, raw) in readbacks)
        {
            if (raw != null && raw.IsCreated()) ReadRaw(product, raw);
            else product.rawReadback = "Source texture is no longer available after the render request.";
        }
        return frame ?? new Frame
        {
            mode = mode, camera = camera.name, featureActive = feature.isActive, managedRtEnabled = feature.RayTracedShadowsEnabled,
            configured = JsonUtility.ToJson(feature.RayTracedShadowSettings),
            status = "The render request did not complete the selected Base camera.",
            requestedLights = -1, hostRequests = -1, scheduledLights = -1, writtenProducts = -1, rayDispatches = -1,
            products = Array.Empty<Product>(), rejected = Array.Empty<string>()
        };
    }

    static void ReadManagedFrame(Frame frame, HoShadowsRendererFeature feature, ScriptableRenderer renderer, Camera camera,
        List<(Product product, RenderTexture raw)> readbacks)
    {
        frame.featureActive = feature.isActive;
        frame.managedRtEnabled = feature.RayTracedShadowsEnabled;
        frame.configured = JsonUtility.ToJson(feature.RayTracedShadowSettings);
        frame.products = Array.Empty<Product>();
        frame.rejected = Array.Empty<string>();
        if (!frame.managedRtEnabled || feature.RayTracedShadowSettings?.enabled != true)
        {
            frame.status = "Managed RT is disabled on Ho Shadows.";
            return;
        }
        if (!SystemInfo.supportsRayTracing || !SystemInfo.supportsInlineRayTracing ||
            SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
        {
            frame.status = "Managed RT requires D3D12 native and inline ray tracing.";
            return;
        }

        // The renderer owns the producer and the planner. Read their existing per-camera state here.
        var producerField = typeof(HoShadowsRendererFeature).GetField("rayProducer", InstanceMembers);
        if (producerField == null)
        {
            frame.status = "Managed RT producer diagnostics are unavailable in this package version.";
            frame.requestedLights = frame.hostRequests = frame.scheduledLights = frame.writtenProducts = frame.rayDispatches = -1;
            return;
        }
        object producer = producerField.GetValue(feature);
        if (producer == null)
        {
            frame.status = "Managed RT has no producer state for the captured camera.";
            return;
        }
        Type producerType = producer.GetType();
        var lastCamera = producerType.GetProperty("LastCamera")?.GetValue(producer) as Camera;
        if (lastCamera != camera)
        {
            frame.status = "The managed producer did not report the captured camera.";
            frame.requestedLights = frame.hostRequests = frame.scheduledLights = frame.writtenProducts = frame.rayDispatches = -1;
            return;
        }

        var lights = producerType.GetProperty("LightDiagnostics")?.GetValue(producer) as IEnumerable<HoRTShadowLightDiagnostics>;
        var rejected = producerType.GetProperty("UnavailableProducts")?.GetValue(producer) as IReadOnlyList<HoRTShadowProduct>;
        var tryRead = producerType.GetMethod("TryGetCompatibilityProduct");
        if (lights == null || rejected == null || tryRead == null)
        {
            frame.status = "Managed RT product diagnostics are unavailable in this package version.";
            frame.requestedLights = frame.hostRequests = frame.scheduledLights = frame.writtenProducts = frame.rayDispatches = -1;
            return;
        }

        frame.featureCamera = lastCamera.name;
        frame.status = producerType.GetProperty("Status")?.GetValue(producer) as string;
        var planCamera = producerType.GetField("managedPlanCamera", InstanceMembers);
        var requested = producerType.GetField("managedRequestedCount", InstanceMembers);
        if (planCamera == null || requested == null) frame.requestedLights = -1;
        else if (planCamera.GetValue(producer) as Camera == camera)
        {
            frame.requestedLights = (int)requested.GetValue(producer);
            var effective = producerType.GetProperty("LastEffectiveSettings")?.GetValue(producer) as HoRTShadowSettings;
            if (effective != null)
            {
                frame.effective = JsonUtility.ToJson(effective);
                frame.effectiveSettingsAvailable = true;
            }
        }
        var requestOwner = producer as Object ?? feature;
        frame.hostRequests = ReadManagedHostRequests(renderer, camera, requestOwner);
        ReadProducts(frame, lights, rejected, (TryReadProduct)tryRead.CreateDelegate(typeof(TryReadProduct), producer), camera, readbacks);
    }

    static int ReadManagedHostRequests(ScriptableRenderer renderer, Camera camera, Object requestOwner)
    {
        var active = typeof(HoRTRendererFeature).GetField("active", BindingFlags.Static | BindingFlags.NonPublic);
        if (!(active?.GetValue(null) is IDictionary domains)) return -1;
        object domain = domains[(renderer, camera)];
        if (domain == null) return 0;
        var requests = domain.GetType().GetField("requests", InstanceMembers)?.GetValue(domain) as IDictionary;
        return requests == null ? -1 : requests.Contains(requestOwner) ? 1 : 0;
    }

    static void ReadLegacyFrame(Frame frame, HoRTShadowRendererFeature feature, Camera camera, bool rendererVerified)
    {
        frame.featureCamera = feature.LastCamera != null ? feature.LastCamera.name : "";
        frame.featureActive = feature.isActive;
        frame.configured = JsonUtility.ToJson(feature.Settings);
        frame.requestedLights = frame.hostRequests = -1;
        if (!rendererVerified || feature.LastCamera != camera)
        {
            frame.status = rendererVerified ? "The last legacy RT producer observation belongs to another camera or is unavailable."
                : "The stage renderer supplies the configured settings, but the camera's legacy RT producer could not be verified.";
            frame.scheduledLights = frame.writtenProducts = frame.rayDispatches = -1;
            frame.products = Array.Empty<Product>();
            frame.rejected = Array.Empty<string>();
            return;
        }
        frame.status = feature.Status;
        var effective = feature.LastEffectiveSettings;
        if (effective != null)
        {
            frame.effective = JsonUtility.ToJson(effective);
            frame.effectiveSettingsAvailable = true;
        }
        frame.activeGlobal = Shader.GetGlobalFloat("_HoRTShadowActive");
        frame.consumerDebugGlobal = Shader.GetGlobalFloat("_HoRTShadowConsumerDebugMode");
        ReadProducts(frame, feature.LightDiagnostics, feature.UnavailableProducts, feature.TryGetCompatibilityProduct, camera);
    }

    static void ReadProducts(Frame frame, IEnumerable<HoRTShadowLightDiagnostics> lights, IReadOnlyList<HoRTShadowProduct> rejected,
        TryReadProduct tryRead, Camera camera,
        List<(Product product, RenderTexture raw)> readbacks = null)
    {
        frame.products = lights.Select(light => ReadProduct(tryRead, camera, light, readbacks)).ToArray();
        frame.scheduledLights = frame.products.Length;
        frame.writtenProducts = frame.products.Count(product => product.currentProduct);
        frame.rayDispatches = frame.products.Sum(product => product.rayDispatches);
        frame.rejected = rejected.Select(product => (product.Light != null ? product.Light.name : "<null>") +
            ": " + product.Status + " - " + product.Reason).ToArray();
    }

    static string WriteUnavailableReport(string folder, Scene scene, IEnumerable<ScriptableRendererFeature> features, string backend, string status)
    {
        Directory.CreateDirectory(folder);
        var report = new Report
        {
            scene = scene.path, device = SystemInfo.graphicsDeviceName, hardwareRayTracing = SystemInfo.supportsRayTracing,
            shadowBackend = backend, status = status, features = DescribeFeatures(features), frames = Array.Empty<Frame>()
        };
        File.WriteAllText(Path.Combine(folder, "report.json"), JsonUtility.ToJson(report, true));
        Debug.Log(status);
        return folder;
    }

    static string[] DescribeFeatures(IEnumerable<ScriptableRendererFeature> features) => features == null ? Array.Empty<string>() :
        features.Select(x => x == null ? "<missing>" : x.GetType().Name + " active=" + x.isActive).ToArray();

    static Receiver DescribeReceiver(HoRTShadowReceiver receiver)
    {
        var group = receiver.objectGroup;
        var renderers = new List<Renderer>();
        if (group != null) group.GetAssignedRenderers(renderers);
        return new Receiver {
            name = PathOf(receiver.transform), enabled = receiver.isActiveAndEnabled,
            group = group != null ? PathOf(group.transform) : "<missing>",
            validGroup = group != null && (bool)typeof(HoObjectBufferRegistry).GetMethod("IsGroupValid",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { group }),
            groupId = group != null ? group.groupId : 0,
            wholeGroup = receiver.receiveWholeGroup, allLights = receiver.receiveAllLights,
            selectedParts = receiver.receiverParts.ToArray(),
            identities = group != null ? group.GetPartNames().Select(x => x + "=" +
                HoObjectBufferRegistry.GetPartId(group.groupId, x)).ToArray() : Array.Empty<string>(),
            materials = renderers.SelectMany(x => x.sharedMaterials.Where(m => m != null)
                .Select(m => $"{PathOf(x.transform)} / {m.name} / {m.shader.name} / " +
                    string.Join(" ", new[] { "_UseShadow", "_ShadowReceive", "_ShadowStrength", "_HoRTShadowReceive" }
                        .Where(m.HasProperty).Select(p => p + "=" + m.GetFloat(p)))))
                .Distinct().Take(100).ToArray()
        };
    }

    static Product ReadProduct(TryReadProduct tryRead, Camera camera, HoRTShadowLightDiagnostics light,
        List<(Product product, RenderTexture raw)> readbacks)
    {
        var result = new Product { light = light.Light != null ? light.Light.name : "<null>", slot = light.Slot,
            receivers = light.Receivers, width = light.Width, height = light.Height, reconstruction = light.Reconstruction.ToString() };
        if (!tryRead(camera, light.Light, out var product) || product.Raw?.rt == null) return result;
        result.currentProduct = true;
        // The producer records one DispatchRays per estimate before marking this serial complete.
        result.rayDispatches = light.Estimates;
        result.renderSerial = product.RenderSerial;
        if (readbacks == null) ReadRaw(result, product.Raw.rt);
        else readbacks.Add((result, product.Raw.rt));
        return result;
    }

    static void ReadRaw(Product result, RenderTexture texture)
    {
        var raw = Read(texture);
        result.statusCounts = Enumerable.Range(0, 5).Select(s => raw.Count(p => Mathf.RoundToInt(p.b) == s)).ToArray();
        var valid = raw.Where(p => Mathf.RoundToInt(p.b) == 1).ToArray();
        if (valid.Length > 0)
        {
            result.minimumVisibility = valid.Min(p => p.r);
            result.meanVisibility = valid.Average(p => p.r);
            result.shadowPixels = valid.Count(p => p.r < .99f);
            var distances = valid.Select(p => p.g).OrderBy(x => x).ToArray();
            result.blockerDistances = new[] { distances[0], distances[distances.Length / 2], distances[distances.Length - 1] };
            result.validOwners = valid.GroupBy(p => Mathf.RoundToInt(p.a)).OrderBy(x => x.Key)
                .Select(x => x.Key + ": " + x.Count()).ToArray();
        }
    }

    static string PathOf(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null) { transform = transform.parent; path = transform.name + "/" + path; }
        return path;
    }
    static Color[] Read(RenderTexture target)
    {
        var previous = RenderTexture.active;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); return image.GetPixels(); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }
    static void SavePng(RenderTexture target, string path)
    {
        var pixels = Read(target);
        bool gamma = QualitySettings.activeColorSpace == ColorSpace.Linear && !target.sRGB;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { image.SetPixels32(pixels.Select(p => (Color32)(gamma ? p.gamma : p)).ToArray()); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
        finally { Object.DestroyImmediate(image); }
    }
    sealed class ProfileCopy : IDisposable
    {
        readonly Volume volume;
        readonly VolumeProfile previous, source, copy;
        internal ProfileCopy(Volume volume)
        {
            this.volume = volume;
            previous = volume.HasInstantiatedProfile() ? volume.profile : null;
            source = previous != null ? previous : volume.sharedProfile;
            copy = ScriptableObject.CreateInstance<VolumeProfile>();
            copy.hideFlags = HideFlags.HideAndDontSave;
            foreach (var component in source.components.Where(x => x != null))
            {
                var cloned = Object.Instantiate(component); cloned.hideFlags = HideFlags.HideAndDontSave; copy.components.Add(cloned);
            }
            if (!copy.TryGet<HoRTShadowVolume>(out _)) copy.Add<HoRTShadowVolume>();
            volume.profile = copy;
        }
        internal void Mode(string mode)
        {
            copy.TryGet(out HoRTShadowVolume settings);
            if (source.TryGet(out HoRTShadowVolume original)) EditorUtility.CopySerialized(original, settings);
            if (mode == "rtshadow-off" || mode == "no-cast") settings.enable.Override(false);
            if (mode == "consumer-status" || mode == "force-lit" || mode == "force-shadow")
            {
                settings.active = true;
                settings.enable.Override(true);
                settings.debugMode.Override(mode == "consumer-status" ? HoRTShadowDebugMode.Consumption :
                    mode == "force-lit" ? HoRTShadowDebugMode.ForceLit : HoRTShadowDebugMode.ForceShadow);
                settings.debugInSceneView.Override(true);
                settings.debugInGameView.Override(true);
            }
            if (copy.TryGet(out lilToon.URP.Extensions.PostProcessing.ImageProcessStackVolume image) &&
                source.TryGet(out lilToon.URP.Extensions.PostProcessing.ImageProcessStackVolume originalImage))
            {
                image.Enable.value = originalImage.Enable.value;
                image.Enable.overrideState = originalImage.Enable.overrideState;
                if (mode == "consumer-status") image.Enable.Override(false);
            }
            if (copy.TryGet(out lilToon.URP.Extensions.PostProcessing.ScreenProcessStackVolume screen) &&
                source.TryGet(out lilToon.URP.Extensions.PostProcessing.ScreenProcessStackVolume originalScreen))
            {
                screen.Enable.value = originalScreen.Enable.value;
                screen.Enable.overrideState = originalScreen.Enable.overrideState;
                if (mode == "consumer-status") screen.Enable.Override(false);
            }
            copy.isDirty = true;
        }
        public void Dispose()
        {
            volume.profile = previous;
            foreach (var component in copy.components) Object.DestroyImmediate(component);
            Object.DestroyImmediate(copy);
        }
    }
    [Serializable] sealed class Report { public string scene, device, shadowBackend, status, rendererSource, capturePath, consumerWitness, perLightScreenVisibility; public bool hardwareRayTracing; public string[] features, lights, rendererCasting; public Frame[] frames; public Receiver[] receivers; }
    [Serializable] sealed class Frame { public string mode, camera, featureCamera, status, host, configured, effective; public bool featureActive, managedRtEnabled, effectiveSettingsAvailable, ordinaryMainMaps; public int requestedLights, hostRequests, scheduledLights, writtenProducts, rayDispatches; public float activeGlobal, consumerDebugGlobal, characterCast, legacyCast; public Product[] products; public string[] rejected; }
    [Serializable] sealed class Product { public string light, reconstruction, rawReadback; public int slot, width, height, receivers, shadowPixels, rayDispatches; public long renderSerial; public bool currentProduct; public int[] statusCounts; public float minimumVisibility, meanVisibility; public float[] blockerDistances; public string[] validOwners; }
    [Serializable] sealed class Receiver { public string name, group; public bool enabled, validGroup, wholeGroup, allLights; public int groupId; public string[] selectedParts, identities, materials; }
}
