#pragma warning disable CS0618, CS0672
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.RT
{
    // Hit-material adapters remain effect-specific; their actual resource
    // lifetime belongs to the scene view, including its recorded GPU uses.
    internal interface IHoRTMaterialProfile : IDisposable
    {
        void BeginPrepare();
        Material Resolve(Material source);
        bool SupportsCutout(Material source);
        void EndPrepare();
        int Signature { get; }
    }

    internal sealed class HoRTViewRequest
    {
        internal readonly int geometryLayers;
        internal readonly Material[] excludedMaterials;
        internal readonly uint mask;
        internal readonly object profileKey;
        internal readonly Func<IHoRTMaterialProfile> createProfile;
        internal readonly HoRTGeometryPolicy geometryPolicy;

        internal HoRTViewRequest(int geometryLayers, IReadOnlyList<Material> excludedMaterials = null,
            uint mask = 255, object profileKey = null, Func<IHoRTMaterialProfile> createProfile = null,
            HoRTGeometryPolicy geometryPolicy = HoRTGeometryPolicy.Surface)
        {
            if (mask > 255) throw new ArgumentOutOfRangeException(nameof(mask));
            if (createProfile != null && profileKey == null) throw new ArgumentException("A material profile factory requires a stable profile key", nameof(profileKey));
            this.geometryLayers = geometryLayers; this.mask = mask;
            this.profileKey = profileKey; this.createProfile = createProfile;
            this.geometryPolicy = geometryPolicy;
            this.excludedMaterials = new Material[excludedMaterials?.Count ?? 0];
            for (int i = 0; i < this.excludedMaterials.Length; i++) this.excludedMaterials[i] = excludedMaterials[i];
        }
    }

    // A borrowed view is valid only in the renderer/camera epoch that produced
    // it. A later camera cannot silently make an earlier request valid again.
    internal readonly struct HoRTView
    {
        private readonly HoRTRendererFeature.Domain domain;
        private readonly HoRTRendererFeature.View resources;
        private readonly ulong epoch;
        internal HoRTView(HoRTRendererFeature.Domain domain, HoRTRendererFeature.View resources)
        { this.domain = domain; this.resources = resources; epoch = domain.epoch; }
        internal bool IsValid => domain != null && !domain.retired && domain.frozen && domain.epoch == epoch
            && domain.owner != null && domain.owner.isActive;
        internal HoRTScene Scene => IsValid ? resources.scene : throw new InvalidOperationException("Stale Ho-RT view");
        internal int MaterialSignature => IsValid ? resources.profile?.Signature ?? 0 : 0;
        internal IHoRTMaterialProfile MaterialProfile => IsValid ? resources.profile : throw new InvalidOperationException("Stale Ho-RT material profile");
        internal HoRTScene.Inputs Read(RenderGraph graph, IUnsafeRenderGraphBuilder builder)
        { var scene = Scene; domain.capturedInput = true; return scene.Read(graph, builder); }
        internal HoRTScene.Inputs ReadCompatibility()
        { var scene = Scene; domain.capturedInput = true; return scene.ReadCompatibility(); }
        // Consumer-owned GPU resources can outlive Create/Dispose while a
        // captured view is still executing. Reuse the host's completion fence.
        internal void ReleaseAfterUse(IDisposable resource)
        { HoRTRendererFeature.ReleaseAfterUse(domain, resource); }
    }

    [DisallowMultipleRendererFeature("Ho-RT")]
    public sealed class HoRTRendererFeature : ScriptableRendererFeature
    {
        // This registry locates owned domains. It does not own one global AS.
        private static readonly Dictionary<ScriptableRenderer, Domain> active = new();
        private static readonly List<Domain> retiring = new();
        private static readonly List<(Domain domain, IDisposable resource)> retiringResources = new();
        private static bool retirementHooked;
        private readonly Dictionary<ScriptableRenderer, Domain> domains = new();
        private FencePass fencePass;
        public string Status { get; private set; } = "等待 RT 消费者";
        public double LastPrepareWaitMilliseconds { get; private set; }
        internal uint BakeCount { get { uint count = 0; foreach (var d in domains.Values) count += d.geometry.BakeCount; return count; } }
        internal int ViewCount { get { int count = 0; foreach (var d in domains.Values) count += d.views.Count; return count; } }
        internal static int RetiredDomainCount => retiring.Count;
        internal static int RetiredResourceCount => retiringResources.Count;
        internal int ActiveDomainCount => domains.Count;

        internal sealed class View : IDisposable
        {
            internal readonly HoRTScene scene;
            internal readonly IHoRTMaterialProfile profile;
            internal readonly object profileKey;
            internal View(Domain domain, HoRTViewRequest request)
            {
                profileKey = request.profileKey;
                profile = request.createProfile?.Invoke();
                try { scene = new HoRTScene(profile == null ? null : profile.Resolve, profile == null ? null : profile.SupportsCutout, domain.geometry, domain.CaptureInput); }
                catch { profile?.Dispose(); throw; }
            }
            public void Dispose() { scene.Dispose(); profile?.Dispose(); }
        }

        internal sealed class Domain : IDisposable
        {
            internal readonly HoRTRendererFeature owner;
            internal readonly HoRTGeometryCache geometry = new();
            internal readonly Dictionary<Object, View> views = new();
            internal readonly Dictionary<Object, HoRTViewRequest> requests = new();
            internal Camera camera;
            internal ulong epoch;
            internal bool frozen, retired, fenceExpected, fenceRecorded, capturedInput;
            internal GraphicsFence fence;
            internal readonly List<Renderer> candidates = new();
            private readonly List<Object> removed = new();
            internal Domain(HoRTRendererFeature owner) { this.owner = owner; }
            internal void CaptureInput()
            {
                if (retired || !frozen) throw new InvalidOperationException("Ho-RT scene is outside its prepared render epoch");
                capturedInput = true;
            }

            internal bool WaitForPrevious()
            {
                // The initial implementation deliberately serializes mutation.
                // Correct CPU Mesh/material/AS lifetime comes before overlap.
                if (fenceExpected && !fenceRecorded && capturedInput) return false;
                if (!fenceRecorded) return true;
                var watch = Stopwatch.StartNew();
                while (!fence.passed)
                {
                    if (watch.ElapsedMilliseconds > 5000) return false;
                    Thread.Sleep(1);
                }
                return true;
            }
            internal bool CanDispose => !fenceExpected || !capturedInput || (fenceRecorded && fence.passed);
            internal void Begin(Camera nextCamera)
            {
                camera = nextCamera; epoch++; frozen = false;
                requests.Clear(); candidates.Clear();
                fenceExpected = fenceRecorded = false;
                capturedInput = false;
            }
            internal void Prepare()
            {
                removed.Clear();
                foreach (var pair in views)
                    if (pair.Key == null || !requests.TryGetValue(pair.Key, out var request)
                        || !ReferenceEquals(pair.Value.profileKey, request.profileKey)) removed.Add(pair.Key);
                foreach (var key in removed) { views[key].Dispose(); views.Remove(key); }
                // One world scan; individual policies select from this snapshot.
                int union = 0; foreach (var request in requests.Values) union |= request.geometryLayers;
                if (requests.Count > 0)
                    foreach (var item in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                        if ((union & (1 << item.gameObject.layer)) != 0) candidates.Add(item);
                geometry.Prepare(candidates);
                foreach (var pair in requests)
                {
                    if (!views.TryGetValue(pair.Key, out var view))
                    { view = new View(this, pair.Value); views.Add(pair.Key, view); }
                    var selected = new List<Renderer>();
                    foreach (var item in candidates)
                        if ((pair.Value.geometryLayers & (1 << item.gameObject.layer)) != 0) selected.Add(item);
                    view.profile?.BeginPrepare();
                    view.scene.Synchronize(selected, pair.Value.mask, pair.Value.excludedMaterials, pair.Value.geometryPolicy);
                    view.profile?.EndPrepare();
                }
                frozen = true;
            }
            public void Dispose()
            {
                foreach (var view in views.Values) view.Dispose(); views.Clear();
                geometry.Dispose(); requests.Clear(); candidates.Clear();
            }
        }

        public override void Create()
        { RetireDomains(); fencePass = new FencePass(); Status = "等待 RT 消费者"; }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            CollectRetired();
            if (!HoRTScene.IsSupported || !SystemInfo.supportsGraphicsFence)
            { Status = "需要 D3D12 inline RT 与 GraphicsFence"; return; }
            if (!domains.TryGetValue(renderer, out var domain))
            { domain = new Domain(this); domains.Add(renderer, domain); }
            var wait = Stopwatch.StartNew();
            if (!domain.WaitForPrevious())
            { LastPrepareWaitMilliseconds = wait.Elapsed.TotalMilliseconds; active.Remove(renderer); Status = "上一相机场景仍在使用，拒绝修改"; return; }
            LastPrepareWaitMilliseconds = wait.Elapsed.TotalMilliseconds;
            domain.Begin(cameraData.camera); active[renderer] = domain;
        }

        internal static bool Declare(ScriptableRenderer renderer, Camera camera, Object client, HoRTViewRequest request, out string reason)
        {
            if (client == null || request == null) { reason = "无效的 RT 请求"; return false; }
            if (!Find(renderer, camera, out var domain)) { reason = "缺少先准备的 Ho-RT"; return false; }
            if (domain.frozen) { reason = "RT 请求晚于场景冻结"; return false; }
            domain.requests[client] = request; reason = null; return true;
        }

        internal static bool TryAcquire(ScriptableRenderer renderer, Camera camera, Object client, out HoRTView view, out string reason)
        {
            view = default;
            if (!Find(renderer, camera, out var domain)) { reason = "缺少当前相机的 Ho-RT"; return false; }
            if (!domain.frozen || !domain.requests.ContainsKey(client) || !domain.views.TryGetValue(client, out var resources))
            { reason = "RT 场景尚未准备或未声明请求"; return false; }
            view = new HoRTView(domain, resources); reason = null; return true;
        }

        private static bool Find(ScriptableRenderer renderer, Camera camera, out Domain domain)
        {
            if (renderer == null) { domain = null; return false; }
            return active.TryGetValue(renderer, out domain) && !domain.retired
                && domain.owner != null && domain.owner.isActive && domain.camera == camera;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!Find(renderer, renderingData.cameraData.camera, out var domain) || domain.owner != this) return;
            domain.Prepare();
            if (domain.requests.Count == 0) { Status = "没有 RT 请求"; return; }
            domain.fenceExpected = true;
            fencePass.domain = domain; renderer.EnqueuePass(fencePass);
            Status = $"views={domain.views.Count}, epoch={domain.epoch}, bakes={domain.geometry.BakeCount}, wait={LastPrepareWaitMilliseconds:F2}ms";
        }

        private void RetireDomains()
        {
            foreach (var pair in domains)
            {
                pair.Value.retired = true;
                if (active.TryGetValue(pair.Key, out var current) && ReferenceEquals(current, pair.Value)) active.Remove(pair.Key);
                retiring.Add(pair.Value);
            }
            domains.Clear(); CollectRetired();
            EnsureRetirementHook();
        }
        internal static void ReleaseAfterUse(Domain domain, IDisposable resource)
        {
            if (resource == null) return;
            if (domain == null || domain.CanDispose) { resource.Dispose(); return; }
            retiringResources.Add((domain, resource)); EnsureRetirementHook();
        }
        private static void EnsureRetirementHook()
        {
            if ((retiring.Count > 0 || retiringResources.Count > 0) && !retirementHooked)
            {
                retirementHooked = true;
                RenderPipelineManager.endFrameRendering += CollectRetiredFrame;
                #if UNITY_EDITOR
                UnityEditor.EditorApplication.update += CollectRetired;
                #endif
            }
        }
        private static void CollectRetiredFrame(ScriptableRenderContext context, Camera[] cameras) => CollectRetired();
        internal static void CollectRetired()
        {
            for (int i = retiringResources.Count - 1; i >= 0; i--)
                if (retiringResources[i].domain.CanDispose)
                { retiringResources[i].resource.Dispose(); retiringResources.RemoveAt(i); }
            for (int i = retiring.Count - 1; i >= 0; i--)
                if (retiring[i].CanDispose) { retiring[i].Dispose(); retiring.RemoveAt(i); }
            if (retiring.Count == 0 && retiringResources.Count == 0 && retirementHooked)
            {
                retirementHooked = false;
                RenderPipelineManager.endFrameRendering -= CollectRetiredFrame;
                #if UNITY_EDITOR
                UnityEditor.EditorApplication.update -= CollectRetired;
                #endif
            }
        }
        protected override void Dispose(bool disposing) { RetireDomains(); Status = "已释放"; }

        // Explicit source-Mesh edits invalidate every dependent AS view and the
        // shared pose cache together, never just the first caller's private view.
        internal bool MarkGeometryDirty(ScriptableRenderer renderer)
        {
            if (!domains.TryGetValue(renderer, out var domain) || !domain.WaitForPrevious()) return false;
            foreach (var view in domain.views.Values) view.scene.MarkGeometryDirty();
            domain.geometry.MarkGeometryDirty(); domain.frozen = false; return true;
        }

        private sealed class FencePass : ScriptableRenderPass
        {
            internal Domain domain;
            internal FencePass() { renderPassEvent = RenderPassEvent.AfterRendering; }
            private sealed class Data { internal Domain domain; }
            private static void RecordFence(Domain domain, CommandBuffer cmd)
            {
                domain.fence = cmd.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.AllGPUOperations);
                domain.fenceRecorded = true;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                using (var builder = graph.AddUnsafePass<Data>("Ho-RT scene lifetime fence", out var data))
                {
                    data.domain = domain; builder.EnableAsyncCompute(false);
                    builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (Data d, UnsafeGraphContext context) =>
                        RecordFence(d.domain, CommandBufferHelpers.GetNativeCommandBuffer(context.cmd)));
                }
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get("Ho-RT scene lifetime fence");
                RecordFence(domain, cmd); context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            }
        }
    }
}
