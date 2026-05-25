using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ScreenProcessStackVolumeEditor
    {
        private static readonly string[] DepthOfFieldModes =
        {
            "Gaussian",
            "Bokeh",
            "目标跟焦 Bokeh"
        };

        private const float DepthOfFieldMaxRadiusLimit = 96.0f;

        private static int GetDepthOfFieldLineCount(SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            int mode = parameters0 != null ? Mathf.Clamp(Mathf.RoundToInt(parameters0.vector4Value.x), 0, DepthOfFieldModes.Length - 1) : 1;
            if (mode == 0)
            {
                return 8;
            }

            return mode == 2 ? 19 : 14;
        }

        private static void DrawDepthOfFieldProperties(Rect rect, ref float y, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty focusTarget = element.FindPropertyRelative("depthOfFieldFocusTarget");
            SerializedProperty focusTargetPath = element.FindPropertyRelative("depthOfFieldFocusTargetPath");
            SerializedProperty focusOffset = element.FindPropertyRelative("depthOfFieldFocusOffset");
            if (parameters0 == null || parameters1 == null || parameters2 == null || parameters3 == null)
            {
                return;
            }

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            Vector4 p3 = parameters3.vector4Value;
            if (p0 == Vector4.zero && p1 == Vector4.zero && p2 == Vector4.zero && p3 == Vector4.zero)
            {
                p0 = new Vector4(1.0f, 10.0f, 50.0f, 5.6f);
                p1 = new Vector4(10.0f, 30.0f, 18.0f, 1.0f);
                p2 = new Vector4(5.0f, 1.0f, 0.0f, 0.0f);
                p3 = new Vector4(3.0f, 1.35f, 1.0f, 1.0f);
            }
            else
            {
                if (p3.x <= 0.0001f)
                {
                    p3.x = 3.0f;
                }

                if (p3.y <= 0.0001f)
                {
                    p3.y = 1.35f;
                }

                if (p3.z <= 0.0001f)
                {
                    p3.z = 1.0f;
                }

                if (p3.w <= 0.0001f)
                {
                    p3.w = 1.0f;
                }
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
                p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "最大半径", p1.z, 0.0f, DepthOfFieldMaxRadiusLimit);
                y += LineHeight + LineSpacing;
                p1.w = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "高质量采样", p1.w > 0.5f) ? 1.0f : 0.0f;
                y += LineHeight + LineSpacing;
                p3.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "景深强度", Mathf.Clamp(p3.x, 0.1f, 12.0f), 0.1f, 12.0f);
                y += LineHeight + LineSpacing;
                p3.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "虚化曲线", Mathf.Clamp(p3.w, 0.25f, 4.0f), 0.25f, 4.0f);
                y += LineHeight + LineSpacing;
            }
            else
            {
                if (mode == 2)
                {
                    DrawDepthOfFieldFocusTargetStatus(rect, ref y, focusTarget, focusTargetPath);
                    DrawDepthOfFieldPickFocusTargetButton(rect, ref y, focusTarget, focusTargetPath);

                    if (focusTargetPath != null)
                    {
                        focusTargetPath.stringValue = EditorGUI.TextField(new Rect(rect.x, y, rect.width, LineHeight), "跟焦路径", focusTargetPath.stringValue);
                        y += LineHeight + LineSpacing;
                    }

                    p0.y = EditorGUI.FloatField(new Rect(rect.x, y, rect.width, LineHeight), "备用焦点距离", Mathf.Max(0.001f, p0.y));
                    y += LineHeight + LineSpacing;

                    if (focusOffset != null)
                    {
                        focusOffset.floatValue = EditorGUI.FloatField(new Rect(rect.x, y, rect.width, LineHeight), "跟焦偏移", focusOffset.floatValue);
                        y += LineHeight + LineSpacing;
                    }
                }
                else
                {
                    p0.y = EditorGUI.FloatField(new Rect(rect.x, y, rect.width, LineHeight), "焦点距离", Mathf.Max(0.001f, p0.y));
                    y += LineHeight + LineSpacing;
                }

                p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "焦距", p0.z, 1.0f, 300.0f);
                y += LineHeight + LineSpacing;
                p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "光圈", p0.w, 1.0f, 32.0f);
                y += LineHeight + LineSpacing;
                p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "最大半径", p1.z, 0.0f, DepthOfFieldMaxRadiusLimit);
                y += LineHeight + LineSpacing;
                p1.w = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "高质量采样", p1.w > 0.5f) ? 1.0f : 0.0f;
                y += LineHeight + LineSpacing;
                p3.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "景深强度", Mathf.Clamp(p3.x, 0.1f, 12.0f), 0.1f, 12.0f);
                y += LineHeight + LineSpacing;
                p3.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "前景虚化", Mathf.Clamp(p3.y, 0.1f, 4.0f), 0.1f, 4.0f);
                y += LineHeight + LineSpacing;
                p3.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "远景虚化", Mathf.Clamp(p3.z, 0.1f, 4.0f), 0.1f, 4.0f);
                y += LineHeight + LineSpacing;
                p3.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "虚化曲线", Mathf.Clamp(p3.w, 0.25f, 4.0f), 0.25f, 4.0f);
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
            parameters3.vector4Value = p3;
        }

        private static void DrawDepthOfFieldFocusTargetStatus(
            Rect rect,
            ref float y,
            SerializedProperty focusTarget,
            SerializedProperty focusTargetPath)
        {
            string label = GetDepthOfFieldFocusTargetDisplayName(focusTarget, focusTargetPath);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.TextField(new Rect(rect.x, y, rect.width, LineHeight), "当前目标", label);
            }

            y += LineHeight + LineSpacing;
        }

        private static void DrawDepthOfFieldPickFocusTargetButton(
            Rect rect,
            ref float y,
            SerializedProperty focusTarget,
            SerializedProperty focusTargetPath)
        {
            using (new EditorGUI.DisabledScope(Selection.activeTransform == null))
            {
                if (GUI.Button(new Rect(rect.x, y, rect.width, LineHeight), "吸取当前选择"))
                {
                    Transform target = Selection.activeTransform;
                    if (focusTarget != null)
                    {
                        focusTarget.objectReferenceValue = target;
                    }

                    if (focusTargetPath != null)
                    {
                        focusTargetPath.stringValue = GetDepthOfFieldFocusTargetPath(target);
                    }
                }
            }

            y += LineHeight + LineSpacing;
        }

        private static string GetDepthOfFieldFocusTargetDisplayName(
            SerializedProperty focusTarget,
            SerializedProperty focusTargetPath)
        {
            Transform target = focusTarget != null ? focusTarget.objectReferenceValue as Transform : null;
            if (target != null)
            {
                return GetDepthOfFieldFocusTargetPath(target);
            }

            if (focusTargetPath != null && !string.IsNullOrEmpty(focusTargetPath.stringValue))
            {
                return focusTargetPath.stringValue;
            }

            return "未设置";
        }

        private static string GetDepthOfFieldFocusTargetPath(Transform target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            string path = target.name;
            Transform parent = target.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }
    }
}
