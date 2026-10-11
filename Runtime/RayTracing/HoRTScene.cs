using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.RayTracing
{
    // Explicit, camera-independent registry for opaque mesh world visibility.
    // No lighting, material evaluation, renderer feature or automatic scene scan.
    internal sealed class HoRTScene : IDisposable
    {
        private static long nextLifetime;
        private static readonly int AccelerationId = Shader.PropertyToID("_HoRTSceneAccelerationStructure");
        private static readonly int InstancesId = Shader.PropertyToID("_HoRTSceneInstances");
        private static readonly int StateId = Shader.PropertyToID("_HoRTSceneState");
        private readonly Dictionary<(int renderer, int submesh), Entry> entries = new();
        private readonly Dictionary<uint, Entry> identities = new();
        private readonly Dictionary<Material, uint> materialIds = new(MaterialReferences.Instance);
        private readonly HashSet<(int renderer, int submesh)> seen = new();
        private readonly HashSet<int> seenRenderers = new();
        private readonly List<(int renderer, int submesh)> removed = new();
        private readonly Dictionary<int, HoRTSkinnedMesh> skinnedMeshes = new();
        private readonly HashSet<int> seenSkinned = new();
        private readonly List<int> removedSkinned = new();
        private readonly Func<Material, Material> secondaryMaterial;
        private readonly Func<Material, bool> supportsCutout;
        internal readonly List<string> Rejections = new();
        private RayTracingAccelerationStructure acceleration;
        private GraphicsBuffer instanceBuffer;
        private InstanceData[] instanceData;
        private uint nextIdentity = 1, nextMaterial = 1;
        private bool asDirty = true, metadataDirty = true, disposed;
        internal long Lifetime { get; } = Interlocked.Increment(ref nextLifetime);
        internal uint Revision { get; private set; } = 1;
        internal uint BuildCount { get; private set; }
        internal int InstanceCount => entries.Count;
        internal int CutoutCount { get; private set; }
        internal int SkinnedMeshCount => skinnedMeshes.Count;
        internal uint SkinnedBakeCount { get; private set; }
        internal bool IsAvailable => !disposed && acceleration != null;
        internal static bool IsSupported => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 && SystemInfo.supportsInlineRayTracing;

        [StructLayout(LayoutKind.Sequential)]
        private struct InstanceData { public uint submesh, material, triangles, valid; }

        private sealed class MaterialReferences : IEqualityComparer<Material>
        {
            internal static readonly MaterialReferences Instance = new();
            public bool Equals(Material a, Material b) => ReferenceEquals(a, b);
            public int GetHashCode(Material material) => RuntimeHelpers.GetHashCode(material);
        }

        private sealed class Entry
        {
            internal Renderer renderer;
            internal Mesh mesh;
            internal Material material;
            internal Material hitMaterial;
            internal uint identity, materialId, triangles, mask;
            internal int submesh, handle;
            internal bool cutout;
            internal Matrix4x4 matrix;
        }

        internal readonly struct Instance
        {
            internal readonly uint identity, materialId, triangleCount;
            internal readonly Renderer renderer;
            internal readonly Mesh mesh;
            internal readonly Material material;
            internal readonly int submesh;
            internal Instance(uint identity, uint materialId, uint triangleCount, Renderer renderer, Mesh mesh, Material material, int submesh)
            { this.identity = identity; this.materialId = materialId; this.triangleCount = triangleCount; this.renderer = renderer; this.mesh = mesh; this.material = material; this.submesh = submesh; }
        }

        internal HoRTScene(Func<Material, Material> secondaryMaterial = null, Func<Material, bool> supportsCutout = null)
        {
            this.secondaryMaterial = secondaryMaterial;
            this.supportsCutout = supportsCutout;
            if (!IsSupported) return;
            acceleration = new RayTracingAccelerationStructure(new RayTracingAccelerationStructure.Settings
            {
                managementMode = RayTracingAccelerationStructure.ManagementMode.Manual,
                rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                buildFlagsStaticGeometries = RayTracingAccelerationStructureBuildFlags.PreferFastTrace
            });
            EnsureMetadataCapacity();
        }

        // Call before graph recording, never between Read and its execution. The list is the
        // world set, not camera culling results. Mesh content edits require MarkGeometryDirty.
        internal void Synchronize(IReadOnlyList<Renderer> renderers, uint mask = 0xff)
        {
            ThrowIfDisposed();
            if (renderers == null) throw new ArgumentNullException(nameof(renderers));
            if (mask > 0xff) throw new ArgumentOutOfRangeException(nameof(mask), "RT instance masks are eight bits.");
            Rejections.Clear(); seen.Clear(); seenRenderers.Clear(); seenSkinned.Clear();
            if (!IsAvailable) { Rejections.Add("D3D12 inline ray tracing is unavailable."); return; }
            bool changed = false;
            foreach (Renderer candidate in renderers)
            {
                if (candidate == null || !seenRenderers.Add(candidate.GetInstanceID())) continue;
                if (!candidate.enabled || !candidate.gameObject.activeInHierarchy || candidate.forceRenderingOff) continue;
                var renderer = candidate;
                var rigid = renderer as MeshRenderer; var skin = renderer as SkinnedMeshRenderer;
                if (rigid == null && skin == null) { Rejections.Add(candidate.name + ": requires MeshRenderer or SkinnedMeshRenderer."); continue; }
                var filter = rigid != null ? rigid.GetComponent<MeshFilter>() : null;
                var mesh = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
                if (mesh == null || renderer.isPartOfStaticBatch || (rigid != null && rigid.additionalVertexStreams != null))
                { Rejections.Add(renderer.name + ": missing mesh, static batch or additional vertex stream."); continue; }
                var materials = renderer.sharedMaterials;
                if (materials.Length != mesh.subMeshCount)
                { Rejections.Add(renderer.name + ": requires exactly one material per submesh."); continue; }
                bool poseChanged = false;
                if (skin != null)
                {
                    if (!ValidTransform(renderer.localToWorldMatrix))
                    { Rejections.Add(renderer.name + ": non-finite or singular transform."); continue; }
                    bool hasOpaque = false;
                    for (int i = 0; i < materials.Length; ++i) hasOpaque |= Opaque(materials[i]) || (supportsCutout != null && supportsCutout(materials[i]));
                    if (hasOpaque)
                    {
                        int id = skin.GetInstanceID();
                        if (!skinnedMeshes.TryGetValue(id, out var snapshot) || !ReferenceEquals(snapshot.renderer, skin))
                        {
                            snapshot?.Dispose(); snapshot = new HoRTSkinnedMesh(skin); skinnedMeshes[id] = snapshot;
                        }
                        if (!snapshot.Synchronize(out poseChanged))
                        { Rejections.Add(renderer.name + ": missing or non-finite skin pose."); continue; }
                        seenSkinned.Add(id); mesh = snapshot.mesh;
                        if (poseChanged) SkinnedBakeCount++;
                    }
                }
                for (int submesh = 0; submesh < mesh.subMeshCount; ++submesh)
                {
                    var material = materials[submesh]; var descriptor = mesh.GetSubMesh(submesh);
                    bool cutout = !Opaque(material) && supportsCutout != null && supportsCutout(material);
                    if ((!Opaque(material) && !cutout) || mesh.GetTopology(submesh) != MeshTopology.Triangles || descriptor.indexCount == 0 || descriptor.indexCount % 3 != 0)
                    { Rejections.Add(renderer.name + " submesh " + submesh + ": requires opaque triangle geometry and a material."); continue; }
                    if (cutout && (renderer.HasPropertyBlock() || !mesh.HasVertexAttribute(VertexAttribute.TexCoord0) || mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0) < 2))
                    { Rejections.Add(renderer.name + " submesh " + submesh + ": cutout requires UV0 and no material property block."); continue; }
                    var key = (renderer.GetInstanceID(), submesh); seen.Add(key);
                    var hitMaterial = secondaryMaterial?.Invoke(material);
                    var matrix = renderer.localToWorldMatrix; uint triangles = (uint)(descriptor.indexCount / 3);
                    if (!ValidTransform(matrix))
                    { seen.Remove(key); Rejections.Add(renderer.name + ": non-finite or singular transform."); continue; }
                    if (entries.TryGetValue(key, out var entry) && (!ReferenceEquals(entry.renderer, renderer) || !ReferenceEquals(entry.mesh, mesh) || entry.triangles != triangles || !ReferenceEquals(entry.hitMaterial, hitMaterial) || entry.cutout != cutout))
                    { Remove(key); entry = null; changed = true; }
                    if (entry == null)
                    {
                        if (nextIdentity >= 0x1000000) throw new InvalidOperationException("RT instance ID domain exhausted; recreate the scene service.");
                        uint identity = nextIdentity++;
                        // Opaque two-sided reference geometry; no ShaderPass/material hit group is needed for inline queries.
                        var config = new RayTracingMeshInstanceConfig(mesh, (uint)submesh, hitMaterial)
                        {
                            mask = mask, enableTriangleCulling = false,
                            // Allow explicit geometry refresh instead of reusing a static BLAS after a Mesh edit.
                            rayTracingMode = UnityEngine.Experimental.Rendering.RayTracingMode.DynamicGeometryManualUpdate,
                            subMeshFlags = cutout ? RayTracingSubMeshFlags.Enabled : RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly
                        };
                        int handle = acceleration.AddInstance(config, matrix, null, identity);
                        // Native instance handles are opaque signed tokens, not positive array indices.
                        acceleration.UpdateInstanceGeometry(handle);
                        entry = new Entry { renderer = renderer, mesh = mesh, material = material, submesh = submesh,
                            identity = identity, handle = handle, matrix = matrix, triangles = triangles, mask = mask, materialId = MaterialIdentity(material), hitMaterial = hitMaterial, cutout = cutout };
                        entries.Add(key, entry); identities.Add(identity, entry); asDirty = metadataDirty = true; changed = true;
                    }
                    else
                    {
                        if (poseChanged) { acceleration.UpdateInstanceGeometry(entry.handle); asDirty = true; changed = true; }
                        if (entry.matrix != matrix) { acceleration.UpdateInstanceTransform(entry.handle, matrix); entry.matrix = matrix; asDirty = true; changed = true; }
                        if (entry.mask != mask) { acceleration.UpdateInstanceMask(entry.handle, mask); entry.mask = mask; asDirty = true; changed = true; }
                        if (!ReferenceEquals(entry.material, material)) { entry.material = material; entry.materialId = MaterialIdentity(material); metadataDirty = true; changed = true; }
                    }
                }
            }
            removed.Clear(); foreach (var pair in entries) if (!seen.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (var key in removed) { Remove(key); changed = true; }
            removedSkinned.Clear(); foreach (var pair in skinnedMeshes) if (!seenSkinned.Contains(pair.Key)) removedSkinned.Add(pair.Key);
            foreach (int id in removedSkinned) { skinnedMeshes[id].Dispose(); skinnedMeshes.Remove(id); }
            CutoutCount = 0; foreach (var entry in entries.Values) if (entry.cutout) CutoutCount++;
            if (changed) { unchecked { Revision++; } EnsureMetadataCapacity(); }
        }

        private static bool Opaque(Material material) => material != null && material.renderQueue < (int)RenderQueue.AlphaTest
            && material.GetTag("RenderType", false) != "Transparent" && material.GetTag("RenderType", false) != "TransparentCutout"
            && !material.IsKeywordEnabled("_ALPHATEST_ON") && (!material.HasProperty("_AlphaClip") || material.GetFloat("_AlphaClip") < .5f);

        private uint MaterialIdentity(Material material)
        {
            if (!materialIds.TryGetValue(material, out uint id)) { id = nextMaterial++; materialIds.Add(material, id); }
            return id;
        }

        private static bool ValidTransform(Matrix4x4 matrix)
        {
            for (int i = 0; i < 16; ++i) if (float.IsNaN(matrix[i]) || float.IsInfinity(matrix[i])) return false;
            float determinant = matrix.determinant;
            return determinant != 0 && !float.IsNaN(determinant) && !float.IsInfinity(determinant);
        }

        private void Remove((int renderer, int submesh) key)
        {
            var entry = entries[key];
            // Unity already removes mesh instances when the Mesh itself is destroyed.
            if (entry.mesh != null) acceleration.RemoveInstance(entry.handle);
            entries.Remove(key); identities.Remove(entry.identity); asDirty = metadataDirty = true;
        }

        internal void MarkGeometryDirty()
        {
            ThrowIfDisposed();
            // Explicit full re-registration keeps old primitive IDs from referring to edited topology.
            if (!IsAvailable) return;
            acceleration.ClearInstances(); entries.Clear(); identities.Clear();
            CutoutCount = 0;
            foreach (var snapshot in skinnedMeshes.Values) snapshot.Dispose(); skinnedMeshes.Clear();
            asDirty = metadataDirty = true; unchecked { Revision++; }
        }

        internal bool TryResolve(uint identity, out Instance instance)
        {
            ThrowIfDisposed();
            if (identities.TryGetValue(identity, out var e))
            { instance = new Instance(e.identity, e.materialId, e.triangles, e.renderer, e.mesh, e.material, e.submesh); return true; }
            instance = default; return false;
        }

        private void EnsureMetadataCapacity()
        {
            if (!IsAvailable) return;
            int count = Mathf.NextPowerOfTwo(Mathf.Max(16, (int)nextIdentity));
            if (instanceBuffer != null && instanceBuffer.count >= count) return;
            instanceBuffer?.Dispose(); instanceData = new InstanceData[count];
            instanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, Marshal.SizeOf<InstanceData>()) { name = "HoRTScene instance mapping" };
            metadataDirty = true;
        }

        internal readonly struct Inputs
        {
            private readonly HoRTScene scene;
            private readonly uint revision;
            internal Inputs(HoRTScene scene) { this.scene = scene; revision = scene.Revision; }
            private void Build(CommandBuffer cmd)
            {
                scene.ThrowIfDisposed();
                if (!scene.IsAvailable || scene.Revision != revision) throw new InvalidOperationException("RT scene unavailable or changed after graph recording.");
                if (scene.metadataDirty)
                {
                    Array.Clear(scene.instanceData, 0, scene.instanceData.Length);
                    foreach (var e in scene.entries.Values) scene.instanceData[e.identity] = new InstanceData { submesh = (uint)e.submesh, material = e.materialId, triangles = e.triangles, valid = 1 };
                    cmd.SetBufferData(scene.instanceBuffer, scene.instanceData); scene.metadataDirty = false;
                }
                if (scene.asDirty) { cmd.BuildRayTracingAccelerationStructure(scene.acceleration); scene.asDirty = false; scene.BuildCount++; }
            }
            internal void BuildAndBind(CommandBuffer cmd, ComputeShader shader, int kernel)
            {
                Build(cmd);
                cmd.SetRayTracingAccelerationStructure(shader, kernel, AccelerationId, scene.acceleration);
                cmd.SetComputeBufferParam(shader, kernel, InstancesId, scene.instanceBuffer);
                cmd.SetComputeVectorParam(shader, StateId, new Vector4(1, scene.instanceBuffer.count, scene.entries.Count, 0));
            }
            internal void BuildAndBind(CommandBuffer cmd, RayTracingShader shader)
            {
                Build(cmd);
                cmd.SetRayTracingAccelerationStructure(shader, AccelerationId, scene.acceleration);
            }
        }

        // AS has no native RenderGraph handle: consumers must use a non-async unsafe pass,
        // AllowGlobalStateModification(true) and AllowPassCulling(false) for build/trace ordering.
        internal Inputs Read(RenderGraph graph, IUnsafeRenderGraphBuilder builder)
        {
            ThrowIfDisposed(); if (!IsAvailable) throw new NotSupportedException("D3D12 inline ray tracing is unavailable.");
            builder.EnableAsyncCompute(false);
            builder.AllowGlobalStateModification(true);
            builder.AllowPassCulling(false);
            builder.UseBuffer(graph.ImportBuffer(instanceBuffer), AccessFlags.ReadWrite);
            return new Inputs(this);
        }
        internal Inputs ReadCompatibility()
        {
            ThrowIfDisposed(); if (!IsAvailable) throw new NotSupportedException("D3D12 inline ray tracing is unavailable.");
            return new Inputs(this);
        }
        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(HoRTScene)); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            acceleration?.Dispose(); acceleration = null; instanceBuffer?.Dispose(); instanceBuffer = null;
            foreach (var snapshot in skinnedMeshes.Values) snapshot.Dispose(); skinnedMeshes.Clear();
            entries.Clear(); identities.Clear(); materialIds.Clear(); instanceData = null;
        }
    }
}
