using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private static readonly string[] VhsTypeNames = { "弱", "中", "强" };

        private static bool GetVhsUsesScanline(SerializedProperty element)
        {
            SerializedProperty parameters0 = element?.FindPropertyRelative("parameters0");
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            return parameters0.vector4Value.w > 0.5f;
        }

        private void DrawVhsElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty texture = element.FindPropertyRelative("texture");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureVhsDefaults(parameters0, parameters1);
            if (texture != null && texture.propertyType == SerializedPropertyType.ObjectReference && texture.objectReferenceValue == null)
            {
                texture.objectReferenceValue = LoadDefaultVhsEdgeNoiseTexture();
            }

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;

            int type = Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, VhsTypeNames.Length - 1);
            type = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "类型", type, VhsTypeNames);
            p0.x = type;
            y += LineHeight + LineSpacing;

            p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "噪点强度", Mathf.Clamp01(p0.y), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "锐化", Mathf.Clamp01(p0.z), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            p0.w = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "扫描线", p0.w > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;

            if (p0.w > 0.5f)
            {
                p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "大小", Mathf.Clamp(p1.x, 0.01f, 1.0f), 0.01f, 1.0f);
                y += LineHeight + LineSpacing;
            }

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            EditorGUI.indentLevel--;
        }

        private static void EnsureVhsDefaults(SerializedProperty parameters0, SerializedProperty parameters1)
        {
            bool hasParameterMarker = parameters1 != null
                && parameters1.propertyType == SerializedPropertyType.Vector4
                && parameters1.vector4Value.w > 0.5f;

            if (!hasParameterMarker
                && parameters0 != null
                && parameters0.propertyType == SerializedPropertyType.Vector4
                && parameters0.vector4Value.sqrMagnitude <= 0.000001f
                && parameters1 != null
                && parameters1.propertyType == SerializedPropertyType.Vector4
                && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 p1 = parameters1.vector4Value;
                if (p1.x <= 0.000001f)
                {
                    p1.x = 1.0f;
                }

                p1.w = 1.0f;
                parameters1.vector4Value = p1;
            }
        }

        private static Texture2D LoadDefaultVhsEdgeNoiseTexture()
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(DefaultVhsEdgeNoiseTextureGuid);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
    }
}
