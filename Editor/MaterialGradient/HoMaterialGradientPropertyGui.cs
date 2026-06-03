using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.MaterialGradient
{
    internal sealed class HoMaterialGradientPropertyGui
    {
        private static readonly Dictionary<string, Texture2D> LastKnownTextures = new();
        private static readonly Dictionary<string, Gradient> GradientCache = new();

        public static float PropertyHeight => EditorGUIUtility.singleLineHeight;

        public void OnGUI(Rect rect, MaterialProperty property, GUIContent label, MaterialEditor editor)
        {
            if (property.targets.Length != 1)
            {
                EditorGUI.LabelField(rect, label, new GUIContent("Gradient editing supports one material at a time."));
                return;
            }

            Material material = (Material)property.targets[0];
            string materialKey = material.GetInstanceID() + "_" + property.name;
            Texture2D currentTexture = GetCurrentTexture(material, property.name);
            bool textureChanged = UpdateTextureChangeState(materialKey, currentTexture);
            Gradient gradient = ResolveGradient(materialKey, currentTexture, material, property.name);

            Rect valueRect = EditorGUI.PrefixLabel(rect, label);

            EditorGUI.BeginChangeCheck();
            gradient = EditorGUI.GradientField(valueRect, gradient);
            bool gradientChanged = EditorGUI.EndChangeCheck();

            if (textureChanged)
            {
                editor.Repaint();
            }

            if (!gradientChanged)
            {
                return;
            }

            GradientCache[materialKey] = gradient;
            bool commit = Event.current.type == EventType.MouseUp
                || Event.current.type == EventType.KeyUp
                || Event.current.type == EventType.ExecuteCommand;

            HoMaterialGradientDebounce.Request(
                materialKey,
                0.1,
                () => HoMaterialGradientTextureBaker.ApplyGradient(material, property.name, gradient),
                commit);
        }

        private static Texture2D GetCurrentTexture(Material material, string propertyName)
        {
#if UNITY_2021_1_OR_NEWER
            return material.HasTexture(propertyName) ? material.GetTexture(propertyName) as Texture2D : null;
#else
            return material.GetTexture(propertyName) as Texture2D;
#endif
        }

        private static bool UpdateTextureChangeState(string materialKey, Texture2D currentTexture)
        {
            if (LastKnownTextures.TryGetValue(materialKey, out Texture2D lastTexture) && lastTexture == currentTexture)
            {
                return false;
            }

            LastKnownTextures[materialKey] = currentTexture;
            GradientCache.Remove(materialKey);
            return true;
        }

        private static Gradient ResolveGradient(string materialKey, Texture2D currentTexture, Material material, string propertyName)
        {
            if (GradientCache.TryGetValue(materialKey, out Gradient cached))
            {
                return cached;
            }

            if (HoMaterialGradientTextureBaker.TryDecode(currentTexture, propertyName, out Gradient gradient))
            {
                GradientCache[materialKey] = gradient;
                return gradient;
            }

            string materialPath = AssetDatabase.GetAssetPath(material);
            if (!string.IsNullOrEmpty(materialPath))
            {
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(materialPath))
                {
                    if (asset is Texture2D texture && HoMaterialGradientTextureBaker.TryDecode(texture, propertyName, out gradient))
                    {
                        GradientCache[materialKey] = gradient;
                        return gradient;
                    }
                }
            }

            gradient = HoMaterialGradientTextureBaker.CreateDefaultGradient();
            GradientCache[materialKey] = gradient;
            return gradient;
        }
    }
}
