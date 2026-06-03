using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.MaterialGradient
{
    internal static class HoMaterialGradientTextureBaker
    {
        internal const int Resolution = 256;

        public static Gradient CreateDefaultGradient()
        {
            return new Gradient
            {
                mode = GradientMode.Blend,
                colorKeys = new[]
                {
                    new GradientColorKey(Color.black, 0.0f),
                    new GradientColorKey(Color.white, 1.0f)
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(1.0f, 1.0f)
                }
            };
        }

        public static Gradient CloneGradient(Gradient source)
        {
            if (source == null)
            {
                return CreateDefaultGradient();
            }

            return new Gradient
            {
                mode = source.mode,
                colorKeys = source.colorKeys.ToArray(),
                alphaKeys = source.alphaKeys.ToArray()
            };
        }

        public static bool TryDecode(Texture2D texture, string propertyName, out Gradient gradient)
        {
            gradient = null;
            if (texture == null || string.IsNullOrEmpty(texture.name))
            {
                return false;
            }

            return TryDecode(texture.name, propertyName, out gradient);
        }

        public static bool TryDecode(string textureName, string propertyName, out Gradient gradient)
        {
            gradient = null;
            if (string.IsNullOrEmpty(textureName))
            {
                return false;
            }

            string[] prefixes =
            {
                HoMaterialGradientPropertyRules.GetTextureNamePrefix(propertyName)
            };

            foreach (string prefix in prefixes)
            {
                if (!textureName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string json = textureName.Substring(prefix.Length);
                try
                {
                    GradientPayload payload = JsonUtility.FromJson<GradientPayload>(json);
                    gradient = payload?.ToGradient(new Gradient());
                    return gradient != null;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return false;
        }

        public static void ApplyGradient(Material material, string propertyName, Gradient gradient)
        {
            if (material == null || gradient == null || !AssetDatabase.Contains(material))
            {
                return;
            }

            string materialPath = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(materialPath))
            {
                return;
            }

            Texture2D texture = GetOrCreateTexture(material, materialPath, propertyName, gradient.mode);
            if (texture == null)
            {
                return;
            }

            Undo.RecordObject(texture, "Change Material Gradient Texture");
            texture.name = HoMaterialGradientPropertyRules.GetTextureNamePrefix(propertyName) + Encode(gradient);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = ResolveFilterMode(gradient.mode);

            Bake(gradient, texture);
            material.SetTexture(propertyName, texture);
            EditorUtility.SetDirty(texture);
            EditorUtility.SetDirty(material);
        }

        public static void CleanUnused(Material material, MaterialProperty[] properties)
        {
            if (material == null || !AssetDatabase.Contains(material))
            {
                return;
            }

            properties ??= Array.Empty<MaterialProperty>();

            string materialPath = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(materialPath))
            {
                return;
            }

            HashSet<Texture2D> used = new();
            foreach (MaterialProperty property in properties.Where(HoMaterialGradientPropertyRules.IsGradientTexture))
            {
                Texture2D texture = material.GetTexture(property.name) as Texture2D;
                if (texture != null && AssetDatabase.GetAssetPath(texture) == materialPath)
                {
                    used.Add(texture);
                }
            }

            Texture2D[] generatedTextures = AssetDatabase.LoadAllAssetsAtPath(materialPath)
                .OfType<Texture2D>()
                .Where(texture => HoMaterialGradientPropertyRules.IsGeneratedTextureName(texture.name))
                .ToArray();

            foreach (Texture2D texture in generatedTextures)
            {
                if (!used.Contains(texture))
                {
                    AssetDatabase.RemoveObjectFromAsset(texture);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static Texture2D GetOrCreateTexture(Material material, string materialPath, string propertyName, GradientMode mode)
        {
            Texture2D texture = material.GetTexture(propertyName) as Texture2D;
            if (!IsReusableGeneratedTexture(texture, materialPath, propertyName))
            {
                texture = LoadTexture(materialPath, propertyName);
            }

            if (texture == null)
            {
                texture = new Texture2D(Resolution, 1, TextureFormat.ARGB32, false)
                {
                    name = HoMaterialGradientPropertyRules.GetTextureNamePrefix(propertyName),
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = ResolveFilterMode(mode)
                };

                AssetDatabase.AddObjectToAsset(texture, materialPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(materialPath);
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = ResolveFilterMode(mode);

            if (texture.width != Resolution || texture.height != 1)
            {
#if UNITY_2021_2_OR_NEWER
                texture.Reinitialize(Resolution, 1);
#else
                texture.Resize(Resolution, 1);
#endif
            }

            return texture;
        }

        private static bool IsReusableGeneratedTexture(Texture2D texture, string materialPath, string propertyName)
        {
            if (texture == null || AssetDatabase.GetAssetPath(texture) != materialPath)
            {
                return false;
            }

            return texture.name.StartsWith(HoMaterialGradientPropertyRules.GetTextureNamePrefix(propertyName), StringComparison.OrdinalIgnoreCase)
                && HoMaterialGradientPropertyRules.IsGeneratedTextureName(texture.name);
        }

        private static FilterMode ResolveFilterMode(GradientMode mode)
        {
            return mode == GradientMode.Fixed ? FilterMode.Point : FilterMode.Bilinear;
        }

        private static Texture2D LoadTexture(string materialPath, string propertyName)
        {
            string currentPrefix = HoMaterialGradientPropertyRules.GetTextureNamePrefix(propertyName);

            return AssetDatabase.LoadAllAssetsAtPath(materialPath)
                .OfType<Texture2D>()
                .FirstOrDefault(texture => texture.name.StartsWith(currentPrefix, StringComparison.OrdinalIgnoreCase));
        }

        private static void Bake(Gradient gradient, Texture2D texture)
        {
            for (int x = 0; x < texture.width; x++)
            {
                Color color = gradient.Evaluate((float)x / (texture.width - 1));
                texture.SetPixel(x, 0, color);
            }

            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        private static string Encode(Gradient gradient)
        {
            return JsonUtility.ToJson(new GradientPayload(gradient));
        }

        [Serializable]
        private sealed class GradientPayload
        {
            public GradientMode mode;
            public ColorKey[] colorKeys;
            public AlphaKey[] alphaKeys;

            public GradientPayload()
            {
            }

            public GradientPayload(Gradient source)
            {
                mode = source.mode;
                colorKeys = source.colorKeys.Select(key => new ColorKey(key)).ToArray();
                alphaKeys = source.alphaKeys.Select(key => new AlphaKey(key)).ToArray();
            }

            public Gradient ToGradient(Gradient gradient)
            {
                if (colorKeys == null || alphaKeys == null)
                {
                    return null;
                }

                gradient.mode = mode;
                gradient.colorKeys = colorKeys.Select(key => key.ToGradientKey()).ToArray();
                gradient.alphaKeys = alphaKeys.Select(key => key.ToGradientKey()).ToArray();
                return gradient;
            }
        }

        [Serializable]
        private struct ColorKey
        {
            public Color color;
            public float time;

            public ColorKey(GradientColorKey source)
            {
                color = source.color;
                time = source.time;
            }

            public GradientColorKey ToGradientKey()
            {
                return new GradientColorKey(color, time);
            }
        }

        [Serializable]
        private struct AlphaKey
        {
            public float alpha;
            public float time;

            public AlphaKey(GradientAlphaKey source)
            {
                alpha = source.alpha;
                time = source.time;
            }

            public GradientAlphaKey ToGradientKey()
            {
                return new GradientAlphaKey(alpha, time);
            }
        }
    }
}
