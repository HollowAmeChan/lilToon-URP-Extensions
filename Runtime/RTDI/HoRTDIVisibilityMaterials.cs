using lilToon.URP.Extensions.RayTracing;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.RTDI
{
    // Native material hit groups borrow original textures and Unity's mesh attributes.
    // Used only for DI's final visibility; source raster materials are never changed.
    internal sealed class HoRTDIVisibilityMaterials : IDisposable
    {
        private readonly Shader shader;
        private readonly Material opaque;
        private readonly Dictionary<Material, Material> adapters = new(References.Instance);
        private readonly HashSet<Material> seen = new(References.Instance);
        private readonly List<Material> removed = new();
        private static readonly string[] Scalars = { "_Cutoff", "_AlphaMaskMode", "_AlphaMaskScale", "_AlphaMaskValue", "_ShiftBackfaceUV" };
        internal int Signature { get; private set; }
        private sealed class References : IEqualityComparer<Material>
        {
            internal static readonly References Instance = new();
            public bool Equals(Material a, Material b) => ReferenceEquals(a, b);
            public int GetHashCode(Material material) => RuntimeHelpers.GetHashCode(material);
        }
        internal HoRTDIVisibilityMaterials(Shader shader)
        {
            this.shader = shader;
            opaque = new Material(shader) { name = "Ho RTDI opaque visibility", hideFlags = HideFlags.HideAndDontSave };
            opaque.SetFloat("_HoRTDICutout", 0);
        }
        internal void BeginFrame() { seen.Clear(); Signature = 0; }

        private static float Float(Material m, string p) => m.HasProperty(p) ? m.GetFloat(p) : 0;
        private static Vector4 Vector(Material m, string p) => m.HasProperty(p) ? m.GetVector(p) : Vector4.zero;

        internal Material Resolve(Material source)
        {
            if (!HoRTCutoutSupport.Supports(source)) return opaque;
            if (!seen.Add(source)) return adapters[source];
            if (!adapters.TryGetValue(source, out var adapter))
            {
                adapter = new Material(shader) { name = "Ho RTDI visibility: " + source.name, hideFlags = HideFlags.HideAndDontSave };
                adapters.Add(source, adapter);
            }
            adapter.CopyPropertiesFromMaterial(source); adapter.shaderKeywords = Array.Empty<string>();
            bool cutout = HoRTCutoutSupport.Supports(source); adapter.SetFloat("_HoRTDICutout", cutout ? 1 : 0);
            if (cutout) unchecked
            {
                int hash = source.GetInstanceID();
                hash = hash * 397 ^ source.GetColor("_Color").a.GetHashCode();
                hash = hash * 397 ^ Vector(source, "_MainTex_ScrollRotate").GetHashCode();
                foreach (string name in Scalars) hash = hash * 397 ^ Float(source, name).GetHashCode();
                foreach (string name in new[] { "_MainTex", "_AlphaMask" })
                {
                    if (!source.HasProperty(name)) continue;
                    var texture = source.GetTexture(name);
                    hash = hash * 397 ^ (texture != null ? texture.GetInstanceID() ^ (int)texture.updateCount : 0);
                    if (texture != null) { hash = hash * 397 ^ (int)texture.filterMode; hash = hash * 397 ^ (int)texture.wrapModeU; hash = hash * 397 ^ (int)texture.wrapModeV; }
                    hash = hash * 397 ^ source.GetTextureScale(name).GetHashCode(); hash = hash * 397 ^ source.GetTextureOffset(name).GetHashCode();
                }
                Signature ^= hash;
            }
            return adapter;
        }
        internal void EndFrame()
        {
            removed.Clear(); foreach (var pair in adapters) if (pair.Key == null || !seen.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (var material in removed) { CoreUtils.Destroy(adapters[material]); adapters.Remove(material); }
        }
        public void Dispose() { foreach (var adapter in adapters.Values) CoreUtils.Destroy(adapter); adapters.Clear(); CoreUtils.Destroy(opaque); }
    }
}
