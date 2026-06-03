using System;
using UnityEditor;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.MaterialGradient
{
    internal static class HoMaterialGradientPropertyRules
    {
        internal const string GeneratedTextureMarker = " HoGradientTexture ";

        public static bool IsGradientTexture(MaterialProperty property)
        {
            if (property == null || property.propertyType != ShaderPropertyType.Texture)
            {
                return false;
            }

            return ContainsKey(property.name, "Ramp")
                || ContainsKey(property.displayName, "Ramp")
                || ContainsKey(property.name, "Gradient")
                || ContainsKey(property.displayName, "Gradient");
        }

        public static bool IsGeneratedTextureName(string textureName)
        {
            return !string.IsNullOrEmpty(textureName)
                && textureName.IndexOf(GeneratedTextureMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string GetTextureNamePrefix(string propertyName)
        {
            return propertyName + GeneratedTextureMarker;
        }

        private static bool ContainsKey(string value, string key)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
