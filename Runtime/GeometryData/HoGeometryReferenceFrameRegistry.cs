using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.GeometryData
{
    /// <summary>Renderer-scoped frame producers. No identity, OB group, part names or camera state.</summary>
    public static class HoGeometryReferenceFrameRegistry
    {
        private static readonly List<HoGeometryDataReferenceFrame> Sources = new List<HoGeometryDataReferenceFrame>();
        internal static void Register(HoGeometryDataReferenceFrame source) { if (!Sources.Contains(source)) Sources.Add(source); }
        internal static void Unregister(HoGeometryDataReferenceFrame source) => Sources.Remove(source);
        public static IReadOnlyList<HoGeometryDataReferenceFrame> GetActiveSources() => Sources;
        public static bool TryGetFrame(Renderer renderer, out HoGeometryFrameData data)
        {
            data = default;
            if (renderer == null) return false;
            HoGeometryDataReferenceFrame found = null;
            foreach (var source in Sources)
            {
                if (source == null || !source.TryGetFrame(out var sample)) continue;
                foreach (var target in source.GetTargetRenderers())
                {
                    if (target != renderer) continue;
                    if (found != null && found != source)
                    {
                        data = default;
                        return false; // Ambiguous producer, no overwrite priority.
                    }
                    found = source;
                    data = sample;
                    break;
                }
            }
            return found != null;
        }
    }
}
