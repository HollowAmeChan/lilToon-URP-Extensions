#pragma warning disable CS0618
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Profiling;
using Hollow.HoMmdStages;
using Hollow.HoUnityTools.MmdMotion;
using lilToon.URP.Extensions.ShadowCast;
using lilToon.URP.Extensions.RTGI;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;

// This one-off experiment may edit only its physically copied research project.
public static class HoLightingBakeExperiment
{
    const string Root = "Assets/LightingBakeAudit";
    const string Evidence = "D:/Unity_Fork/lilToon-URP-Extensions/Temp~/LightingBakeAudit-20261102";
    static string stage, mode, phase;
    static double started, nextProgress;
    static bool completed, apvStarted;
    static int benchFrame, lastTimingFrame = -1;
    static ulong lastFrameTimestamp;
    static readonly List<double> frameGpu = new();
    static readonly List<double> shadowMarkerMs = new(), rtgiMarkerMs = new();
    static readonly List<int> markerFrameIds = new();
    static RenderTexture benchTarget;
    static Camera benchCamera;
    static ProfilingSampler shadowSampler, rtgiSampler;
    static int lastMarkerFrame = -1;
    static long lastShadowGpuSampleCount, lastRtgiGpuSampleCount;
    static double lastShadowGpuElapsed, lastRtgiGpuElapsed;
    static bool previousProfilerEnabled, previousProfileEditor, previousGpuArea;
    [Serializable] sealed class PrepReport
    {
        public string stage, scene, mode;
        public int staticRenderers, excludedRenderers, generatedMeshes, probePositions, mixedLights, bakedLights;
        public long sourceVertices, unwrappedVertices;
        public string[] lights, motionPlayers, cameraPlayers;
        public float motionFrame;
        public int probePoseSamples;
        public Bounds staticBounds, apvBounds;
    }
    [Serializable] sealed class BakeReport
    {
        public string stage, mode, scene, lightingData, settings, error;
        public double seconds;
        public bool completed, apvData, standardBakeCompleted, apvBakeCompleted;
        public int lightmaps, legacyProbes, directSamples, indirectSamples, environmentSamples, bounces;
        public float bakeResolution;
        public string[] textures;
    }
    [Serializable] sealed class BenchReport
    {
        public string stage, mode, gpu, api, scene, shadowMarker, rtgiMarker;
        public int frames, warmup, frameSamples;
        public double frameGpuMeanMs, frameGpuMedianMs, frameGpuP95Ms;
        public int shadowMarkerFrameSamples, rtgiMarkerFrameSamples;
        public double shadowMarkerMeanMs, shadowMarkerMedianMs, shadowMarkerP95Ms;
        public double rtgiMarkerMeanMs, rtgiMarkerMedianMs, rtgiMarkerP95Ms, elapsedSeconds;
        public bool timingFeatureEnabled;
        public int profilerDriverFrames;
        public int[] markerFrameIds;
    }
    static string Arg(string key, string fallback = null)
    {
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }
    static void Guard()
    {
        if (!Application.isBatchMode ||
            !File.Exists("Library/HoLightingBakeAudit.private") ||
            File.ReadAllText("Library/HoLightingBakeAudit.private") != "ho-lighting-bake-audit-20261102")
            throw new InvalidOperationException("Only the private LightingBakeAudit project may run this experiment.");
    }
    public static void RunBatch()
    {
        try
        {
            Guard(); Directory.CreateDirectory(Evidence);
            stage = Arg("-hoStage", "花吐电気"); mode = Arg("-hoMode", "A"); phase = Arg("-hoPhase", "prepare");
            if (phase == "repair-import") { RepairImport(); Done("reimported"); }
            else if (phase == "prepare") { Prepare(); Done("prepared"); }
            else if (phase == "pose") { PoseAll(); Done("posed"); }
            else if (phase == "prepare-probes") { PrepareProbeLayouts(); Done("probe-layouts-prepared"); }
            else if (phase == "bake") BeginBake();
            else if (phase == "render") BeginRender();
            else if (phase == "bench") BeginBench();
            else if (phase == "build-player") BuildPlayer();
            else throw new ArgumentException("Unknown phase " + phase);
        }
        catch (Exception error) { Fail(error); }
    }
    static string SourceScene => stage == "花吐电気"
        ? "Assets/00动画/MMD动画场景/花吐电気通用动画/Scenes/頑張りたい人へ.unity"
        : "Assets/00动画/MMD场景/H2Cu阿相/" + stage + "/" + stage + ".unity";
    static string Folder => Root + "/" + stage;
    static string CaseFolder(string value) => Folder + "/" + value;
    static string CaseScene(string value) => CaseFolder(value) + "/" + stage + "-" + value + ".unity";
    static bool UsesAPV(string value) => "ABDF".Contains(value);
    static bool UsesBakedGI(string value) => "CDEF".Contains(value);
    static bool UsesLegacyProbes(string value) => value == "C" || value == "E";
    static bool UsesRTGI(string value) => "ABCD".Contains(value);
    static void RepairImport()
    {
        var shader = Shader.Find("lilToon");
        if (shader == null)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { "Packages/jp.lilxyzw.liltoon" }))
                AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
            shader = Shader.Find("lilToon");
        }
        if (shader == null) throw new InvalidOperationException("Ho lilToon shader is still unavailable after shader import.");
        foreach (string path in Directory.GetFiles("Assets/00动画", "*.pmx", SearchOption.AllDirectories))
            AssetDatabase.ImportAsset(path.Replace('\\', '/'), ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        AssetDatabase.SaveAssets();
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    static IEnumerable<T> Components<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<T>(true));
    static UniversalRenderPipelineAsset Pipeline => AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
    static void ProbeSystem(int value)
    {
        using var serialized = new SerializedObject(Pipeline);
        serialized.FindProperty("m_LightProbeSystem").intValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(Pipeline); AssetDatabase.SaveAssetIfDirty(Pipeline);
    }
    static void ConfigureFeature(string value)
    {
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        foreach (var feature in data.rendererFeatures)
        {
            if (feature is HoShadowCastRendererFeature shadow)
            { shadow.SetActive(value == "A"); EditorUtility.SetDirty(shadow); }
            if (feature is HoRTGIRendererFeature gi)
            { gi.SetActive("ABCD".Contains(value)); EditorUtility.SetDirty(gi); }
        }
        // This asset is a private copy; never write it back to BREAK_URP.
        EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
    }
    static void Prepare()
    {
        EnsureFolder(Folder); EnsureFolder(Folder + "/Meshes");
        foreach (string value in new[] { "A", "B", "C", "D", "E", "F" })
        {
            EnsureFolder(CaseFolder(value));
            if (File.Exists(CaseScene(value)))
            {
                string prior = Evidence + "/" + stage + "-" + value + "-prepared.json";
                if (File.Exists(prior))
                {
                    var old = JsonUtility.FromJson<PrepReport>(File.ReadAllText(prior));
                    if (!UsesBakedGI(value) || old.staticRenderers > 0) continue;
                }
                if (!UsesBakedGI(value)) throw new IOException("Partial case exists; inspect it before retry: " + CaseScene(value));
                if (!AssetDatabase.DeleteAsset(CaseFolder(value)))
                    throw new IOException("Cannot remove incomplete private bake case: " + CaseFolder(value));
                if (File.Exists(prior)) File.Delete(prior);
                EnsureFolder(CaseFolder(value));
            }
            if (!AssetDatabase.CopyAsset(SourceScene, CaseScene(value))) throw new IOException("Cannot copy " + SourceScene);
            var scene = EditorSceneManager.OpenScene(CaseScene(value), OpenSceneMode.Single);
            var report = new PrepReport { stage = stage, mode = value, scene = scene.path };
            CloneProfiles(scene, value);
            PoseCharacters(scene, report, 240);
            if ("CDEF".Contains(value))
            {
                PrepareGeometry(scene, report);
                PrepareLights(scene, report);
                var settings = new LightingSettings { name = stage + "-" + value,
                    bakedGI = true, realtimeGI = false, autoGenerate = false,
                    lightmapResolution = 8, lightmapMaxSize = 2048,
                    mixedBakeMode = MixedLightingMode.IndirectOnly,
                    directSampleCount = 64, indirectSampleCount = 256, environmentSampleCount = 128,
                    minBounces = 2, maxBounces = 4 };
                var lightmapper = typeof(LightingSettings).GetProperty("lightmapper");
                lightmapper.SetValue(settings, Enum.Parse(lightmapper.PropertyType, "ProgressiveGPU"));
                AssetDatabase.CreateAsset(settings, CaseFolder(value) + "/LightingSettings.asset");
                Lightmapping.lightingSettings = settings;
                LightmapSettings.lightmapsMode = LightmapsMode.CombinedDirectional;
            if (UsesLegacyProbes(value)) PrepareLegacyProbes(scene, report);
                else PrepareAPV(scene, value);
            }
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText(Evidence + "/" + stage + "-" + value + "-prepared.json", JsonUtility.ToJson(report, true));
        }
        AssetDatabase.SaveAssets();
    }
    static void PoseCharacters(Scene scene, PrepReport report, float frame)
    {
        var players = Components<HoVmdMotionPlayer>(scene).Where(x => x.Animation != null).ToArray();
        report.motionFrame = frame;
        report.motionPlayers = players.Select(x => x.name + " | " + x.Animation.name +
            " | rate=" + x.FrameRate + " | root=" + x.ModelRoot.name).ToArray();
        foreach (var player in players)
        {
            player.Playing = false; player.RebuildBindings(); player.ApplyAtFrame(frame);
            EditorUtility.SetDirty(player);
        }
        var cameraPlayers = Components<HoVmdCameraPlayer>(scene).Where(x => x.Animation != null).ToArray();
        report.cameraPlayers = cameraPlayers.Select(x => x.name + " | " + x.Animation.name + " | rate=" + x.FrameRate).ToArray();
        foreach (var player in cameraPlayers)
        { player.Playing = false; player.RebuildTrack(); player.ApplyAtFrame(frame); EditorUtility.SetDirty(player); }
        foreach (var player in Components<HoVmdSequencePlayer>(scene))
        { player.Playing = false; EditorUtility.SetDirty(player); }
        if (players.Length > 0 || cameraPlayers.Length > 0) EditorSceneManager.MarkSceneDirty(scene);
    }
    static void PoseAll()
    {
        foreach (string value in new[] { "A", "B", "C", "D", "E", "F" })
        {
            var scene = EditorSceneManager.OpenScene(CaseScene(value), OpenSceneMode.Single);
            var report = new PrepReport { stage = stage, mode = value, scene = scene.path };
            PoseCharacters(scene, report, 240);
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText(Evidence + "/" + stage + "-" + value + "-motion-pose.json", JsonUtility.ToJson(report, true));
        }
        AssetDatabase.SaveAssets();
    }
    static void CloneProfiles(Scene scene, string value)
    {
        int index = 0;
        foreach (var volume in Components<Volume>(scene))
        {
            if (volume.sharedProfile == null) continue;
            string path = CaseFolder(value) + "/Volume-" + (++index) + ".asset";
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(volume.sharedProfile), path))
                throw new IOException("Cannot isolate Volume");
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (volume.sharedProfile.TryGet<HoRTGIVolume>(out var gi))
            { gi.enable.Override(UsesRTGI(value)); EditorUtility.SetDirty(gi); EditorUtility.SetDirty(volume.sharedProfile); }
        }
    }
    static void PrepareGeometry(Scene scene, PrepReport report)
    {
        foreach (var filter in Components<MeshFilter>(scene))
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || filter.sharedMesh == null) continue;
            bool sky = filter.name.Contains("天空") || filter.name.ToLowerInvariant().Contains("sky");
            bool transparent = renderer.sharedMaterials.All(m => m == null || m.renderQueue > 2500);
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || sky || transparent)
            { report.excludedRenderers++; continue; }
            var original = filter.sharedMesh;
            report.sourceVertices += original.vertexCount;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out string guid, out long localId);
            string meshPath = Folder + "/Meshes/" + guid + "-" + localId + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = Object.Instantiate(original); mesh.name = original.name + "-LightmapUV";
                UnwrapParam.SetDefaults(out var unwrap); unwrap.packMargin = 4f / 1024;
                if (!Unwrapping.GenerateSecondaryUVSet(mesh, unwrap)) throw new InvalidOperationException("UV unwrap failed: " + original.name);
                AssetDatabase.CreateAsset(mesh, meshPath); report.generatedMeshes++;
            }
            filter.sharedMesh = mesh; renderer.receiveGI = ReceiveGI.Lightmaps;
            GameObjectUtility.SetStaticEditorFlags(filter.gameObject,
                GameObjectUtility.GetStaticEditorFlags(filter.gameObject) | StaticEditorFlags.ContributeGI);
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter.gameObject);
            report.staticRenderers++; report.unwrappedVertices += mesh.vertexCount;
        }
    }
    static void PrepareLights(Scene scene, PrepReport report)
    {
        var names = new List<string>();
        foreach (var light in Components<Light>(scene))
        {
            if (!light.isActiveAndEnabled) continue;
            var volume = light.GetComponent<lilToon.URP.Extensions.VolumeLight.HoVolumeLightSource>();
            bool dynamic = light == RenderSettings.sun || light.type == LightType.Directional ||
                (volume != null && volume.isActiveAndEnabled);
            light.lightmapBakeType = dynamic ? LightmapBakeType.Mixed : LightmapBakeType.Baked;
            if (dynamic) report.mixedLights++; else report.bakedLights++;
            names.Add(light.name + " -> " + light.lightmapBakeType + " shadows=" + light.shadows);
            PrefabUtility.RecordPrefabInstancePropertyModifications(light);
        }
        report.lights = names.ToArray();
    }
    static Bounds StaticBounds(Scene scene)
    {
        Bounds? result = null;
        foreach (var renderer in Components<MeshRenderer>(scene))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                renderer.sharedMaterials.All(m => m == null || m.renderQueue > 2500) ||
                renderer.name.Contains("天空") || renderer.name.ToLowerInvariant().Contains("sky")) continue;
            if (result == null) result = renderer.bounds;
            else { var bounds = result.Value; bounds.Encapsulate(renderer.bounds); result = bounds; }
        }
        if (result == null) throw new InvalidOperationException("No static opaque geometry to bound the bake.");
        return result.Value;
    }
    static Bounds DynamicProbeBounds(Scene scene, PrepReport report)
    {
        var players = Components<HoVmdMotionPlayer>(scene).Where(x => x.Animation != null).ToArray();
        var renderers = Components<SkinnedMeshRenderer>(scene)
            .Where(x => x.enabled && x.gameObject.activeInHierarchy && x.sharedMesh != null).ToArray();
        if (renderers.Length == 0) return StaticBounds(scene);
        foreach (var player in players) player.RebuildBindings();
        uint last = players.Select(x => x.Animation.LastBoneFrame).DefaultIfEmpty(0u).Max();
        int step = Mathf.Max(1, Mathf.CeilToInt(last / 24f));
        Bounds? result = null;
        var baked = new Mesh { name = "LightingBakeAudit.ProbeBounds" };
        try
        {
            for (uint frame = 0; frame <= last; frame = Math.Min(last, frame + (uint)step))
            {
                foreach (var player in players) player.ApplyAtFrame(Mathf.Min(frame, player.Animation.LastBoneFrame));
                foreach (var renderer in renderers)
                {
                    renderer.BakeMesh(baked, true);
                    var b = baked.bounds; var matrix = renderer.transform.localToWorldMatrix;
                    for (int x = -1; x <= 1; x += 2)
                        for (int y = -1; y <= 1; y += 2)
                            for (int z = -1; z <= 1; z += 2)
                            {
                                var point = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z)));
                                if (result == null) result = new Bounds(point, Vector3.zero);
                                else { var bounds = result.Value; bounds.Encapsulate(point); result = bounds; }
                            }
                    baked.Clear();
                }
                report.probePoseSamples++;
                if (frame >= last) break;
            }
        }
        finally { Object.DestroyImmediate(baked); }
        if (result == null) throw new InvalidOperationException("Could not bound animated receivers.");
        var restore = new PrepReport(); PoseCharacters(scene, restore, 240);
        return result.Value;
    }
    static void PrepareLegacyProbes(Scene scene, PrepReport report)
    {
        foreach (var old in Components<LightProbeGroup>(scene)) Object.DestroyImmediate(old.gameObject);
        bool dynamicReceiver = Components<SkinnedMeshRenderer>(scene).Any(x => x.enabled && x.gameObject.activeInHierarchy);
        var bounds = DynamicProbeBounds(scene, report); bounds.Expand(dynamicReceiver ? new Vector3(4, 3, 4) : new Vector3(2, 2, 2));
        var node = new GameObject("烘焙验证 · 传统光照探针");
        node.transform.position = bounds.center; report.staticBounds = bounds;
        var group = node.AddComponent<LightProbeGroup>(); var positions = new List<Vector3>();
        if (dynamicReceiver)
        {
            for (float x = bounds.min.x; x <= bounds.max.x + .01f; x += 2)
                for (float z = bounds.min.z; z <= bounds.max.z + .01f; z += 2)
                    foreach (float y in new[] { .65f, 1.5f, 3f, 5f })
                        positions.Add(new Vector3(x - bounds.center.x, y - bounds.center.y, z - bounds.center.z));
        }
        else
        {
            for (float x = bounds.min.x; x <= bounds.max.x + .01f; x += 4)
                for (float y = bounds.min.y; y <= bounds.max.y + .01f; y += 4)
                    for (float z = bounds.min.z; z <= bounds.max.z + .01f; z += 4)
                        positions.Add(new Vector3(x - bounds.center.x, y - bounds.center.y, z - bounds.center.z));
        }
        if (positions.Count > 6000) throw new InvalidOperationException("Probe grid exceeds the audit cap; adjust spacing.");
        group.probePositions = positions.ToArray(); report.probePositions = positions.Count;
    }
    static void PrepareAPV(Scene scene, string value)
    {
        var probeReport = new PrepReport(); var bounds = DynamicProbeBounds(scene, probeReport);
        bounds.Expand(new Vector3(4, 3, 4));
        var node = new GameObject("烘焙验证 · Adaptive Probe Volume");
        node.transform.position = bounds.center;
        var volume = node.AddComponent<ProbeVolume>(); volume.mode = ProbeVolume.Mode.Local;
        volume.size = bounds.size;
        var set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>(); set.name = stage + "-APV";
        set.minDistanceBetweenProbes = .5f; set.simplificationLevels = 2; set.skyOcclusion = false;
        set.renderersLayerMask = -1;
        AssetDatabase.CreateAsset(set, CaseFolder(value) + "/ProbeBakingSet.asset");
        if (!set.TryAddScene(AssetDatabase.AssetPathToGUID(scene.path))) throw new InvalidOperationException("Cannot register APV scene");
        set.SetSceneBaking(AssetDatabase.AssetPathToGUID(scene.path), true);
        EditorUtility.SetDirty(set);
        var reportPath = Evidence + "/" + stage + "-" + value + "-apv-volume.json";
        probeReport.stage = stage; probeReport.mode = value; probeReport.scene = scene.path; probeReport.apvBounds = bounds;
        File.WriteAllText(reportPath, JsonUtility.ToJson(probeReport, true));
    }
    static void PrepareProbeLayouts()
    {
        // This only edits probe objects/settings in the private cases; it does not touch authored sources.
        foreach (string value in new[] { "C", "E" })
        {
            var scene = EditorSceneManager.OpenScene(CaseScene(value), OpenSceneMode.Single);
            var report = new PrepReport { stage = stage, mode = value, scene = scene.path };
            PrepareLegacyProbes(scene, report); EditorSceneManager.SaveScene(scene);
            File.WriteAllText(Evidence + "/" + stage + "-" + value + "-probe-layout.json", JsonUtility.ToJson(report, true));
        }
        foreach (string value in new[] { "D", "F" })
        {
            var scene = EditorSceneManager.OpenScene(CaseScene(value), OpenSceneMode.Single);
            var volume = Components<ProbeVolume>(scene).FirstOrDefault();
            var set = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(CaseFolder(value) + "/ProbeBakingSet.asset");
            if (volume == null || set == null) throw new InvalidOperationException("APV layout assets are missing: " + value);
            var report = new PrepReport();
            var bounds = DynamicProbeBounds(scene, report); bounds.Expand(new Vector3(4, 3, 4));
            volume.transform.position = bounds.center; volume.size = bounds.size;
            set.minDistanceBetweenProbes = .5f; set.simplificationLevels = 2; set.skyOcclusion = false;
            EditorUtility.SetDirty(volume); EditorUtility.SetDirty(set);
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            report.stage = stage; report.mode = value; report.scene = scene.path; report.apvBounds = bounds;
            File.WriteAllText(Evidence + "/" + stage + "-" + value + "-probe-layout.json", JsonUtility.ToJson(report, true));
        }
    }
    static void BeginBake()
    {
        if (!UsesBakedGI(mode)) throw new ArgumentException("Only C/D/E/F are baked.");
        PinShadowQuality();
        ProbeSystem(UsesAPV(mode) ? 1 : 0); ConfigureFeature(mode);
        var scene = EditorSceneManager.OpenScene(CaseScene(mode), OpenSceneMode.Single);
        var bakeSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(CaseFolder(mode) + "/LightingSettings.asset");
        if (bakeSettings == null) throw new InvalidOperationException("LightingSettings asset missing.");
        bakeSettings.lightmapResolution = 16;
        bakeSettings.lightmapMaxSize = 4096;
        bakeSettings.directSampleCount = 128;
        bakeSettings.indirectSampleCount = 512;
        bakeSettings.environmentSampleCount = 256;
        bakeSettings.minBounces = 2;
        bakeSettings.maxBounces = 4;
        EditorUtility.SetDirty(bakeSettings); AssetDatabase.SaveAssetIfDirty(bakeSettings);
        Lightmapping.lightingSettings = bakeSettings;
        if (UsesAPV(mode))
        {
            var set = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(CaseFolder(mode) + "/ProbeBakingSet.asset");
            ProbeReferenceVolume.instance.SetActiveBakingSet(set);
            EditorSceneManager.SaveScene(scene);
        }
        started = EditorApplication.timeSinceStartup; completed = false; nextProgress = 0;
        Lightmapping.bakeCompleted += BakeCompleted;
        apvStarted = false;
        if (!Lightmapping.BakeAsync()) throw new InvalidOperationException("Lightmapping refused BakeAsync");
        EditorApplication.update += BakeTick;
    }
    static void PinShadowQuality()
    {
        // Pin the same native shadow budget for every private test case.
        QualitySettings.shadows = UnityEngine.ShadowQuality.All;
        QualitySettings.shadowResolution = UnityEngine.ShadowResolution.VeryHigh;
        QualitySettings.shadowProjection = ShadowProjection.StableFit;
        QualitySettings.shadowCascades = 4;
        QualitySettings.shadowDistance = 90;
        QualitySettings.shadowmaskMode = ShadowmaskMode.DistanceShadowmask;
    }
    static void BakeCompleted() => completed = true;
    static void BakeTick()
    {
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - started;
            if (elapsed > 1800) { Lightmapping.Cancel(); throw new TimeoutException("Bake exceeded 30 minutes"); }
            if (EditorApplication.timeSinceStartup > nextProgress)
            {
                nextProgress = EditorApplication.timeSinceStartup + 3;
                File.WriteAllText(Evidence + "/bake-progress.json", "{\"stage\":\"" + stage + "\",\"mode\":\"" + mode +
                    "\",\"seconds\":" + elapsed.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"progress\":" + Lightmapping.buildProgress.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
            }
            if (Lightmapping.isRunning) return;
            if (UsesAPV(mode) && !apvStarted)
            {
                apvStarted = true;
                if (!AdaptiveProbeVolumes.BakeAsync())
                    throw new InvalidOperationException("APV bake refused after lightmap bake: " + mode);
                return;
            }
            if (UsesAPV(mode) && AdaptiveProbeVolumes.isRunning) return;
            if (!completed && elapsed < 10) return;
            EditorApplication.update -= BakeTick; Lightmapping.bakeCompleted -= BakeCompleted;
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            var usesAPV = UsesAPV(mode);
            var set = usesAPV ? AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(CaseFolder(mode) + "/ProbeBakingSet.asset") : null;
            var report = new BakeReport { stage = stage, mode = mode, scene = CaseScene(mode), completed = completed,
                standardBakeCompleted = completed, apvBakeCompleted = !usesAPV || (set != null && set.HasBakedData()),
                seconds = elapsed, lightmaps = LightmapSettings.lightmaps.Length,
                legacyProbes = LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0,
                apvData = usesAPV && set != null && set.HasBakedData(),
                lightingData = AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset),
                settings = EditorJsonUtility.ToJson(Lightmapping.lightingSettings),
                textures = LightmapSettings.lightmaps.Select(x => AssetDatabase.GetAssetPath(x.lightmapColor)).ToArray(),
                bakeResolution = Lightmapping.lightingSettings.lightmapResolution,
                directSamples = Lightmapping.lightingSettings.directSampleCount,
                indirectSamples = Lightmapping.lightingSettings.indirectSampleCount,
                environmentSamples = Lightmapping.lightingSettings.environmentSampleCount,
                bounces = Lightmapping.lightingSettings.maxBounces };
            File.WriteAllText(Evidence + "/" + stage + "-" + mode + "-baked.json", JsonUtility.ToJson(report, true));
            if (!completed || report.lightmaps == 0 || (usesAPV && !report.apvData))
                throw new InvalidOperationException("Bake produced incomplete lighting data");
            Done("baked");
        }
        catch (Exception error) { EditorApplication.update -= BakeTick; Lightmapping.bakeCompleted -= BakeCompleted; Fail(error); }
    }
    static void BeginRender()
    {
        ProbeSystem(UsesAPV(mode) ? 1 : 0); ConfigureFeature(mode);
        string path = CaseScene(mode); string output = Evidence + "/" + stage + "/" + mode;
        Directory.CreateDirectory(output);
        var request = new HoMmdStageWork.Request { action = "review", scenePath = path, outputPath = output,
            width = 1280, height = 720, warmup = 64 };
        // Saved authored cameras and filters are kept. This captures the graphics path, not video encoding.
        HoMmdSceneWorkshop.Review(request, result => Done(result), Fail);
    }
    static void BeginBench()
    {
        ProbeSystem(UsesAPV(mode) ? 1 : 0); ConfigureFeature(mode);
        var scene = EditorSceneManager.OpenScene(CaseScene(mode), OpenSceneMode.Single);
        var environment = Components<HoMmdStageEnvironment>(scene).FirstOrDefault(x => x.isActiveAndEnabled);
        environment?.ApplyNow();
        benchCamera = Components<Camera>(scene).Where(x => x.isActiveAndEnabled)
            .OrderByDescending(x => x.CompareTag("MainCamera")).ThenByDescending(x => x.depth).FirstOrDefault();
        if (benchCamera == null) throw new InvalidOperationException("No active camera.");
        foreach (var camera in Components<Camera>(scene)) camera.enabled = camera == benchCamera;
        benchCamera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
        benchCamera.GetUniversalAdditionalCameraData().resetHistory = true;
        benchTarget = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default)
        { name = "HoLightingBakeBench", antiAliasing = 1 };
        if (!benchTarget.Create()) throw new InvalidOperationException("Cannot create benchmark target.");
        // Set the same global volume stack used by the capture tool.
        VolumeManager.instance.Update(benchCamera.transform, -1);
        var shadowType = typeof(HoShadowCastRendererFeature).Assembly.GetType("lilToon.URP.Extensions.ShadowCast.HoShadowCastPass");
        shadowSampler = (ProfilingSampler)shadowType?.GetField("ProfilingSampler", System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic)?.GetValue(null);
        if (shadowSampler != null) shadowSampler.enableRecording = true;
        var giType = typeof(HoRTGIRendererFeature).GetNestedType("HoRTGIPass",
            System.Reflection.BindingFlags.NonPublic);
        rtgiSampler = (ProfilingSampler)giType?.GetField("LightingBakeAuditSampler",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(null);
        if (rtgiSampler != null) rtgiSampler.enableRecording = true;
        lastShadowGpuSampleCount = shadowSampler != null ? shadowSampler.gpuSampleCount : 0;
        lastShadowGpuElapsed = shadowSampler != null ? shadowSampler.gpuElapsedTime : 0;
        lastRtgiGpuSampleCount = rtgiSampler != null ? rtgiSampler.gpuSampleCount : 0;
        lastRtgiGpuElapsed = rtgiSampler != null ? rtgiSampler.gpuElapsedTime : 0;
        frameGpu.Clear(); shadowMarkerMs.Clear(); rtgiMarkerMs.Clear(); markerFrameIds.Clear();
        benchFrame = 0; lastTimingFrame = -1; lastFrameTimestamp = 0; lastMarkerFrame = -1;
        started = EditorApplication.timeSinceStartup; nextProgress = 0;
        previousProfilerEnabled = UnityEditorInternal.ProfilerDriver.enabled;
        previousProfileEditor = UnityEditorInternal.ProfilerDriver.profileEditor;
        previousGpuArea = UnityEditorInternal.ProfilerDriver.IsAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU);
        Profiler.enabled = true; UnityEditorInternal.ProfilerDriver.profileEditor = true;
        UnityEditorInternal.ProfilerDriver.enabled = true;
        UnityEditorInternal.ProfilerDriver.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU, true);
        EditorApplication.update += BenchTick; EditorApplication.QueuePlayerLoopUpdate();
    }
    static void BuildPlayer()
    {
        string probeSystem = Arg("-hoProbeSystem", "APV");
        bool apv = probeSystem == "APV";
        if (!apv && probeSystem != "Legacy") throw new ArgumentException("Probe system must be APV or Legacy.");
        ProbeSystem(apv ? 1 : 0);
        PlayerSettings.enableFrameTimingStats = true;
        string runtimeDir = "Assets/LightingBakeAudit/Runtime";
        EnsureFolder(runtimeDir);
        AssetDatabase.CopyAsset("Assets/00动画/MMD场景/_制作工具/Editor/HoLightingBakePlayer.cs",
            runtimeDir + "/HoLightingBakePlayer.cs");
        var scenes = new List<string>();
        string bootstrap = "Assets/LightingBakeAudit/BenchmarkBootstrap.unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(bootstrap) == null)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, bootstrap);
        }
        scenes.Add(bootstrap);
        var cases = apv ? new[] { "A", "B", "D", "F" } : new[] { "A", "B", "C", "E" };
        var auditStages = new[] { "花吐电気", "圣诞万花镜", "孔雀月令" };
        foreach (string auditStage in auditStages)
            foreach (string mode in cases)
            {
                string scene = "Assets/LightingBakeAudit/" + auditStage + "/" + mode + "/" + auditStage + "-" + mode + ".unity";
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
                    throw new FileNotFoundException("Prepare all stages before building the cross-stage audit.", scene);
                scenes.Add(scene);
            }
        string directory = Path.Combine(Evidence, "Player", probeSystem); Directory.CreateDirectory(directory);
        var scenePaths = scenes.Distinct().ToArray();
        var previousBuildScenes = EditorBuildSettings.scenes;
        var previousBuildProfile = BuildProfile.GetActiveBuildProfile();
        bool previousProfileEditor = UnityEditorInternal.ProfilerDriver.profileEditor;
        PropertyInfo profileScenes = previousBuildProfile != null
            ? previousBuildProfile.GetType().GetProperty("scenes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            : null;
        PropertyInfo profileOverride = previousBuildProfile != null
            ? previousBuildProfile.GetType().GetProperty("overrideGlobalScenes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            : null;
        object previousProfileScenes = profileScenes?.GetValue(previousBuildProfile);
        object previousProfileOverride = profileOverride?.GetValue(previousBuildProfile);
        UnityEditorInternal.ProfilerDriver.profileEditor = false;
        try
        {
            var auditBuildScenes = scenePaths.Select(x => new EditorBuildSettingsScene(x, true)).ToArray();
            if (previousBuildProfile != null && profileScenes?.CanWrite == true && profileOverride?.CanWrite == true)
            {
                profileScenes.SetValue(previousBuildProfile, auditBuildScenes);
                profileOverride.SetValue(previousBuildProfile, true);
                EditorUtility.SetDirty(previousBuildProfile);
            }
            else EditorBuildSettings.scenes = auditBuildScenes;
            var resolvedScenes = previousBuildProfile != null
                ? previousBuildProfile.GetScenesForBuild().Where(x => x.enabled).Select(x => x.path).ToArray()
                : EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray();
            string sceneReport = Path.Combine(directory, "build-scenes.json");
            File.WriteAllText(sceneReport, JsonUtility.ToJson(new BuildSceneReport {
                profile = previousBuildProfile != null ? AssetDatabase.GetAssetPath(previousBuildProfile) : "<platform profile>",
                overrideGlobalScenes = previousBuildProfile != null && profileOverride != null &&
                    Convert.ToBoolean(profileOverride.GetValue(previousBuildProfile)),
                configured = resolvedScenes, included = scenePaths,
                runtimeBuildIndexCount = SceneManager.sceneCountInBuildSettings,
                runtimeBuildIndexPaths = Enumerable.Range(0, SceneManager.sceneCountInBuildSettings)
                    .Select(SceneUtility.GetScenePathByBuildIndex).ToArray()
            }, true));
            if (!scenePaths.ToHashSet(StringComparer.Ordinal).SetEquals(resolvedScenes))
                throw new InvalidOperationException("Active build profile did not resolve the audit scenes. See " + sceneReport);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = scenePaths, locationPathName = Path.Combine(directory, "HoLightingBakeAudit.exe"),
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development | BuildOptions.CompressWithLz4
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Player build failed: " + report.summary.result);
            File.WriteAllText(Path.Combine(directory, "build.json"), JsonUtility.ToJson(report.summary, true));
        }
        finally
        {
            if (previousBuildProfile != null && profileScenes?.CanWrite == true && profileOverride?.CanWrite == true)
            {
                profileScenes.SetValue(previousBuildProfile, previousProfileScenes);
                profileOverride.SetValue(previousBuildProfile, previousProfileOverride);
                EditorUtility.SetDirty(previousBuildProfile);
                AssetDatabase.SaveAssetIfDirty(previousBuildProfile);
            }
            EditorBuildSettings.scenes = previousBuildScenes;
            UnityEditorInternal.ProfilerDriver.profileEditor = previousProfileEditor;
        }
        Done(Path.Combine(directory, "HoLightingBakeAudit.exe"));
    }
    [Serializable] sealed class BuildSceneReport
    {
        public string profile;
        public bool overrideGlobalScenes;
        public int runtimeBuildIndexCount;
        public string[] configured, included, runtimeBuildIndexPaths;
    }
    static void BenchTick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - started > 300)
                throw new TimeoutException("GPU benchmark exceeded five minutes.");
            if (benchFrame >= 112)
            {
                EditorApplication.update -= BenchTick;
                var sorted = frameGpu.OrderBy(x => x).ToArray();
                var shadowSorted = shadowMarkerMs.OrderBy(x => x).ToArray();
                var giSorted = rtgiMarkerMs.OrderBy(x => x).ToArray();
                var report = new BenchReport {
                    stage = stage, mode = mode, gpu = SystemInfo.graphicsDeviceName,
                    api = SystemInfo.graphicsDeviceType.ToString(), scene = CaseScene(mode),
                    shadowMarker = shadowSampler != null ? "Ho-ShadowCast" : "unavailable",
                    rtgiMarker = rtgiSampler != null ? "Ho-RTGI Audit (isolated profiling hook)" : "unavailable",
                    frames = benchFrame, warmup = 24, frameSamples = sorted.Length,
                    frameGpuMeanMs = sorted.Length > 0 ? sorted.Average() : -1,
                    frameGpuMedianMs = sorted.Length > 0 ? sorted[sorted.Length / 2] : -1,
                    frameGpuP95Ms = sorted.Length > 0 ? sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Length * .95f) - 1, 0, sorted.Length - 1)] : -1,
                    shadowMarkerFrameSamples = shadowSorted.Length, rtgiMarkerFrameSamples = giSorted.Length,
                    shadowMarkerMeanMs = shadowSorted.Length > 0 ? shadowSorted.Average() : -1,
                    shadowMarkerMedianMs = shadowSorted.Length > 0 ? shadowSorted[shadowSorted.Length / 2] : -1,
                    shadowMarkerP95Ms = Percentile(shadowSorted, .95),
                    rtgiMarkerMeanMs = giSorted.Length > 0 ? giSorted.Average() : -1,
                    rtgiMarkerMedianMs = giSorted.Length > 0 ? giSorted[giSorted.Length / 2] : -1,
                    rtgiMarkerP95Ms = Percentile(giSorted, .95), markerFrameIds = markerFrameIds.ToArray(),
                    elapsedSeconds = EditorApplication.timeSinceStartup - started,
                    timingFeatureEnabled = FrameTimingManager.IsFeatureEnabled(),
                    profilerDriverFrames = UnityEditorInternal.ProfilerDriver.lastFrameIndex
                };
                if (shadowSampler != null) shadowSampler.enableRecording = false;
                if (rtgiSampler != null) rtgiSampler.enableRecording = false;
                UnityEditorInternal.ProfilerDriver.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU, previousGpuArea);
                UnityEditorInternal.ProfilerDriver.profileEditor = previousProfileEditor;
                UnityEditorInternal.ProfilerDriver.enabled = previousProfilerEnabled;
                benchTarget.Release(); Object.DestroyImmediate(benchTarget); benchTarget = null; benchCamera = null;
                string output = Evidence + "/" + stage + "-" + mode + "-bench.json";
                File.WriteAllText(output, JsonUtility.ToJson(report, true));
                Done(output); return;
            }
            var request = new RenderPipeline.StandardRequest { destination = benchTarget };
            RenderPipeline.SubmitRenderRequest(benchCamera, request);
            FrameTimingManager.CaptureFrameTimings();
            int profilerFrame = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
            if (profilerFrame > lastMarkerFrame)
            {
                lastMarkerFrame = profilerFrame;
                bool sampled = false;
                AddMarkerSample(shadowSampler, shadowMarkerMs, ref lastShadowGpuSampleCount,
                    ref lastShadowGpuElapsed, ref sampled);
                AddMarkerSample(rtgiSampler, rtgiMarkerMs, ref lastRtgiGpuSampleCount,
                    ref lastRtgiGpuElapsed, ref sampled);
                if (sampled) markerFrameIds.Add(profilerFrame);
            }
            if (FrameTimingManager.GetLatestTimings(1, frameTimings) > 0)
            {
                var timing = frameTimings[0];
                if (timing.frameStartTimestamp > 0 && timing.frameStartTimestamp != lastFrameTimestamp)
                {
                    lastFrameTimestamp = timing.frameStartTimestamp; lastTimingFrame++;
                    if (lastTimingFrame >= 6 && timing.gpuFrameTime > 0 &&
                        !double.IsNaN(timing.gpuFrameTime) && !double.IsInfinity(timing.gpuFrameTime))
                        frameGpu.Add(timing.gpuFrameTime);
                }
            }
            benchFrame++;
            if (EditorApplication.timeSinceStartup > nextProgress)
            {
                nextProgress = EditorApplication.timeSinceStartup + 2;
                File.WriteAllText(Evidence + "/bench-progress.json",
                    "{\"stage\":\"" + stage + "\",\"mode\":\"" + mode + "\",\"frames\":" + benchFrame + "}");
            }
            EditorApplication.QueuePlayerLoopUpdate();
        }
        catch (Exception error)
        {
            EditorApplication.update -= BenchTick;
            if (shadowSampler != null) shadowSampler.enableRecording = false;
            if (rtgiSampler != null) rtgiSampler.enableRecording = false;
            UnityEditorInternal.ProfilerDriver.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU, previousGpuArea);
            UnityEditorInternal.ProfilerDriver.profileEditor = previousProfileEditor;
            UnityEditorInternal.ProfilerDriver.enabled = previousProfilerEnabled;
            Fail(error);
        }
    }
    static void AddMarkerSample(ProfilingSampler sampler, List<double> samples,
        ref long lastCount, ref double lastElapsed, ref bool recorded)
    {
        if (sampler == null) return;
        long count = sampler.gpuSampleCount; double elapsed = sampler.gpuElapsedTime;
        long deltaCount = count - lastCount; double deltaElapsed = elapsed - lastElapsed;
        if (deltaCount > 0 && deltaElapsed > 0)
        { samples.Add(deltaElapsed / deltaCount); recorded = true; }
        lastCount = count; lastElapsed = elapsed;
    }
    static double Percentile(double[] sorted, double percentile) =>
        sorted.Length > 0 ? sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Length * (float)percentile) - 1, 0, sorted.Length - 1)] : -1;
    static readonly FrameTiming[] frameTimings = new FrameTiming[1];
    static void Done(string value) { Debug.Log("HO_BAKE_EXPERIMENT=" + phase + " " + stage + " " + mode + " " + value); EditorApplication.Exit(0); }
    static void Fail(Exception error)
    {
        Debug.LogException(error);
        Directory.CreateDirectory(Evidence);
        File.WriteAllText(Evidence + "/last-error.txt", error.ToString());
        EditorApplication.Exit(1);
    }
}
