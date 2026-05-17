using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static PostProcessLayerViewControlSession ShoostCenterRadiusViewControl =
            new PostProcessLayerViewControlSession("Shoost.CenterRadius");

        private static PostProcessLayerViewControlSession ShoostDirectionDistanceViewControl =
            new PostProcessLayerViewControlSession("Shoost.DirectionDistance");

        private float DrawShoostCenterRadiusViewControlButton(Rect rect, float y, SerializedProperty element)
        {
            bool active = IsShoostCenterRadiusViewControlActive(element);
            if (GUI.Button(new Rect(rect.x, y, rect.width, LineHeight), active ? "\u505c\u6b62\u6e38\u620f\u89c6\u56fe\u63a7\u5236" : "\u5728\u6e38\u620f\u89c6\u56fe\u4e2d\u8c03\u6574"))
            {
                if (active)
                {
                    ShoostCenterRadiusViewControl.Stop();
                }
                else if (serializedObject?.targetObject != null)
                {
                    ShoostDirectionDistanceViewControl.Stop();
                    ShoostCenterRadiusViewControl.Start(serializedObject.targetObject, element, OnShoostCenterRadiusGameViewGUI);
                }
            }

            return y + LineHeight + LineSpacing;
        }

        private float DrawShoostDirectionDistanceViewControlButton(Rect rect, float y, SerializedProperty element)
        {
            bool active = IsShoostDirectionDistanceViewControlActive(element);
            if (GUI.Button(new Rect(rect.x, y, rect.width, LineHeight), active ? "\u505c\u6b62\u6e38\u620f\u89c6\u56fe\u63a7\u5236" : "\u5728\u6e38\u620f\u89c6\u56fe\u4e2d\u8c03\u6574"))
            {
                if (active)
                {
                    ShoostDirectionDistanceViewControl.Stop();
                }
                else if (serializedObject?.targetObject != null)
                {
                    ShoostCenterRadiusViewControl.Stop();
                    ShoostDirectionDistanceViewControl.Start(serializedObject.targetObject, element, OnShoostDirectionDistanceGameViewGUI);
                }
            }

            return y + LineHeight + LineSpacing;
        }

        private bool IsShoostCenterRadiusViewControlActive(SerializedProperty element)
        {
            return ShoostCenterRadiusViewControl.IsActive(serializedObject?.targetObject, element);
        }

        private bool IsShoostDirectionDistanceViewControlActive(SerializedProperty element)
        {
            return ShoostDirectionDistanceViewControl.IsActive(serializedObject?.targetObject, element);
        }

        private void DisableShoostLayerViewControlsForThisEditor()
        {
            if (serializedObject?.targetObject == null)
            {
                return;
            }

            ShoostCenterRadiusViewControl.StopIfOwnedBy(serializedObject.targetObject);
            ShoostDirectionDistanceViewControl.StopIfOwnedBy(serializedObject.targetObject);
        }

        private static void OnShoostCenterRadiusGameViewGUI(Rect viewRect, Event evt)
        {
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                ShoostCenterRadiusViewControl.Stop();
                evt.Use();
                return;
            }

            if (!ShoostCenterRadiusViewControl.TryGetElement(out UnityEngine.Object target, out SerializedObject so, out SerializedProperty element))
            {
                return;
            }

            bool changed = false;
            switch (GetEffect(element))
            {
                case ShoostPostProcessEffect.VignetteCustom:
                    changed = HandleShoostVignetteViewControl(viewRect, evt, target, element);
                    break;
                case ShoostPostProcessEffect.CenterColorCorrection:
                    changed = HandleShoostCenterColorCorrectionViewControl(viewRect, evt, target, element);
                    break;
                case ShoostPostProcessEffect.IrisBlur:
                    changed = HandleShoostIrisBlurViewControl(viewRect, evt, target, element);
                    break;
                case ShoostPostProcessEffect.BokehZoomBlur:
                    changed = HandleShoostBokehZoomBlurViewControl(viewRect, evt, target, element);
                    break;
                case ShoostPostProcessEffect.PrismFracture:
                    changed = HandleShoostPrismFractureViewControl(viewRect, evt, target, element);
                    break;
                default:
                    ShoostCenterRadiusViewControl.Stop();
                    return;
            }

            if (changed)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private static void OnShoostDirectionDistanceGameViewGUI(Rect viewRect, Event evt)
        {
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                ShoostDirectionDistanceViewControl.Stop();
                evt.Use();
                return;
            }

            if (!ShoostDirectionDistanceViewControl.TryGetElement(out UnityEngine.Object target, out SerializedObject so, out SerializedProperty element))
            {
                return;
            }

            bool changed = false;
            switch (GetEffect(element))
            {
                case ShoostPostProcessEffect.LensFlare:
                    changed = HandleShoostLensFlareViewControl(viewRect, evt, target, element);
                    break;
                default:
                    ShoostDirectionDistanceViewControl.Stop();
                    return;
            }

            if (changed)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private static bool HandleShoostVignetteViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
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
                "Adjust Shoost Vignette In View",
                ref ShoostCenterRadiusViewControl.ActiveHandle,
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

        private static bool HandleShoostCenterColorCorrectionViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
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
                "Adjust Shoost Center Color Correction In View",
                ref ShoostCenterRadiusViewControl.ActiveHandle,
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

        private static bool HandleShoostIrisBlurViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
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
                "Adjust Shoost Iris Blur In View",
                ref ShoostCenterRadiusViewControl.ActiveHandle,
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

        private static bool HandleShoostBokehZoomBlurViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
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
                "Adjust Shoost Bokeh Zoom Blur In View",
                ref ShoostCenterRadiusViewControl.ActiveHandle,
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

        private static bool HandleShoostPrismFractureViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
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
                "Adjust Shoost Prism Fracture In View",
                ref ShoostCenterRadiusViewControl.ActiveHandle,
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

        private static bool HandleShoostLensFlareViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
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
                "Adjust Shoost Lens Flare In View",
                ref ShoostDirectionDistanceViewControl.ActiveHandle,
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
