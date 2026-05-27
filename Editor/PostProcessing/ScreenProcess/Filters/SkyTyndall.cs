using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ScreenProcessStackVolumeEditor
    {
        private static readonly string[] SkyTyndallQualities =
        {
            "Low",
            "Medium",
            "High"
        };

        private void DrawSkyTyndallProperties(Rect rect, ref float y, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            if (parameters0 == null || parameters1 == null || parameters2 == null || parameters3 == null)
            {
                return;
            }

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            Vector4 p3 = parameters3.vector4Value;
            EnsureSkyTyndallDefaults(ref p0, ref p1, ref p2, ref p3);

            p0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "Radius", p0.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "Sky Threshold", p0.y, 0.0f, 8.0f);
            y += LineHeight + LineSpacing;
            p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "Soft Knee", p0.z, 0.0f, 4.0f);
            y += LineHeight + LineSpacing;
            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "Sky Exposure", p0.w, 0.0f, 8.0f);
            y += LineHeight + LineSpacing;

            p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "Center X", p1.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "Center Y", p1.y, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "Decay", p1.z, 0.0f, 4.0f);
            y += LineHeight + LineSpacing;
            p1.w = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "Quality", Mathf.Clamp(Mathf.RoundToInt(p1.w), 0, SkyTyndallQualities.Length - 1), SkyTyndallQualities);
            y += LineHeight + LineSpacing;

            p2.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "Foreground", p2.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p2.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "Normal Angle", p2.y, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p2.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "Sky Gain", p2.z, 0.0f, 8.0f);
            y += LineHeight + LineSpacing;
            p3.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "Opacity", p3.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p3.y = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "Show Rays Only", p3.y > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = p3;
        }

        private static void EnsureSkyTyndallDefaults(ref Vector4 p0, ref Vector4 p1, ref Vector4 p2, ref Vector4 p3)
        {
            if (p0 == Vector4.zero && p1 == Vector4.zero && p2 == Vector4.zero && p3 == Vector4.zero)
            {
                p0 = new Vector4(0.62f, 1.0f, 0.45f, 1.0f);
                p1 = new Vector4(0.5f, 0.18f, 1.6f, 1.0f);
                p2 = new Vector4(0.35f, 0.45f, 1.15f, 0.75f);
                p3 = new Vector4(0.65f, 0.0f, 1.0f, 1.0f);
            }
        }
    }
}
