#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor
{
    internal static class UnityGltfPbrGraphToUrpLitConverter
    {
        private const string AssetMenuPath = "Assets/LilMatConvert/UnityGLTF PBRGraph -> URP Lit";
        private const string PbrGraphShaderPrefix = "UnityGLTF/PBRGraph";
        private const string UrpLitShaderName = "Universal Render Pipeline/Lit";
        private const string GeneratedMaskDir = "Assets/Generated/UnityGLTF_URP_Masks";

        [MenuItem(AssetMenuPath, false, 2100)]
        public static void ConvertSelectedAssetMaterials()
        {
            var materials = Selection.objects
                .OfType<Material>()
                .Where(IsPbrGraphMaterial)
                .ToArray();

            ConvertMaterials(materials);
        }

        [MenuItem(AssetMenuPath, true)]
        public static bool ConvertSelectedAssetMaterialsValidate()
        {
            return Selection.objects
                .OfType<Material>()
                .Any(IsPbrGraphMaterial);
        }

        private static void ConvertMaterials(Material[] materials)
        {
            if (materials == null || materials.Length == 0)
                return;

            var urpLit = Shader.Find(UrpLitShaderName);
            if (urpLit == null)
            {
                EditorUtility.DisplayDialog(
                    "UnityGLTF Material Converter",
                    "Could not find shader: " + UrpLitShaderName,
                    "OK");
                return;
            }

            var converted = 0;
            foreach (var material in materials)
            {
                if (ConvertMaterial(material, urpLit))
                    converted++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Converted {converted} UnityGLTF/PBRGraph material(s) to URP/Lit.");
        }

        private static bool ConvertMaterial(Material material, Shader urpLit)
        {
            if (material == null || !IsPbrGraphMaterial(material))
                return false;

            var baseColor = GetColor(material, "baseColorFactor", Color.white);
            var baseMap = GetTexture(material, "baseColorTexture");
            var baseOffset = GetTextureOffset(material, "baseColorTexture", Vector2.zero);
            var baseScale = GetTextureScale(material, "baseColorTexture", Vector2.one);

            var metallic = GetFloat(material, "metallicFactor", 0f);
            var roughness = GetFloat(material, "roughnessFactor", 0.5f);
            var metallicRoughness = GetTexture(material, "metallicRoughnessTexture");

            var normalMap = GetTexture(material, "normalTexture");
            var normalScale = GetFloat(material, "normalScale", 1f);

            var occlusionMap = GetTexture(material, "occlusionTexture");
            var occlusionStrength = GetFloat(material, "occlusionStrength", 1f);

            var emissionMap = GetTexture(material, "emissiveTexture");
            var emissionColor = GetColor(material, "emissiveFactor", Color.black);

            var cutoff = GetFloat(material, "alphaCutoff", 0.5f);
            var cull = GetFloat(material, "_Cull", 2f);
            var transparent = material.GetTag("RenderType", false) == "Transparent" ||
                              GetFloat(material, "_Surface", 0f) > 0.5f ||
                              material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT");
            var alphaClip = material.IsKeywordEnabled("_ALPHATEST_ON") ||
                            material.GetTag("RenderType", false) == "TransparentCutout";

            Undo.RecordObject(material, "Convert UnityGLTF PBRGraph to URP Lit");
            material.shader = urpLit;

            SetColor(material, "_BaseColor", baseColor);
            SetTexture(material, "_BaseMap", baseMap);
            SetTextureOffset(material, "_BaseMap", baseOffset);
            SetTextureScale(material, "_BaseMap", baseScale);

            SetFloat(material, "_Metallic", metallic);
            SetFloat(material, "_Smoothness", Mathf.Clamp01(1f - roughness));

            if (metallicRoughness != null)
            {
                var urpMask = CreateUrpMetallicSmoothnessMap(metallicRoughness, material);
                if (urpMask != null)
                {
                    SetTexture(material, "_MetallicGlossMap", urpMask);
                    SetFloat(material, "_SmoothnessTextureChannel", 0f);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
            }
            else
            {
                material.DisableKeyword("_METALLICSPECGLOSSMAP");
            }

            SetTexture(material, "_BumpMap", normalMap);
            SetFloat(material, "_BumpScale", normalScale);
            SetKeyword(material, "_NORMALMAP", normalMap != null);

            SetTexture(material, "_OcclusionMap", occlusionMap);
            SetFloat(material, "_OcclusionStrength", occlusionStrength);

            SetTexture(material, "_EmissionMap", emissionMap);
            SetColor(material, "_EmissionColor", emissionColor);
            var hasEmission = emissionMap != null || emissionColor.maxColorComponent > 0.0001f;
            SetKeyword(material, "_EMISSION", hasEmission);
            material.globalIlluminationFlags = hasEmission
                ? MaterialGlobalIlluminationFlags.BakedEmissive
                : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            ApplySurfaceSettings(material, transparent, alphaClip, cutoff);
            SetFloat(material, "_Cull", cull);

            EditorUtility.SetDirty(material);
            return true;
        }

        private static void ApplySurfaceSettings(Material material, bool transparent, bool alphaClip, float cutoff)
        {
            SetFloat(material, "_AlphaClip", alphaClip ? 1f : 0f);
            SetFloat(material, "_Cutoff", cutoff);
            SetKeyword(material, "_ALPHATEST_ON", alphaClip);

            if (transparent)
            {
                SetFloat(material, "_Surface", 1f);
                SetFloat(material, "_Blend", 0f);
                SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
                SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                SetFloat(material, "_ZWrite", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                return;
            }

            SetFloat(material, "_Surface", 0f);
            SetFloat(material, "_SrcBlend", (float)BlendMode.One);
            SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
            SetFloat(material, "_ZWrite", 1f);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");

            if (alphaClip)
            {
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = -1;
            }
        }

        private static Texture2D CreateUrpMetallicSmoothnessMap(Texture source, Material owner)
        {
            if (!(source is Texture2D sourceTexture))
            {
                Debug.LogWarning($"Skipping metallic-roughness conversion for {owner.name}: source texture is not a Texture2D.", owner);
                return null;
            }

            var sourcePath = AssetDatabase.GetAssetPath(sourceTexture);
            if (string.IsNullOrEmpty(sourcePath))
                return null;

            Directory.CreateDirectory(GeneratedMaskDir);
            var outputPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{GeneratedMaskDir}/{SanitizeFileName(owner.name)}_MetallicSmoothness.png");

            var sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            var shouldRestoreReadable = sourceImporter != null && !sourceImporter.isReadable;
            if (shouldRestoreReadable)
            {
                sourceImporter.isReadable = true;
                sourceImporter.SaveAndReimport();
                sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            }

            if (sourceTexture == null || !sourceTexture.isReadable)
            {
                if (shouldRestoreReadable)
                {
                    sourceImporter.isReadable = false;
                    sourceImporter.SaveAndReimport();
                }
                Debug.LogWarning($"Skipping metallic-roughness conversion for {owner.name}: could not read {source.name}.", owner);
                return null;
            }

            Color32[] pixels;
            var width = sourceTexture.width;
            var height = sourceTexture.height;
            try
            {
                pixels = sourceTexture.GetPixels32();
            }
            finally
            {
                if (shouldRestoreReadable)
                {
                    sourceImporter.isReadable = false;
                    sourceImporter.SaveAndReimport();
                }
            }

            var outputPixels = new Color32[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                outputPixels[i] = new Color32(p.b, 0, 0, (byte)(255 - p.g));
            }

            var output = new Texture2D(width, height, TextureFormat.RGBA32, true, true);
            output.SetPixels32(outputPixels);
            output.Apply(true, false);
            File.WriteAllBytes(outputPath, output.EncodeToPNG());
            Object.DestroyImmediate(output);

            AssetDatabase.ImportAsset(outputPath);
            var outputImporter = AssetImporter.GetAtPath(outputPath) as TextureImporter;
            if (outputImporter != null)
            {
                outputImporter.sRGBTexture = false;
                outputImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                outputImporter.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        }

        private static bool IsPbrGraphMaterial(Material material)
        {
            return material != null &&
                   material.shader != null &&
                   material.shader.name.StartsWith(PbrGraphShaderPrefix);
        }

        private static Texture GetTexture(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetTexture(propertyName) : null;
        }

        private static float GetFloat(Material material, string propertyName, float fallback)
        {
            return material.HasProperty(propertyName) ? material.GetFloat(propertyName) : fallback;
        }

        private static Color GetColor(Material material, string propertyName, Color fallback)
        {
            return material.HasProperty(propertyName) ? material.GetColor(propertyName) : fallback;
        }

        private static Vector2 GetTextureOffset(Material material, string propertyName, Vector2 fallback)
        {
            return material.HasProperty(propertyName) ? material.GetTextureOffset(propertyName) : fallback;
        }

        private static Vector2 GetTextureScale(Material material, string propertyName, Vector2 fallback)
        {
            return material.HasProperty(propertyName) ? material.GetTextureScale(propertyName) : fallback;
        }

        private static void SetTexture(Material material, string propertyName, Texture value)
        {
            if (material.HasProperty(propertyName))
                material.SetTexture(propertyName, value);
        }

        private static void SetTextureOffset(Material material, string propertyName, Vector2 value)
        {
            if (material.HasProperty(propertyName))
                material.SetTextureOffset(propertyName, value);
        }

        private static void SetTextureScale(Material material, string propertyName, Vector2 value)
        {
            if (material.HasProperty(propertyName))
                material.SetTextureScale(propertyName, value);
        }

        private static void SetFloat(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }

        private static void SetColor(Material material, string propertyName, Color value)
        {
            if (material.HasProperty(propertyName))
                material.SetColor(propertyName, value);
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
#endif
