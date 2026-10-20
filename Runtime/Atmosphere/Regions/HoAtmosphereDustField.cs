using System;
using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.Atmosphere
{
    [Serializable]
    public sealed class HoAtmosphereDustSettings
    {
        [InspectorName("启用灰尘")] public bool enabled;
        [Tooltip("留空使用内置簇状尘点贴图；读取 Alpha 通道。"), InspectorName("尘点贴图")] public Texture2D texture;
        [Range(0, 1), InspectorName("颗粒密度")] public float grainDensity = 1;
        [Min(2), InspectorName("簇片大小（米）")] public float clusterSize = 8;
        [Min(.25f), InspectorName("贴图平铺大小（米）")] public float textureWorldSize = 8;
        [Min(0), InspectorName("灰尘亮度")] public float brightness = 20;
        [Min(0), InspectorName("漂浮幅度（米）")] public float flutter = .12f;
        [Min(.1f), InspectorName("显示距离（米）")] public float maxDistance = 30;
        [Range(8, 256), InspectorName("最多绘制片数")] public int maxClusters = 64;
        [InspectorName("随机种子")] public int seed = 17;
        internal void Constrain()
        {
            var s = new HoAtmosphereDustSnapshot(this);
            grainDensity = s.grainDensity; clusterSize = s.clusterSize; textureWorldSize = s.textureWorldSize;
            brightness = s.brightness; flutter = s.flutter; maxDistance = s.maxDistance; maxClusters = s.maxClusters;
        }
    }
    public readonly struct HoAtmosphereDustSnapshot
    {
        public readonly bool enabled;
        public readonly Texture2D texture;
        public readonly float grainDensity, clusterSize, textureWorldSize, brightness, flutter, maxDistance;
        public readonly int maxClusters, seed;
        internal HoAtmosphereDustSnapshot(HoAtmosphereDustSettings value)
        {
            this = default;
            if (value == null) return;
            enabled = value.enabled; texture = value.texture;
            grainDensity = Mathf.Clamp01(HoAtmosphereRegion.Finite(value.grainDensity));
            clusterSize = Mathf.Clamp(HoAtmosphereRegion.Finite(value.clusterSize, 8), 2, 1000);
            textureWorldSize = Mathf.Max(.25f, HoAtmosphereRegion.Finite(value.textureWorldSize, 8));
            brightness = Mathf.Max(0, HoAtmosphereRegion.Finite(value.brightness));
            flutter = Mathf.Clamp(HoAtmosphereRegion.Finite(value.flutter), 0, clusterSize);
            maxDistance = Mathf.Clamp(HoAtmosphereRegion.Finite(value.maxDistance, 30), .1f, 1000);
            maxClusters = Mathf.Clamp(value.maxClusters, 8, 256); seed = value.seed;
        }
    }
    // A shared analytic world grid, with small per-observer visibility lists. Cameras
    // select the same seeds; they do not create or simulate independent populations.
    internal sealed class HoAtmosphereDustField : IDisposable
    {
        internal sealed class View : IDisposable
        {
            internal readonly Camera camera;
            internal GraphicsBuffer buffer;
            internal int count;
            internal float cardSize, distance;
            internal bool limited;
            internal readonly List<Candidate> candidates = new();
            internal readonly Plane[] planes = new Plane[6];
            internal Vector4[] upload;
            internal View(Camera camera) { this.camera = camera; }
            public void Dispose() { buffer?.Dispose(); buffer = null; count = 0; }
        }
        internal readonly struct Candidate
        {
            internal readonly Vector4 seed;
            internal readonly float distanceSquared;
            internal Candidate(Vector4 seed, float distanceSquared) { this.seed = seed; this.distanceSquared = distanceSquared; }
        }
        private readonly List<View> views = new();
        private View last;
        internal GraphicsBuffer buffer => last?.buffer;
        internal bool HasBuffer => views.Exists(v => v.buffer != null && v.buffer.IsValid());
        internal int Count => last?.count ?? 0;
        internal bool CapacityLimited => last != null && last.limited;
        internal int RebuildCount { get; private set; }
        internal View Prepare(HoAtmosphereMediumSnapshot medium, Camera camera)
        {
            var dust = medium.dust;
            if (!medium.valid || !dust.enabled || dust.grainDensity <= 0 || dust.brightness <= 0 || camera == null) return null;
            for (int i = views.Count - 1; i >= 0; --i)
                if (views[i].camera == null) { views[i].Dispose(); views.RemoveAt(i); }
            var view = views.Find(v => v.camera == camera);
            if (view == null) { view = new View(camera); views.Add(view); }
            last = view; view.candidates.Clear();
            // Bound traversal by the display radius, never by the size of the region.
            // Large display distances use a stable, distance-setting-dependent grid LOD.
            float cell = dust.clusterSize * .65f, cardSize = dust.clusterSize;
            while (2 * dust.maxDistance / cell > 18) { cell *= 2; cardSize *= 2; }
            float radius = cardSize * .707107f + dust.flutter;
            Vector3 observer = camera.transform.position;
            var bounds = medium.bounds; bounds.Expand(radius * 2);
            Vector3 lo = Vector3.Max(bounds.min, observer - Vector3.one * (dust.maxDistance + radius));
            Vector3 hi = Vector3.Min(bounds.max, observer + Vector3.one * (dust.maxDistance + radius));
            view.cardSize = cardSize; view.distance = dust.maxDistance; view.limited = false; view.count = 0;
            if (lo.x > hi.x || lo.y > hi.y || lo.z > hi.z) return view;
            Vector3 advection = (Vector3)medium.wind * medium.sampleTime;
            Vector3Int minimum = Vector3Int.FloorToInt((lo - advection) / cell);
            Vector3Int maximum = Vector3Int.FloorToInt((hi - advection) / cell);
            Vector3Int extent = maximum - minimum + Vector3Int.one;
            GeometryUtility.CalculateFrustumPlanes(camera, view.planes);
            for (int iz = 0; iz < extent.z; ++iz)
                for (int iy = 0; iy < extent.y; ++iy)
                    for (int ix = 0; ix < extent.x; ++ix)
                    {
                        int x = minimum.x + ix, y = minimum.y + iy, z = minimum.z + iz;
                        uint hash = Hash(x, y, z, dust.seed);
                        var seed = new Vector4((x + Random(hash)) * cell, (y + Random(hash ^ 0x68bc21ebu)) * cell,
                            (z + Random(hash ^ 0x02e5be93u)) * cell, Random(hash ^ 0x967a889bu));
                        Vector3 center = (Vector3)seed + advection;
                        float distanceSquared = (center - observer).sqrMagnitude;
                        if (distanceSquared > dust.maxDistance * dust.maxDistance || !bounds.Contains(center)) continue;
                        bool visible = true;
                        foreach (var plane in view.planes) if (plane.GetDistanceToPoint(center) < -radius) { visible = false; break; }
                        if (visible) view.candidates.Add(new Candidate(seed, distanceSquared));
                    }
            // This selects a budget, not a transparency draw order. Additive cards
            // need no back-to-front sorting. Fade at the selected radius avoids popping.
            view.candidates.Sort(static (a, b) => a.distanceSquared.CompareTo(b.distanceSquared));
            view.count = Mathf.Min(view.candidates.Count, dust.maxClusters);
            view.limited = view.candidates.Count > view.count;
            if (view.limited) view.distance = Mathf.Min(view.distance, Mathf.Sqrt(view.candidates[view.count - 1].distanceSquared));
            if (view.count == 0) return view;
            if (view.buffer == null || !view.buffer.IsValid() || view.buffer.count != dust.maxClusters)
            {
                view.Dispose(); view.count = Mathf.Min(view.candidates.Count, dust.maxClusters);
                view.buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, dust.maxClusters, 16) { name = "HoAtmosphere Visible Dust Clusters" };
                view.upload = new Vector4[dust.maxClusters]; RebuildCount++;
            }
            for (int i = 0; i < view.count; ++i) view.upload[i] = view.candidates[i].seed;
            view.buffer.SetData(view.upload, 0, 0, view.count);
            return view;
        }
        private static uint Hash(int x, int y, int z, int salt) { unchecked { return (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)z * 0xcb1ab31fu ^ (uint)salt; } }
        private static float Random(uint h) { unchecked { h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16; return (h >> 8) * (1f / 16777216); } }
        public void Dispose() { foreach (var view in views) view.Dispose(); views.Clear(); last = null; }
    }
}
