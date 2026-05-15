using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private void DrawDistortionElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty texture = element.FindPropertyRelative("texture");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureDistortionDefaults(parameters0, parameters1);
            if (texture != null && texture.propertyType == SerializedPropertyType.ObjectReference && texture.objectReferenceValue == null)
            {
                texture.objectReferenceValue = LoadDefaultDistortionTexture();
            }

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: true, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 distortionParams0 = parameters0.vector4Value;
            Vector4 distortionParams1 = parameters1.vector4Value;
            distortionParams1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "纹理平铺 X", distortionParams1.x, 0.01f, 10.0f);
            y += LineHeight + LineSpacing;
            distortionParams1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "纹理平铺 Y", distortionParams1.y, 0.01f, 10.0f);
            y += LineHeight + LineSpacing;
            distortionParams0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "衰减", distortionParams0.x, 0.0f, 10.0f);
            y += LineHeight + LineSpacing;
            distortionParams0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "X 影响", distortionParams0.y, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            distortionParams0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "Y 影响", distortionParams0.z, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            distortionParams0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "流动强度", distortionParams0.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            distortionParams1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "速度 X", distortionParams1.z, -2.0f, 2.0f);
            y += LineHeight + LineSpacing;
            distortionParams1.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "速度 Y", distortionParams1.w, -2.0f, 2.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = distortionParams0;
            parameters1.vector4Value = distortionParams1;
            EditorGUI.indentLevel--;
        }

        private static void EnsureDistortionDefaults(SerializedProperty parameters0, SerializedProperty parameters1)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(5.0f, 0.1f, 0.2f, 0.1f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(1.0f, 1.0f, 0.0f, -2.0f);
            }
        }

        private static Texture2D LoadDefaultDistortionTexture()
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(DefaultDistortionTextureGuid);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
    }
}
