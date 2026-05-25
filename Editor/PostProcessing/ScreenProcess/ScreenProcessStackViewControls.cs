using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ScreenProcessStackVolumeEditor
    {
        private static PostProcessLayerViewControlSession ScreenProcessCenterRadiusViewControl =
            new PostProcessLayerViewControlSession("ScreenProcess.CenterRadius");

        private static PostProcessLayerViewControlSession ScreenProcessDirectionDistanceViewControl =
            new PostProcessLayerViewControlSession("ScreenProcess.DirectionDistance");

        private float DrawScreenProcessCenterRadiusViewControlButton(Rect rect, float y, SerializedProperty element)
        {
            bool active = IsScreenProcessCenterRadiusViewControlActive(element);
            if (GUI.Button(new Rect(rect.x, y, rect.width, LineHeight), active ? "\u505c\u6b62\u6e38\u620f\u89c6\u56fe\u63a7\u5236" : "\u5728\u6e38\u620f\u89c6\u56fe\u4e2d\u8c03\u6574"))
            {
                if (active)
                {
                    ScreenProcessCenterRadiusViewControl.Stop();
                }
                else if (serializedObject?.targetObject != null)
                {
                    ScreenProcessDirectionDistanceViewControl.Stop();
                    ScreenProcessCenterRadiusViewControl.Start(serializedObject.targetObject, element, OnScreenProcessCenterRadiusGameViewGUI);
                }
            }

            return y + LineHeight + LineSpacing;
        }

        private float DrawScreenProcessDirectionDistanceViewControlButton(Rect rect, float y, SerializedProperty element)
        {
            bool active = IsScreenProcessDirectionDistanceViewControlActive(element);
            if (GUI.Button(new Rect(rect.x, y, rect.width, LineHeight), active ? "\u505c\u6b62\u6e38\u620f\u89c6\u56fe\u63a7\u5236" : "\u5728\u6e38\u620f\u89c6\u56fe\u4e2d\u8c03\u6574"))
            {
                if (active)
                {
                    ScreenProcessDirectionDistanceViewControl.Stop();
                }
                else if (serializedObject?.targetObject != null)
                {
                    ScreenProcessCenterRadiusViewControl.Stop();
                    ScreenProcessDirectionDistanceViewControl.Start(serializedObject.targetObject, element, OnScreenProcessDirectionDistanceGameViewGUI);
                }
            }

            return y + LineHeight + LineSpacing;
        }

        private bool IsScreenProcessCenterRadiusViewControlActive(SerializedProperty element)
        {
            return ScreenProcessCenterRadiusViewControl.IsActive(serializedObject?.targetObject, element);
        }

        private bool IsScreenProcessDirectionDistanceViewControlActive(SerializedProperty element)
        {
            return ScreenProcessDirectionDistanceViewControl.IsActive(serializedObject?.targetObject, element);
        }

        private void DisableScreenProcessLayerViewControlsForThisEditor()
        {
            if (serializedObject?.targetObject != null)
            {
                ScreenProcessCenterRadiusViewControl.StopIfOwnedBy(serializedObject.targetObject);
                ScreenProcessDirectionDistanceViewControl.StopIfOwnedBy(serializedObject.targetObject);
            }
        }

        private static void OnScreenProcessCenterRadiusGameViewGUI(Rect viewRect, Event evt)
        {
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                ScreenProcessCenterRadiusViewControl.Stop();
                evt.Use();
                return;
            }

            if (!ScreenProcessCenterRadiusViewControl.TryGetElement(out UnityEngine.Object target, out SerializedObject so, out SerializedProperty element))
            {
                return;
            }

            bool changed = false;
            switch (GetEffect(element))
            {
                case ScreenProcessEffect.PostLighting:
                    changed = HandleScreenProcessPostLightingCenterViewControl(viewRect, evt, target, element);
                    break;
                default:
                    ScreenProcessCenterRadiusViewControl.Stop();
                    return;
            }

            if (changed)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private static void OnScreenProcessDirectionDistanceGameViewGUI(Rect viewRect, Event evt)
        {
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                ScreenProcessDirectionDistanceViewControl.Stop();
                evt.Use();
                return;
            }

            if (!ScreenProcessDirectionDistanceViewControl.TryGetElement(out UnityEngine.Object target, out SerializedObject so, out SerializedProperty element))
            {
                return;
            }

            bool changed = false;
            switch (GetEffect(element))
            {
                case ScreenProcessEffect.EdgeLight:
                    changed = HandleScreenProcessEdgeLightViewControl(viewRect, evt, target, element);
                    break;
                case ScreenProcessEffect.DropShadow:
                    changed = HandleScreenProcessDropShadowViewControl(viewRect, evt, target, element);
                    break;
                case ScreenProcessEffect.PostLighting:
                    changed = HandleScreenProcessPostLightingDirectionViewControl(viewRect, evt, target, element);
                    break;
                default:
                    ScreenProcessDirectionDistanceViewControl.Stop();
                    return;
            }

            if (changed)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private static bool HandleScreenProcessEdgeLightViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
        {
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            if (parameters1 == null || parameters1.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            Vector4 p1 = parameters1.vector4Value;
            Vector2 origin = new Vector2(0.5f, 0.5f);
            float angle = p1.x;
            float distance = 1.0f;
            bool changed = PostProcessScreenSpaceControlTemplates.HandleDirectionDistance(
                viewRect,
                evt,
                target,
                "Adjust ScreenProcess Edge Light In View",
                ref ScreenProcessDirectionDistanceViewControl.ActiveHandle,
                ref origin,
                ref angle,
                ref distance,
                0.0f,
                1.0f,
                0.28f,
                false,
                false,
                "\u8fb9\u7f18\u5149  D \u65b9\u5411  Esc \u9000\u51fa");
            if (changed)
            {
                p1.x = Mathf.DeltaAngle(0.0f, angle);
                parameters1.vector4Value = p1;
            }

            return changed;
        }

        private static bool HandleScreenProcessDropShadowViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            Vector4 p0 = parameters0.vector4Value;
            Vector2 origin = new Vector2(0.5f, 0.5f);
            float angle = p0.y;
            float distance = p0.x;
            bool changed = PostProcessScreenSpaceControlTemplates.HandleDirectionDistance(
                viewRect,
                evt,
                target,
                "Adjust ScreenProcess Drop Shadow In View",
                ref ScreenProcessDirectionDistanceViewControl.ActiveHandle,
                ref origin,
                ref angle,
                ref distance,
                0.0f,
                1.0f,
                0.35f,
                false,
                true,
                "\u6295\u5f71  D \u65b9\u5411/\u8ddd\u79bb  Esc \u9000\u51fa");
            if (changed)
            {
                p0.x = distance;
                p0.y = Mathf.DeltaAngle(0.0f, angle);
                parameters0.vector4Value = p0;
            }

            return changed;
        }

        private static bool HandleScreenProcessPostLightingDirectionViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
        {
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            if (parameters1 == null || parameters1.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            Vector4 p1 = parameters1.vector4Value;
            Vector2 origin = new Vector2(0.5f, 0.5f);
            float angle = p1.x;
            float distance = 1.0f;
            bool changed = PostProcessScreenSpaceControlTemplates.HandleDirectionDistance(
                viewRect,
                evt,
                target,
                "Adjust ScreenProcess Post Lighting Direction In View",
                ref ScreenProcessDirectionDistanceViewControl.ActiveHandle,
                ref origin,
                ref angle,
                ref distance,
                0.0f,
                1.0f,
                0.32f,
                false,
                false,
                "后期打光  D 方向  Esc 退出");
            if (changed)
            {
                p1.x = Mathf.DeltaAngle(0.0f, angle);
                parameters1.vector4Value = p1;
            }

            return changed;
        }

        private static bool HandleScreenProcessPostLightingCenterViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
        {
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            if (parameters2 == null || parameters2.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            Vector4 p2 = parameters2.vector4Value;
            Vector2 center = new Vector2(p2.x, p2.y);
            float radius = p2.z;
            bool changed = PostProcessScreenSpaceControlTemplates.HandleCenterRadius(
                viewRect,
                evt,
                target,
                "Adjust ScreenProcess Post Lighting Center In View",
                ref ScreenProcessCenterRadiusViewControl.ActiveHandle,
                ref center,
                ref radius,
                0.01f,
                1.5f,
                "后期打光  C 中心  R 半径  Esc 退出");
            if (changed)
            {
                p2.x = center.x;
                p2.y = center.y;
                p2.z = radius;
                parameters2.vector4Value = p2;
            }

            return changed;
        }
    }
}
