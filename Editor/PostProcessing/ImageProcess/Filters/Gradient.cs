using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        // modes 0..3 are the original shapes (kept byte-compatible with existing profiles),
        // modes 4..7 are the two-point geometry added on top of them.
        private static readonly string[] GradientModeNames =
        {
            "单色", "线性", "圆形", "椭圆",
            "线性（两点）", "径向（两点）", "椭圆（两点）", "锥形（两点）"
        };

        private static readonly string[] GradientCurveNames =
        {
            "线性", "平滑两端", "缓入起点", "缓入终点", "更平滑"
        };

        private static readonly string[] GradientInterpolationSpaceNames = { "显示空间", "线性光" };

        private const string GradientViewControlOwner = "ImageProcess.Gradient";
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
            ScaleY = 5,
            EndPoint = 6,
            EllipseAspect = 7
        }

        private static int GetGradientLineCount(SerializedProperty element)
        {
            if (element == null ||
                element.FindPropertyRelative("parameters0") == null ||
                element.FindPropertyRelative("parameters1") == null ||
                element.FindPropertyRelative("parameters2") == null ||
                element.FindPropertyRelative("parameters3") == null ||
                element.FindPropertyRelative("parameters4") == null ||
                element.FindPropertyRelative("parameters5") == null)
            {
                // DrawGradientElement falls back to the foldout-only generic row for malformed
                // serialized data; keep the height calculation in sync with that fallback.
                return 1;
            }

            int mode = GetGradientMode(element);
            int count = 7; // foldout, mode, view control, blend mode, colour 1, colour 2, invert

            if (mode >= 4)
            {
                count += 7; // point label, A x/y, B x/y, curve, mirror
                if (mode == 6)
                {
                    count += 1; // ellipse aspect
                }
            }
            else if (mode != 0)
            {
                count += 4; // radius, smoothness, offset x/y
                if (mode == 1)
                {
                    count += 2; // angle, aspect correction toggle
                }

                if (mode == 3)
                {
                    count += 2; // scale x/y
                }
            }

            return count + 4; // interpolation space, gradient resolution, dither, opacity
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
                // EnsureGradientDefaults initializes an empty layer to the linear mode (1).
                // Return the same mode here so ReorderableList reserves the correct height
                // before DrawGradientElement performs that initialization.
                return 1;
            }

            return Mathf.Clamp(Mathf.RoundToInt(value.x), 0, GradientModeNames.Length - 1);
        }

        private static bool GetGradientUsesTwoPointGeometry(int mode)
        {
            return mode >= 4;
        }

        private void DrawGradientElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty parameters4 = element.FindPropertyRelative("parameters4");
            SerializedProperty parameters5 = element.FindPropertyRelative("parameters5");
            SerializedProperty blendMode = element.FindPropertyRelative("blendMode");
            SerializedProperty color = element.FindPropertyRelative("color");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            // Keep malformed/legacy serialized data from throwing during a domain reload.
            // The generic layer UI still exposes the effect and allows the asset to be repaired.
            if (parameters0 == null || parameters1 == null || parameters2 == null ||
                parameters3 == null || parameters4 == null || parameters5 == null ||
                blendMode == null || color == null || enabled == null)
            {
                DrawSimpleLayerElement(rect, element);
                return;
            }

            EnsureGradientDefaults(parameters0, parameters1, parameters2, parameters3, parameters4, parameters5);

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
            Vector4 p3 = parameters3.vector4Value;
            Vector4 p4 = parameters4.vector4Value;
            Vector4 p5 = parameters5.vector4Value;

            int mode = Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, GradientModeNames.Length - 1);
            mode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "模式", mode, GradientModeNames);
            p0.x = mode;
            bool twoPoint = GetGradientUsesTwoPointGeometry(mode);
            y += LineHeight + LineSpacing;

            EditorGUI.BeginDisabledGroup(mode == 0);
            bool viewControlActive = IsGradientViewControlActive(element);
            string viewControlLabel = mode == 0
                ? "\u7eaf\u8272\u6a21\u5f0f\u65e0\u89c6\u56fe\u63a7\u4ef6"
                : (viewControlActive ? "\u505c\u6b62\u6e38\u620f\u89c6\u56fe\u63a7\u5236" : "\u5728\u6e38\u620f\u89c6\u56fe\u4e2d\u8c03\u6574");
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
            y = DrawPropertyLine(rect.x, y, rect.width, color, twoPoint ? "颜色 1（终点 B）" : "颜色 1");
            p3 = DrawVectorColorLineWithAlpha(rect.x, y, rect.width, twoPoint ? "颜色 2（起点 A）" : "颜色 2", p3);
            y += LineHeight + LineSpacing;

            p1.w = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "反转颜色", p1.w > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;

            if (twoPoint)
            {
                EditorGUI.LabelField(new Rect(rect.x, y, rect.width, LineHeight), "起点 A / 终点 B（相对画面中心）", EditorStyles.miniBoldLabel);
                y += LineHeight + LineSpacing;
                p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "A X", p1.x, -2.0f, 2.0f);
                y += LineHeight + LineSpacing;
                p1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "A Y", p1.y, -2.0f, 2.0f);
                y += LineHeight + LineSpacing;
                p4.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "B X", p4.x, -2.0f, 2.0f);
                y += LineHeight + LineSpacing;
                p4.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "B Y", p4.y, -2.0f, 2.0f);
                y += LineHeight + LineSpacing;
                int curve = Mathf.Clamp(Mathf.RoundToInt(p4.z), 0, GradientCurveNames.Length - 1);
                curve = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "过渡曲线", curve, GradientCurveNames);
                p4.z = curve;
                y += LineHeight + LineSpacing;
                p4.w = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "镜像（A—B 中点对称）", p4.w > 0.5f) ? 1.0f : 0.0f;
                y += LineHeight + LineSpacing;

                if (mode == 6)
                {
                    p5.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "椭圆纵横比", p5.x, 0.05f, 8.0f);
                    y += LineHeight + LineSpacing;
                }
            }
            else
            {
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
                    p5.z = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "角度按画幅校正", p5.z > 0.5f) ? 1.0f : 0.0f;
                    y += LineHeight + LineSpacing;
                }

                if (mode == 3)
                {
                    p2.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "横向缩放", p2.x, 0.1f, 3.0f);
                    y += LineHeight + LineSpacing;
                    p2.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "纵向缩放", p2.y, 0.1f, 3.0f);
                    y += LineHeight + LineSpacing;
                }
            }

            int interpolationSpace = Mathf.Clamp(Mathf.RoundToInt(p5.y), 0, GradientInterpolationSpaceNames.Length - 1);
            interpolationSpace = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "插值空间", interpolationSpace, GradientInterpolationSpaceNames);
            p5.y = interpolationSpace;
            y += LineHeight + LineSpacing;

            p2.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "渐变分辨率", p2.z, 0.02f, 1.0f);
            y += LineHeight + LineSpacing;

            EditorGUI.LabelField(new Rect(rect.x, y, rect.width, LineHeight), new GUIContent("抖动", "0.5 = ±0.5/255（1 个 8-bit 码）；作用在输出上，用来压掉大面积渐变的色带"));
            p2.w = EditorGUI.Slider(new Rect(rect.x + 76.0f, y, rect.width - 76.0f, LineHeight), p2.w, 0.0f, 4.0f);
            y += LineHeight + LineSpacing;

            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", p0.w, 0.0f, 1.0f);

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = p3;
            parameters4.vector4Value = p4;
            parameters5.vector4Value = p5;
            EditorGUI.indentLevel--;
        }

        private static Vector4 DrawVectorColorLineWithAlpha(float x, float y, float width, string label, Vector4 value)
        {
            Color color = new Color(value.x, value.y, value.z, value.w);
            color = EditorGUI.ColorField(new Rect(x, y, width, LineHeight), new GUIContent(label), color, true, true, false);
            return new Vector4(color.r, color.g, color.b, color.a);
        }

        private static void EnsureGradientDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty parameters2,
            SerializedProperty parameters3,
            SerializedProperty parameters4,
            SerializedProperty parameters5)
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

            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            int mode = Mathf.Clamp(Mathf.RoundToInt(parameters0.vector4Value.x), 0, GradientModeNames.Length - 1);
            if (!GetGradientUsesTwoPointGeometry(mode))
            {
                return;
            }

            // Two-point geometry needs a usable default: B below the centre, smooth falloff,
            // aspect 1. Zero means "degenerate ramp" / "maximally squashed ellipse", so it has
            // to be replaced the first time such a layer is drawn.
            if (parameters4 != null && parameters4.propertyType == SerializedPropertyType.Vector4 && parameters4.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters4.vector4Value = new Vector4(0.0f, -0.25f, 1.0f, 0.0f);
            }

            if (parameters5 != null && parameters5.propertyType == SerializedPropertyType.Vector4 && parameters5.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters5.vector4Value = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
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
            // Unity objects compare equal to null after destruction. Do not leave the static
            // GameView overlay attached when the inspected Volume is deleted or reloaded.
            if (activeGradientViewTarget == null ||
                (serializedObject?.targetObject != null &&
                 activeGradientViewTargetId == serializedObject.targetObject.GetInstanceID()))
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

            SerializedObject so;
            try
            {
                so = new SerializedObject(target);
                so.Update();
            }
            catch (System.Exception)
            {
                DisableGradientViewControl();
                return;
            }
            SerializedProperty element = so.FindProperty(activeGradientViewPropertyPath);
            if (element == null || GetEffect(element) != ImageProcessEffect.Gradient)
            {
                DisableGradientViewControl();
                return;
            }

            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty parameters4 = element.FindPropertyRelative("parameters4");
            SerializedProperty parameters5 = element.FindPropertyRelative("parameters5");
            if (parameters0 == null || parameters1 == null || parameters2 == null ||
                parameters3 == null || parameters4 == null || parameters5 == null)
            {
                DisableGradientViewControl();
                return;
            }
            EnsureGradientDefaults(parameters0, parameters1, parameters2, parameters3, parameters4, parameters5);

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            Vector4 p4 = parameters4.vector4Value;
            Vector4 p5 = parameters5.vector4Value;
            int mode = Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, GradientModeNames.Length - 1);
            if (mode == 0)
            {
                DisableGradientViewControl();
                return;
            }

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            float aspect = PostProcessScreenSpaceViewControl.Aspect(viewRect);
            Vector2 centerUv = PostProcessScreenSpaceViewControl.OffsetToUvCenter(new Vector2(p1.x, p1.y));
            Vector2 centerGui = GradientUvToGui(centerUv, viewRect);
            Vector2 endUv = PostProcessScreenSpaceViewControl.OffsetToUvCenter(new Vector2(p4.x, p4.y));
            Vector2 endGui = GradientUvToGui(endUv, viewRect);
            Vector2 radiusGui = GetGradientRadiusHandleGui(mode, p0, p1, p2, p4, p5, viewRect, aspect);
            Vector2 angleGui = GetGradientAngleHandleGui(mode, p0, p1, viewRect);
            Vector2 scaleXGui = GetGradientScaleHandleGui(p0, p1, p2, viewRect, aspect, true);
            Vector2 scaleYGui = GetGradientScaleHandleGui(p0, p1, p2, viewRect, aspect, false);
            PostProcessScreenSpaceHandle[] handles = BuildGradientHandles(mode, centerGui, endGui, radiusGui, angleGui, scaleXGui, scaleYGui);

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                activeGradientViewHandle = PostProcessScreenSpaceViewControl.PickHandle(evt.mousePosition, handles, (int)GradientViewHandle.None);
                if (activeGradientViewHandle != (int)GradientViewHandle.None)
                {
                    GUIUtility.hotControl = controlId;
                    PostProcessScreenSpaceViewControl.RequestRepaint();
                    Undo.RecordObject(target, "Adjust ImageProcess Gradient In View");
                    evt.Use();
                }
            }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == controlId && activeGradientViewHandle != (int)GradientViewHandle.None)
            {
                Vector2 uv = GradientGuiToUv(evt.mousePosition, viewRect);
                ApplyGradientViewDrag((GradientViewHandle)activeGradientViewHandle, mode, uv, centerUv, aspect, ref p0, ref p1, ref p2, ref p4, ref p5);
                parameters0.vector4Value = p0;
                parameters1.vector4Value = p1;
                parameters2.vector4Value = p2;
                parameters4.vector4Value = p4;
                parameters5.vector4Value = p5;
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

            DrawGradientViewHandles(viewRect, mode, centerGui, endGui, handles);
        }

        private static Vector2 GradientUvToGui(Vector2 uv, Rect rect)
        {
            return PostProcessScreenSpaceViewControl.UvToGui(uv, rect);
        }

        private static Vector2 GradientGuiToUv(Vector2 gui, Rect rect)
        {
            return PostProcessScreenSpaceViewControl.GuiToUv(gui, rect);
        }

        private static Vector2 GetGradientRadiusHandleGui(int mode, Vector4 p0, Vector4 p1, Vector4 p2, Vector4 p4, Vector4 p5, Rect rect, float aspect)
        {
            Vector2 center = PostProcessScreenSpaceViewControl.OffsetToUvCenter(new Vector2(p1.x, p1.y));
            if (GetGradientUsesTwoPointGeometry(mode))
            {
                // the radius handle sits at B, slightly off-axis so it never overlaps the B handle
                Vector2 direction = GetGradientTwoPointDirection(p1, p4, aspect);
                float distance = GetGradientTwoPointDistance(p1, p4, aspect);
                Vector2 perpendicular = new Vector2(-direction.y, direction.x);
                return GradientUvToGui(center + direction * distance + perpendicular * 0.04f, rect);
            }

            float radius = Mathf.Max(p0.y, 0.0001f);
            if (mode == 1)
            {
                Vector2 direction = PostProcessScreenSpaceViewControl.DirectionFromDegrees(p1.z);
                return GradientUvToGui(center + direction * radius * 0.5f, rect);
            }

            float scaleX = mode == 3 ? Mathf.Max(p2.x, 0.1f) : 1.0f;
            return GradientUvToGui(center + new Vector2(radius * scaleX / Mathf.Max(aspect, 0.0001f), 0.0f), rect);
        }

        private static Vector2 GetGradientTwoPointDirection(Vector4 p1, Vector4 p4, float aspect)
        {
            Vector2 axis = new Vector2((p4.x - p1.x) * aspect, p4.y - p1.y);
            return axis.sqrMagnitude <= 0.0000001f ? new Vector2(0.0f, 1.0f) : axis.normalized;
        }

        private static float GetGradientTwoPointDistance(Vector4 p1, Vector4 p4, float aspect)
        {
            Vector2 axis = new Vector2((p4.x - p1.x) * aspect, p4.y - p1.y);
            return Mathf.Max(axis.magnitude / Mathf.Max(aspect, 0.0001f), 0.0001f);
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

        private static void ApplyGradientViewDrag(
            GradientViewHandle handle,
            int mode,
            Vector2 uv,
            Vector2 centerUv,
            float aspect,
            ref Vector4 p0,
            ref Vector4 p1,
            ref Vector4 p2,
            ref Vector4 p4,
            ref Vector4 p5)
        {
            Vector2 delta = uv - centerUv;
            bool twoPoint = GetGradientUsesTwoPointGeometry(mode);
            float offsetLimit = twoPoint ? 2.0f : 3.0f;

            if (handle == GradientViewHandle.Center)
            {
                p1.x = Mathf.Clamp(uv.x - 0.5f, -offsetLimit, offsetLimit);
                p1.y = Mathf.Clamp(uv.y - 0.5f, -offsetLimit, offsetLimit);
                return;
            }

            if (handle == GradientViewHandle.EndPoint && twoPoint)
            {
                p4.x = Mathf.Clamp(uv.x - 0.5f, -2.0f, 2.0f);
                p4.y = Mathf.Clamp(uv.y - 0.5f, -2.0f, 2.0f);
                return;
            }

            if (handle == GradientViewHandle.EllipseAspect && mode == 6)
            {
                Vector2 direction = GetGradientTwoPointDirection(p1, p4, aspect);
                Vector2 perpendicular = new Vector2(-direction.y, direction.x);
                float distance = GetGradientTwoPointDistance(p1, p4, aspect);
                float along = Mathf.Abs(delta.x * aspect * perpendicular.x + delta.y * perpendicular.y);
                p5.x = Mathf.Clamp(along / Mathf.Max(distance, 0.0001f), 0.05f, 8.0f);
                return;
            }

            if (handle == GradientViewHandle.Angle)
            {
                p1.z = PostProcessScreenSpaceViewControl.AngleDegreesFromUvDelta(uv, centerUv);
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

            // Radius handle: in the two-point geometry the spread is defined by point B, so the
            // handle slides B along the current axis instead of touching the legacy radius field.
            if (twoPoint)
            {
                Vector2 direction = GetGradientTwoPointDirection(p1, p4, aspect);
                float projection = delta.x * aspect * direction.x + delta.y * direction.y;
                p4.x = Mathf.Clamp(p1.x + projection * direction.x / Mathf.Max(aspect, 0.0001f), -2.0f, 2.0f);
                p4.y = Mathf.Clamp(p1.y + projection * direction.y, -2.0f, 2.0f);
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

        private static PostProcessScreenSpaceHandle[] BuildGradientHandles(
            int mode,
            Vector2 center,
            Vector2 endPoint,
            Vector2 radius,
            Vector2 angle,
            Vector2 scaleX,
            Vector2 scaleY)
        {
            if (GetGradientUsesTwoPointGeometry(mode))
            {
                if (mode == 6)
                {
                    return new[]
                    {
                        new PostProcessScreenSpaceHandle((int)GradientViewHandle.Center, "A", center, Color.white, PostProcessScreenSpaceHandleKind.Point, false),
                        new PostProcessScreenSpaceHandle((int)GradientViewHandle.EndPoint, "B", endPoint, Color.white, PostProcessScreenSpaceHandleKind.Point, true),
                        new PostProcessScreenSpaceHandle((int)GradientViewHandle.EllipseAspect, "E", radius, Color.white, PostProcessScreenSpaceHandleKind.HorizontalScale, true, 0.54f),
                    };
                }

                return new[]
                {
                    new PostProcessScreenSpaceHandle((int)GradientViewHandle.Center, "A", center, Color.white, PostProcessScreenSpaceHandleKind.Point, false),
                    new PostProcessScreenSpaceHandle((int)GradientViewHandle.EndPoint, "B", endPoint, Color.white, PostProcessScreenSpaceHandleKind.Point, true),
                };
            }

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

        private static void DrawGradientViewHandles(Rect viewRect, int mode, Vector2 center, Vector2 endPoint, PostProcessScreenSpaceHandle[] handles)
        {
            if (GetGradientUsesTwoPointGeometry(mode))
            {
                PostProcessScreenSpaceViewControl.DrawDashedLine(center, endPoint, new Color(1.0f, 1.0f, 1.0f, 0.55f));
            }

            string hint = GetGradientUsesTwoPointGeometry(mode)
                ? (mode == 6
                    ? "渐变  A 起点  B 终点  E 椭圆比例  Esc 退出"
                    : "渐变  A 起点  B 终点  Esc 退出")
                : mode == 1
                    ? "\u6e10\u53d8  C \u4e2d\u5fc3  R \u534a\u5f84  A \u89d2\u5ea6  Esc \u9000\u51fa"
                    : mode == 3
                        ? "\u6e10\u53d8  C \u4e2d\u5fc3  R \u534a\u5f84  X/Y \u6bd4\u4f8b  Esc \u9000\u51fa"
                        : "\u6e10\u53d8  C \u4e2d\u5fc3  R \u534a\u5f84  Esc \u9000\u51fa";
            PostProcessScreenSpaceViewControl.DrawHandleSet(viewRect, center, handles, activeGradientViewHandle, hint);
        }
    }
}
