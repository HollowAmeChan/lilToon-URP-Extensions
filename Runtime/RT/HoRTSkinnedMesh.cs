using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.RT
{
    // An owned current-pose mesh for the world visibility registry. Raster data is untouched.
    // Unchanged source geometry and skin poses reuse the snapshot and BLAS.
    internal sealed class HoRTSkinnedMesh : IDisposable
    {
        internal readonly SkinnedMeshRenderer renderer;
        internal readonly Mesh mesh;
        private Mesh source;
        private Matrix4x4[] boneMatrices = Array.Empty<Matrix4x4>();
        private float[] blendWeights = Array.Empty<float>();
        private SkinQuality quality;
        private SkinWeights globalQuality;
        private ulong geometryHash;
        private int geometryVertexCount;
        private int geometrySubMeshCount;
        private bool[] usedBones = Array.Empty<bool>();
        private readonly List<Vector3> positions = new();
        private readonly List<Vector3> normals = new();
        private readonly List<Vector4> tangents = new();
        private bool initialized;

        internal HoRTSkinnedMesh(SkinnedMeshRenderer renderer)
        {
            this.renderer = renderer;
            mesh = new Mesh { name = "Ho RT current pose: " + renderer.name, hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
        }

        internal bool Synchronize(out bool changed)
        {
            changed = false;
            if (renderer == null) return false;
            var current = renderer.sharedMesh;
            var bones = renderer.bones;
            if (bones == null) return false;
            if (current == null) return false;
            // MeshCloth writes a live Mesh without changing its object identity,
            // bone matrices or blend weights. Include the CPU deformation inputs.
            bool geometryReadable = current.isReadable;
            ulong currentGeometryHash = 0;
            if (geometryReadable && !TryReadGeometry(current, bones.Length, out currentGeometryHash)) return false;
            var inverse = renderer.worldToLocalMatrix;
            for (int boneIndex = 0; boneIndex < bones.Length; ++boneIndex)
            {
                var bone = bones[boneIndex];
                // Use the CURRENT mesh's weights after runtime remapping. An
                // unused null palette slot does not invalidate the whole skin.
                // A genuinely weighted missing bone must still fail closed.
                if (bone == null)
                {
                    if (!geometryReadable || usedBones[boneIndex]) return false;
                    continue;
                }
                var matrix = inverse * bone.localToWorldMatrix;
                for (int i = 0; i < 16; ++i)
                    if (float.IsNaN(matrix[i]) || float.IsInfinity(matrix[i])) return false;
            }
            for (int i = 0; i < current.blendShapeCount; ++i)
            {
                float weight = renderer.GetBlendShapeWeight(i);
                if (float.IsNaN(weight) || float.IsInfinity(weight)) return false;
            }
            changed = !initialized || !ReferenceEquals(source, current) || quality != renderer.quality
                || globalQuality != QualitySettings.skinWeights || boneMatrices.Length != bones.Length
                || blendWeights.Length != current.blendShapeCount
                || !geometryReadable
                || geometryVertexCount != current.vertexCount
                || geometrySubMeshCount != current.subMeshCount
                || geometryHash != currentGeometryHash;
            if (boneMatrices.Length != bones.Length) boneMatrices = new Matrix4x4[bones.Length];
            if (blendWeights.Length != current.blendShapeCount) blendWeights = new float[current.blendShapeCount];
            for (int i = 0; i < bones.Length; ++i)
            {
                var bone = bones[i];
                var matrix = bone == null ? default : inverse * bone.localToWorldMatrix;
                changed |= !boneMatrices[i].Equals(matrix);
                boneMatrices[i] = matrix;
            }
            for (int i = 0; i < blendWeights.Length; ++i)
            {
                float weight = renderer.GetBlendShapeWeight(i);
                changed |= blendWeights[i] != weight;
                blendWeights[i] = weight;
            }
            if (!changed) return true;
            // Compensate the renderer scale; the AS applies localToWorld exactly once.
            renderer.BakeMesh(mesh, true);
            mesh.GetVertices(positions);
            if (positions.Count != current.vertexCount) { changed = false; return false; }
            foreach (var p in positions)
                if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z)) { changed = false; return false; }
            HoRTRayMeshIndices.CanonicalizeOwned(mesh);
            source = current; quality = renderer.quality; globalQuality = QualitySettings.skinWeights;
            geometryHash = currentGeometryHash;
            geometryVertexCount = current.vertexCount;
            geometrySubMeshCount = current.subMeshCount;
            initialized = true;
            return true;
        }

        private bool TryReadGeometry(Mesh current, int boneCount, out ulong hash)
        {
            hash = 1469598103934665603UL;
            current.GetVertices(positions);
            current.GetNormals(normals);
            current.GetTangents(tangents);
            foreach (var p in positions)
            {
                if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z)) return false;
                hash = Hash(Hash(Hash(hash, p.x.GetHashCode()), p.y.GetHashCode()), p.z.GetHashCode());
            }
            foreach (var n in normals)
                hash = Hash(Hash(Hash(hash, n.x.GetHashCode()), n.y.GetHashCode()), n.z.GetHashCode());
            foreach (var t in tangents)
                hash = Hash(Hash(Hash(Hash(hash, t.x.GetHashCode()), t.y.GetHashCode()), t.z.GetHashCode()), t.w.GetHashCode());
            var bindposes = current.bindposes;
            foreach (var bindpose in bindposes)
                for (int i = 0; i < 16; ++i) hash = Hash(hash, bindpose[i].GetHashCode());
            if (usedBones.Length != boneCount) usedBones = new bool[boneCount];
            else Array.Clear(usedBones, 0, usedBones.Length);
            using (var counts = current.GetBonesPerVertex())
            using (var weights = current.GetAllBoneWeights())
            {
                foreach (byte count in counts) hash = Hash(hash, count);
                foreach (var weight in weights)
                {
                    hash = Hash(Hash(hash, weight.boneIndex), weight.weight.GetHashCode());
                    if (!Finite(weight.weight) || weight.weight < 0f) return false;
                    if (weight.weight == 0f) continue;
                    if (weight.boneIndex < 0 || weight.boneIndex >= boneCount
                        || weight.boneIndex >= bindposes.Length) return false;
                    usedBones[weight.boneIndex] = true;
                }
            }
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static ulong Hash(ulong hash, int value) => unchecked((hash ^ (uint)value) * 1099511628211UL);
        public void Dispose() => CoreUtils.Destroy(mesh);
    }
}
