using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal enum PostProcessCenterRadiusHandle
    {
        None = 0,
        Center = 1,
        Radius = 2
    }

    internal enum PostProcessDirectionDistanceHandle
    {
        None = 0,
        Center = 1,
        Direction = 2
    }

    internal static class PostProcessScreenSpaceControlTemplates
    {
        private static readonly Color HandleWhite = new Color(1.0f, 1.0f, 1.0f, 0.94f);
        private static readonly Color HandleBlack = new Color(0.0f, 0.0f, 0.0f, 0.86f);

        public static bool HandleCenterRadius(
            Rect viewRect,
            Event evt,
            UnityEngine.Object undoTarget,
            string undoName,
            ref int activeHandle,
            ref Vector2 centerUv,
            ref float radius,
            float minRadius,
            float maxRadius,
            string hint)
        {
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            float aspect = PostProcessScreenSpaceViewControl.Aspect(viewRect);
            centerUv = ClampUv(centerUv);
            radius = Mathf.Clamp(radius, minRadius, maxRadius);

            Vector2 centerGui = PostProcessScreenSpaceViewControl.UvToGui(centerUv, viewRect);
            Vector2 radiusUv = centerUv + new Vector2(radius / aspect, 0.0f);
            Vector2 radiusGui = PostProcessScreenSpaceViewControl.UvToGui(radiusUv, viewRect);
            PostProcessScreenSpaceHandle[] handles =
            {
                new PostProcessScreenSpaceHandle((int)PostProcessCenterRadiusHandle.Center, "C", centerGui, Color.white, PostProcessScreenSpaceHandleKind.Point, false),
                new PostProcessScreenSpaceHandle((int)PostProcessCenterRadiusHandle.Radius, "R", radiusGui, Color.white, PostProcessScreenSpaceHandleKind.Radius)
            };

            bool changed = false;
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                activeHandle = PostProcessScreenSpaceViewControl.PickHandle(evt.mousePosition, handles, (int)PostProcessCenterRadiusHandle.None);
                if (activeHandle != (int)PostProcessCenterRadiusHandle.None)
                {
                    GUIUtility.hotControl = controlId;
                    if (undoTarget != null)
                    {
                        Undo.RecordObject(undoTarget, undoName);
                    }

                    evt.Use();
                    PostProcessScreenSpaceViewControl.RequestRepaint();
                }
            }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == controlId && activeHandle != (int)PostProcessCenterRadiusHandle.None)
            {
                Vector2 uv = PostProcessScreenSpaceViewControl.GuiToUv(evt.mousePosition, viewRect);
                if (activeHandle == (int)PostProcessCenterRadiusHandle.Center)
                {
                    centerUv = ClampUv(uv);
                }
                else
                {
                    radius = Mathf.Clamp(PostProcessScreenSpaceViewControl.RadiusFromUvDelta(uv, centerUv, aspect, Vector2.one), minRadius, maxRadius);
                }

                changed = true;
                evt.Use();
                PostProcessScreenSpaceViewControl.RequestRepaint();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
            {
                GUIUtility.hotControl = 0;
                activeHandle = (int)PostProcessCenterRadiusHandle.None;
                evt.Use();
                PostProcessScreenSpaceViewControl.RequestRepaint();
            }

            centerGui = PostProcessScreenSpaceViewControl.UvToGui(centerUv, viewRect);
            radiusUv = centerUv + new Vector2(radius / aspect, 0.0f);
            radiusGui = PostProcessScreenSpaceViewControl.UvToGui(radiusUv, viewRect);
            handles[0].Position = centerGui;
            handles[1].Position = radiusGui;
            DrawCenterRadius(viewRect, centerGui, Mathf.Abs(radiusGui.x - centerGui.x), handles, activeHandle, hint);
            return changed;
        }

        public static bool HandleDirectionDistance(
            Rect viewRect,
            Event evt,
            UnityEngine.Object undoTarget,
            string undoName,
            ref int activeHandle,
            ref Vector2 originUv,
            ref float angleDegrees,
            ref float distance,
            float minDistance,
            float maxDistance,
            float visualDistanceScale,
            bool allowMoveOrigin,
            bool allowEditDistance,
            string hint)
        {
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            originUv = ClampUv(originUv);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            visualDistanceScale = Mathf.Max(visualDistanceScale, 0.0001f);

            Vector2 direction = PostProcessScreenSpaceViewControl.DirectionFromDegrees(angleDegrees, 0.0f);
            float visualDistance = allowEditDistance ? distance * visualDistanceScale : visualDistanceScale;
            Vector2 originGui = PostProcessScreenSpaceViewControl.UvToGui(originUv, viewRect);
            Vector2 directionGui = PostProcessScreenSpaceViewControl.UvToGui(originUv + direction * visualDistance, viewRect);
            PostProcessScreenSpaceHandle[] handles = allowMoveOrigin
                ? new[]
                {
                    new PostProcessScreenSpaceHandle((int)PostProcessDirectionDistanceHandle.Center, "C", originGui, Color.white, PostProcessScreenSpaceHandleKind.Point, false),
                    new PostProcessScreenSpaceHandle((int)PostProcessDirectionDistanceHandle.Direction, "D", directionGui, Color.white, PostProcessScreenSpaceHandleKind.Direction)
                }
                : new[]
                {
                    new PostProcessScreenSpaceHandle((int)PostProcessDirectionDistanceHandle.Direction, "D", directionGui, Color.white, PostProcessScreenSpaceHandleKind.Direction)
                };

            bool changed = false;
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                activeHandle = PostProcessScreenSpaceViewControl.PickHandle(evt.mousePosition, handles, (int)PostProcessDirectionDistanceHandle.None);
                if (activeHandle != (int)PostProcessDirectionDistanceHandle.None)
                {
                    GUIUtility.hotControl = controlId;
                    if (undoTarget != null)
                    {
                        Undo.RecordObject(undoTarget, undoName);
                    }

                    evt.Use();
                    PostProcessScreenSpaceViewControl.RequestRepaint();
                }
            }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == controlId && activeHandle != (int)PostProcessDirectionDistanceHandle.None)
            {
                Vector2 uv = PostProcessScreenSpaceViewControl.GuiToUv(evt.mousePosition, viewRect);
                if (activeHandle == (int)PostProcessDirectionDistanceHandle.Center && allowMoveOrigin)
                {
                    originUv = ClampUv(uv);
                }
                else
                {
                    Vector2 delta = uv - originUv;
                    if (delta.sqrMagnitude > 0.000001f)
                    {
                        angleDegrees = PostProcessScreenSpaceViewControl.AngleDegreesFromUvDelta(uv, originUv, 0.0f);
                        if (allowEditDistance)
                        {
                            distance = Mathf.Clamp(delta.magnitude / visualDistanceScale, minDistance, maxDistance);
                        }
                    }
                }

                changed = true;
                evt.Use();
                PostProcessScreenSpaceViewControl.RequestRepaint();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
            {
                GUIUtility.hotControl = 0;
                activeHandle = (int)PostProcessDirectionDistanceHandle.None;
                evt.Use();
                PostProcessScreenSpaceViewControl.RequestRepaint();
            }

            direction = PostProcessScreenSpaceViewControl.DirectionFromDegrees(angleDegrees, 0.0f);
            visualDistance = allowEditDistance ? distance * visualDistanceScale : visualDistanceScale;
            originGui = PostProcessScreenSpaceViewControl.UvToGui(originUv, viewRect);
            directionGui = PostProcessScreenSpaceViewControl.UvToGui(originUv + direction * visualDistance, viewRect);
            if (allowMoveOrigin)
            {
                handles[0].Position = originGui;
                handles[1].Position = directionGui;
            }
            else
            {
                handles[0].Position = directionGui;
            }

            DrawDirectionDistance(viewRect, originGui, handles, activeHandle, hint);
            return changed;
        }

        private static void DrawCenterRadius(Rect viewRect, Vector2 center, float radiusGui, PostProcessScreenSpaceHandle[] handles, int activeHandle, string hint)
        {
            PostProcessScreenSpaceViewControl.DrawDashedCircle(center, radiusGui, HandleBlack, 3.0f, 8.0f, 5.0f);
            PostProcessScreenSpaceViewControl.DrawDashedCircle(center, radiusGui, HandleWhite, 1.0f, 8.0f, 5.0f);
            PostProcessScreenSpaceViewControl.DrawHandleSet(viewRect, center, handles, activeHandle, hint);
        }

        private static void DrawDirectionDistance(Rect viewRect, Vector2 origin, PostProcessScreenSpaceHandle[] handles, int activeHandle, string hint)
        {
            PostProcessScreenSpaceViewControl.DrawHandleSet(viewRect, origin, handles, activeHandle, hint);
        }

        private static Vector2 ClampUv(Vector2 uv)
        {
            return new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
        }
    }
}
