using System;
using UnityEngine;

namespace lilToon.URP.Extensions.Atmosphere
{
    [Serializable]
    public sealed class HoAtmosphereDustSettings
    {
        [InspectorName("启用尘粒")] public bool enabled;
        [Range(0, 100), InspectorName("每立方米数量")] public float numberDensity = .5f;
        [InspectorName("粒径范围（米）")] public Vector2 size = new(.008f, .025f);
        [Min(0), InspectorName("尘粒亮度")] public float brightness = 20;
        [Min(0), InspectorName("漂浮幅度（米）")] public float flutter = .12f;
        [Min(.1f), InspectorName("显示距离（米）")] public float maxDistance = 30;
        [Range(128, 65536), InspectorName("数量上限")] public int maxParticles = 8192;
        [InspectorName("随机种子")] public int seed = 17;
        internal void Constrain()
        {
            var s = new HoAtmosphereDustSnapshot(this);
            numberDensity = s.numberDensity; brightness = s.brightness; flutter = s.flutter;
            maxDistance = s.maxDistance; size = s.size; maxParticles = s.maxParticles;
        }
    }
    public readonly struct HoAtmosphereDustSnapshot
    {
        public readonly bool enabled;
        public readonly float numberDensity, brightness, flutter, maxDistance;
        public readonly Vector2 size;
        public readonly int maxParticles, seed;
        internal HoAtmosphereDustSnapshot(HoAtmosphereDustSettings value)
        {
            this = default;
            if (value == null) return;
            enabled = value.enabled;
            numberDensity = Mathf.Clamp(HoAtmosphereRegion.Finite(value.numberDensity), 0, 100);
            brightness = Mathf.Max(0, HoAtmosphereRegion.Finite(value.brightness));
            flutter = Mathf.Max(0, HoAtmosphereRegion.Finite(value.flutter));
            maxDistance = Mathf.Max(.1f, HoAtmosphereRegion.Finite(value.maxDistance, 30));
            float a = Mathf.Max(.0001f, HoAtmosphereRegion.Finite(value.size.x, .008f)), b = Mathf.Max(.0001f, HoAtmosphereRegion.Finite(value.size.y, .025f));
            size = new Vector2(Mathf.Min(a, b), Mathf.Max(a, b));
            maxParticles = Mathf.Clamp(value.maxParticles, 128, 65536); seed = value.seed;
        }
    }
    // One world field per region. Positions are analytic in common world time, never stepped per camera.
    internal sealed class HoAtmosphereDustField : IDisposable
    {
        internal GraphicsBuffer buffer;
        internal bool HasBuffer => buffer != null && buffer.IsValid();
        internal int Count { get; private set; }
        internal int RebuildCount { get; private set; }
        internal bool CapacityLimited { get; private set; }
        private Vector3Int minimum, maximum;
        private float spacing;
        private int seed;

        internal void Prepare(HoAtmosphereMediumSnapshot medium)
        {
            var dust = medium.dust;
            if (!medium.valid || !dust.enabled || dust.numberDensity <= 0 || dust.brightness <= 0) { Dispose(); return; }
            var bounds = medium.bounds;
            bounds.Expand(2 * (dust.flutter + dust.size.y));
            Vector3 advection = (Vector3)medium.wind * medium.sampleTime;
            float cell = Mathf.Pow(1 / dust.numberDensity, 1f / 3);
            Vector3Int lo = default, hi = default;
            long count = 0;
            CapacityLimited = false;
            for (int attempt = 0; attempt < 16; ++attempt)
            {
                // Use a conservative count independent of wind/time, so budgeting never
                // changes grid spacing while the field drifts across cell boundaries.
                count = (long)(Mathf.CeilToInt(bounds.size.x / cell) + 2) * (Mathf.CeilToInt(bounds.size.y / cell) + 2) * (Mathf.CeilToInt(bounds.size.z / cell) + 2);
                if (count > 0 && count <= dust.maxParticles) break;
                CapacityLimited = true;
                cell *= count > 0 ? Mathf.Max(1.1f, Mathf.Pow((float)count / dust.maxParticles, 1f / 3)) : 2;
            }
            if (count <= 0 || count > dust.maxParticles) { Dispose(); return; }
            lo = Vector3Int.FloorToInt((bounds.min - advection) / cell);
            hi = Vector3Int.FloorToInt((bounds.max - advection) / cell);
            long nx = (long)hi.x - lo.x + 1, ny = (long)hi.y - lo.y + 1, nz = (long)hi.z - lo.z + 1;
            if (nx <= 0 || ny <= 0 || nz <= 0 || nx > dust.maxParticles || ny > dust.maxParticles || nz > dust.maxParticles) { Dispose(); return; }
            count = nx * ny * nz;
            if (count <= 0 || count > dust.maxParticles) { Dispose(); return; }
            if (HasBuffer && minimum == lo && maximum == hi && spacing == cell && seed == dust.seed) return;
            Dispose();
            var values = new Vector4[(int)count]; int index = 0;
            for (int iz = 0; iz < nz; ++iz)
                for (int iy = 0; iy < ny; ++iy)
                    for (int ix = 0; ix < nx; ++ix)
                    {
                        int x = lo.x + ix, y = lo.y + iy, z = lo.z + iz;
                        uint hash = Hash(x, y, z, dust.seed);
                        values[index++] = new Vector4((x + Random(hash)) * cell, (y + Random(hash ^ 0x68bc21ebu)) * cell,
                            (z + Random(hash ^ 0x02e5be93u)) * cell, Random(hash ^ 0x967a889bu));
                    }
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, values.Length, 16) { name = "HoAtmosphere Shared Dust Seeds" };
            buffer.SetData(values);
            minimum = lo; maximum = hi; spacing = cell; seed = dust.seed; Count = values.Length; RebuildCount++;
        }
        private static uint Hash(int x, int y, int z, int salt) { unchecked { return (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)z * 0xcb1ab31fu ^ (uint)salt; } }
        private static float Random(uint h) { unchecked { h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16; return (h >> 8) * (1f / 16777216); } }
        public void Dispose() { buffer?.Dispose(); buffer = null; Count = 0; }
    }
}
