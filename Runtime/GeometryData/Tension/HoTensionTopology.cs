using System;
using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.GeometryData
{
    internal sealed class HoTensionTopology
    {
        internal Vector4[] positions, metrics;
        internal int[] triangles, neighborOffsets, neighbors, cornerOffsets, corners;
        internal float lengthEpsilon, areaEpsilon;
        internal static HoTensionTopology Build(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0)
                throw new InvalidOperationException("参考 Mesh 需要可读且包含顶点。");
            var source = mesh.vertices;
            var result = new HoTensionTopology { positions = new Vector4[source.Length] };
            float scale = mesh.bounds.size.magnitude;
            result.lengthEpsilon = Mathf.Max(1e-8f, scale * 1e-6f);
            result.areaEpsilon = result.lengthEpsilon * result.lengthEpsilon;
            for (int i = 0; i < source.Length; i++)
            {
                Vector3 p = source[i];
                if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z)) throw new InvalidOperationException("参考位置含非有限值。");
                result.positions[i] = new Vector4(p.x, p.y, p.z, 1);
            }
            var indices = new List<int>();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var topology = mesh.GetTopology(sub);
                if (topology != MeshTopology.Triangles && topology != MeshTopology.Quads)
                    throw new InvalidOperationException("张力拓扑仅支持三角面 / 四边面。");
                var raw = mesh.GetIndices(sub);
                if (topology == MeshTopology.Triangles) indices.AddRange(raw);
                else for (int i = 0; i < raw.Length; i += 4)
                {
                    indices.Add(raw[i]); indices.Add(raw[i + 1]); indices.Add(raw[i + 2]);
                    indices.Add(raw[i]); indices.Add(raw[i + 2]); indices.Add(raw[i + 3]);
                }
            }
            if (indices.Count == 0) throw new InvalidOperationException("没有可测量的三角面。");
            result.triangles = indices.ToArray(); result.metrics = new Vector4[indices.Count / 3];
            var adjacent = new HashSet<int>[source.Length]; var incident = new List<int>[source.Length];
            for (int i = 0; i < source.Length; i++) { adjacent[i] = new HashSet<int>(); incident[i] = new List<int>(); }
            for (int face = 0; face < result.metrics.Length; face++)
            {
                int a = indices[face * 3], b = indices[face * 3 + 1], c = indices[face * 3 + 2];
                if ((uint)a >= source.Length || (uint)b >= source.Length || (uint)c >= source.Length)
                    throw new InvalidOperationException("面索引超出参考顶点范围。");
                var ab = source[b] - source[a]; var ac = source[c] - source[a]; var bc = source[c] - source[b];
                float area = Vector3.Cross(ab, ac).magnitude * 0.5f;
                result.metrics[face] = new Vector4(area, Angle(ab, ac), Angle(-ab, bc), Angle(-ac, -bc));
                for (int corner = 0; corner < 3; corner++)
                {
                    int v = indices[face * 3 + corner];
                    for (int other = 0; other < 3; other++) if (other != corner && indices[face * 3 + other] != v) adjacent[v].Add(indices[face * 3 + other]);
                    incident[v].Add(face * 3 + corner);
                }
            }
            result.neighborOffsets = new int[source.Length + 1]; result.cornerOffsets = new int[source.Length + 1];
            var allNeighbors = new List<int>(); var allCorners = new List<int>();
            for (int i = 0; i < source.Length; i++)
            {
                result.neighborOffsets[i] = allNeighbors.Count; result.cornerOffsets[i] = allCorners.Count;
                var sorted = new List<int>(adjacent[i]); sorted.Sort(); allNeighbors.AddRange(sorted); allCorners.AddRange(incident[i]);
            }
            result.neighborOffsets[source.Length] = allNeighbors.Count; result.cornerOffsets[source.Length] = allCorners.Count;
            result.neighbors = allNeighbors.ToArray(); result.corners = allCorners.ToArray(); return result;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Angle(Vector3 a, Vector3 b) => Mathf.Atan2(Vector3.Cross(a, b).magnitude, Vector3.Dot(a, b));
    }
}
