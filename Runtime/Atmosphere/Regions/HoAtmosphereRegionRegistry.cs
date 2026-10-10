using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.Atmosphere
{
    /// <summary>World state, independent of cameras, lamps and environment capture.</summary>
    public static class HoAtmosphereRegionRegistry
    {
        private static readonly HashSet<HoAtmosphereRegion> regions = new();
        public static int ActiveCount { get { regions.RemoveWhere(r => r == null || !r.isActiveAndEnabled); return regions.Count; } }
        internal static void Register(HoAtmosphereRegion region) { if (region != null) regions.Add(region); }
        internal static void Unregister(HoAtmosphereRegion region) { if (region != null) regions.Remove(region); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => regions.Clear();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RestoreAfterSceneLoad()
        {
            // Restore existing ExecuteAlways components with domain reload disabled.
            foreach (var region in Object.FindObjectsByType<HoAtmosphereRegion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (region.isActiveAndEnabled) Register(region);
        }

        public static HoAtmosphereMediumSnapshot Capture()
        {
            int count = ActiveCount;
            if (count != 1) return new HoAtmosphereMediumSnapshot(count == 0 ? "空介质" : "v0.1 仅支持一个有效大气区域", count);
            foreach (var region in regions) return new HoAtmosphereMediumSnapshot(region);
            return new HoAtmosphereMediumSnapshot("空介质", 0);
        }
    }

    /// <summary>One world clock shared by every observer. Override enables deterministic offline sampling.</summary>
    public static class HoAtmosphereMediumClock
    {
        public static float? TimeOverride { get; set; }
        private static float editorTime;
        public static float SampleTime => TimeOverride.HasValue ? HoAtmosphereRegion.Finite(TimeOverride.Value) : Application.isPlaying ? Time.time : editorTime;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => TimeOverride = null;
        #if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeEditor()
        {
            AdvanceEditorTime();
            UnityEditor.EditorApplication.update -= AdvanceEditorTime;
            UnityEditor.EditorApplication.update += AdvanceEditorTime;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
        }
        private static void AdvanceEditorTime() => editorTime = (float)UnityEditor.EditorApplication.timeSinceStartup;
        private static void Shutdown() => UnityEditor.EditorApplication.update -= AdvanceEditorTime;
        #endif
    }

    /// <summary>Immutable CPU description for one render invocation; no cached RG handles.</summary>
    public readonly struct HoAtmosphereMediumSnapshot
    {
        public readonly bool valid;
        public readonly int regionCount;
        public readonly string status;
        public readonly Matrix4x4 worldToRegion;
        public readonly Vector4 halfSizeAndBlend, densityAndWeight, tint, noise, wind;
        public readonly Bounds bounds;
        public readonly Texture3D densityTexture;
        public readonly uint revision;
        public readonly float sampleTime;

        internal HoAtmosphereMediumSnapshot(string status, int count)
        {
            this = default; this.status = status; regionCount = count;
        }
        internal HoAtmosphereMediumSnapshot(HoAtmosphereRegion region)
        {
            this = default;
            sampleTime = HoAtmosphereMediumClock.SampleTime;
            regionCount = 1;
            worldToRegion = region.transform.worldToLocalMatrix;
            bounds = region.WorldBounds;
            Vector3 half = region.size * .5f;
            halfSizeAndBlend = new Vector4(half.x, half.y, half.z, Mathf.Max(0, HoAtmosphereRegion.Finite(region.blendDistance)));
            densityAndWeight = new Vector4(Mathf.Max(0, HoAtmosphereRegion.Finite(region.density)), Mathf.Clamp01(HoAtmosphereRegion.Finite(region.volumeLightWeight)), 0, 1);
            tint = new Vector4(Mathf.Max(0, HoAtmosphereRegion.Finite(region.tint.r)), Mathf.Max(0, HoAtmosphereRegion.Finite(region.tint.g)), Mathf.Max(0, HoAtmosphereRegion.Finite(region.tint.b)), 1);
            densityTexture = region.densityTexture;
            noise = new Vector4(1 / Mathf.Max(.0001f, HoAtmosphereRegion.Finite(region.noiseWorldSize, 5)), Mathf.Clamp01(HoAtmosphereRegion.Finite(region.noiseStrength)), densityTexture != null ? 1 : 0, 0);
            wind = new Vector4(HoAtmosphereRegion.Finite(region.wind.x), HoAtmosphereRegion.Finite(region.wind.y), HoAtmosphereRegion.Finite(region.wind.z), 0);
            valid = half.x > 0 && half.y > 0 && half.z > 0 && Mathf.Abs(region.transform.localToWorldMatrix.determinant) > 1e-10f;
            for (int i = 0; i < 16; i++) valid &= !float.IsNaN(worldToRegion[i]) && !float.IsInfinity(worldToRegion[i]);
            status = valid ? "介质可用" : "区域尺寸或 Transform 无效";
            unchecked
            {
                int hash = region.GetInstanceID();
                foreach (int value in new[] { worldToRegion.GetHashCode(), halfSizeAndBlend.GetHashCode(), densityAndWeight.GetHashCode(), tint.GetHashCode(), noise.GetHashCode(), wind.GetHashCode(), densityTexture != null ? densityTexture.GetInstanceID() : 0, densityTexture != null ? (int)densityTexture.updateCount : 0 }) hash = hash * 397 ^ value;
                revision = valid ? (uint)hash : 0;
            }
        }
        public float ActivationAt(Vector3 worldPosition)
        {
            if (!valid) return 0;
            Vector3 p = worldToRegion.MultiplyPoint3x4(worldPosition);
            Vector3 gap = new Vector3(halfSizeAndBlend.x - Mathf.Abs(p.x), halfSizeAndBlend.y - Mathf.Abs(p.y), halfSizeAndBlend.z - Mathf.Abs(p.z));
            if (gap.x < 0 || gap.y < 0 || gap.z < 0) return 0;
            float nearest = float.PositiveInfinity;
            for (int axis = 0; axis < 3; axis++)
                nearest = Mathf.Min(nearest, gap[axis] / Mathf.Max(1e-8f, ((Vector3)worldToRegion.GetRow(axis)).magnitude));
            float edge = halfSizeAndBlend.w > 0 ? Mathf.SmoothStep(0, 1, Mathf.Clamp01(nearest / halfSizeAndBlend.w)) : 1;
            return edge * densityAndWeight.y;
        }
    }
}
