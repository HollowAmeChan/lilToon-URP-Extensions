using System;
using lilToon.URP.Extensions.GeometryData;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GeometryData
{
    [InitializeOnLoad]
    internal static class HoGeometryDataTensionPreparation
    {
        static HoGeometryDataTensionPreparation() => HoGeometryDataTension.EditorPrepareUnreadableMesh = PrepareImportedMesh;
        private static bool PrepareImportedMesh(Mesh source, Action<Mesh> prepare)
        {
            using (var data = MeshUtility.AcquireReadOnlyMeshData(source))
            {
                var input = data[0];
                var copy = new Mesh { name = source.name + " (GD reference input)", hideFlags = HideFlags.HideAndDontSave,
                    indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                try
                {
                    using (var positions = new NativeArray<Vector3>(input.vertexCount, Allocator.Temp))
                    {
                        input.GetVertices(positions); copy.SetVertices(positions);
                    }
                    copy.subMeshCount = input.subMeshCount;
                    for (int sub = 0; sub < input.subMeshCount; sub++)
                    {
                        var desc = input.GetSubMesh(sub);
                        using (var indices = new NativeArray<int>(desc.indexCount, Allocator.Temp))
                        {
                            input.GetIndices(indices, sub, true); copy.SetIndices(indices, desc.topology, sub, false);
                        }
                    }
                    copy.bounds = source.bounds;
                    prepare(copy); return true;
                }
                finally { UnityEngine.Object.DestroyImmediate(copy); }
            }
        }
    }
}
