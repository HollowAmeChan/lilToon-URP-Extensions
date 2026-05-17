using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class HoPostProcessStackVolumeEditor
    {
        private void DrawDropShadowProperties(Rect rect, ref float y, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            if (parameters0 == null || parameters1 == null)
            {
                return;
            }

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            if (p0 == Vector4.zero && p1 == Vector4.zero)
            {
                p0 = new Vector4(0.35f, -45.0f, 0.85f, 6.0f);
                p1 = new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
            }

            y = DrawHoPostDirectionDistanceViewControlButton(rect, y, element);

            p0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "距离", p0.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "角度", p0.y, -180.0f, 180.0f);
            y += LineHeight + LineSpacing;
            p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", p0.z, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "柔和度(像素)", p0.w, 0.0f, 32.0f);
            y += LineHeight + LineSpacing;
            p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "扩散(像素)", p1.x, 0.0f, 8.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
        }
    }
}
