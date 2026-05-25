using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private void DrawGateWeaveElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureGateWeaveDefaults(parameters0, parameters1);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 gateParams0 = parameters0.vector4Value;
            Vector4 gateParams1 = parameters1.vector4Value;

            gateParams0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "位置抖动幅度", gateParams0.x, 0.0f, 0.1f);
            y += LineHeight + LineSpacing;
            gateParams0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "位置抖动频率", gateParams0.y, 0.0f, 50.0f);
            y += LineHeight + LineSpacing;
            gateParams0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "旋转抖动幅度", gateParams0.z, 0.0f, 0.1f);
            y += LineHeight + LineSpacing;
            gateParams0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "旋转抖动频率", gateParams0.w, 0.0f, 50.0f);
            y += LineHeight + LineSpacing;
            gateParams1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "缩放", gateParams1.x, 1.0f, 2.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = gateParams0;
            parameters1.vector4Value = gateParams1;
            EditorGUI.indentLevel--;
        }

        private static void EnsureGateWeaveDefaults(SerializedProperty parameters0, SerializedProperty parameters1)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.02f, 20.0f, 0.05f, 15.0f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
            }
        }
    }
}
