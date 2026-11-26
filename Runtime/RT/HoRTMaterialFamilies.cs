using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.RT
{
    // Runtime consumer mapping. No material-package or Editor assembly dependency.
    internal enum HoRTMaterialFamily { Unsupported, LegacyLilToon, Environment, Cloth, Hair, Skin, Eye }

    internal static class HoRTMaterialFamilies
    {
        internal static HoRTMaterialFamily Get(Material source)
        {
            if (source == null || source.shader == null) return HoRTMaterialFamily.Unsupported;
            if (HoRTCutoutSupport.IsEnvironmentSurface(source)) return HoRTMaterialFamily.Environment;
            return source.shader.name switch
            {
                "Ho/Cloth/URP/Cloth" => HoRTMaterialFamily.Cloth,
                "Ho/Hair/URP/Hair" => HoRTMaterialFamily.Hair,
                "Ho/Skin/URP/Stylized" => HoRTMaterialFamily.Skin,
                "Ho/Skin/URP/Eye" => HoRTMaterialFamily.Eye,
                "lilToon" or "Hidden/lilToonOutline" or "Hidden/lilToonCutout" or "Hidden/lilToonCutoutOutline" => HoRTMaterialFamily.LegacyLilToon,
                _ => HoRTMaterialFamily.Unsupported
            };
        }

        internal static float Float(Material source, string name, float fallback = 0) => source.HasProperty(name) ? source.GetFloat(name) : fallback;

        internal static void CopyTexture(Material source, string from, Material target, string to)
        {
            var texture = source.HasProperty(from) ? source.GetTexture(from) : null;
            // A null binding in a native hit group is not the ShaderLab raster
            // default. Supply the same identity explicitly for ray evaluation.
            bool normal = to == "_BumpMap" || to == "_HoTesionStretchNormal" || to == "_HoTesionCompressionNormal";
            target.SetTexture(to, texture != null ? texture : normal ? Texture2D.normalTexture : Texture2D.whiteTexture);
            target.SetTextureScale(to, source.HasProperty(from) ? source.GetTextureScale(from) : Vector2.one);
            target.SetTextureOffset(to, source.HasProperty(from) ? source.GetTextureOffset(from) : Vector2.zero);
        }

        internal static void CopyBaseAndAlpha(Material source, Material target)
        {
            HoRTMaterialFamily family = Get(source);
            bool legacy = family == HoRTMaterialFamily.LegacyLilToon;
            CopyTexture(source, legacy ? "_MainTex" : "_BaseMap", target, "_MainTex");
            Color color = source.GetColor(legacy ? "_Color" : "_BaseColor");
            string colorProperty = legacy ? "_Color" : "_BaseColor";
            int colorIndex = source.shader.FindPropertyIndex(colorProperty);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear && colorIndex >= 0
                && (source.shader.GetPropertyFlags(colorIndex) & ShaderPropertyFlags.HDR) == 0)
                color = color.linear;
            // Hair's solid coverage ignores tint alpha and its optical _Opacity.
            if (family == HoRTMaterialFamily.Hair) color.a = 1;
            target.SetColor("_Color", color);
            bool coverage = family == HoRTMaterialFamily.Eye || family == HoRTMaterialFamily.Skin;
            target.SetFloat("_HoRTAlphaDomain", legacy ? 0 : family == HoRTMaterialFamily.Cloth ? 2 : coverage ? 3 : 1);
            target.SetFloat("_Cutoff", Float(source, "_Cutoff", .5f));
            bool arithmetic = legacy || family == HoRTMaterialFamily.Cloth;
            target.SetFloat("_AlphaMaskMode", arithmetic ? Float(source, "_AlphaMaskMode") : 0);
            target.SetFloat("_AlphaMaskScale", arithmetic ? Float(source, "_AlphaMaskScale", 1) : 1);
            target.SetFloat("_AlphaMaskValue", arithmetic ? Float(source, "_AlphaMaskValue") : 0);
            CopyTexture(source, coverage ? "_CoverageMap" : "_AlphaMask", target, "_AlphaMask");
        }
    }
}
