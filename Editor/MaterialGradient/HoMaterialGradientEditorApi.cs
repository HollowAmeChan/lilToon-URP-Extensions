using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.MaterialGradient
{
    public static class HoMaterialGradientEditorApi
    {
        private static readonly HoMaterialGradientPropertyGui Gui = new();

        public static bool IsGradientTexture(MaterialProperty property)
        {
            return HoMaterialGradientPropertyRules.IsGradientTexture(property);
        }

        public static bool TryDrawGradientTexture(Rect rect, MaterialProperty property, GUIContent label, MaterialEditor editor)
        {
            if (!IsGradientTexture(property))
            {
                return false;
            }

            Gui.OnGUI(rect, property, label, editor);
            return true;
        }

        public static bool TryDrawGradientTextureLayout(MaterialProperty property, GUIContent label, MaterialEditor editor)
        {
            if (!IsGradientTexture(property))
            {
                return false;
            }

            Gui.OnGUI(EditorGUILayout.GetControlRect(false, HoMaterialGradientPropertyGui.PropertyHeight), property, label, editor);
            return true;
        }

        public static void CleanUnused(Material material, MaterialProperty[] properties)
        {
            HoMaterialGradientTextureBaker.CleanUnused(material, properties);
        }
    }
}
