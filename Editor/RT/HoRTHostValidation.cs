#pragma warning disable CS0618, CS0672
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.RT
{
    // Actual renderer callbacks and GPU queries, independent of DI/GI algorithms.
    public static class HoRTHostValidation
    {
        [Serializable] private sealed class Report
        {
            public bool pass, compatibility, unsupported, retirement;
            public string api, failure;
            public int checks, renderRequests, createdProfiles, disposedProfiles;
            public uint bakeCount;
            public List<string> evidence = new();
        }
        [StructLayout(LayoutKind.Sequential)] private struct TestRay
        { public Vector4 originMin, directionMax; public uint mask, pad0, pad1, pad2; }

        private const int TestLayer = 29;
        private static readonly List<Object> objects = new();
        private static bool compatibility, unsupported, previousAsync, retirement;
        private static RenderPipelineAsset previousDefault, previousQuality;
        private static UniversalRendererData rendererData;
        private static HoRTRendererFeature host;
        private static HoRTHostProbeFeature first, second;
        private static Camera cameraA, cameraB;
        private static Transform bone;
        private static SkinnedMeshRenderer skin;
        private static Mesh sourceMesh, sharedPose;
        private static ComputeShader shader;
        private static GraphicsBuffer rays;
        private static RenderTexture target;
        private static RTHandle outputA, outputB;
        private static HoRTView oldView;
        private static HoRTScene oldScene;
        private static uint initialBake, movedBake;
        private static int step, retireTicks;
        private static Report report;
        internal static int CreatedProfiles, DisposedProfiles;

        public static void RunCompatibility() { compatibility = true; Run(); }
        public static void RunUnsupported() { unsupported = true; Run(); }
        public static void RunRetirement() { retirement = true; Run(); }
        public static void RunRetirementCompatibility() { compatibility = true; RunRetirement(); }
        public static void Run()
        {
            try
            {
                if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.codex-research/"))
                    throw new InvalidOperationException("Use an independent hidden batch project");
                if (unsupported ? HoRTScene.IsSupported : !HoRTScene.IsSupported)
                    throw new InvalidOperationException("Wrong GPU API for requested host validation");
                previousDefault = GraphicsSettings.defaultRenderPipeline;
                previousQuality = QualitySettings.renderPipeline;
                previousAsync = ShaderUtil.allowAsyncCompilation;
                ShaderUtil.allowAsyncCompilation = false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                report = new Report { compatibility = compatibility, unsupported = unsupported, retirement = retirement, api = SystemInfo.graphicsDeviceType.ToString() };
                bool rejectedProfileKey = false;
                try { _ = new HoRTViewRequest(-1, createProfile: () => new CountingProfile()); }
                catch (ArgumentException) { rejectedProfileKey = true; }
                Check(rejectedProfileKey, "Material profile factories require a stable key");
                CreatedProfiles = DisposedProfiles = 0;
                rendererData = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                host = Keep(ScriptableObject.CreateInstance<HoRTRendererFeature>());
                first = Keep(ScriptableObject.CreateInstance<HoRTHostProbeFeature>());
                second = Keep(ScriptableObject.CreateInstance<HoRTHostProbeFeature>());
                rendererData.rendererFeatures.Add(host);
                rendererData.rendererFeatures.Add(first);
                rendererData.rendererFeatures.Add(second);
                var pipeline = Keep(UniversalRenderPipelineAsset.Create(rendererData)); pipeline.msaaSampleCount = 1;
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = compatibility;
                #else
                Check(!compatibility, "Compatibility support is compiled in the validation project");
                #endif
                cameraA = Camera("RT host camera A"); cameraB = Camera("RT host camera B");
                target = Keep(new RenderTexture(32, 32, 24)); target.Create();
                if (!unsupported)
                {
                    shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RT/HoRTHostValidation.compute");
                    Check(shader != null, "Host probe shader loaded by current module path");
                    rays = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, Marshal.SizeOf<TestRay>());
                    var testRays = new TestRay[8];
                    for (int i = 0; i < 8; i++) testRays[i] = new TestRay
                    { originMin = new Vector4(.1f, .1f, -3, 0), directionMax = new Vector4(0, 0, 1, 20), mask = 1 };
                    rays.SetData(testRays);
                    outputA = Output("RT host output A"); outputB = Output("RT host output B");
                    first.shader = second.shader = shader; first.rays = second.rays = rays;
                    first.output = outputA; second.output = outputB;
                    SetupGeometry();
                }
                first.layers = second.layers = 1 << TestLayer;
                first.profileKey = new object(); second.profileKey = new object();
                EditorApplication.update += Tick;
                Debug.Log("[HoRTHostValidation] START API=" + report.api + "; compatibility=" + compatibility + "; unsupported=" + unsupported);
            }
            catch (Exception error) { Finish(error); }
        }

        private static void SetupGeometry()
        {
            sourceMesh = Keep(new Mesh { name = "RT host shared skin source" });
            sourceMesh.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) };
            sourceMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 }; sourceMesh.RecalculateNormals(); sourceMesh.RecalculateBounds();
            sourceMesh.bindposes = new[] { Matrix4x4.identity };
            var weights = new BoneWeight[4]; for (int i = 0; i < 4; i++) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
            sourceMesh.boneWeights = weights;
            var root = Keep(new GameObject("RT host shared skin")); root.layer = TestLayer;
            bone = Keep(new GameObject("RT host shared bone")).transform; bone.SetParent(root.transform, false);
            skin = root.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = sourceMesh;
            skin.bones = new[] { bone }; skin.rootBone = bone; skin.updateWhenOffscreen = false;
            var material = Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            skin.sharedMaterial = material;
        }

        private static Camera Camera(string name)
        {
            var camera = Keep(new GameObject(name)).AddComponent<Camera>(); camera.enabled = false;
            camera.transform.position = Vector3.back * 3; camera.farClipPlane = 10; camera.cullingMask = 0;
            return camera;
        }
        private static RTHandle Output(string name)
        {
            var texture = Keep(new RenderTexture(8, 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            { enableRandomWrite = true, name = name }); texture.Create(); return RTHandles.Alloc(texture);
        }

        private static void Tick()
        {
            try
            {
                if (retireTicks > 0)
                {
                    HoRTRendererFeature.CollectRetired();
                    if (DisposedProfiles == CreatedProfiles && HoRTRendererFeature.RetiredDomainCount == 0)
                    { Check(true, "All host-owned views and material profiles retire after GPU completion"); Complete(); return; }
                    if (++retireTicks > 120) throw new Exception("Host retirement did not release all profiles/domains within 120 updates");
                    return;
                }
                if (unsupported)
                {
                    Render(cameraA);
                    Check(!first.declared && !second.declared && !first.acquired && !second.acquired, "Unsupported API rejects declarations and acquisition");
                    Check(host.ViewCount == 0 && host.BakeCount == 0 && host.ActiveDomainCount == 0, "Unsupported host allocates no scene view or pose");
                    Check(CreatedProfiles == 0, "Unsupported path does not create hit-material adapters");
                    Complete(); return;
                }
                switch (step++)
                {
                    case 0: case 1: Render(cameraA); break;
                    case 2:
                        Render(cameraA); BothHit(3);
                        Check(host.ViewCount == 2 && host.ActiveDomainCount == 1, "Two declared clients own two AS views in one renderer domain");
                        Check(!ReferenceEquals(first.view.Scene, second.view.Scene), "Different clients retain distinct AS view identities");
                        sharedPose = ResolvePose(first); Check(sharedPose == ResolvePose(second), "Both AS views reference the same current-pose Mesh");
                        initialBake = host.BakeCount; Check(initialBake == 1, "Initial shared pose is baked exactly once");
                        Check(first.lateRejected && second.lateRejected, "Requests after freeze are explicitly rejected");
                        Check(first.wrongCameraRejected && second.wrongCameraRejected, "Acquisition for another camera is rejected");
                        oldView = first.view; bone.localPosition = Vector3.forward * .25f;
                        if (retirement) second.retireHostDuringTrace = true;
                        break;
                    case 3:
                        if (retirement)
                        {
                            Render(cameraA); RetiredHit(first, 3.25f); RetiredHit(second, 3.25f);
                            Check(second.retirementObserved, "Host recreation actually occurred after the second trace dispatch was recorded");
                            Check(second.resourcesHeldBeforeFence, "Retired domain and profiles remain alive before the tail fence is recorded");
                            Check(!first.view.IsValid && !second.view.IsValid, "Recreation invalidates both old view handles while captured GPU inputs remain usable");
                            Check(report.bakeCount == initialBake + 1, "Retirement follows one actual shared-pose update across both views");
                            retireTicks = 1;
                            break;
                        }
                        Render(cameraA); BothHit(3.25f);
                        movedBake = host.BakeCount; Check(movedBake == initialBake + 1, "One pose change adds one Bake across both views");
                        Check(ResolvePose(first) == ResolvePose(second), "Both views observe the updated shared pose Mesh");
                        Check(!oldView.IsValid, "Previous render epoch no longer exposes a valid view");
                        oldView = first.view;
                        break;
                    case 4:
                        Render(cameraB); BothHit(3.25f);
                        Check(!oldView.IsValid, "Second camera invalidates the first camera epoch");
                        Check(host.BakeCount == movedBake, "Stable pose reuses Bake across cameras");
                        oldView = first.view;
                        var changedVertices = sourceMesh.vertices;
                        for (int i = 0; i < changedVertices.Length; i++) changedVertices[i].z += .25f;
                        sourceMesh.vertices = changedVertices; sourceMesh.RecalculateBounds();
                        Check(host.MarkGeometryDirty(first.lastRenderer), "Source Mesh edit invalidates all views through the host");
                        break;
                    case 5:
                        int frame = Time.frameCount;
                        Render(cameraA); BothHit(3.5f); var intermediate = first.view;
                        Check(host.BakeCount == movedBake + 1, "Source Mesh edit rebakes once and refreshes both view hits");
                        movedBake = host.BakeCount;
                        Render(cameraB); BothHit(3.5f);
                        Check(Time.frameCount == frame, "Two explicit camera renders execute within one frameCount");
                        Check(!oldView.IsValid && !intermediate.IsValid, "Every repeated render advances its own epoch");
                        Check(host.BakeCount == movedBake, "Same-frame repeated renders do not rebake unchanged pose");
                        first.SetActive(false); sharedPose = ResolvePose(second);
                        break;
                    case 6:
                        Render(cameraB); Hit(second, 3.5f);
                        Check(host.ViewCount == 1 && DisposedProfiles == 1, "Disabled client view/profile retires independently");
                        Check(sharedPose != null && ResolvePose(second) == sharedPose, "Remaining client keeps shared pose and valid trace");
                        Check(host.BakeCount == movedBake, "Removing a client does not rebake the remaining pose");
                        oldScene = second.view.Scene; second.profileKey = new object();
                        break;
                    case 7:
                        Render(cameraB); Hit(second, 3.5f);
                        Check(!ReferenceEquals(oldScene, second.view.Scene) && DisposedProfiles == 2, "Material profile change replaces only that view and disposes old profile");
                        oldView = second.view; host.SetActive(false);
                        break;
                    case 8:
                        Render(cameraA);
                        Check(!second.declared && !second.acquired && !string.IsNullOrEmpty(second.reason), "Disabled/missing host returns explicit failure");
                        Check(!oldView.IsValid, "Host disabled invalidates previously borrowed view");
                        host.SetActive(true);
                        break;
                    case 9:
                        Render(cameraA); Hit(second, 3.5f);
                        Check(host.BakeCount == movedBake, "Host re-enable reuses unchanged pose after completed GPU use");
                        oldView = second.view; report.bakeCount = host.BakeCount; host.Create();
                        Check(!oldView.IsValid && host.ActiveDomainCount == 0, "Host recreation invalidates and detaches previous domains");
                        Check(!HoRTRendererFeature.TryAcquire(second.lastRenderer, cameraA, second, out _, out _), "After host disposal there is no old domain acquisition");
                        retireTicks = 1;
                        break;
                    default: throw new Exception("Unexpected host validation step");
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Render(Camera camera)
        {
            report.renderRequests++;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        }
        private static void BothHit(float distance) { Hit(first, distance); Hit(second, distance); }
        private static Color[] ReadProbe(HoRTHostProbeFeature consumer)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(8, 4, TextureFormat.RGBAFloat, false, true);
            try { RenderTexture.active = consumer.output.rt; image.ReadPixels(new Rect(0, 0, 8, 4), 0, 0); image.Apply(); return image.GetPixels(); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
        private static void RetiredHit(HoRTHostProbeFeature consumer, float distance)
        {
            var pixels = ReadProbe(consumer);
            Check(pixels[24].a == consumer.generation, "Captured input trace finishes with current generation after host retirement");
            Check(pixels[16].a == 1 && Mathf.Abs(pixels[0].r - distance) < .002f,
                "Captured input GPU hit remains correct after retirement: expected " + distance + ", got " + pixels[0].r);
        }
        internal static void RetireDuringTrace(HoRTHostProbeFeature consumer)
        {
            int before = DisposedProfiles;
            uint bakes = host.BakeCount;
            host.Create();
            consumer.retirementObserved = true;
            consumer.resourcesHeldBeforeFence = HoRTRendererFeature.RetiredDomainCount > 0 && DisposedProfiles == before;
            Check(consumer.resourcesHeldBeforeFence, "Pending GPU use prevents immediate domain/profile destruction during trace recording");
            Check(!consumer.view.IsValid, "Host recreation immediately rejects the old public view");
            report.bakeCount = bakes;
        }
        private static void Hit(HoRTHostProbeFeature consumer, float distance)
        {
            Check(consumer.declared && consumer.acquired, "Client declared/acquired its current scene view");
            var pixels = ReadProbe(consumer);
            Check(pixels[24].a == consumer.generation, "GPU output generation matches this explicit render request");
            Check(pixels[16].a == 1, "Actual GPU ray hit the supported current pose");
            Check(Mathf.Abs(pixels[0].r - distance) < .002f, "Current GPU hit distance expected " + distance + ", got " + pixels[0].r);
            Check(consumer.view.Scene.TryResolve((uint)pixels[0].g, out var instance) && instance.renderer == skin, "GPU identity resolves to shared skin");
            consumer.pose = instance.mesh;
        }
        private static Mesh ResolvePose(HoRTHostProbeFeature consumer) => consumer.pose;
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            if (report != null) { report.checks++; report.evidence.Add(message); }
        }
        private static T Keep<T>(T value) where T : Object { objects.Add(value); return value; }
        private static void Complete()
        {
            if (!unsupported)
                foreach (var message in ShaderUtil.GetComputeShaderMessages(shader))
                    Check(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error && message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning, message.message);
            report.pass = true;
            Debug.Log("[HoRTHostValidation] PASS: real callbacks, shared pose/two views, epochs/cameras, explicit failures, profile and GPU lifetime; compatibility=" + compatibility + "; unsupported=" + unsupported + "; retirement=" + retirement);
            Finish(null);
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            if (report != null)
            {
                report.createdProfiles = CreatedProfiles; report.disposedProfiles = DisposedProfiles;
                report.failure = error?.ToString(); report.pass &= error == null;
                // The evidence belongs to the package workspace, never the user's project.
                string packageRoot = Path.GetFullPath("Packages/jp.lilxyzw.liltoon.urp.extensions");
                // Resolve the local package through AssetDatabase's package metadata.
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RT/HoRTHostValidation.cs");
                if (package != null) packageRoot = package.resolvedPath;
                string folder = Path.Combine(packageRoot, "Tests~/RT/R2"); Directory.CreateDirectory(folder);
                string reportName = unsupported ? "report-unsupported.json" : retirement
                    ? compatibility ? "report-retirement-compat.json" : "report-retirement-rg.json"
                    : compatibility ? "report-compat.json" : "report-rg.json";
                File.WriteAllText(Path.Combine(folder, reportName), JsonUtility.ToJson(report, true));
            }
            GraphicsSettings.defaultRenderPipeline = previousDefault; QualitySettings.renderPipeline = previousQuality;
            ShaderUtil.allowAsyncCompilation = previousAsync;
            host?.Dispose(); HoRTRendererFeature.CollectRetired();
            outputA?.Release(); outputB?.Release(); rays?.Dispose(); outputA = outputB = null; rays = null;
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]); objects.Clear();
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }

        internal sealed class CountingProfile : IHoRTMaterialProfile
        {
            private bool disposed;
            internal CountingProfile() { CreatedProfiles++; }
            public int Signature => 17;
            public void BeginPrepare() { if (disposed) throw new ObjectDisposedException(nameof(CountingProfile)); }
            public Material Resolve(Material source) => null;
            public bool SupportsCutout(Material source) => false;
            public void EndPrepare() { }
            public void Dispose() { if (!disposed) { disposed = true; DisposedProfiles++; } }
        }
    }

    [SupportedOnRenderer()]
    internal sealed class HoRTHostProbeFeature : ScriptableRendererFeature
    {
        internal int layers, generation;
        internal object profileKey;
        internal ComputeShader shader;
        internal GraphicsBuffer rays;
        internal RTHandle output;
        internal Mesh pose;
        internal bool declared, acquired, lateRejected, wrongCameraRejected;
        internal bool retireHostDuringTrace, retirementObserved, resourcesHeldBeforeFence;
        internal string reason;
        internal HoRTView view;
        internal ScriptableRenderer lastRenderer;
        private ProbePass pass;
        public override void Create() { pass = new ProbePass(); }
        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            lastRenderer = renderer; acquired = false;
            declared = HoRTRendererFeature.Declare(renderer, cameraData.camera, this,
                new HoRTViewRequest(layers, mask: 1, profileKey: profileKey, createProfile: () => new HoRTHostValidation.CountingProfile()), out reason);
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            acquired = HoRTRendererFeature.TryAcquire(renderer, renderingData.cameraData.camera, this, out view, out reason);
            if (!acquired) return;
            lateRejected = !HoRTRendererFeature.Declare(renderer, renderingData.cameraData.camera, this, new HoRTViewRequest(layers), out _);
            wrongCameraRejected = !HoRTRendererFeature.TryAcquire(renderer, null, this, out _, out _);
            generation++; pass.owner = this; pass.current = view; renderer.EnqueuePass(pass);
        }
        private sealed class ProbePass : ScriptableRenderPass
        {
            internal HoRTHostProbeFeature owner;
            internal HoRTView current;
            internal ProbePass() { renderPassEvent = RenderPassEvent.AfterRenderingOpaques; }
            private sealed class Data
            { internal HoRTScene.Inputs scene; internal ComputeShader shader; internal GraphicsBuffer rays; internal TextureHandle output; internal int generation; internal HoRTHostProbeFeature retireOwner; }
            private static void Trace(CommandBuffer cmd, HoRTScene.Inputs scene, ComputeShader shader, GraphicsBuffer rays, RenderTargetIdentifier output, int generation)
            {
                scene.BuildAndBind(cmd, shader, 0); cmd.SetComputeBufferParam(shader, 0, "_TestRays", rays);
                cmd.SetComputeTextureParam(shader, 0, "_TestOutput", output); cmd.SetComputeFloatParam(shader, "_TestGeneration", generation);
                cmd.DispatchCompute(shader, 0, 1, 1, 1);
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                using (var builder = graph.AddUnsafePass<Data>("Ho-RT host GPU probe", out var data))
                {
                    // Direct scene reads must also notify the host capture pin.
                    data.scene = current.Scene.Read(graph, builder); data.shader = owner.shader; data.rays = owner.rays; data.generation = owner.generation;
                    data.retireOwner = owner.retireHostDuringTrace ? owner : null;
                    data.output = graph.ImportTexture(owner.output); builder.UseBuffer(graph.ImportBuffer(owner.rays)); builder.UseTexture(data.output, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (Data d, UnsafeGraphContext context) =>
                    {
                        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        d.scene.BuildAndBind(cmd, d.shader, 0); cmd.SetComputeBufferParam(d.shader, 0, "_TestRays", d.rays);
                        cmd.SetComputeTextureParam(d.shader, 0, "_TestOutput", d.output); cmd.SetComputeFloatParam(d.shader, "_TestGeneration", d.generation);
                        cmd.DispatchCompute(d.shader, 0, 1, 1, 1);
                        if (d.retireOwner != null)
                        {
                            d.retireOwner.retireHostDuringTrace = false;
                            HoRTHostValidation.RetireDuringTrace(d.retireOwner);
                        }
                    });
                }
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get("Ho-RT host GPU probe");
                Trace(cmd, current.Scene.ReadCompatibility(), owner.shader, owner.rays, owner.output.nameID, owner.generation);
                if (owner.retireHostDuringTrace)
                {
                    owner.retireHostDuringTrace = false;
                    HoRTHostValidation.RetireDuringTrace(owner);
                }
                context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            }
        }
    }
}
