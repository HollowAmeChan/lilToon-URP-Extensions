using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class HoPostProcessStackVolumeEditor
    {
        private static PostProcessLayerViewControlSession HoPostDirectionDistanceViewControl =
            new PostProcessLayerViewControlSession("HoPost.DirectionDistance");

        private float DrawHoPostDirectionDistanceViewControlButton(Rect rect, float y, SerializedProperty element)
        {
            bool active = IsHoPostDirectionDistanceViewControlActive(element);
            if (GUI.Button(new Rect(rect.x, y, rect.width, LineHeight), active ? "\u505c\u6b62\u6e38\u620f\u89c6\u56fe\u63a7\u5236" : "\u5728\u6e38\u620f\u89c6\u56fe\u4e2d\u8c03\u6574"))
            {
                if (active)
                {
                    HoPostDirectionDistanceViewControl.Stop();
                }
                else if (serializedObject?.targetObject != null)
                {
                    HoPostDirectionDistanceViewControl.Start(serializedObject.targetObject, element, OnHoPostDirectionDistanceGameViewGUI);
                }
            }

            return y + LineHeight + LineSpacing;
        }

        private bool IsHoPostDirectionDistanceViewControlActive(SerializedProperty element)
        {
            return HoPostDirectionDistanceViewControl.IsActive(serializedObject?.targetObject, element);
        }

        private void DisableHoPostLayerViewControlsForThisEditor()
        {
            if (serializedObject?.targetObject != null)
            {
                HoPostDirectionDistanceViewControl.StopIfOwnedBy(serializedObject.targetObject);
            }
        }

        private static void OnHoPostDirectionDistanceGameViewGUI(Rect viewRect, Event evt)
        {
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                HoPostDirectionDistanceViewControl.Stop();
                evt.Use();
                return;
            }

            if (!HoPostDirectionDistanceViewControl.TryGetElement(out UnityEngine.Object target, out SerializedObject so, out SerializedProperty element))
            {
                return;
            }

            bool changed = false;
            switch (GetEffect(element))
            {
                case HoPostProcessEffect.EdgeLight:
                    changed = HandleHoPostEdgeLightViewControl(viewRect, evt, target, element);
                    break;
                case HoPostProcessEffect.DropShadow:
                    changed = HandleHoPostDropShadowViewControl(viewRect, evt, target, element);
                    break;
                default:
                    HoPostDirectionDistanceViewControl.Stop();
                    return;
            }

            if (changed)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private static bool HandleHoPostEdgeLightViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
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
                "Adjust HoPost Edge Light In View",
                ref HoPostDirectionDistanceViewControl.ActiveHandle,
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

        private static bool HandleHoPostDropShadowViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
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
                "Adjust HoPost Drop Shadow In View",
                ref HoPostDirectionDistanceViewControl.ActiveHandle,
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
    }
}
