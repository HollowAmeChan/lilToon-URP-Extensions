using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static readonly string[] GradientModeNames = { "单色", "线性", "圆形", "椭圆" };

        private const float GradientSceneHandleSize = 9.0f;
        private static int activeGradientSceneTargetId;
        private static string activeGradientScenePropertyPath;
        private static GradientSceneHandle activeGradientSceneHandle = GradientSceneHandle.None;
        private static bool gradientSceneViewRegistered;

        private enum GradientSceneHandle
        {
            None,
            Center,
            Radius,
            Angle,
            ScaleX,
            ScaleY
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
                if (IsGradientSceneControlActive(element))
                {
                    DisableGradientSceneControl();
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
            bool sceneControlActive = IsGradientSceneControlActive(element);
            string sceneControlLabel = mode == 0 ? "\u7eaf\u8272\u6a21\u5f0f\u65e0\u89c6\u56fe\u63a7\u4ef6" : (sceneControlActive ? "\u505c\u6b62\u89c6\u56fe\u63a7\u5236" : "\u5728\u89c6\u56fe\u4e2d\u8c03\u6574");
            if (GUI.Button(new Rect(rect.x, y, rect.width, LineHeight), sceneControlLabel))
            {
                if (sceneControlActive)
                {
                    DisableGradientSceneControl();
                }
                else
                {
                    EnableGradientSceneControl(element);
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

        private static void RegisterSceneViewControls()
        {
            if (gradientSceneViewRegistered)
            {
                return;
            }

            SceneView.duringSceneGui += OnGradientSceneGUI;
            gradientSceneViewRegistered = true;
        }

        private static void UnregisterSceneViewControls()
        {
            if (!gradientSceneViewRegistered)
            {
                return;
            }

            SceneView.duringSceneGui -= OnGradientSceneGUI;
            gradientSceneViewRegistered = false;
        }

        private void EnableGradientSceneControl(SerializedProperty element)
        {
            if (serializedObject?.targetObject == null || element == null)
            {
                return;
            }

            activeGradientSceneTargetId = serializedObject.targetObject.GetInstanceID();
            activeGradientScenePropertyPath = element.propertyPath;
            activeGradientSceneHandle = GradientSceneHandle.None;
            SceneView.RepaintAll();
        }

        private static void DisableGradientSceneControl()
        {
            activeGradientSceneTargetId = 0;
            activeGradientScenePropertyPath = null;
            activeGradientSceneHandle = GradientSceneHandle.None;
            SceneView.RepaintAll();
        }

        private void DisableGradientSceneControlForThisEditor()
        {
            if (serializedObject?.targetObject != null &&
                activeGradientSceneTargetId == serializedObject.targetObject.GetInstanceID())
            {
                DisableGradientSceneControl();
            }
        }

        private bool IsGradientSceneControlActive(SerializedProperty element)
        {
            return serializedObject?.targetObject != null &&
                   element != null &&
                   activeGradientSceneTargetId == serializedObject.targetObject.GetInstanceID() &&
                   activeGradientScenePropertyPath == element.propertyPath;
        }

        private static void OnGradientSceneGUI(SceneView sceneView)
        {
            if (activeGradientSceneTargetId == 0 || string.IsNullOrEmpty(activeGradientScenePropertyPath))
            {
                return;
            }

            UnityEngine.Object target = EditorUtility.InstanceIDToObject(activeGradientSceneTargetId);
            if (target == null)
            {
                DisableGradientSceneControl();
                return;
            }

            SerializedObject so = new SerializedObject(target);
            so.Update();
            SerializedProperty element = so.FindProperty(activeGradientScenePropertyPath);
            if (element == null || GetEffect(element) != ShoostPostProcessEffect.Gradient)
            {
                DisableGradientSceneControl();
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
                DisableGradientSceneControl();
                return;
            }

            Event evt = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (evt.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlId);
            }

            Rect viewRect = GetGradientSceneViewRect(sceneView);
            float aspect = Mathf.Max(viewRect.width / Mathf.Max(viewRect.height, 1.0f), 0.0001f);
            Vector2 centerUv = new Vector2(0.5f + p1.x, 0.5f + p1.y);
            Vector2 centerGui = GradientUvToGui(centerUv, viewRect);
            Vector2 radiusGui = GetGradientRadiusHandleGui(mode, p0, p1, p2, viewRect, aspect);
            Vector2 angleGui = GetGradientAngleHandleGui(mode, p0, p1, viewRect);
            Vector2 scaleXGui = GetGradientScaleHandleGui(p0, p1, p2, viewRect, aspect, true);
            Vector2 scaleYGui = GetGradientScaleHandleGui(p0, p1, p2, viewRect, aspect, false);

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                activeGradientSceneHandle = PickGradientSceneHandle(evt.mousePosition, mode, centerGui, radiusGui, angleGui, scaleXGui, scaleYGui);
                if (activeGradientSceneHandle != GradientSceneHandle.None)
                {
                    GUIUtility.hotControl = controlId;
                    Undo.RecordObject(target, "Adjust Shoost Gradient In View");
                    evt.Use();
                }
            }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == controlId && activeGradientSceneHandle != GradientSceneHandle.None)
            {
                Vector2 uv = GradientGuiToUv(evt.mousePosition, viewRect);
                ApplyGradientSceneDrag(activeGradientSceneHandle, mode, uv, aspect, ref p0, ref p1, ref p2);
                parameters0.vector4Value = p0;
                parameters1.vector4Value = p1;
                parameters2.vector4Value = p2;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                evt.Use();
                sceneView.Repaint();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
            {
                GUIUtility.hotControl = 0;
                activeGradientSceneHandle = GradientSceneHandle.None;
                evt.Use();
            }
            else if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                DisableGradientSceneControl();
                evt.Use();
            }

            DrawGradientSceneHandles(sceneView, mode, centerGui, radiusGui, angleGui, scaleXGui, scaleYGui);
        }

        private static Rect GetGradientSceneViewRect(SceneView sceneView)
        {
            return new Rect(0.0f, 0.0f, Mathf.Max(1.0f, sceneView.position.width), Mathf.Max(1.0f, sceneView.position.height));
        }

        private static Vector2 GradientUvToGui(Vector2 uv, Rect rect)
        {
            return new Vector2(rect.x + uv.x * rect.width, rect.y + (1.0f - uv.y) * rect.height);
        }

        private static Vector2 GradientGuiToUv(Vector2 gui, Rect rect)
        {
            return new Vector2(
                Mathf.Clamp01((gui.x - rect.x) / Mathf.Max(rect.width, 1.0f)),
                Mathf.Clamp01(1.0f - ((gui.y - rect.y) / Mathf.Max(rect.height, 1.0f))));
        }

        private static Vector2 GetGradientRadiusHandleGui(int mode, Vector4 p0, Vector4 p1, Vector4 p2, Rect rect, float aspect)
        {
            Vector2 center = new Vector2(0.5f + p1.x, 0.5f + p1.y);
            float radius = Mathf.Max(p0.y, 0.0001f);
            if (mode == 1)
            {
                float angle = (p1.z + 90.0f) * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                return GradientUvToGui(center + direction * radius * 0.5f, rect);
            }

            float scaleX = mode == 3 ? Mathf.Max(p2.x, 0.1f) : 1.0f;
            return GradientUvToGui(center + new Vector2(radius * scaleX / Mathf.Max(aspect, 0.0001f), 0.0f), rect);
        }

        private static Vector2 GetGradientAngleHandleGui(int mode, Vector4 p0, Vector4 p1, Rect rect)
        {
            Vector2 center = new Vector2(0.5f + p1.x, 0.5f + p1.y);
            if (mode != 1)
            {
                return center;
            }

            float angle = (p1.z + 90.0f) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            return GradientUvToGui(center + direction * Mathf.Max(p0.y, 0.1f) * 0.35f, rect);
        }

        private static Vector2 GetGradientScaleHandleGui(Vector4 p0, Vector4 p1, Vector4 p2, Rect rect, float aspect, bool horizontal)
        {
            Vector2 center = new Vector2(0.5f + p1.x, 0.5f + p1.y);
            float radius = Mathf.Max(p0.y, 0.0001f);
            if (horizontal)
            {
                return GradientUvToGui(center + new Vector2(radius * Mathf.Max(p2.x, 0.1f) / Mathf.Max(aspect, 0.0001f), 0.0f), rect);
            }

            return GradientUvToGui(center + new Vector2(0.0f, radius * Mathf.Max(p2.y, 0.1f)), rect);
        }

        private static GradientSceneHandle PickGradientSceneHandle(Vector2 mouse, int mode, Vector2 center, Vector2 radius, Vector2 angle, Vector2 scaleX, Vector2 scaleY)
        {
            if (Vector2.Distance(mouse, center) <= GradientSceneHandleSize * 1.8f)
            {
                return GradientSceneHandle.Center;
            }

            if (mode == 1 && Vector2.Distance(mouse, angle) <= GradientSceneHandleSize * 1.8f)
            {
                return GradientSceneHandle.Angle;
            }

            if (mode == 3)
            {
                if (Vector2.Distance(mouse, scaleY) <= GradientSceneHandleSize * 1.8f)
                {
                    return GradientSceneHandle.ScaleY;
                }

                if (Vector2.Distance(mouse, scaleX) <= GradientSceneHandleSize * 1.8f)
                {
                    return GradientSceneHandle.ScaleX;
                }
            }

            if (Vector2.Distance(mouse, radius) <= GradientSceneHandleSize * 1.8f)
            {
                return GradientSceneHandle.Radius;
            }

            return GradientSceneHandle.None;
        }

        private static void ApplyGradientSceneDrag(GradientSceneHandle handle, int mode, Vector2 uv, float aspect, ref Vector4 p0, ref Vector4 p1, ref Vector4 p2)
        {
            Vector2 center = new Vector2(0.5f + p1.x, 0.5f + p1.y);
            Vector2 delta = uv - center;

            if (handle == GradientSceneHandle.Center)
            {
                p1.x = Mathf.Clamp(uv.x - 0.5f, -3.0f, 3.0f);
                p1.y = Mathf.Clamp(uv.y - 0.5f, -3.0f, 3.0f);
                return;
            }

            if (handle == GradientSceneHandle.Angle)
            {
                if (delta.sqrMagnitude > 0.000001f)
                {
                    p1.z = Mathf.DeltaAngle(0.0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90.0f);
                }
                return;
            }

            if (handle == GradientSceneHandle.ScaleX)
            {
                p2.x = Mathf.Clamp(Mathf.Abs(delta.x) * aspect / Mathf.Max(p0.y, 0.0001f), 0.1f, 3.0f);
                return;
            }

            if (handle == GradientSceneHandle.ScaleY)
            {
                p2.y = Mathf.Clamp(Mathf.Abs(delta.y) / Mathf.Max(p0.y, 0.0001f), 0.1f, 3.0f);
                return;
            }

            if (mode == 1)
            {
                float angle = (p1.z + 90.0f) * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
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

        private static void DrawGradientSceneHandles(SceneView sceneView, int mode, Vector2 center, Vector2 radius, Vector2 angle, Vector2 scaleX, Vector2 scaleY)
        {
            Handles.BeginGUI();
            Color oldColor = Handles.color;
            Handles.color = new Color(0.25f, 0.75f, 1.0f, 0.95f);
            Handles.DrawAAPolyLine(2.0f, center, radius);
            if (mode == 1)
            {
                Handles.color = new Color(1.0f, 0.82f, 0.25f, 0.95f);
                Handles.DrawAAPolyLine(2.0f, center, angle);
            }
            else if (mode == 3)
            {
                Handles.color = new Color(0.5f, 1.0f, 0.55f, 0.95f);
                Handles.DrawAAPolyLine(2.0f, center, scaleX);
                Handles.DrawAAPolyLine(2.0f, center, scaleY);
            }

            DrawGradientScenePoint(center, "C", new Color(0.20f, 0.72f, 1.0f, 0.95f));
            DrawGradientScenePoint(radius, "R", new Color(0.20f, 0.72f, 1.0f, 0.95f));
            if (mode == 1)
            {
                DrawGradientScenePoint(angle, "A", new Color(1.0f, 0.78f, 0.18f, 0.95f));
            }
            else if (mode == 3)
            {
                DrawGradientScenePoint(scaleX, "X", new Color(0.46f, 1.0f, 0.55f, 0.95f));
                DrawGradientScenePoint(scaleY, "Y", new Color(0.46f, 1.0f, 0.55f, 0.95f));
            }

            Handles.color = oldColor;
            DrawGradientSceneOverlayContent(sceneView, "Shoost Gradient View Control  |  Drag C/R" + (mode == 1 ? "/A" : (mode == 3 ? "/X/Y" : string.Empty)) + "  |  Esc to exit");
            Handles.EndGUI();
        }

        private static void DrawGradientScenePoint(Vector2 point, string label, Color color)
        {
            Rect rect = new Rect(point.x - GradientSceneHandleSize, point.y - GradientSceneHandleSize, GradientSceneHandleSize * 2.0f, GradientSceneHandleSize * 2.0f);
            EditorGUI.DrawRect(rect, new Color(0.0f, 0.0f, 0.0f, 0.55f));
            Rect inner = new Rect(rect.x + 2.0f, rect.y + 2.0f, rect.width - 4.0f, rect.height - 4.0f);
            EditorGUI.DrawRect(inner, color);
            GUI.Label(new Rect(rect.x - 12.0f, rect.y - 18.0f, 44.0f, 16.0f), label, EditorStyles.whiteMiniLabel);
        }

        private static void DrawGradientSceneOverlayContent(SceneView sceneView, string text)
        {
            Rect rect = new Rect(12.0f, 12.0f, Mathf.Min(420.0f, sceneView.position.width - 24.0f), 24.0f);
            EditorGUI.DrawRect(rect, new Color(0.0f, 0.0f, 0.0f, 0.62f));
            GUI.Label(new Rect(rect.x + 8.0f, rect.y + 3.0f, rect.width - 16.0f, rect.height), text, EditorStyles.whiteMiniLabel);
        }
    }
}
