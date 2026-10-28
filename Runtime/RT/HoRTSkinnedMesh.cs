using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.RT
{
    // An owned current-pose mesh for the world visibility registry. Raster data is untouched.
    // BakeMesh is CPU work: unchanged bone/blend-shape poses reuse the snapshot and BLAS.
    internal sealed class HoRTSkinnedMesh : IDisposable
    {
        internal readonly SkinnedMeshRenderer renderer;
        internal readonly Mesh mesh;
        private Mesh source;
        private Matrix4x4[] boneMatrices = Array.Empty<Matrix4x4>();
        private float[] blendWeights = Array.Empty<float>();
        private SkinQuality quality;
        private SkinWeights globalQuality;
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
            var current = renderer.sharedMesh;
            var bones = renderer.bones;
            if (current == null) return false;
            var inverse = renderer.worldToLocalMatrix;
            // A missing/non-finite bone must not leave an older pose casting shadows.
            foreach (var bone in bones)
            {
                if (bone == null) return false;
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
                || blendWeights.Length != current.blendShapeCount;
            if (boneMatrices.Length != bones.Length) boneMatrices = new Matrix4x4[bones.Length];
            if (blendWeights.Length != current.blendShapeCount) blendWeights = new float[current.blendShapeCount];
            for (int i = 0; i < bones.Length; ++i)
            {
                var matrix = inverse * bones[i].localToWorldMatrix;
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
            HoRTRayMeshIndices.CanonicalizeOwned(mesh);
            source = current; quality = renderer.quality; globalQuality = QualitySettings.skinWeights;
            initialized = true;
            return true;
        }

        public void Dispose() => CoreUtils.Destroy(mesh);
    }
}
