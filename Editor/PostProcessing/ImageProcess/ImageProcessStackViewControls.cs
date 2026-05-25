using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private static PostProcessLayerViewControlSession ImageProcessCenterRadiusViewControl =
            new PostProcessLayerViewControlSession("ImageProcess.CenterRadius");

        private static PostProcessLayerViewControlSession ImageProcessDirectionDistanceViewControl =
            new PostProcessLayerViewControlSession("ImageProcess.DirectionDistance");

        private float DrawImageProcessCenterRadiusViewControlButton(Rect rect, float y, SerializedProperty element)
        {
            bool active = IsImageProcessCenterRadiusViewControlActive(element);
            if (GUI.Button(new Rect(rect.x, y, rect.width, LineHeight), active ? "\u505c\u6b62\u6e38\u620f\u89c6\u56fe\u63a7\u5236" : "\u5728\u6e38\u620f\u89c6\u56fe\u4e2d\u8c03\u6574"))
            {
                if (active)
                {
                    ImageProcessCenterRadiusViewControl.Stop();
                }
                else if (serializedObject?.targetObject != null)
                {
                    ImageProcessDirectionDistanceViewControl.Stop();
                    ImageProcessCenterRadiusViewControl.Start(serializedObject.targetObject, element, OnImageProcessCenterRadiusGameViewGUI);
                }
            }

            return y + LineHeight + LineSpacing;
        }

        private float DrawImageProcessDirectionDistanceViewControlButton(Rect rect, float y, SerializedProperty element)
        {
            bool active = IsImageProcessDirectionDistanceViewControlActive(element);
            if (GUI.Button(new Rect(rect.x, y, rect.width, LineHeight), active ? "\u505c\u6b62\u6e38\u620f\u89c6\u56fe\u63a7\u5236" : "\u5728\u6e38\u620f\u89c6\u56fe\u4e2d\u8c03\u6574"))
            {
                if (active)
                {
                    ImageProcessDirectionDistanceViewControl.Stop();
                }
                else if (serializedObject?.targetObject != null)
                {
                    ImageProcessCenterRadiusViewControl.Stop();
                    ImageProcessDirectionDistanceViewControl.Start(serializedObject.targetObject, element, OnImageProcessDirectionDistanceGameViewGUI);
                }
            }

            return y + LineHeight + LineSpacing;
        }

        private bool IsImageProcessCenterRadiusViewControlActive(SerializedProperty element)
        {
            return ImageProcessCenterRadiusViewControl.IsActive(serializedObject?.targetObject, element);
        }

        private bool IsImageProcessDirectionDistanceViewControlActive(SerializedProperty element)
        {
            return ImageProcessDirectionDistanceViewControl.IsActive(serializedObject?.targetObject, element);
        }

        private void DisableImageProcessLayerViewControlsForThisEditor()
        {
            if (serializedObject?.targetObject == null)
            {
                return;
            }

            ImageProcessCenterRadiusViewControl.StopIfOwnedBy(serializedObject.targetObject);
            ImageProcessDirectionDistanceViewControl.StopIfOwnedBy(serializedObject.targetObject);
        }

        private static void OnImageProcessCenterRadiusGameViewGUI(Rect viewRect, Event evt)
        {
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                ImageProcessCenterRadiusViewControl.Stop();
                evt.Use();
                return;
            }

            if (!ImageProcessCenterRadiusViewControl.TryGetElement(out UnityEngine.Object target, out SerializedObject so, out SerializedProperty element))
            {
                return;
            }

            bool changed = false;
            switch (GetEffect(element))
            {
                case ImageProcessEffect.VignetteCustom:
                    changed = HandleImageProcessVignetteViewControl(viewRect, evt, target, element);
                    break;
                case ImageProcessEffect.CenterColorCorrection:
                    changed = HandleImageProcessCenterColorCorrectionViewControl(viewRect, evt, target, element);
                    break;
                case ImageProcessEffect.IrisBlur:
                    changed = HandleImageProcessIrisBlurViewControl(viewRect, evt, target, element);
                    break;
                case ImageProcessEffect.BokehZoomBlur:
                    changed = HandleImageProcessBokehZoomBlurViewControl(viewRect, evt, target, element);
                    break;
                case ImageProcessEffect.PrismFracture:
                    changed = HandleImageProcessPrismFractureViewControl(viewRect, evt, target, element);
                    break;
                default:
                    ImageProcessCenterRadiusViewControl.Stop();
                    return;
            }

            if (changed)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private static void OnImageProcessDirectionDistanceGameViewGUI(Rect viewRect, Event evt)
        {
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                ImageProcessDirectionDistanceViewControl.Stop();
                evt.Use();
                return;
            }

            if (!ImageProcessDirectionDistanceViewControl.TryGetElement(out UnityEngine.Object target, out SerializedObject so, out SerializedProperty element))
            {
                return;
            }

            bool changed = false;
            switch (GetEffect(element))
            {
                case ImageProcessEffect.LensFlare:
                    changed = HandleImageProcessLensFlareViewControl(viewRect, evt, target, element);
                    break;
                default:
                    ImageProcessDirectionDistanceViewControl.Stop();
                    return;
            }

            if (changed)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private static bool HandleImageProcessVignetteViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            EnsureVignetteCustomDefaults(parameters0);
            Vector4 p0 = parameters0.vector4Value;
            Vector2 center = new Vector2(p0.x, p0.y);
            float radius = p0.z;
            bool changed = PostProcessScreenSpaceControlTemplates.HandleCenterRadius(
                viewRect,
                evt,
                target,
                "Adjust ImageProcess Vignette In View",
                ref ImageProcessCenterRadiusViewControl.ActiveHandle,
                ref center,
                ref radius,
                0.0f,
                2.0f,
                "\u6697\u89d2  C \u4e2d\u5fc3  R \u534a\u5f84  Esc \u9000\u51fa");
            if (changed)
            {
                p0.x = center.x;
                p0.y = center.y;
                p0.z = radius;
                parameters0.vector4Value = p0;
            }

            return changed;
        }

        private static bool HandleImageProcessCenterColorCorrectionViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            EnsureCenterColorCorrectionDefaults(parameters0, parameters1, parameters2);
            Vector4 p1 = parameters1.vector4Value;
            Vector2 center = new Vector2(0.5f + p1.z * 0.5f, 0.5f + p1.w * 0.5f);
            float radius = p1.x;
            bool changed = PostProcessScreenSpaceControlTemplates.HandleCenterRadius(
                viewRect,
                evt,
                target,
                "Adjust ImageProcess Center Color Correction In View",
                ref ImageProcessCenterRadiusViewControl.ActiveHandle,
                ref center,
                ref radius,
                0.0f,
                1.0f,
                "\u4e2d\u5fc3\u8c03\u8272  C \u4e2d\u5fc3  R \u534a\u5f84  Esc \u9000\u51fa");
            if (changed)
            {
                p1.x = radius;
                p1.z = Mathf.Clamp((center.x - 0.5f) * 2.0f, -1.0f, 1.0f);
                p1.w = Mathf.Clamp((center.y - 0.5f) * 2.0f, -1.0f, 1.0f);
                parameters1.vector4Value = p1;
            }

            return changed;
        }

        private static bool HandleImageProcessIrisBlurViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            EnsureIrisBlurDefaults(parameters0, parameters1, parameters2, parameters3);
            Vector4 p2 = parameters2.vector4Value;
            Vector2 center = new Vector2(p2.x, p2.y);
            float radius = p2.z;
            bool changed = PostProcessScreenSpaceControlTemplates.HandleCenterRadius(
                viewRect,
                evt,
                target,
                "Adjust ImageProcess Iris Blur In View",
                ref ImageProcessCenterRadiusViewControl.ActiveHandle,
                ref center,
                ref radius,
                0.0f,
                1.0f,
                "\u5149\u5708\u6a21\u7cca  C \u4e2d\u5fc3  R \u534a\u5f84  Esc \u9000\u51fa");
            if (changed)
            {
                p2.x = center.x;
                p2.y = center.y;
                p2.z = radius;
                parameters2.vector4Value = p2;
            }

            return changed;
        }

        private static bool HandleImageProcessBokehZoomBlurViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty color = element.FindPropertyRelative("color");
            EnsureBokehZoomBlurDefaults(parameters0, parameters1, parameters2, parameters3, color);
            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector2 center = new Vector2(0.5f + p1.x * 0.5f, 0.5f + p1.y * 0.5f);
            float radius = p0.x;
            bool changed = PostProcessScreenSpaceControlTemplates.HandleCenterRadius(
                viewRect,
                evt,
                target,
                "Adjust ImageProcess Bokeh Zoom Blur In View",
                ref ImageProcessCenterRadiusViewControl.ActiveHandle,
                ref center,
                ref radius,
                0.0f,
                1.0f,
                "\u5149\u6591\u53d8\u7126  C \u4e2d\u5fc3  R \u534a\u5f84  Esc \u9000\u51fa");
            if (changed)
            {
                p0.x = radius;
                p1.x = Mathf.Clamp((center.x - 0.5f) * 2.0f, -1.0f, 1.0f);
                p1.y = Mathf.Clamp((center.y - 0.5f) * 2.0f, -1.0f, 1.0f);
                parameters0.vector4Value = p0;
                parameters1.vector4Value = p1;
            }

            return changed;
        }

        private static bool HandleImageProcessPrismFractureViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            EnsurePrismFractureDefaults(parameters0, element.FindPropertyRelative("parameters1"), element.FindPropertyRelative("parameters2"));
            Vector4 p0 = parameters0.vector4Value;
            Vector2 center = new Vector2(p0.x, p0.y);
            float radius = p0.z;
            bool changed = PostProcessScreenSpaceControlTemplates.HandleCenterRadius(
                viewRect,
                evt,
                target,
                "Adjust ImageProcess Prism Fracture In View",
                ref ImageProcessCenterRadiusViewControl.ActiveHandle,
                ref center,
                ref radius,
                0.0f,
                1.5f,
                "棱镜破碎  C 中心  R 半径  Esc 退出");
            if (changed)
            {
                p0.x = center.x;
                p0.y = center.y;
                p0.z = radius;
                parameters0.vector4Value = p0;
            }

            return changed;
        }

        private static bool HandleImageProcessLensFlareViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty color = element.FindPropertyRelative("color");
            EnsureLensFlareDefaults(parameters0, parameters1, parameters2, parameters3, color);
            Vector4 p0 = parameters0.vector4Value;
            Vector2 origin = new Vector2(0.5f + p0.x * 0.5f, 0.5f + p0.y * 0.5f);
            float angle = p0.z;
            float distance = p0.w;
            bool changed = PostProcessScreenSpaceControlTemplates.HandleDirectionDistance(
                viewRect,
                evt,
                target,
                "Adjust ImageProcess Lens Flare In View",
                ref ImageProcessDirectionDistanceViewControl.ActiveHandle,
                ref origin,
                ref angle,
                ref distance,
                0.0f,
                2.0f,
                0.18f,
                true,
                true,
                "\u955c\u5934\u5149\u6655  C \u592a\u9633  D \u65b9\u5411/\u8ddd\u79bb  Esc \u9000\u51fa");
            if (changed)
            {
                p0.x = Mathf.Clamp((origin.x - 0.5f) * 2.0f, -1.0f, 1.0f);
                p0.y = Mathf.Clamp((origin.y - 0.5f) * 2.0f, -1.0f, 1.0f);
                p0.z = Mathf.DeltaAngle(0.0f, angle);
                p0.w = distance;
                parameters0.vector4Value = p0;
            }

            return changed;
        }
    }
}
