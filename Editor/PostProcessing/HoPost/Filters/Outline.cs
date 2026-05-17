using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class HoPostProcessStackVolumeEditor
    {
        private static void DrawOutlineProperties(Rect rect, ref float y, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            if (parameters0 == null || parameters1 == null)
            {
                return;
            }

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;

            p0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "线宽(像素)", p0.x, 0.0f, 8.0f);
            y += LineHeight + LineSpacing;
            p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "深度权重", p0.y, 0.0f, 10.0f);
            y += LineHeight + LineSpacing;
            p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "法线权重", p0.z, 0.0f, 10.0f);
            y += LineHeight + LineSpacing;
            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "阈值", p0.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "柔和度", p1.x, 0.0001f, 1.0f);
            y += LineHeight + LineSpacing;
            p1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "深度灵敏度", p1.y, 0.0f, 5.0f);
            y += LineHeight + LineSpacing;
            p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "法线灵敏度", p1.z, 0.0f, 5.0f);
            y += LineHeight + LineSpacing;
            p1.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", p1.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
        }
    }
}
