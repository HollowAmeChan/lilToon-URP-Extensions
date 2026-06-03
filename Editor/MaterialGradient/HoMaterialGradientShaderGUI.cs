using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.MaterialGradient
{
    public sealed class HoMaterialGradientShaderGUI : ShaderGUI
    {
        private readonly Dictionary<string, HoMaterialGradientPropertyGui> gradientDrawers = new();

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            EditorGUI.BeginChangeCheck();

            foreach (MaterialProperty property in properties)
            {
                if (property.propertyFlags.HasFlag(UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector))
                {
                    continue;
                }

                if (HoMaterialGradientPropertyRules.IsGradientTexture(property))
                {
                    DrawGradientProperty(materialEditor, property);
                }
                else
                {
                    materialEditor.ShaderProperty(property, property.displayName);
                }
            }

            base.OnGUI(materialEditor, Array.Empty<MaterialProperty>());

            if (EditorGUI.EndChangeCheck())
            {
                foreach (UnityEngine.Object target in materialEditor.targets)
                {
                    EditorUtility.SetDirty(target);
                }
            }
        }

        private void DrawGradientProperty(MaterialEditor materialEditor, MaterialProperty property)
        {
            if (!gradientDrawers.TryGetValue(property.name, out HoMaterialGradientPropertyGui drawer))
            {
                drawer = new HoMaterialGradientPropertyGui();
                gradientDrawers[property.name] = drawer;
            }

            Rect rect = EditorGUILayout.GetControlRect(false, HoMaterialGradientPropertyGui.PropertyHeight);
            drawer.OnGUI(rect, property, new GUIContent(property.displayName), materialEditor);
        }
    }
}
