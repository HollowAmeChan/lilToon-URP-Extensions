using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ScreenProcessStackVolumeEditor
    {
        private static readonly string[] BokehZoomFocusModes = { "固定对焦", "目标跟焦" };
        private static readonly string[] BokehZoomQuality = { "低（8）", "中（16）", "高（32）" };

        private static int GetBokehZoomLineCount(SerializedProperty element)
        {
            bool target = Mathf.RoundToInt(element.FindPropertyRelative("parameters0").vector4Value.x) == 1;
            return target ? 32 : 28; // Includes foldout, color, blend and the mask switch.
        }

        private static Rect BokehZoomRow(Rect rect, ref float y)
        {
            Rect row = new Rect(rect.x, y, rect.width, LineHeight);
            y += LineHeight + LineSpacing;
            return row;
        }

        private void DrawBokehZoomProperties(Rect rect, ref float y, SerializedProperty element)
        {
            var p0Property = element.FindPropertyRelative("parameters0");
            var p1Property = element.FindPropertyRelative("parameters1");
            var p2Property = element.FindPropertyRelative("parameters2");
            var p3Property = element.FindPropertyRelative("parameters3");
            var p4Property = element.FindPropertyRelative("parameters4");
            var p5Property = element.FindPropertyRelative("parameters5");
            Vector4 p0 = p0Property.vector4Value, p1 = p1Property.vector4Value, p2 = p2Property.vector4Value;
            Vector4 p3 = p3Property.vector4Value, p4 = p4Property.vector4Value, p5 = p5Property.vector4Value;
            y = DrawScreenProcessCenterRadiusViewControlButton(rect, y, element);
            p0.x = EditorGUI.Popup(BokehZoomRow(rect, ref y), "对焦模式", Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, 1), BokehZoomFocusModes);
            bool target = p0.x > 0.5f;
            if (target)
            {
                var focusTarget = element.FindPropertyRelative("bokehZoomFocusTarget");
                var path = element.FindPropertyRelative("bokehZoomFocusTargetPath");
                DrawDepthOfFieldFocusTargetStatus(rect, ref y, focusTarget, path);
                DrawDepthOfFieldPickFocusTargetButton(rect, ref y, focusTarget, path);
                path.stringValue = EditorGUI.TextField(BokehZoomRow(rect, ref y), "跟焦路径", path.stringValue);
            }
            p0.y = Mathf.Max(0.001f, EditorGUI.FloatField(BokehZoomRow(rect, ref y), target ? "备用焦点距离" : "焦点距离", p0.y));
            if (target)
            {
                var offset = element.FindPropertyRelative("bokehZoomFocusOffset");
                offset.floatValue = EditorGUI.FloatField(BokehZoomRow(rect, ref y), "跟焦偏移", offset.floatValue);
            }
            p0.z = EditorGUI.Slider(BokehZoomRow(rect, ref y), "焦距", p0.z, 1.0f, 300.0f);
            p0.w = EditorGUI.Slider(BokehZoomRow(rect, ref y), "光圈", p0.w, 1.0f, 32.0f);
            p3.x = EditorGUI.Slider(BokehZoomRow(rect, ref y), "失焦响应", p3.x, 0.0f, 12.0f);
            p3.y = EditorGUI.Slider(BokehZoomRow(rect, ref y), "前景作用", p3.y, 0.0f, 4.0f);
            p3.z = EditorGUI.Slider(BokehZoomRow(rect, ref y), "远景作用", p3.z, 0.0f, 4.0f);
            p3.w = EditorGUI.Slider(BokehZoomRow(rect, ref y), "失焦曲线", p3.w, 0.25f, 4.0f);
            p5.z = Mathf.Max(0.0f, EditorGUI.FloatField(BokehZoomRow(rect, ref y), "合焦范围（±米）", p5.z));
            p1.x = EditorGUI.Slider(BokehZoomRow(rect, ref y), "变焦拉伸", p1.x, 0.0f, 1.0f);
            p2.z = EditorGUI.Slider(BokehZoomRow(rect, ref y), "亮度衰减", p2.z, 0.0f, 4.0f);
            p2.w = EditorGUI.Popup(BokehZoomRow(rect, ref y), "质量", Mathf.Clamp(Mathf.RoundToInt(p2.w), 0, 2), BokehZoomQuality);
            p2.x = EditorGUI.Slider(BokehZoomRow(rect, ref y), "中心位置 X", p2.x, -1.0f, 1.0f);
            p2.y = EditorGUI.Slider(BokehZoomRow(rect, ref y), "中心位置 Y", p2.y, -1.0f, 1.0f);
            p1.y = EditorGUI.Slider(BokehZoomRow(rect, ref y), "亮部阈值", p1.y, 0.0f, 8.0f);
            p1.z = EditorGUI.Slider(BokehZoomRow(rect, ref y), "阈值柔化", p1.z, 0.0f, 4.0f);
            p1.w = EditorGUI.Slider(BokehZoomRow(rect, ref y), "光斑曝光", p1.w, 0.0f, 4.0f);
            p5.x = EditorGUI.Slider(BokehZoomRow(rect, ref y), "光斑增益", p5.x, 0.0f, 8.0f);
            p4.x = EditorGUI.IntSlider(BokehZoomRow(rect, ref y), "叶片数量", Mathf.RoundToInt(p4.x), 0, 12);
            p4.y = EditorGUI.Slider(BokehZoomRow(rect, ref y), "叶片曲率", p4.y, 0.0f, 1.0f);
            p4.z = EditorGUI.Slider(BokehZoomRow(rect, ref y), "叶片旋转", p4.z, -180.0f, 180.0f);
            p4.w = EditorGUI.Slider(BokehZoomRow(rect, ref y), "色散", p4.w, 0.0f, 1.0f);
            p5.y = EditorGUI.Toggle(BokehZoomRow(rect, ref y), "只显示光斑层", p5.y > 0.5f) ? 1.0f : 0.0f;
            p0Property.vector4Value = p0; p1Property.vector4Value = p1; p2Property.vector4Value = p2;
            p3Property.vector4Value = p3; p4Property.vector4Value = p4; p5Property.vector4Value = p5;
        }
    }
}
