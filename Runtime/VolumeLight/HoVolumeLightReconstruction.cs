using System.Collections.Generic;
using lilToon.URP.Extensions.Atmosphere;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.VolumeLight
{
    // The scene's atmosphere is shared; only observer histories belong here.
    internal sealed class HoVolumeLightReconstruction
    {
        private readonly Dictionary<int, HoVolumeLightHistory> histories = new();
        private readonly List<int> stale = new();
        internal int Count => histories.Count;
        internal void Dispose()
        {
            foreach (var h in histories.Values) h.Dispose();
            histories.Clear();
        }
        internal void Remove(Camera camera)
        {
            int id = camera.GetInstanceID();
            if (histories.TryGetValue(id, out var h)) { h.Dispose(); histories.Remove(id); }
        }
        private HoVolumeLightHistory Get(Camera camera, int width, int height)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            stale.Clear();
            foreach (var pair in histories)
                if (pair.Value.camera == null || now - pair.Value.lastUse > 10) stale.Add(pair.Key);
            foreach (int id in stale) { histories[id].Dispose(); histories.Remove(id); }
            int key = camera.GetInstanceID();
            if (!histories.TryGetValue(key, out var history)) { history = new HoVolumeLightHistory(camera); histories.Add(key, history); }
            history.Ensure(width, height); history.lastUse = now;
            return history;
        }
        private sealed class ResolveData
        {
            internal Material material;
            internal TextureHandle current, depth, previous, previousDepth;
            internal Matrix4x4 previousVP, previousInverseVP, currentVP, projection;
            internal Vector4 previousPosition, sampling, parameters, historyParameters;
            internal HoAtmosphereMediumResources medium;
            internal HoVolumeLightHistory history;
            internal Vector3 position, forward;
            internal bool orthographic;
            internal int writeIndex, signature;
            internal float time;
        }
        private sealed class PresentData { internal Material material; internal TextureHandle source; internal int pass; }

        internal TextureHandle Record(RenderGraph graph, ContextContainer frame, TextureHandle current, Material material,
            Vector4 parameters, Vector4 sampling, HoAtmosphereMediumResources medium, HoVolumeLightAntialiasing mode, int signature)
        {
            var cameraData = frame.Get<UniversalCameraData>();
            var resources = frame.Get<UniversalResourceData>();
            Camera camera = cameraData.camera;
            int width = (int)sampling.z, height = (int)sampling.w;
            bool temporal = mode == HoVolumeLightAntialiasing.Temporal;
            var history = temporal ? Get(camera, width, height) : null;
            // RG camera targets are textures. The compatibility getter dereferences legacy RTHandles.
            Matrix4x4 vp = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true) * cameraData.GetViewMatrix();
            Matrix4x4 projection = camera.nonJitteredProjectionMatrix;
            bool reuse = temporal && history.valid && history.signature == signature && history.orthographic == camera.orthographic &&
                Vector3.Distance(history.position, camera.transform.position) < .5f && Vector3.Dot(history.forward, camera.transform.forward) > .98f &&
                (history.projection.GetColumn(0) - projection.GetColumn(0)).sqrMagnitude < 1e-6f &&
                (history.projection.GetColumn(1) - projection.GetColumn(1)).sqrMagnitude < 1e-6f &&
                (history.projection.GetColumn(2) - projection.GetColumn(2)).sqrMagnitude < 1e-6f;
            float elapsed = temporal ? Mathf.Abs(medium.snapshot.sampleTime - history.sampleTime) : 0;
            float maxWeight = .92f;
            if (medium.snapshot.noise.z > .5f)
            {
                // Flowing world noise must not freeze into camera history.
                float speed = ((Vector3)medium.snapshot.wind).magnitude * medium.snapshot.noise.x;
                maxWeight *= Mathf.Exp(-elapsed * speed * 32);
                reuse &= elapsed < .25f;
            }
            var colorDesc = new TextureDesc(width, height) { name = "Ho-VolumeLight Spatial", colorFormat = GraphicsFormat.R16G16B16A16_SFloat, clearBuffer = false };
            var depthDesc = new TextureDesc(width, height) { name = "Ho-VolumeLight Depth", colorFormat = GraphicsFormat.R32_SFloat, clearBuffer = false };
            int write = temporal ? 1 - history.index : 0;
            TextureHandle destination = temporal ? graph.ImportTexture(history.color[write]) : graph.CreateTexture(colorDesc);
            TextureHandle depthTarget = temporal ? graph.ImportTexture(history.depth[write]) : graph.CreateTexture(depthDesc);
            using (var builder = graph.AddRasterRenderPass<ResolveData>("Ho-VolumeLight Reconstruct", out var data))
            {
                data.material = material; data.current = current; data.depth = resources.cameraDepthTexture;
                data.parameters = parameters; data.sampling = sampling; data.medium = medium;
                data.historyParameters = new Vector4(reuse ? 1 : 0, maxWeight, width, height);
                data.history = history; data.writeIndex = write; data.signature = signature;
                data.currentVP = vp; data.projection = projection; data.position = camera.transform.position; data.forward = camera.transform.forward;
                data.orthographic = camera.orthographic; data.time = medium.snapshot.sampleTime;
                builder.UseTexture(current, AccessFlags.Read); builder.UseTexture(data.depth, AccessFlags.Read);
                builder.UseTexture(medium.densityTexture, AccessFlags.Read);
                if (reuse)
                {
                    data.previous = graph.ImportTexture(history.color[history.index]); data.previousDepth = graph.ImportTexture(history.depth[history.index]);
                    builder.UseTexture(data.previous, AccessFlags.Read); builder.UseTexture(data.previousDepth, AccessFlags.Read);
                    data.previousVP = history.viewProjection; data.previousInverseVP = history.inverseViewProjection;
                    data.previousPosition = new Vector4(history.position.x, history.position.y, history.position.z, history.orthographic ? 1 : 0);
                }
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll); builder.SetRenderAttachment(depthTarget, 1, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ResolveData d, RasterGraphContext context) =>
                {
                    HoAtmosphereMediumBindings.Bind(context.cmd, d.medium, d.time);
                    context.cmd.SetGlobalTexture("_HoVolumeLightCurrent", d.current); context.cmd.SetGlobalTexture("_CameraDepthTexture", d.depth);
                    context.cmd.SetGlobalVector("_HoVolumeLightParams", d.parameters); context.cmd.SetGlobalVector("_HoVolumeLightSampling", d.sampling);
                    context.cmd.SetGlobalVector("_HoVolumeLightHistory", d.historyParameters);
                    if (d.historyParameters.x > .5f)
                    {
                        context.cmd.SetGlobalTexture("_HoVolumeLightPrevious", d.previous); context.cmd.SetGlobalTexture("_HoVolumeLightPreviousDepth", d.previousDepth);
                        context.cmd.SetGlobalMatrix("_HoVolumeLightPreviousVP", d.previousVP); context.cmd.SetGlobalMatrix("_HoVolumeLightPreviousInverseVP", d.previousInverseVP);
                        context.cmd.SetGlobalVector("_HoVolumeLightPreviousPosition", d.previousPosition);
                    }
                    Blitter.BlitTexture(context.cmd, new Vector4(1, 1, 0, 0), d.material, 3);
                    if (d.history != null)
                    {
                        var h = d.history;
                        h.index = d.writeIndex; h.signature = d.signature; h.viewProjection = d.currentVP; h.inverseViewProjection = d.currentVP.inverse;
                        h.position = d.position; h.forward = d.forward; h.projection = d.projection; h.orthographic = d.orthographic;
                        h.sampleTime = d.time; h.valid = true; h.sequence = (h.sequence + 1) % 4096;
                    }
                });
            }
            return destination;
        }
        internal int Sequence(Camera camera) => histories.TryGetValue(camera.GetInstanceID(), out var history) ? history.sequence : 0;
        internal static void Present(RenderGraph graph, Material material, TextureHandle source, TextureHandle target, bool debug)
        {
            using (var builder = graph.AddRasterRenderPass<PresentData>(debug ? "Ho-VolumeLight Debug" : "Ho-VolumeLight Composite", out var data))
            {
                data.material = material; data.source = source; data.pass = debug ? 5 : 4;
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(target, 0, debug ? AccessFlags.WriteAll : AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PresentData d, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture("_HoVolumeLightCurrent", d.source);
                    Blitter.BlitTexture(context.cmd, new Vector4(1, 1, 0, 0), d.material, d.pass);
                });
            }
        }
    }
}
