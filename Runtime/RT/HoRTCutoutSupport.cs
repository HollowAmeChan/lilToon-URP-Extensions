using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace lilToon.URP.Extensions.RT
{
    // Shared bounded alpha domain; shading adapters remain owned by their channels.
    internal static class HoRTCutoutSupport
    {
        private static readonly string[] Scalars = { "_Cutoff", "_AlphaMaskMode", "_AlphaMaskScale", "_AlphaMaskValue" };
        internal static bool Supports(Material material)
        {
            if (material == null || material.shader == null || material.renderQueue >= (int)RenderQueue.Transparent
                || material.GetTag("RenderType", false) != "TransparentCutout") return false;
            if (IsEnvironmentSurface(material))
                return Float(material, "_AlphaClip") > 0.5f && Finite(Float(material, "_Cutoff"))
                    && Finite(material.GetColor("_BaseColor").a) && SupportsTexture(material, "_BaseMap");
            if (!material.shader.name.StartsWith("Hidden/lilToonCutout", StringComparison.Ordinal)) return false;
            if (Enabled(material, "_UseParallax") || Enabled(material, "_UseDither")) return false;
            if (Vector(material, "_DissolveParams").x != 0
                || Vector(material, "_DistanceFade").z != 0) return false;
            float mode = Float(material, "_AlphaMaskMode");
            if (mode < 0 || mode > 4 || mode != Mathf.Floor(mode)) return false;
            foreach (string name in Scalars) if (!Finite(Float(material, name))) return false;
            if (!Finite(material.GetColor("_Color").a)) return false;
            foreach (string name in new[] { "_MainTex", "_AlphaMask" })
            {
                if (!SupportsTexture(material, name)) return false;
            }
            return true;
        }

        internal static bool IsEnvironmentSurface(Material material) => material != null && material.shader != null
            && material.GetTag("HoEnvironmentSurface", false) == "1"
            && material.HasProperty("_BaseMap") && material.HasProperty("_BaseColor");

        internal static void CopyEnvironmentBaseToAdapter(Material source, Material adapter)
        {
            adapter.SetColor("_Color", source.GetColor("_BaseColor"));
            adapter.SetTexture("_MainTex", source.GetTexture("_BaseMap"));
            adapter.SetTextureScale("_MainTex", source.GetTextureScale("_BaseMap"));
            adapter.SetTextureOffset("_MainTex", source.GetTextureOffset("_BaseMap"));
            adapter.SetFloat("_AlphaMaskMode", 0);
        }

        private static bool SupportsTexture(Material material, string name)
        {
            if (!material.HasProperty(name)) return true;
            var texture = material.GetTexture(name);
            if (texture != null && !(texture is Texture2D)) return false;
            var scale = material.GetTextureScale(name); var offset = material.GetTextureOffset(name);
            return Finite(scale.x) && Finite(scale.y) && Finite(offset.x) && Finite(offset.y);
        }
        private static bool Enabled(Material m, string p) => Float(m, p) != 0;
        private static float Float(Material m, string p) => m.HasProperty(p) ? m.GetFloat(p) : 0;
        private static Vector4 Vector(Material m, string p) => m.HasProperty(p) ? m.GetVector(p) : Vector4.zero;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    }
}
