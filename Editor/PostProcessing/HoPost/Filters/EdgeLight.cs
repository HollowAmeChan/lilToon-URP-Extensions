using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class HoPostProcessStackVolumeEditor
    {
        private static readonly string[] EdgeLightModes =
        {
            "单向",
            "双向",
            "单向锐化",
            "双向锐化"
        };

        private void DrawEdgeLightProperties(Rect rect, ref float y, SerializedProperty element)
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
            if (p2 == Vector4.zero)
            {
                p2 = new Vector4(1.0f, 0.65f, 0.45f, 1.0f);
            }

            y = DrawHoPostDirectionDistanceViewControlButton(rect, y, element);

            p0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "边缘宽度", p0.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "HDR 亮度", p0.y, 0.0f, 10.0f);
            y += LineHeight + LineSpacing;
            p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "对比度", p0.z, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", p0.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "角度", p1.x, -180.0f, 180.0f);
            y += LineHeight + LineSpacing;
            p1.y = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "模式", Mathf.Clamp(Mathf.RoundToInt(p1.y), 0, EdgeLightModes.Length - 1), EdgeLightModes);
            y += LineHeight + LineSpacing;
            p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "外扩宽度", p1.z, 0.0f, 8.0f);
            y += LineHeight + LineSpacing;
            p1.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "外扩强度", p1.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p2.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "表面法线权重", p2.x, 0.0f, 2.0f);
            y += LineHeight + LineSpacing;
            p2.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "深度边界权重", p2.y, 0.0f, 2.0f);
            y += LineHeight + LineSpacing;
            p2.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "深度灵敏度", p2.z, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p2.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "方向影响", p2.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
        }
    }
}
