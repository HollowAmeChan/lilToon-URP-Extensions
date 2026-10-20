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
        [Range(.25f, 8), InspectorName("空间厚度"), Tooltip("扩大簇片覆盖，增加沿视线的颗粒叠层；片数不变，透明像素开销会增加。")]
        public float thickness = 2;
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
            grainDensity = s.grainDensity; thickness = s.thickness; clusterSize = s.clusterSize; textureWorldSize = s.textureWorldSize;
            brightness = s.brightness; flutter = s.flutter; maxDistance = s.maxDistance; maxClusters = s.maxClusters;
        }
    }
    public readonly struct HoAtmosphereDustSnapshot
    {
        public readonly bool enabled;
        public readonly Texture2D texture;
        public readonly float grainDensity, thickness, clusterSize, textureWorldSize, brightness, flutter, maxDistance;
        public readonly int maxClusters, seed;
        internal HoAtmosphereDustSnapshot(HoAtmosphereDustSettings value)
        {
            this = default;
            if (value == null) return;
            enabled = value.enabled; texture = value.texture;
            grainDensity = Mathf.Clamp01(HoAtmosphereRegion.Finite(value.grainDensity));
            thickness = Mathf.Clamp(HoAtmosphereRegion.Finite(value.thickness, 2), .25f, 8);
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
        private const int LevelCount = 9;
        internal sealed class View : IDisposable
        {
            internal readonly Camera camera;
            internal GraphicsBuffer buffer;
            internal int count;
            internal float cardSize, distance;
            internal bool limited;
            internal readonly List<Candidate>[] levels = new List<Candidate>[LevelCount];
            internal readonly int[] selected = new int[LevelCount];
            internal readonly Plane[] planes = new Plane[6];
            internal Vector4[] upload;
            internal View(Camera camera) { this.camera = camera; for (int i = 0; i < LevelCount; ++i) levels[i] = new List<Candidate>(); }
            public void Dispose() { buffer?.Dispose(); buffer = null; count = 0; }
        }
        internal readonly struct Candidate
        {
            internal readonly Vector4 seed;
            internal readonly float priority;
            internal Candidate(Vector4 seed, float priority) { this.seed = seed; this.priority = priority; }
        }
        private readonly List<View> views = new();
        private View last;
        internal GraphicsBuffer buffer => last?.buffer;
        internal bool HasBuffer => views.Exists(v => v.buffer != null && v.buffer.IsValid());
        internal int Count => last?.count ?? 0;
        internal bool CapacityLimited => last != null && last.limited;
        internal int RebuildCount { get; private set; }
        internal static int MaximumLevel(HoAtmosphereDustSnapshot dust) => Mathf.Clamp(
            Mathf.CeilToInt(Mathf.Log(dust.maxDistance / (dust.clusterSize * 2), 2)), 0, Mathf.Min(LevelCount - 1, dust.maxClusters - 1));
        internal View Prepare(HoAtmosphereMediumSnapshot medium, Camera camera)
        {
            var dust = medium.dust;
            if (!medium.valid || !dust.enabled || dust.grainDensity <= 0 || dust.brightness <= 0 || camera == null) return null;
            for (int i = views.Count - 1; i >= 0; --i)
                if (views[i].camera == null) { views[i].Dispose(); views.RemoveAt(i); }
            var view = views.Find(v => v.camera == camera);
            if (view == null) { view = new View(camera); views.Add(view); }
            last = view;
            for (int i = 0; i < LevelCount; ++i) { view.levels[i].Clear(); view.selected[i] = 0; }
            Vector3 observer = camera.transform.position;
            Vector3 forward = camera.transform.forward;
            Vector3 absForward = new(Mathf.Abs(forward.x), Mathf.Abs(forward.y), Mathf.Abs(forward.z));
            var regionBounds = medium.bounds;
            float planeExtent = Vector3.Dot(absForward, regionBounds.extents) + dust.flutter * 2;
            view.cardSize = dust.clusterSize; view.distance = dust.maxDistance; view.limited = false; view.count = 0;
            Vector3 advection = (Vector3)medium.wind * medium.sampleTime;
            GeometryUtility.CalculateFrustumPlanes(camera, view.planes);
            int activeLevels = 0, totalCandidates = 0;
            // Each distance band has its own world-aligned grid. Extending display
            // distance adds far bands without replacing the near grid. Traversal per
            // band stays bounded; no grid covers the entire region.
            int maximumLevel = MaximumLevel(dust);
            for (int level = 0; level <= maximumLevel; ++level)
            {
                float scale = 1 << level, edge = dust.clusterSize * 2 * scale;
                float inner = level == 0 ? 0 : edge * .5f * .85f;
                if (inner >= dust.maxDistance) break;
                float outer = level == maximumLevel ? dust.maxDistance : Mathf.Min(dust.maxDistance, edge * 1.15f);
                float cell = dust.clusterSize * .65f * scale;
                float cardSize = dust.clusterSize * scale * Mathf.Sqrt(dust.thickness);
                float radius = cardSize * .707107f + dust.flutter * 2;
                var bounds = regionBounds; bounds.Expand(radius * 2);
                Vector3 lo = Vector3.Max(bounds.min, observer - Vector3.one * outer);
                Vector3 hi = Vector3.Min(bounds.max, observer + Vector3.one * outer);
                if (lo.x > hi.x || lo.y > hi.y || lo.z > hi.z) continue;
                Vector3Int minimum = Vector3Int.FloorToInt((lo - advection) / cell);
                Vector3Int maximum = Vector3Int.FloorToInt((hi - advection) / cell);
                Vector3Int extent = maximum - minimum + Vector3Int.one;
                var candidates = view.levels[level];
                for (int iz = 0; iz < extent.z; ++iz)
                    for (int iy = 0; iy < extent.y; ++iy)
                        for (int ix = 0; ix < extent.x; ++ix)
                        {
                            int x = minimum.x + ix, y = minimum.y + iy, z = minimum.z + iz;
                            uint hash = Hash(x, y, z, dust.seed ^ (level * 19349663));
                            // w packs the grid level and a fractional random seed.
                            var seed = new Vector4((x + Random(hash)) * cell, (y + Random(hash ^ 0x68bc21ebu)) * cell,
                                (z + Random(hash ^ 0x02e5be93u)) * cell, level + Random(hash ^ 0x967a889bu) * .999f);
                            Vector3 center = (Vector3)seed + advection;
                            Vector3 offset = center - observer;
                            float distanceSquared = offset.sqrMagnitude;
                            if (distanceSquared < inner * inner || distanceSquared >= outer * outer ||
                                distanceSquared <= cardSize * cardSize * .0225f || !bounds.Contains(center) ||
                                Vector3.Dot(offset, forward) <= camera.nearClipPlane ||
                                Mathf.Abs(Vector3.Dot(center - regionBounds.center, forward)) > planeExtent) continue;
                            bool visible = true;
                            foreach (var plane in view.planes) if (plane.GetDistanceToPoint(center) < -radius) { visible = false; break; }
                            if (!visible) continue;
                            // Stable weighted sampling favours projected coverage within
                            // a band, instead of always taking its nearest few cells.
                            float priority = -Mathf.Log(Mathf.Max(.000001f, Random(hash ^ 0x5bd1e995u))) * distanceSquared / (cardSize * cardSize);
                            candidates.Add(new Candidate(seed, priority));
                        }
                if (candidates.Count == 0) continue;
                candidates.Sort(static (a, b) => a.priority.CompareTo(b.priority));
                activeLevels++; totalCandidates += candidates.Count;
            }
            view.count = Mathf.Min(totalCandidates, dust.maxClusters);
            view.limited = totalCandidates > view.count;
            if (view.count == 0) return view;
            // Reserve a fair share for every occupied depth band. A tight budget
            // never silently shortens the requested display distance.
            int remaining = view.count, share = view.count / activeLevels;
            for (int level = 0; level < LevelCount; ++level)
            {
                view.selected[level] = Mathf.Min(share, view.levels[level].Count);
                remaining -= view.selected[level];
            }
            while (remaining > 0)
                for (int level = 0; level < LevelCount && remaining > 0; ++level)
                    if (view.selected[level] < view.levels[level].Count) { view.selected[level]++; remaining--; }
            if (view.buffer == null || !view.buffer.IsValid() || view.buffer.count != dust.maxClusters)
            {
                view.Dispose(); view.count = Mathf.Min(totalCandidates, dust.maxClusters);
                view.buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, dust.maxClusters, 16) { name = "HoAtmosphere Visible Dust Clusters" };
                view.upload = new Vector4[dust.maxClusters]; RebuildCount++;
            }
            int index = 0;
            for (int level = 0; level < LevelCount; ++level)
                for (int i = 0; i < view.selected[level]; ++i) view.upload[index++] = view.levels[level][i].seed;
            view.buffer.SetData(view.upload, 0, 0, view.count);
            return view;
        }
        private static uint Hash(int x, int y, int z, int salt) { unchecked { return (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)z * 0xcb1ab31fu ^ (uint)salt; } }
        private static float Random(uint h) { unchecked { h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16; return (h >> 8) * (1f / 16777216); } }
        public void Dispose() { foreach (var view in views) view.Dispose(); views.Clear(); last = null; }
    }
}
