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

        internal static bool SupportsCutout(Material material)
        {
            if (material == null || material.shader == null || material.renderQueue >= (int)RenderQueue.Transparent
                || material.GetTag("RenderType", false) != "TransparentCutout"
                || !material.shader.name.StartsWith("Hidden/lilToonCutout", StringComparison.Ordinal)) return false;
            if (Enabled(material, "_UseMain2ndTex") || Enabled(material, "_UseMain3rdTex")
                || Enabled(material, "_UseParallax") || Enabled(material, "_UseDither")) return false;
            var animation = Vector(material, "_MainTex_ScrollRotate");
            if (animation.x != 0 || animation.y != 0 || animation.w != 0 || Vector(material, "_DissolveParams").x != 0
                || Vector(material, "_DistanceFade").z != 0) return false;
            float mode = Float(material, "_AlphaMaskMode");
            if (mode < 0 || mode > 4 || mode != Mathf.Floor(mode)) return false;
            foreach (string name in Scalars) if (!Finite(Float(material, name))) return false;
            if (!Finite(material.GetColor("_Color").a) || !Finite(animation.z)) return false;
            foreach (string name in new[] { "_MainTex", "_AlphaMask" })
            {
                var texture = material.HasProperty(name) ? material.GetTexture(name) : null;
                if (texture != null && !(texture is Texture2D)) return false;
                if (!material.HasProperty(name)) continue;
                var scale = material.GetTextureScale(name); var offset = material.GetTextureOffset(name);
                if (!Finite(scale.x) || !Finite(scale.y) || !Finite(offset.x) || !Finite(offset.y)) return false;
            }
            return true;
        }
        private static bool Enabled(Material m, string p) => Float(m, p) != 0;
        private static float Float(Material m, string p) => m.HasProperty(p) ? m.GetFloat(p) : 0;
        private static Vector4 Vector(Material m, string p) => m.HasProperty(p) ? m.GetVector(p) : Vector4.zero;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal Material Resolve(Material source)
        {
            if (!SupportsCutout(source)) return opaque;
            if (!seen.Add(source)) return adapters[source];
            if (!adapters.TryGetValue(source, out var adapter))
            {
                adapter = new Material(shader) { name = "Ho RTDI visibility: " + source.name, hideFlags = HideFlags.HideAndDontSave };
                adapters.Add(source, adapter);
            }
            adapter.CopyPropertiesFromMaterial(source); adapter.shaderKeywords = Array.Empty<string>();
            bool cutout = SupportsCutout(source); adapter.SetFloat("_HoRTDICutout", cutout ? 1 : 0);
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
