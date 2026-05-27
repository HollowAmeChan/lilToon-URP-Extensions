using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ScreenProcessStackVolumeEditor
    {
        private static readonly string[] SkyTyndallQualities =
        {
            "低",
            "中",
            "高"
        };

        private static readonly string[] SkyTyndallDitherModes =
        {
            "蓝噪声",
            "半调",
            "关闭"
        };

        private static int GetSkyTyndallLineCount(SerializedProperty element)
        {
            SerializedProperty parameters4 = element.FindPropertyRelative("parameters4");
            Vector4 p4 = parameters4 != null ? parameters4.vector4Value : Vector4.zero;
            bool fixedDirection = p4.w > 0.5f;
            return fixedDirection ? 19 : 20;
        }

        private void DrawSkyTyndallProperties(Rect rect, ref float y, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty parameters4 = element.FindPropertyRelative("parameters4");
            SerializedProperty parameters5 = element.FindPropertyRelative("parameters5");
            if (parameters0 == null || parameters1 == null || parameters2 == null || parameters3 == null || parameters4 == null || parameters5 == null)
            {
                return;
            }

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            Vector4 p3 = parameters3.vector4Value;
            Vector4 p4 = parameters4.vector4Value;
            Vector4 p5 = parameters5.vector4Value;
            EnsureSkyTyndallDefaults(ref p0, ref p1, ref p2, ref p3, ref p4, ref p5);

            p0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "半径", p0.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "天空阈值", p0.y, 0.0f, 8.0f);
            y += LineHeight + LineSpacing;
            p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "柔和过渡", p0.z, 0.0f, 4.0f);
            y += LineHeight + LineSpacing;
            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "天空曝光", p0.w, 0.0f, 8.0f);
            y += LineHeight + LineSpacing;

            p4.w = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "固定方向", p4.w > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;
            if (p4.w > 0.5f)
            {
                p4.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "方向角度", p4.z, -180.0f, 180.0f);
                p4 = BuildSkyTyndallDirection(p4.z, true);
                y += LineHeight + LineSpacing;
            }
            else
            {
                p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "中心 X", p1.x, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                p1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "中心 Y", p1.y, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
            }

            p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "衰减", p1.z, 0.70f, 0.99f);
            y += LineHeight + LineSpacing;
            p1.w = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "质量", Mathf.Clamp(Mathf.RoundToInt(p1.w), 0, SkyTyndallQualities.Length - 1), SkyTyndallQualities);
            y += LineHeight + LineSpacing;

            p5.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "权重", p5.x, 0.001f, 0.15f);
            y += LineHeight + LineSpacing;
            p5.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "采样模糊", p5.y, 0.0f, 3.0f);
            y += LineHeight + LineSpacing;
            p5.z = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "抖动模式", Mathf.Clamp(Mathf.RoundToInt(p5.z), 0, SkyTyndallDitherModes.Length - 1), SkyTyndallDitherModes);
            y += LineHeight + LineSpacing;
            p5.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "抖动强度", p5.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            p2.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "前景压制", p2.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p2.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "法线角度", p2.y, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p2.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "天空增益", p2.z, 0.0f, 8.0f);
            y += LineHeight + LineSpacing;
            p3.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", p3.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p3.y = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "仅显示光束", p3.y > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = p3;
            parameters4.vector4Value = p4;
            parameters5.vector4Value = p5;
        }

        private static void EnsureSkyTyndallDefaults(ref Vector4 p0, ref Vector4 p1, ref Vector4 p2, ref Vector4 p3, ref Vector4 p4, ref Vector4 p5)
        {
            if (p0 == Vector4.zero && p1 == Vector4.zero && p2 == Vector4.zero && p3 == Vector4.zero && p4 == Vector4.zero && p5 == Vector4.zero)
            {
                p0 = new Vector4(1.0f, 8.0f, 1.52f, 1.15f);
                p1 = new Vector4(0.65f, 1.0f, 0.70f, 2.0f);
                p2 = new Vector4(0.72f, 0.45f, 1.15f, 0.75f);
                p3 = new Vector4(0.68f, 0.0f, 1.0f, 1.0f);
                p4 = BuildSkyTyndallDirection(90.0f, false);
                p5 = new Vector4(0.06f, 1.2f, 0.0f, 0.99f);
                return;
            }

            if (p1.z <= 0.0001f || p1.z > 1.0f)
            {
                p1.z = 0.94f;
            }

            if (p4 == Vector4.zero)
            {
                p4 = BuildSkyTyndallDirection(90.0f, false);
            }
            else if (p4.w > 0.5f && p4.x * p4.x + p4.y * p4.y <= 0.0001f)
            {
                float angle = Mathf.Abs(p4.z) <= 0.0001f ? 90.0f : p4.z;
                p4 = BuildSkyTyndallDirection(angle, true);
            }

            if (p5 == Vector4.zero)
            {
                p5 = new Vector4(0.06f, 1.2f, 0.0f, 0.99f);
            }
            else if (p5.z < 1.5f && p5.w <= 0.0001f)
            {
                p5.w = 0.99f;
            }
        }

        private static Vector4 BuildSkyTyndallDirection(float angleDegrees, bool enabled)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector4(Mathf.Cos(radians), Mathf.Sin(radians), angleDegrees, enabled ? 1.0f : 0.0f);
        }
    }
}
