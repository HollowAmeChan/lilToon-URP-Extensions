using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private void DrawGrainCustomElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty texture = element.FindPropertyRelative("texture");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureGrainCustomDefaults(parameters0, parameters1);
            if (texture != null && texture.propertyType == SerializedPropertyType.ObjectReference && texture.objectReferenceValue == null)
            {
                texture.objectReferenceValue = LoadDefaultGrainNoiseTexture();
            }

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 grainParams0 = parameters0.vector4Value;
            grainParams0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "强度", grainParams0.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            grainParams0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "大小", grainParams0.y, 0.3f, 3.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = grainParams0;
            EditorGUI.indentLevel--;
        }

        private static void EnsureGrainCustomDefaults(SerializedProperty parameters0, SerializedProperty parameters1)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = parameters0.vector4Value;
            if (value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.5f, 2.0f, 0.9f, 0.0f);
                return;
            }

            if (value.y > 3.0f || value.z > 3.0f || value.w > 1.0f)
            {
                Vector4 legacy = parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 ? parameters1.vector4Value : Vector4.zero;
                float intensity = legacy.sqrMagnitude > 0.000001f ? legacy.y : 0.5f;
                float size = legacy.sqrMagnitude > 0.000001f ? legacy.z : 2.0f;
                float lumContrib = legacy.sqrMagnitude > 0.000001f ? legacy.w : 0.9f;
                float colored = legacy.sqrMagnitude > 0.000001f ? legacy.x : 0.0f;
                parameters0.vector4Value = new Vector4(Mathf.Clamp01(intensity), Mathf.Clamp(size, 0.3f, 3.0f), Mathf.Clamp01(lumContrib), colored > 0.5f ? 1.0f : 0.0f);
                if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4)
                {
                    parameters1.vector4Value = Vector4.zero;
                }
                return;
            }

            if (value.z <= 0.0001f)
            {
                value.z = 0.9f;
                parameters0.vector4Value = value;
            }
        }

        private static Texture2D LoadDefaultGrainNoiseTexture()
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>($"{PackageAssetRoot}/Runtime/ShoostPostProcessing/Textures/ShoostGrainNoise.png");
        }
    }
}
