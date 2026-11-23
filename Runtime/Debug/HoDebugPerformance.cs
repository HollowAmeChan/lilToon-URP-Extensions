using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Debugging
{
    public enum HoDebugTilePerformanceMode
    {
        Off = 0,
        Frame = 1,
        Features = 2,
        FrameAndFeatures = 3
    }

    [Serializable]
    public sealed class HoDebugTilePerformanceModeParameter : VolumeParameter<HoDebugTilePerformanceMode>
    {
        public HoDebugTilePerformanceModeParameter(HoDebugTilePerformanceMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoDebugTilePerformanceMode from, HoDebugTilePerformanceMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    /// <summary>Timing markers owned by one feature and displayed by Ho-DebugPerformance.</summary>
    public sealed class HoDebugPerformanceInfo
    {
        private readonly ProfilingSampler[] samplers;
        private float gpuMilliseconds;
        private float cpuMilliseconds;
        private bool hasGpuSample;
        private bool hasCpuSample;
        private bool refreshedGpuThisUpdate;
        private bool refreshedCpuThisUpdate;

        public HoDebugPerformanceInfo(string featureName, string displayName, params string[] markerNames)
        {
            FeatureName = featureName;
            DisplayName = displayName;
            if (markerNames == null || markerNames.Length == 0)
            {
                samplers = Array.Empty<ProfilingSampler>();
                return;
            }

            HashSet<string> uniqueNames = new HashSet<string>(StringComparer.Ordinal);
            List<ProfilingSampler> declaredSamplers = new List<ProfilingSampler>(markerNames.Length);
            for (int i = 0; i < markerNames.Length; i++)
            {
                string markerName = markerNames[i];
                if (string.IsNullOrWhiteSpace(markerName) || !uniqueNames.Add(markerName))
                {
                    continue;
                }

                ProfilingSampler sampler = HoDebugPerformanceRegistry.GetSampler(markerName);
                declaredSamplers.Add(sampler);
            }

            samplers = declaredSamplers.ToArray();
        }

        public string FeatureName { get; }
        public string DisplayName { get; }
        public float GpuMilliseconds => gpuMilliseconds;
        public float CpuMilliseconds => cpuMilliseconds;
        public bool HasGpuSample => hasGpuSample;
        public bool HasCpuSample => hasCpuSample;
        public bool GpuRefreshedThisUpdate => refreshedGpuThisUpdate;
        public bool CpuRefreshedThisUpdate => refreshedCpuThisUpdate;
        public bool RefreshedThisUpdate => refreshedGpuThisUpdate || refreshedCpuThisUpdate;

        internal void SetRecordingEnabled(bool enabled)
        {
            for (int i = 0; i < samplers.Length; i++)
            {
                samplers[i].enableRecording = enabled;
            }
        }

        internal bool Update()
        {
            refreshedGpuThisUpdate = false;
            refreshedCpuThisUpdate = false;
            int gpuSamples = 0;
            int cpuSamples = 0;
            float gpuElapsed = 0.0f;
            float cpuElapsed = 0.0f;
            for (int i = 0; i < samplers.Length; i++)
            {
                ProfilingSampler sampler = samplers[i];
                gpuSamples += sampler.gpuSampleCount;
                cpuSamples += sampler.cpuSampleCount;
                gpuElapsed += sampler.gpuElapsedTime;
                cpuElapsed += sampler.cpuElapsedTime;
            }

            // Recorder timings are per-frame values, not cumulative counters. CPU reports the
            // previous frame and GPU reports a frame with a backend-defined delay, so cache a
            // valid frame directly instead of subtracting the previous readback.
            if (gpuSamples > 0 || gpuElapsed > 0.0f)
            {
                gpuMilliseconds = Mathf.Max(0.0f, gpuElapsed);
                hasGpuSample = true;
                refreshedGpuThisUpdate = true;
            }

            if (cpuSamples > 0 || cpuElapsed > 0.0f)
            {
                cpuMilliseconds = Mathf.Max(0.0f, cpuElapsed);
                hasCpuSample = true;
                refreshedCpuThisUpdate = true;
            }

            return refreshedGpuThisUpdate || refreshedCpuThisUpdate;
        }
    }

    public readonly struct HoDebugPerformanceSegment
    {
        public HoDebugPerformanceSegment(string text, Color color)
        {
            Text = text;
            Color = color;
        }

        public readonly string Text;
        public readonly Color Color;
    }

    public readonly struct HoDebugPerformanceLine
    {
        public HoDebugPerformanceLine(string text)
            : this(text, Color.white)
        {
        }

        public HoDebugPerformanceLine(string text, Color color)
        {
            Segments = new[] { new HoDebugPerformanceSegment(text, color) };
        }

        public HoDebugPerformanceLine(params HoDebugPerformanceSegment[] segments)
        {
            Segments = segments ?? Array.Empty<HoDebugPerformanceSegment>();
        }

        public readonly HoDebugPerformanceSegment[] Segments;
    }

    /// <summary>Central timing catalog. Feature debug declarations own the marker names.</summary>
    public static class HoDebugPerformanceRegistry
    {
        private static readonly List<HoDebugPerformanceInfo> Entries = new List<HoDebugPerformanceInfo>();
        private static readonly Dictionary<string, HoDebugPerformanceInfo> EntriesByFeature =
            new Dictionary<string, HoDebugPerformanceInfo>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ProfilingSampler> SamplersByName =
            new Dictionary<string, ProfilingSampler>(StringComparer.Ordinal);
        private static int updatedFrame = -1;
        private static float smoothedDeltaTime;
        private static bool hasFrameTiming;
        private static double lastRefreshTime = -1.0;

        public static IReadOnlyList<HoDebugPerformanceInfo> All => Entries;

        public static void EnsureInitialized()
        {
            // Feature declarations self-register when the shared debug catalog is touched.
            _ = HoDebugViewRegistry.AllViews;
        }

        public static void SetRecordingEnabled(bool enabled)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                Entries[i].SetRecordingEnabled(enabled);
            }
        }

        public static ProfilingSampler GetSampler(string markerName)
        {
            if (string.IsNullOrWhiteSpace(markerName))
            {
                throw new ArgumentException("A profiling marker requires a name.", nameof(markerName));
            }

            if (SamplersByName.TryGetValue(markerName, out ProfilingSampler sampler))
            {
                return sampler;
            }

            sampler = new ProfilingSampler(markerName);
            SamplersByName.Add(markerName, sampler);
            return sampler;
        }

        public static HoDebugPerformanceInfo Register(string featureName, string displayName, params string[] markerNames)
        {
            if (string.IsNullOrEmpty(featureName))
            {
                throw new ArgumentException("A performance declaration requires a feature name.", nameof(featureName));
            }

            if (EntriesByFeature.TryGetValue(featureName, out HoDebugPerformanceInfo existing))
            {
                return existing;
            }

            HoDebugPerformanceInfo info = new HoDebugPerformanceInfo(featureName, displayName, markerNames);
            Entries.Add(info);
            EntriesByFeature.Add(featureName, info);
            return info;
        }

        public static void Update()
        {
            if (updatedFrame == Time.frameCount)
            {
                return;
            }

            updatedFrame = Time.frameCount;
            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime > 0.0f)
            {
                smoothedDeltaTime = smoothedDeltaTime <= 0.0f
                    ? deltaTime
                    : Mathf.Lerp(smoothedDeltaTime, deltaTime, 0.1f);
                hasFrameTiming = true;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Update())
                {
                    lastRefreshTime = now;
                }
            }
        }

        public static IReadOnlyList<HoDebugPerformanceLine> BuildLines(HoDebugTilePerformanceMode mode)
        {
            List<HoDebugPerformanceLine> lines = new List<HoDebugPerformanceLine>();
            bool showFrame = mode == HoDebugTilePerformanceMode.Frame
                || mode == HoDebugTilePerformanceMode.FrameAndFeatures;
            bool showFeatures = mode == HoDebugTilePerformanceMode.Features
                || mode == HoDebugTilePerformanceMode.FrameAndFeatures;
            if (showFrame)
            {
                string fps = hasFrameTiming ? Format(1.0f / Mathf.Max(smoothedDeltaTime, 0.0001f), 1) : "--";
                string frameTime = hasFrameTiming ? Format(smoothedDeltaTime * 1000.0f, 2) : "--";
                lines.Add(new HoDebugPerformanceLine(
                    "FPS " + fps + " CPU " + frameTime + "MS"));
            }

            if (showFeatures)
            {
                lines.Add(new HoDebugPerformanceLine("HO FEATURES " + Entries.Count));
                string last = lastRefreshTime >= 0.0
                    ? Format((float)Math.Max(0.0, Time.realtimeSinceStartupAsDouble - lastRefreshTime), 2) + "S"
                    : "--";
                lines.Add(new HoDebugPerformanceLine("LAST " + last));
                for (int i = 0; i < Entries.Count; i++)
                {
                    HoDebugPerformanceInfo entry = Entries[i];
                    string gpu = entry.HasGpuSample ? Format(entry.GpuMilliseconds, 2) + "MS" : "--";
                    string cpu = entry.HasCpuSample ? Format(entry.CpuMilliseconds, 2) + "MS" : "--";
                    Color gpuColor = entry.GpuRefreshedThisUpdate && entry.HasGpuSample
                        ? new Color(1.0f, 0.82f, 0.18f, 1.0f) : Color.white;
                    Color cpuColor = entry.CpuRefreshedThisUpdate && entry.HasCpuSample
                        ? new Color(1.0f, 0.82f, 0.18f, 1.0f) : Color.white;
                    lines.Add(new HoDebugPerformanceLine(
                        new HoDebugPerformanceSegment(entry.DisplayName + " G ", Color.white),
                        new HoDebugPerformanceSegment(gpu, gpuColor),
                        new HoDebugPerformanceSegment(" C ", Color.white),
                        new HoDebugPerformanceSegment(cpu, cpuColor)));
                }
            }

            return lines;
        }

        private static string Format(float value, int decimals)
        {
            return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }
    }
}
