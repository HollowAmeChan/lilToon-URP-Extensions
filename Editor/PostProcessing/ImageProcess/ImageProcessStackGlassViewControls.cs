using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private static PostProcessLayerViewControlSession ImageProcessGlassViewControl =
            new PostProcessLayerViewControlSession("ImageProcess.Glass");

        private enum GlassViewHandle
        {
            None = 0,
            Center = 1,
            Width = 2,
            Height = 3,
            Angle = 4
        }

        private float DrawImageProcessGlassViewControlButton(Rect rect, float y, SerializedProperty element)
        {
            bool active = ImageProcessGlassViewControl.IsActive(serializedObject?.targetObject, element);
            if (GUI.Button(new Rect(rect.x, y, rect.width, LineHeight), active ? "停止游戏视图控制" : "在游戏视图中调整"))
            {
                if (active)
                {
                    ImageProcessGlassViewControl.Stop();
                }
                else if (serializedObject?.targetObject != null)
                {
                    ImageProcessCenterRadiusViewControl.Stop();
                    ImageProcessDirectionDistanceViewControl.Stop();
                    ImageProcessParticleViewControl.Stop();
                    ImageProcessGlassViewControl.Start(serializedObject.targetObject, element, OnImageProcessGlassGameViewGUI);
                }
            }

            return y + LineHeight + LineSpacing;
        }

        private bool IsImageProcessGlassViewControlActive(SerializedProperty element)
        {
            return ImageProcessGlassViewControl.IsActive(serializedObject?.targetObject, element);
        }

        private static void StopImageProcessGlassViewControl()
        {
            ImageProcessGlassViewControl.Stop();
        }

        private static void OnImageProcessGlassGameViewGUI(Rect viewRect, Event evt)
        {
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                ImageProcessGlassViewControl.Stop();
                evt.Use();
                return;
            }

            if (!ImageProcessGlassViewControl.TryGetElement(out UnityEngine.Object target, out SerializedObject so, out SerializedProperty element))
            {
                return;
            }

            if (GetEffect(element) != ImageProcessEffect.Glass)
            {
                ImageProcessGlassViewControl.Stop();
                return;
            }

            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            EnsureGlassDefaults(parameters0, parameters1, parameters2, parameters3);

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            int activeHandle = ImageProcessGlassViewControl.ActiveHandle;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            float aspect = PostProcessScreenSpaceViewControl.Aspect(viewRect);
            Vector2 centerUv = new Vector2(Mathf.Clamp01(p0.x), Mathf.Clamp01(p0.y));
            float width = Mathf.Clamp(p0.z, 0.02f, 2.0f);
            float height = Mathf.Clamp(p0.w, 0.02f, 2.0f);
            float angle = Mathf.Clamp(p1.x, -180.0f, 180.0f);
            Vector2 axisX = PostProcessScreenSpaceViewControl.DirectionFromDegrees(angle, 0.0f);
            Vector2 axisY = new Vector2(-axisX.y, axisX.x);
            PostProcessScreenSpaceHandle[] handles = BuildGlassHandles(viewRect, centerUv, width, height, angle, aspect);

            bool changed = false;
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                activeHandle = PostProcessScreenSpaceViewControl.PickHandle(evt.mousePosition, handles, (int)GlassViewHandle.None);
                if (activeHandle != (int)GlassViewHandle.None)
                {
                    GUIUtility.hotControl = controlId;
                    ImageProcessGlassViewControl.ActiveHandle = activeHandle;
                    Undo.RecordObject(target, "Adjust ImageProcess Glass In View");
                    evt.Use();
                    PostProcessScreenSpaceViewControl.RequestRepaint();
                }
            }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == controlId && activeHandle != (int)GlassViewHandle.None)
            {
                Vector2 uv = PostProcessScreenSpaceViewControl.GuiToUv(evt.mousePosition, viewRect);
                Vector2 delta = uv - centerUv;
                Vector2 physicalDelta = new Vector2(delta.x * aspect, delta.y);
                if (activeHandle == (int)GlassViewHandle.Center)
                {
                    centerUv = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
                }
                else if (activeHandle == (int)GlassViewHandle.Width)
                {
                    width = Mathf.Clamp(Mathf.Abs(Vector2.Dot(physicalDelta, axisX)) * 2.0f, 0.02f, 2.0f);
                }
                else if (activeHandle == (int)GlassViewHandle.Height)
                {
                    width = Mathf.Max(width, 0.02f);
                    height = Mathf.Clamp(Mathf.Abs(Vector2.Dot(physicalDelta, axisY)) * 2.0f, 0.02f, 2.0f);
                }
                else if (activeHandle == (int)GlassViewHandle.Angle && physicalDelta.sqrMagnitude > 0.000001f)
                {
                    float pointerAngle = Mathf.Atan2(physicalDelta.y, physicalDelta.x) * Mathf.Rad2Deg;
                    angle = Mathf.DeltaAngle(0.0f, pointerAngle);
                }

                p0.x = centerUv.x;
                p0.y = centerUv.y;
                p0.z = width;
                p0.w = height;
                p1.x = angle;
                changed = true;
                evt.Use();
                PostProcessScreenSpaceViewControl.RequestRepaint();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
            {
                GUIUtility.hotControl = 0;
                ImageProcessGlassViewControl.ActiveHandle = (int)GlassViewHandle.None;
                evt.Use();
                PostProcessScreenSpaceViewControl.RequestRepaint();
            }

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            if (changed)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }

            centerUv = new Vector2(Mathf.Clamp01(p0.x), Mathf.Clamp01(p0.y));
            handles = BuildGlassHandles(viewRect, centerUv, Mathf.Clamp(p0.z, 0.02f, 2.0f), Mathf.Clamp(p0.w, 0.02f, 2.0f), Mathf.Clamp(p1.x, -180.0f, 180.0f), aspect);
            Vector2 centerGui = PostProcessScreenSpaceViewControl.UvToGui(centerUv, viewRect);
            PostProcessScreenSpaceViewControl.DrawHandleSet(
                viewRect,
                centerGui,
                handles,
                ImageProcessGlassViewControl.ActiveHandle,
                "玻璃  C 中心  X 宽度  Y 高度  A 旋转  Esc 退出");
        }

        private static PostProcessScreenSpaceHandle[] BuildGlassHandles(
            Rect viewRect,
            Vector2 centerUv,
            float width,
            float height,
            float angle,
            float aspect)
        {
            Vector2 axisX = PostProcessScreenSpaceViewControl.DirectionFromDegrees(angle, 0.0f);
            Vector2 axisY = new Vector2(-axisX.y, axisX.x);
            Vector2 centerGui = PostProcessScreenSpaceViewControl.UvToGui(centerUv, viewRect);
            Vector2 widthUv = centerUv + new Vector2(axisX.x * width * 0.5f / aspect, axisX.y * width * 0.5f);
            Vector2 heightUv = centerUv + new Vector2(axisY.x * height * 0.5f / aspect, axisY.y * height * 0.5f);
            float angleDistance = Mathf.Max(width, 0.02f) * 0.72f;
            Vector2 angleUv = centerUv + new Vector2(axisX.x * angleDistance / aspect, axisX.y * angleDistance);
            return new[]
            {
                new PostProcessScreenSpaceHandle((int)GlassViewHandle.Center, "C", centerGui, Color.white, PostProcessScreenSpaceHandleKind.Point, false),
                new PostProcessScreenSpaceHandle((int)GlassViewHandle.Width, "X", PostProcessScreenSpaceViewControl.UvToGui(widthUv, viewRect), Color.white, PostProcessScreenSpaceHandleKind.HorizontalScale),
                new PostProcessScreenSpaceHandle((int)GlassViewHandle.Height, "Y", PostProcessScreenSpaceViewControl.UvToGui(heightUv, viewRect), Color.white, PostProcessScreenSpaceHandleKind.VerticalScale),
                new PostProcessScreenSpaceHandle((int)GlassViewHandle.Angle, "A", PostProcessScreenSpaceViewControl.UvToGui(angleUv, viewRect), Color.white, PostProcessScreenSpaceHandleKind.Angle, true, 0.62f)
            };
        }
    }
}
