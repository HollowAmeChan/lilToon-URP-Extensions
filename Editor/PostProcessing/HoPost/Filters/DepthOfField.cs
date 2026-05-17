using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class HoPostProcessStackVolumeEditor
    {
        private static readonly string[] DepthOfFieldModes =
        {
            "Gaussian",
            "Bokeh"
        };

        private static int GetDepthOfFieldLineCount(SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            int mode = parameters0 != null ? Mathf.Clamp(Mathf.RoundToInt(parameters0.vector4Value.x), 0, DepthOfFieldModes.Length - 1) : 1;
            return mode == 0 ? 6 : 8;
        }

        private static void DrawDepthOfFieldProperties(Rect rect, ref float y, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            if (parameters0 == null || parameters1 == null || parameters2 == null)
            {
                return;
            }

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            if (p0 == Vector4.zero && p1 == Vector4.zero && p2 == Vector4.zero)
            {
                p0 = new Vector4(1.0f, 10.0f, 50.0f, 5.6f);
                p1 = new Vector4(10.0f, 30.0f, 8.0f, 1.0f);
                p2 = new Vector4(5.0f, 1.0f, 0.0f, 0.0f);
            }

            int mode = EditorGUI.Popup(
                new Rect(rect.x, y, rect.width, LineHeight),
                "模式",
                Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, DepthOfFieldModes.Length - 1),
                DepthOfFieldModes);
            p0.x = mode;
            y += LineHeight + LineSpacing;

            if (mode == 0)
            {
                p1.x = EditorGUI.FloatField(new Rect(rect.x, y, rect.width, LineHeight), "开始距离", Mathf.Max(0.0f, p1.x));
                y += LineHeight + LineSpacing;
                p1.y = EditorGUI.FloatField(new Rect(rect.x, y, rect.width, LineHeight), "结束距离", Mathf.Max(p1.x + 0.001f, p1.y));
                y += LineHeight + LineSpacing;
                p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "最大半径", p1.z, 0.0f, 16.0f);
                y += LineHeight + LineSpacing;
                p1.w = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "高质量采样", p1.w > 0.5f) ? 1.0f : 0.0f;
                y += LineHeight + LineSpacing;
            }
            else
            {
                p0.y = EditorGUI.FloatField(new Rect(rect.x, y, rect.width, LineHeight), "焦点距离", Mathf.Max(0.001f, p0.y));
                y += LineHeight + LineSpacing;
                p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "焦距", p0.z, 1.0f, 300.0f);
                y += LineHeight + LineSpacing;
                p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "光圈", p0.w, 1.0f, 32.0f);
                y += LineHeight + LineSpacing;
                p2.x = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "叶片数量", Mathf.Clamp(Mathf.RoundToInt(p2.x), 3, 9), 3, 9);
                y += LineHeight + LineSpacing;
                p2.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "叶片弧度", p2.y, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                p2.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "叶片旋转", p2.z, -180.0f, 180.0f);
                y += LineHeight + LineSpacing;
            }

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
        }
    }
}
