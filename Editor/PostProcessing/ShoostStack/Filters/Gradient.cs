using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static readonly string[] GradientModeNames = { "单色", "线性", "圆形", "椭圆" };

        private const string GradientViewControlOwner = "Shoost.Gradient";
        private static UnityEngine.Object activeGradientViewTarget;
        private static int activeGradientViewTargetId;
        private static string activeGradientViewPropertyPath;
        private static int activeGradientViewHandle;

        private enum GradientViewHandle
        {
            None = 0,
            Center = 1,
            Radius = 2,
            Angle = 3,
            ScaleX = 4,
            ScaleY = 5
        }

        private static int GetGradientLineCount(SerializedProperty element)
        {
            int mode = GetGradientMode(element);
            int count = 7;
            if (mode != 0)
            {
                count += 4;
            }

            if (mode == 1)
            {
                count += 1;
            }

            if (mode == 3)
            {
                count += 2;
            }

            return count;
        }

        private static int GetGradientMode(SerializedProperty element)
        {
            SerializedProperty parameters0 = element?.FindPropertyRelative("parameters0");
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return 2;
            }

            Vector4 value = parameters0.vector4Value;
            if (value.sqrMagnitude <= 0.000001f)
            {
                return 2;
            }

            return Mathf.Clamp(Mathf.RoundToInt(value.x), 0, 3);
        }

        private void DrawGradientElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty blendMode = element.FindPropertyRelative("blendMode");
            SerializedProperty color = element.FindPropertyRelative("color");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureGradientDefaults(parameters0, parameters1, parameters2, parameters3);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                if (IsGradientViewControlActive(element))
                {
                    DisableGradientViewControl();
                }

                return;
            }

            EditorGUI.indentLevel++;
            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            Vector4 background = parameters3.vector4Value;

            int mode = Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, 3);
            mode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "模式", mode, GradientModeNames);
            p0.x = mode;
            y += LineHeight + LineSpacing;

            EditorGUI.BeginDisabledGroup(mode == 0);
            bool viewControlActive = IsGradientViewControlActive(element);
            string viewControlLabel = mode == 0 ? "\u7eaf\u8272\u6a21\u5f0f\u65e0\u89c6\u56fe\u63a7\u4ef6" : (viewControlActive ? "\u505c\u6b62\u6e38\u620f\u89c6\u56fe\u63a7\u5236" : "\u5728\u6e38\u620f\u89c6\u56fe\u4e2d\u8c03\u6574");
            if (GUI.Button(new Rect(rect.x, y, rect.width, LineHeight), viewControlLabel))
            {
                if (viewControlActive)
                {
                    DisableGradientViewControl();
                }
                else
                {
                    EnableGradientViewControl(element);
                }
            }

            EditorGUI.EndDisabledGroup();
            y += LineHeight + LineSpacing;

            y = DrawBlendModeLine(rect.x, y, rect.width, blendMode);
            y = DrawPropertyLine(rect.x, y, rect.width, color, "颜色 1");
            background = DrawVectorColorLine(rect.x, y, rect.width, "颜色 2", background);
            y += LineHeight + LineSpacing;

            p1.w = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "反转颜色", p1.w > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;

            if (mode != 0)
            {
                p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "半径", p0.y, 0.0f, 3.0f);
                y += LineHeight + LineSpacing;
                p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "柔和度", p0.z, 0.0f, 10.0f);
                y += LineHeight + LineSpacing;
                p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "偏移 X", p1.x, -3.0f, 3.0f);
                y += LineHeight + LineSpacing;
                p1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "偏移 Y", p1.y, -3.0f, 3.0f);
                y += LineHeight + LineSpacing;
            }

            if (mode == 1)
            {
                p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "角度", p1.z, -180.0f, 180.0f);
                y += LineHeight + LineSpacing;
            }

            if (mode == 3)
            {
                p2.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "横向缩放", p2.x, 0.1f, 3.0f);
                y += LineHeight + LineSpacing;
                p2.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "纵向缩放", p2.y, 0.1f, 3.0f);
                y += LineHeight + LineSpacing;
            }

            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", p0.w, 0.0f, 1.0f);

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = background;
            EditorGUI.indentLevel--;
        }

        private static void EnsureGradientDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty parameters2,
            SerializedProperty parameters3)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(1.0f, 1.0f, 5.0f, 1.0f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = Vector4.zero;
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4 && parameters2.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters2.vector4Value = new Vector4(1.0f, 0.5f, 1.0f, 0.0f);
            }

            if (parameters3 != null && parameters3.propertyType == SerializedPropertyType.Vector4 && parameters3.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters3.vector4Value = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
            }
        }

        private void EnableGradientViewControl(SerializedProperty element)
        {
            if (serializedObject?.targetObject == null || element == null)
            {
                return;
            }

            activeGradientViewTargetId = serializedObject.targetObject.GetInstanceID();
            activeGradientViewTarget = serializedObject.targetObject;
            activeGradientViewPropertyPath = element.propertyPath;
            activeGradientViewHandle = (int)GradientViewHandle.None;
            PostProcessScreenSpaceViewControl.Start(GradientViewControlOwner, OnGradientGameViewGUI);
        }

        private static void DisableGradientViewControl()
        {
            activeGradientViewTargetId = 0;
            activeGradientViewTarget = null;
            activeGradientViewPropertyPath = null;
            activeGradientViewHandle = (int)GradientViewHandle.None;
            PostProcessScreenSpaceViewControl.Stop(GradientViewControlOwner);
        }

        private void DisableGradientViewControlForThisEditor()
        {
            if (serializedObject?.targetObject != null &&
                activeGradientViewTargetId == serializedObject.targetObject.GetInstanceID())
            {
                DisableGradientViewControl();
            }
        }

        private bool IsGradientViewControlActive(SerializedProperty element)
        {
            return serializedObject?.targetObject != null &&
                   element != null &&
                   activeGradientViewTargetId == serializedObject.targetObject.GetInstanceID() &&
                   activeGradientViewPropertyPath == element.propertyPath &&
                   PostProcessScreenSpaceViewControl.IsActive(GradientViewControlOwner);
        }

        private static void OnGradientGameViewGUI(Rect viewRect, Event evt)
        {
            if (activeGradientViewTargetId == 0 || string.IsNullOrEmpty(activeGradientViewPropertyPath))
            {
                return;
            }

            UnityEngine.Object target = activeGradientViewTarget;
            if (target == null)
            {
                DisableGradientViewControl();
                return;
            }

            SerializedObject so = new SerializedObject(target);
            so.Update();
            SerializedProperty element = so.FindProperty(activeGradientViewPropertyPath);
            if (element == null || GetEffect(element) != ShoostPostProcessEffect.Gradient)
            {
                DisableGradientViewControl();
                return;
            }

            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            EnsureGradientDefaults(parameters0, parameters1, parameters2, parameters3);

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            int mode = Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, 3);
            if (mode == 0)
            {
                DisableGradientViewControl();
                return;
            }

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            float aspect = PostProcessScreenSpaceViewControl.Aspect(viewRect);
            Vector2 centerUv = PostProcessScreenSpaceViewControl.OffsetToUvCenter(new Vector2(p1.x, p1.y));
            Vector2 centerGui = GradientUvToGui(centerUv, viewRect);
            Vector2 radiusGui = GetGradientRadiusHandleGui(mode, p0, p1, p2, viewRect, aspect);
            Vector2 angleGui = GetGradientAngleHandleGui(mode, p0, p1, viewRect);
            Vector2 scaleXGui = GetGradientScaleHandleGui(p0, p1, p2, viewRect, aspect, true);
            Vector2 scaleYGui = GetGradientScaleHandleGui(p0, p1, p2, viewRect, aspect, false);
            PostProcessScreenSpaceHandle[] handles = BuildGradientHandles(mode, centerGui, radiusGui, angleGui, scaleXGui, scaleYGui);

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                activeGradientViewHandle = PostProcessScreenSpaceViewControl.PickHandle(evt.mousePosition, handles, (int)GradientViewHandle.None);
                if (activeGradientViewHandle != (int)GradientViewHandle.None)
                {
                    GUIUtility.hotControl = controlId;
                    PostProcessScreenSpaceViewControl.RequestRepaint();
                    Undo.RecordObject(target, "Adjust Shoost Gradient In View");
                    evt.Use();
                }
            }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == controlId && activeGradientViewHandle != (int)GradientViewHandle.None)
            {
                Vector2 uv = GradientGuiToUv(evt.mousePosition, viewRect);
                ApplyGradientViewDrag((GradientViewHandle)activeGradientViewHandle, mode, uv, aspect, ref p0, ref p1, ref p2);
                parameters0.vector4Value = p0;
                parameters1.vector4Value = p1;
                parameters2.vector4Value = p2;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                evt.Use();
                PostProcessScreenSpaceViewControl.RequestRepaint();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
            {
                GUIUtility.hotControl = 0;
                activeGradientViewHandle = (int)GradientViewHandle.None;
                evt.Use();
                PostProcessScreenSpaceViewControl.RequestRepaint();
            }
            else if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                DisableGradientViewControl();
                evt.Use();
            }

            DrawGradientViewHandles(viewRect, mode, centerGui, handles);
        }

        private static Vector2 GradientUvToGui(Vector2 uv, Rect rect)
        {
            return PostProcessScreenSpaceViewControl.UvToGui(uv, rect);
        }

        private static Vector2 GradientGuiToUv(Vector2 gui, Rect rect)
        {
            return PostProcessScreenSpaceViewControl.GuiToUv(gui, rect);
        }

        private static Vector2 GetGradientRadiusHandleGui(int mode, Vector4 p0, Vector4 p1, Vector4 p2, Rect rect, float aspect)
        {
            Vector2 center = PostProcessScreenSpaceViewControl.OffsetToUvCenter(new Vector2(p1.x, p1.y));
            float radius = Mathf.Max(p0.y, 0.0001f);
            if (mode == 1)
            {
                Vector2 direction = PostProcessScreenSpaceViewControl.DirectionFromDegrees(p1.z);
                return GradientUvToGui(center + direction * radius * 0.5f, rect);
            }

            float scaleX = mode == 3 ? Mathf.Max(p2.x, 0.1f) : 1.0f;
            return GradientUvToGui(center + new Vector2(radius * scaleX / Mathf.Max(aspect, 0.0001f), 0.0f), rect);
        }

        private static Vector2 GetGradientAngleHandleGui(int mode, Vector4 p0, Vector4 p1, Rect rect)
        {
            Vector2 center = PostProcessScreenSpaceViewControl.OffsetToUvCenter(new Vector2(p1.x, p1.y));
            if (mode != 1)
            {
                return center;
            }

            Vector2 direction = PostProcessScreenSpaceViewControl.DirectionFromDegrees(p1.z);
            return GradientUvToGui(center + direction * Mathf.Max(p0.y, 0.1f) * 0.35f, rect);
        }

        private static Vector2 GetGradientScaleHandleGui(Vector4 p0, Vector4 p1, Vector4 p2, Rect rect, float aspect, bool horizontal)
        {
            Vector2 center = PostProcessScreenSpaceViewControl.OffsetToUvCenter(new Vector2(p1.x, p1.y));
            float radius = Mathf.Max(p0.y, 0.0001f);
            if (horizontal)
            {
                return GradientUvToGui(center + new Vector2(radius * Mathf.Max(p2.x, 0.1f) / Mathf.Max(aspect, 0.0001f), 0.0f), rect);
            }

            return GradientUvToGui(center + new Vector2(0.0f, radius * Mathf.Max(p2.y, 0.1f)), rect);
        }

        private static void ApplyGradientViewDrag(GradientViewHandle handle, int mode, Vector2 uv, float aspect, ref Vector4 p0, ref Vector4 p1, ref Vector4 p2)
        {
            Vector2 center = new Vector2(0.5f + p1.x, 0.5f + p1.y);
            Vector2 delta = uv - center;

            if (handle == GradientViewHandle.Center)
            {
                p1.x = Mathf.Clamp(uv.x - 0.5f, -3.0f, 3.0f);
                p1.y = Mathf.Clamp(uv.y - 0.5f, -3.0f, 3.0f);
                return;
            }

            if (handle == GradientViewHandle.Angle)
            {
                p1.z = PostProcessScreenSpaceViewControl.AngleDegreesFromUvDelta(uv, center);
                return;
            }

            if (handle == GradientViewHandle.ScaleX)
            {
                p2.x = Mathf.Clamp(Mathf.Abs(delta.x) * aspect / Mathf.Max(p0.y, 0.0001f), 0.1f, 3.0f);
                return;
            }

            if (handle == GradientViewHandle.ScaleY)
            {
                p2.y = Mathf.Clamp(Mathf.Abs(delta.y) / Mathf.Max(p0.y, 0.0001f), 0.1f, 3.0f);
                return;
            }

            if (mode == 1)
            {
                Vector2 direction = PostProcessScreenSpaceViewControl.DirectionFromDegrees(p1.z);
                p0.y = Mathf.Clamp(Mathf.Abs(Vector2.Dot(delta, direction)) * 2.0f, 0.0f, 3.0f);
                return;
            }

            Vector2 scaled = new Vector2(delta.x * aspect, delta.y);
            if (mode == 3)
            {
                scaled.x /= Mathf.Max(p2.x, 0.1f);
                scaled.y /= Mathf.Max(p2.y, 0.1f);
            }

            p0.y = Mathf.Clamp(scaled.magnitude, 0.0f, 3.0f);
        }

        private static PostProcessScreenSpaceHandle[] BuildGradientHandles(int mode, Vector2 center, Vector2 radius, Vector2 angle, Vector2 scaleX, Vector2 scaleY)
        {
            if (mode == 1)
            {
                return new[]
                {
                    new PostProcessScreenSpaceHandle((int)GradientViewHandle.Center, "C", center, Color.white, PostProcessScreenSpaceHandleKind.Point, false),
                    new PostProcessScreenSpaceHandle((int)GradientViewHandle.Radius, "R", radius, Color.white, PostProcessScreenSpaceHandleKind.Radius),
                    new PostProcessScreenSpaceHandle((int)GradientViewHandle.Angle, "A", angle, Color.white, PostProcessScreenSpaceHandleKind.Angle, true, 0.62f),
                };
            }

            if (mode == 3)
            {
                return new[]
                {
                    new PostProcessScreenSpaceHandle((int)GradientViewHandle.Center, "C", center, Color.white, PostProcessScreenSpaceHandleKind.Point, false),
                    new PostProcessScreenSpaceHandle((int)GradientViewHandle.Radius, "R", radius, Color.white, PostProcessScreenSpaceHandleKind.Radius),
                    new PostProcessScreenSpaceHandle((int)GradientViewHandle.ScaleX, "X", scaleX, Color.white, PostProcessScreenSpaceHandleKind.HorizontalScale, true, 0.54f),
                    new PostProcessScreenSpaceHandle((int)GradientViewHandle.ScaleY, "Y", scaleY, Color.white, PostProcessScreenSpaceHandleKind.VerticalScale, true, 0.54f),
                };
            }

            return new[]
            {
                new PostProcessScreenSpaceHandle((int)GradientViewHandle.Center, "C", center, Color.white, PostProcessScreenSpaceHandleKind.Point, false),
                new PostProcessScreenSpaceHandle((int)GradientViewHandle.Radius, "R", radius, Color.white, PostProcessScreenSpaceHandleKind.Radius),
            };
        }

        private static void DrawGradientViewHandles(Rect viewRect, int mode, Vector2 center, PostProcessScreenSpaceHandle[] handles)
        {
            string hint = mode == 1
                ? "\u6e10\u53d8  C \u4e2d\u5fc3  R \u534a\u5f84  A \u89d2\u5ea6  Esc \u9000\u51fa"
                : mode == 3
                    ? "\u6e10\u53d8  C \u4e2d\u5fc3  R \u534a\u5f84  X/Y \u6bd4\u4f8b  Esc \u9000\u51fa"
                    : "\u6e10\u53d8  C \u4e2d\u5fc3  R \u534a\u5f84  Esc \u9000\u51fa";
            PostProcessScreenSpaceViewControl.DrawHandleSet(viewRect, center, handles, activeGradientViewHandle, hint);
        }
    }
}
