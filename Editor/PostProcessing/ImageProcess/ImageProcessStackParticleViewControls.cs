using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private const float ParticleDirectionVisualScale = 0.25f;
        private const float ParticleFadeDirectionalRangeVisualScale = 0.20f;
        private const float ParticleClampedHandleMargin = 14.0f;

        private static PostProcessLayerViewControlSession ImageProcessParticleViewControl =
            new PostProcessLayerViewControlSession("ImageProcess.Particle");

        private static ParticleViewControlMode activeParticleViewControlMode;

        private enum ParticleViewControlMode
        {
            None = 0,
            Spawn = 1,
            Fade = 2
        }

        private enum ParticleViewHandle
        {
            None = 0,
            SpawnCenter = 1,
            SpawnDirection = 2,
            FadeDirection = 3,
            FadeRange = 4,
            FadeSoftness = 5
        }

        private float DrawImageProcessParticleViewControlButton(Rect rect, float y, SerializedProperty element, ParticleViewControlMode mode, string label)
        {
            bool active = IsImageProcessParticleViewControlActive(element) && activeParticleViewControlMode == mode;
            if (GUI.Button(new Rect(rect.x, y, rect.width, LineHeight), active ? "\u505c\u6b62\u6e38\u620f\u89c6\u56fe\u63a7\u5236" : label))
            {
                if (active)
                {
                    activeParticleViewControlMode = ParticleViewControlMode.None;
                    ImageProcessParticleViewControl.Stop();
                }
                else if (serializedObject?.targetObject != null)
                {
                    ImageProcessCenterRadiusViewControl.Stop();
                    ImageProcessDirectionDistanceViewControl.Stop();
                    DisableGradientViewControl();
                    activeParticleViewControlMode = mode;
                    ImageProcessParticleViewControl.Start(serializedObject.targetObject, element, OnImageProcessParticleGameViewGUI);
                }
            }

            return y + LineHeight + LineSpacing;
        }

        private bool IsImageProcessParticleViewControlActive(SerializedProperty element)
        {
            return ImageProcessParticleViewControl.IsActive(serializedObject?.targetObject, element);
        }

        private static void OnImageProcessParticleGameViewGUI(Rect viewRect, Event evt)
        {
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                activeParticleViewControlMode = ParticleViewControlMode.None;
                ImageProcessParticleViewControl.Stop();
                evt.Use();
                return;
            }

            if (!ImageProcessParticleViewControl.TryGetElement(out UnityEngine.Object target, out SerializedObject so, out SerializedProperty element))
            {
                return;
            }

            if (GetEffect(element) != ImageProcessEffect.Particle)
            {
                activeParticleViewControlMode = ParticleViewControlMode.None;
                ImageProcessParticleViewControl.Stop();
                return;
            }

            bool changed = HandleImageProcessParticleViewControl(viewRect, evt, target, element);
            if (changed)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private static bool HandleImageProcessParticleViewControl(Rect viewRect, Event evt, UnityEngine.Object target, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty parameters4 = element.FindPropertyRelative("parameters4");
            SerializedProperty parameters5 = element.FindPropertyRelative("parameters5");
            SerializedProperty parameters6 = element.FindPropertyRelative("parameters6");
            SerializedProperty parameters7 = element.FindPropertyRelative("parameters7");
            SerializedProperty parameters8 = element.FindPropertyRelative("parameters8");
            SerializedProperty parameters9 = element.FindPropertyRelative("parameters9");
            EnsureParticleDefaults(parameters0, parameters1, parameters2, parameters3, parameters4, parameters5, parameters6, parameters7, parameters8, parameters9);

            bool migratedDefaults = false;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            Vector4 p8 = parameters8.vector4Value;
            Vector4 p9 = parameters9.vector4Value;
            if (p8.w < 1.5f)
            {
                p8.z = p8.z <= 0.0001f ? 1.0f : Mathf.Clamp(p8.z, 0.0f, 3.0f);
                p8.w = 2.0f;
                migratedDefaults = true;
            }
            if (p8.w < 2.5f)
            {
                Vector4 p6 = parameters6.vector4Value;
                p6.z = p6.z <= 0.0001f ? 0.65f : Mathf.Clamp01(p6.z);
                parameters6.vector4Value = p6;
                p8.w = 3.0f;
                migratedDefaults = true;
            }
            if (Mathf.Abs(p2.z) > 0.0001f || Mathf.Abs(p2.w) > 0.0001f)
            {
                p2.z = 0.0f;
                p2.w = 0.0f;
                migratedDefaults = true;
            }
            if (migratedDefaults)
            {
                parameters2.vector4Value = p2;
                parameters8.vector4Value = p8;
            }

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            float aspect = PostProcessScreenSpaceViewControl.Aspect(viewRect);
            Vector2 spawnCenterUv = new Vector2(p2.x, p2.y);
            Vector2 spawnCenterGui = PostProcessScreenSpaceViewControl.UvToGui(spawnCenterUv, viewRect);
            Vector2 fadeCenterUv = new Vector2(0.5f, 0.5f);
            Vector2 fadeCenterGui = PostProcessScreenSpaceViewControl.UvToGui(fadeCenterUv, viewRect);
            bool centerFade = Mathf.RoundToInt(p9.x) != 0;
            ParticleViewControlMode mode = activeParticleViewControlMode == ParticleViewControlMode.None ? ParticleViewControlMode.Spawn : activeParticleViewControlMode;
            PostProcessScreenSpaceHandle[] handles = BuildParticleHandles(viewRect, mode, spawnCenterUv, p1, p8, p9, centerFade);
            PostProcessScreenSpaceHandle[] displayHandles = ClampParticleHandlesToView(viewRect, handles);

            bool changed = migratedDefaults;
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                ImageProcessParticleViewControl.ActiveHandle = PostProcessScreenSpaceViewControl.PickHandle(evt.mousePosition, displayHandles, (int)ParticleViewHandle.None);
                if (ImageProcessParticleViewControl.ActiveHandle != (int)ParticleViewHandle.None)
                {
                    GUIUtility.hotControl = controlId;
                    if (target != null)
                    {
                        Undo.RecordObject(target, "Adjust ImageProcess Particle In View");
                    }

                    evt.Use();
                    PostProcessScreenSpaceViewControl.RequestRepaint();
                }
            }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == controlId && ImageProcessParticleViewControl.ActiveHandle != (int)ParticleViewHandle.None)
            {
                Vector2 uv = ParticleGuiToUvUnclamped(evt.mousePosition, viewRect);
                ApplyParticleViewDrag((ParticleViewHandle)ImageProcessParticleViewControl.ActiveHandle, uv, aspect, ref p1, ref p2, ref p8, ref p9);
                p2.z = 0.0f;
                p2.w = 0.0f;
                parameters1.vector4Value = p1;
                parameters2.vector4Value = p2;
                parameters8.vector4Value = p8;
                parameters9.vector4Value = p9;
                changed = true;
                evt.Use();
                PostProcessScreenSpaceViewControl.RequestRepaint();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
            {
                GUIUtility.hotControl = 0;
                ImageProcessParticleViewControl.ActiveHandle = (int)ParticleViewHandle.None;
                evt.Use();
                PostProcessScreenSpaceViewControl.RequestRepaint();
            }

            if (mode == ParticleViewControlMode.Fade)
            {
                DrawParticleFadeArea(viewRect, centerFade, p9);
            }

            PostProcessScreenSpaceViewControl.DrawHandleSet(
                viewRect,
                mode == ParticleViewControlMode.Fade ? fadeCenterGui : spawnCenterGui,
                displayHandles,
                ImageProcessParticleViewControl.ActiveHandle,
                GetParticleViewControlHint(mode, centerFade));

            return changed;
        }

        private static PostProcessScreenSpaceHandle[] BuildParticleHandles(
            Rect viewRect,
            ParticleViewControlMode mode,
            Vector2 centerUv,
            Vector4 p1,
            Vector4 p8,
            Vector4 p9,
            bool centerFade)
        {
            if (mode == ParticleViewControlMode.Fade)
            {
                return BuildParticleFadeHandles(viewRect, centerFade, p8, p9);
            }

            return BuildParticleSpawnHandles(viewRect, centerUv, p1);
        }

        private static PostProcessScreenSpaceHandle[] BuildParticleSpawnHandles(Rect viewRect, Vector2 centerUv, Vector4 p1)
        {
            Vector2 direction = PostProcessScreenSpaceViewControl.DirectionFromDegrees(p1.y, 0.0f);
            float directionDistance = Mathf.Max(p1.w * ParticleDirectionVisualScale, 0.12f);
            return new[]
            {
                new PostProcessScreenSpaceHandle((int)ParticleViewHandle.SpawnCenter, "C", PostProcessScreenSpaceViewControl.UvToGui(centerUv, viewRect), Color.white, PostProcessScreenSpaceHandleKind.Point, false),
                new PostProcessScreenSpaceHandle((int)ParticleViewHandle.SpawnDirection, "D", PostProcessScreenSpaceViewControl.UvToGui(centerUv + direction * directionDistance, viewRect), Color.white, PostProcessScreenSpaceHandleKind.Direction, true, 0.68f),
            };
        }

        private static PostProcessScreenSpaceHandle[] BuildParticleFadeHandles(Rect viewRect, bool centerFade, Vector4 p8, Vector4 p9)
        {
            Vector2 centerUv = new Vector2(0.5f, 0.5f);
            float softnessDistance = Mathf.Clamp(0.08f + Mathf.Max(p8.z, 0.0f) * 0.10f, 0.08f, 0.38f);
            if (centerFade)
            {
                float aspect = PostProcessScreenSpaceViewControl.Aspect(viewRect);
                float maxCornerDistance = Mathf.Max(new Vector2(0.5f * aspect, 0.5f).magnitude, 0.0001f);
                Vector2 centerFadeRangeUv = centerUv + new Vector2(Mathf.Max(p9.w, 0.05f) * maxCornerDistance / Mathf.Max(aspect, 0.0001f), 0.0f);
                return new[]
                {
                    new PostProcessScreenSpaceHandle((int)ParticleViewHandle.FadeRange, "R", PostProcessScreenSpaceViewControl.UvToGui(centerFadeRangeUv, viewRect), Color.white, PostProcessScreenSpaceHandleKind.Radius, true, 0.50f),
                    new PostProcessScreenSpaceHandle((int)ParticleViewHandle.FadeSoftness, "S", PostProcessScreenSpaceViewControl.UvToGui(centerUv + new Vector2(0.0f, softnessDistance), viewRect), Color.white, PostProcessScreenSpaceHandleKind.VerticalScale, true, 0.46f),
                };
            }

            Vector2 direction = PostProcessScreenSpaceViewControl.DirectionFromDegrees(p9.y, 0.0f);
            Vector2 side = new Vector2(-direction.y, direction.x);
            Vector2 fadeDirectionUv = centerUv + direction * 0.34f;
            Vector2 directionalFadeRangeUv = centerUv + side * Mathf.Clamp(Mathf.Max(p9.w, 0.05f) * ParticleFadeDirectionalRangeVisualScale, 0.04f, 0.45f);
            Vector2 softnessUv = centerUv - side * softnessDistance;
            return new[]
            {
                new PostProcessScreenSpaceHandle((int)ParticleViewHandle.FadeDirection, "F", PostProcessScreenSpaceViewControl.UvToGui(fadeDirectionUv, viewRect), Color.white, PostProcessScreenSpaceHandleKind.Direction, true, 0.50f),
                new PostProcessScreenSpaceHandle((int)ParticleViewHandle.FadeRange, "R", PostProcessScreenSpaceViewControl.UvToGui(directionalFadeRangeUv, viewRect), Color.white, PostProcessScreenSpaceHandleKind.Radius, true, 0.50f),
                new PostProcessScreenSpaceHandle((int)ParticleViewHandle.FadeSoftness, "S", PostProcessScreenSpaceViewControl.UvToGui(softnessUv, viewRect), Color.white, PostProcessScreenSpaceHandleKind.VerticalScale, true, 0.46f),
            };
        }

        private static string GetParticleViewControlHint(ParticleViewControlMode mode, bool centerFade)
        {
            if (mode == ParticleViewControlMode.Fade)
            {
                return centerFade
                    ? "Particle fade  R center fade range  S fade softness  Esc exit"
                    : "Particle fade  F fade angle  R fade range  S fade softness  Esc exit";
            }

            return "Particle direction  C center  D direction/strength  Esc exit";
        }

        private static PostProcessScreenSpaceHandle[] ClampParticleHandlesToView(Rect viewRect, PostProcessScreenSpaceHandle[] handles)
        {
            if (handles == null)
            {
                return null;
            }

            PostProcessScreenSpaceHandle[] clamped = new PostProcessScreenSpaceHandle[handles.Length];
            Rect safeRect = new Rect(
                viewRect.xMin + ParticleClampedHandleMargin,
                viewRect.yMin + ParticleClampedHandleMargin,
                Mathf.Max(1.0f, viewRect.width - ParticleClampedHandleMargin * 2.0f),
                Mathf.Max(1.0f, viewRect.height - ParticleClampedHandleMargin * 2.0f));

            for (int i = 0; i < handles.Length; i++)
            {
                PostProcessScreenSpaceHandle handle = handles[i];
                Vector2 originalPosition = handle.Position;
                Vector2 clampedPosition = new Vector2(
                    Mathf.Clamp(originalPosition.x, safeRect.xMin, safeRect.xMax),
                    Mathf.Clamp(originalPosition.y, safeRect.yMin, safeRect.yMax));

                if ((clampedPosition - originalPosition).sqrMagnitude > 0.01f)
                {
                    handle.Position = clampedPosition;
                    handle.Color = new Color(1.0f, 1.0f, 1.0f, 0.42f);
                    handle.LineAlpha = 0.28f;
                    handle.LineThickness = Mathf.Max(1.0f, handle.LineThickness * 0.75f);
                }

                clamped[i] = handle;
            }

            return clamped;
        }

        private static void ApplyParticleViewDrag(ParticleViewHandle handle, Vector2 uv, float aspect, ref Vector4 p1, ref Vector4 p2, ref Vector4 p8, ref Vector4 p9)
        {
            Vector2 spawnCenter = new Vector2(p2.x, p2.y);
            if (handle == ParticleViewHandle.SpawnCenter)
            {
                p2.x = Mathf.Clamp(uv.x, -0.5f, 1.5f);
                p2.y = Mathf.Clamp(uv.y, -0.5f, 1.5f);
                return;
            }

            if (handle == ParticleViewHandle.SpawnDirection)
            {
                Vector2 delta = uv - spawnCenter;
                if (delta.sqrMagnitude > 0.000001f)
                {
                    p1.y = PostProcessScreenSpaceViewControl.AngleDegreesFromUvDelta(uv, spawnCenter, 0.0f);
                    p1.w = Mathf.Clamp(delta.magnitude / ParticleDirectionVisualScale, 0.0f, 2.0f);
                }

                return;
            }

            Vector2 fadeCenter = new Vector2(0.5f, 0.5f);
            if (handle == ParticleViewHandle.FadeDirection)
            {
                Vector2 delta = uv - fadeCenter;
                if (delta.sqrMagnitude > 0.000001f)
                {
                    p9.x = 0.0f;
                    p9.y = PostProcessScreenSpaceViewControl.AngleDegreesFromUvDelta(uv, fadeCenter, 0.0f);
                }

                return;
            }

            if (handle == ParticleViewHandle.FadeRange)
            {
                bool centerFade = Mathf.RoundToInt(p9.x) != 0;
                Vector2 delta = uv - fadeCenter;
                if (centerFade)
                {
                    float maxCornerDistance = Mathf.Max(new Vector2(0.5f * aspect, 0.5f).magnitude, 0.0001f);
                    p9.w = Mathf.Clamp(new Vector2(delta.x * aspect, delta.y).magnitude / maxCornerDistance, 0.05f, 1.5f);
                }
                else
                {
                    Vector2 direction = PostProcessScreenSpaceViewControl.DirectionFromDegrees(p9.y, 0.0f);
                    Vector2 side = new Vector2(-direction.y, direction.x);
                    p9.w = Mathf.Clamp(Mathf.Abs(Vector2.Dot(delta, side)) / ParticleFadeDirectionalRangeVisualScale, 0.05f, 1.5f);
                }

                return;
            }

            if (handle == ParticleViewHandle.FadeSoftness)
            {
                bool centerFade = Mathf.RoundToInt(p9.x) != 0;
                Vector2 delta = uv - fadeCenter;
                float softnessDistance;
                if (centerFade)
                {
                    softnessDistance = Mathf.Abs(delta.y);
                }
                else
                {
                    Vector2 direction = PostProcessScreenSpaceViewControl.DirectionFromDegrees(p9.y, 0.0f);
                    Vector2 side = new Vector2(-direction.y, direction.x);
                    softnessDistance = Mathf.Abs(Vector2.Dot(delta, side));
                }

                p8.z = Mathf.Clamp((softnessDistance - 0.08f) / 0.10f, 0.0f, 3.0f);
                p8.w = 3.0f;
            }
        }

        private static void DrawParticleFadeArea(Rect viewRect, bool centerFade, Vector4 p9)
        {
            Vector2 centerUv = new Vector2(0.5f, 0.5f);
            Vector2 centerGui = PostProcessScreenSpaceViewControl.UvToGui(centerUv, viewRect);
            if (centerFade)
            {
                float aspect = PostProcessScreenSpaceViewControl.Aspect(viewRect);
                float maxCornerDistance = Mathf.Max(new Vector2(0.5f * aspect, 0.5f).magnitude, 0.0001f);
                Vector2 rangeGui = PostProcessScreenSpaceViewControl.UvToGui(centerUv + new Vector2(Mathf.Max(p9.w, 0.05f) * maxCornerDistance / Mathf.Max(aspect, 0.0001f), 0.0f), viewRect);
                float radius = Mathf.Abs(rangeGui.x - centerGui.x);
                PostProcessScreenSpaceViewControl.DrawDashedCircle(centerGui, radius, new Color(0.0f, 0.0f, 0.0f, 0.72f), 3.0f, 8.0f, 5.0f);
                PostProcessScreenSpaceViewControl.DrawDashedCircle(centerGui, radius, new Color(1.0f, 1.0f, 1.0f, 0.78f), 1.0f, 8.0f, 5.0f);
                return;
            }

            Vector2 direction = PostProcessScreenSpaceViewControl.DirectionFromDegrees(p9.y, 0.0f);
            Vector2 lineStart = PostProcessScreenSpaceViewControl.UvToGui(centerUv - direction * 0.46f, viewRect);
            Vector2 lineEnd = PostProcessScreenSpaceViewControl.UvToGui(centerUv + direction * 0.46f, viewRect);
            PostProcessScreenSpaceViewControl.DrawDashedLine(lineStart, lineEnd, new Color(0.0f, 0.0f, 0.0f, 0.72f), 3.0f, 8.0f, 5.0f);
            PostProcessScreenSpaceViewControl.DrawDashedLine(lineStart, lineEnd, new Color(1.0f, 1.0f, 1.0f, 0.78f), 1.0f, 8.0f, 5.0f);
        }

        private static Vector2 ParticleGuiToUvUnclamped(Vector2 gui, Rect rect)
        {
            return new Vector2(
                (gui.x - rect.x) / Mathf.Max(rect.width, 1.0f),
                1.0f - ((gui.y - rect.y) / Mathf.Max(rect.height, 1.0f)));
        }
    }
}
