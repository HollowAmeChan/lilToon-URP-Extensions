using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace lilToon.URP.Extensions.RayTracing
{
    // Shared bounded alpha domain; shading adapters remain owned by their channels.
    internal static class HoRTCutoutSupport
    {
        private static readonly string[] Scalars = { "_Cutoff", "_AlphaMaskMode", "_AlphaMaskScale", "_AlphaMaskValue", "_ShiftBackfaceUV" };
        internal static bool Supports(Material material)
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

    }
}
