using System;
using UnityEngine;

namespace lilToon.URP.Extensions.GeometryData
{
    internal sealed class HoTensionResources : IDisposable
    {
        internal GraphicsBuffer rest, metrics, triangles, neighborOffsets, neighbors, cornerOffsets, corners, positions, triangleMetrics;
        internal readonly float lengthEpsilon, areaEpsilon;
        internal HoTensionResources(Vector4[] rest, Vector4[] metrics, int[] triangles, int[] neighborOffsets, int[] neighbors,
            int[] cornerOffsets, int[] corners, float lengthEpsilon, float areaEpsilon)
        {
            this.lengthEpsilon = lengthEpsilon; this.areaEpsilon = areaEpsilon;
            try
            {
                this.rest = Buffer(rest, 16); this.metrics = Buffer(metrics, 16); this.triangles = Buffer(triangles, 4);
                this.neighborOffsets = Buffer(neighborOffsets, 4); this.neighbors = Buffer(neighbors, 4);
                this.cornerOffsets = Buffer(cornerOffsets, 4); this.corners = Buffer(corners, 4);
                positions = Buffer(new Vector4[rest.Length], 16); triangleMetrics = Buffer(new Vector4[metrics.Length], 16);
            }
            catch { Dispose(); throw; }
        }
        private static GraphicsBuffer Buffer<T>(T[] data, int stride) where T : struct
        {
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, data.Length), stride);
            if (data.Length > 0) buffer.SetData(data); return buffer;
        }
        public void Dispose()
        {
            rest?.Dispose(); metrics?.Dispose(); triangles?.Dispose(); neighborOffsets?.Dispose(); neighbors?.Dispose();
            cornerOffsets?.Dispose(); corners?.Dispose(); positions?.Dispose(); triangleMetrics?.Dispose();
        }
    }
}
