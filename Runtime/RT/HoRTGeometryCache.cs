using System;
using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.RT
{
    // The owner prepares once before every scene view synchronizes. Preparing
    // mutates native mesh data: the host must first retire the previous GPU use.
    internal sealed class HoRTGeometryCache : IDisposable
    {
        private readonly Dictionary<int, Pose> poses = new();
        private readonly HashSet<Pose> alive = new();
        private readonly HashSet<int> seen = new();
        private readonly List<int> removed = new();
        private ulong nextPoseVersion;
        private bool disposed;
        internal ulong PrepareVersion { get; private set; }
        internal uint BakeCount { get; private set; }
        internal int OwnedMeshCount => alive.Count;
        internal int PreparedPoseCount => poses.Count;

        internal sealed class Pose
        {
            internal HoRTSkinnedMesh snapshot;
            internal ulong version;
            internal bool owned = true;
            internal int references;
        }

        internal sealed class PoseLease : IDisposable
        {
            private HoRTGeometryCache owner;
            private Pose pose;
            internal PoseLease(HoRTGeometryCache owner, Pose pose)
            {
                this.owner = owner; this.pose = pose; pose.references++;
            }
            internal SkinnedMeshRenderer renderer => pose?.snapshot.renderer;
            internal Mesh mesh => pose?.snapshot.mesh;
            internal ulong Version => pose?.version ?? 0;
            public void Dispose()
            {
                if (pose == null) return;
                var previous = pose; pose = null;
                previous.references--; owner.ReleaseIfUnused(previous); owner = null;
            }
        }

        internal void Prepare(IReadOnlyList<Renderer> renderers)
        {
            ThrowIfDisposed();
            if (renderers == null) throw new ArgumentNullException(nameof(renderers));
            PrepareVersion = checked(PrepareVersion + 1); seen.Clear();
            foreach (var candidate in renderers)
            {
                var skin = candidate as SkinnedMeshRenderer;
                if (skin == null || !skin.enabled || !skin.gameObject.activeInHierarchy || skin.forceRenderingOff) continue;
                int id = skin.GetInstanceID();
                if (!seen.Add(id)) continue;
                if (poses.TryGetValue(id, out var pose) && !ReferenceEquals(pose.snapshot.renderer, skin))
                { Retire(id, pose); pose = null; }
                if (!ValidTransform(skin.localToWorldMatrix))
                { if (pose != null) Retire(id, pose); continue; }
                if (pose == null)
                {
                    pose = new Pose { snapshot = new HoRTSkinnedMesh(skin) };
                    poses.Add(id, pose); alive.Add(pose);
                }
                if (!pose.snapshot.Synchronize(out bool changed)) { Retire(id, pose); continue; }
                if (changed) { pose.version = checked(++nextPoseVersion); BakeCount++; }
            }
            removed.Clear();
            foreach (var pair in poses) if (!seen.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (int id in removed) Retire(id, poses[id]);
        }

        internal bool TryGetPose(SkinnedMeshRenderer renderer, out Mesh mesh, out ulong version)
        {
            ThrowIfDisposed(); mesh = null; version = 0;
            if (PrepareVersion == 0 || renderer == null || !poses.TryGetValue(renderer.GetInstanceID(), out var pose)
                || !ReferenceEquals(pose.snapshot.renderer, renderer)) return false;
            mesh = pose.snapshot.mesh; version = pose.version;
            return mesh != null && version != 0;
        }

        internal bool TryAcquire(SkinnedMeshRenderer renderer, out PoseLease lease)
        {
            lease = null;
            if (!TryGetPose(renderer, out _, out _)) return false;
            lease = new PoseLease(this, poses[renderer.GetInstanceID()]);
            return true;
        }

        // Source Mesh edits are explicit, as they are for HoRTScene. Existing
        // leases keep the old mesh alive until all referencing AS views retire.
        internal void MarkGeometryDirty()
        {
            ThrowIfDisposed(); PrepareVersion = checked(PrepareVersion + 1);
            removed.Clear(); foreach (var key in poses.Keys) removed.Add(key);
            foreach (int id in removed) Retire(id, poses[id]);
        }

        private static bool ValidTransform(Matrix4x4 matrix)
        {
            for (int i = 0; i < 16; i++) if (float.IsNaN(matrix[i]) || float.IsInfinity(matrix[i])) return false;
            float determinant = matrix.determinant;
            return determinant != 0 && !float.IsNaN(determinant) && !float.IsInfinity(determinant);
        }
        private void Retire(int id, Pose pose)
        {
            poses.Remove(id); pose.owned = false; ReleaseIfUnused(pose);
        }
        private void ReleaseIfUnused(Pose pose)
        {
            if (pose.owned || pose.references != 0) return;
            pose.snapshot.Dispose(); alive.Remove(pose);
        }
        private void ThrowIfDisposed()
        { if (disposed) throw new ObjectDisposedException(nameof(HoRTGeometryCache)); }
        public void Dispose()
        {
            if (disposed) return;
            MarkGeometryDirty(); disposed = true;
            // Outstanding scene leases retain their snapshots. Their owner must
            // release AS resources before releasing those leases.
        }
    }
}
