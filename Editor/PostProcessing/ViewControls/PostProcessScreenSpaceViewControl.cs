using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal static class PostProcessScreenSpaceViewControl
    {
        private const string OverlayName = "PostProcessScreenSpaceViewControlOverlay";
        private static readonly Color HintText = new Color(0.86f, 0.9f, 0.94f, 0.9f);
        private static readonly Color HandleWhite = new Color(1.0f, 1.0f, 1.0f, 0.94f);
        private static readonly Color HandleBlack = new Color(0.0f, 0.0f, 0.0f, 0.86f);
        private static readonly Color HandleShadow = new Color(0.0f, 0.0f, 0.0f, 0.42f);

        private static Type gameViewType;
        private static EditorWindow gameView;
        private static IMGUIContainer overlay;
        private static Action<Rect, Event> onGUI;
        private static string activeOwner;
        private static GUIStyle hintLabel;

        public static bool IsActive(string owner)
        {
            return !string.IsNullOrEmpty(owner) && activeOwner == owner && onGUI != null;
        }

        public static void Start(string owner, Action<Rect, Event> guiHandler)
        {
            if (string.IsNullOrEmpty(owner) || guiHandler == null)
            {
                return;
            }

            activeOwner = owner;
            onGUI = guiHandler;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            EnsureOverlay(true);
            RequestRepaint();
        }

        public static void Stop(string owner = null)
        {
            if (!string.IsNullOrEmpty(owner) && activeOwner != owner)
            {
                return;
            }

            activeOwner = null;
            onGUI = null;
            DetachOverlay();
            EditorApplication.update -= OnEditorUpdate;
            GUIUtility.hotControl = 0;
            RequestRepaint();
        }

        public static void RequestRepaint()
        {
            EditorWindow view = FindGameView(false);
            if (view != null)
            {
                view.Repaint();
            }
        }

        public static bool Contains(Rect rect, Vector2 point)
        {
            return point.x >= rect.xMin && point.x <= rect.xMax && point.y >= rect.yMin && point.y <= rect.yMax;
        }

        public static Vector2 UvToGui(Vector2 uv, Rect rect)
        {
            return new Vector2(rect.x + uv.x * rect.width, rect.y + (1.0f - uv.y) * rect.height);
        }

        public static Vector2 GuiToUv(Vector2 gui, Rect rect)
        {
            return new Vector2(
                Mathf.Clamp01((gui.x - rect.x) / Mathf.Max(rect.width, 1.0f)),
                Mathf.Clamp01(1.0f - ((gui.y - rect.y) / Mathf.Max(rect.height, 1.0f))));
        }

        public static float Aspect(Rect rect)
        {
            return Mathf.Max(rect.width / Mathf.Max(rect.height, 1.0f), 0.0001f);
        }

        public static Vector2 OffsetToUvCenter(Vector2 offset)
        {
            return new Vector2(0.5f + offset.x, 0.5f + offset.y);
        }

        public static Vector2 UvCenterToOffset(Vector2 uv)
        {
            return new Vector2(uv.x - 0.5f, uv.y - 0.5f);
        }

        public static float AngleDegreesFromUvDelta(Vector2 uv, Vector2 centerUv, float visualOffsetDegrees = -90.0f)
        {
            Vector2 delta = uv - centerUv;
            if (delta.sqrMagnitude <= 0.000001f)
            {
                return 0.0f;
            }

            return Mathf.DeltaAngle(0.0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg + visualOffsetDegrees);
        }

        public static float RadiusFromUvDelta(Vector2 uv, Vector2 centerUv, float aspect, Vector2 scale)
        {
            Vector2 delta = uv - centerUv;
            Vector2 scaled = new Vector2(delta.x * aspect / Mathf.Max(scale.x, 0.0001f), delta.y / Mathf.Max(scale.y, 0.0001f));
            return scaled.magnitude;
        }

        public static Vector2 DirectionFromDegrees(float degrees, float visualOffsetDegrees = 90.0f)
        {
            float angle = (degrees + visualOffsetDegrees) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        public static bool IsNear(Vector2 mouse, Vector2 point, float radius = 16.0f)
        {
            return Vector2.Distance(mouse, point) <= radius;
        }

        public static int PickHandle(Vector2 mouse, PostProcessScreenSpaceHandle[] handles, int fallbackId = 0, float radius = 16.0f)
        {
            if (handles == null)
            {
                return fallbackId;
            }

            for (int i = handles.Length - 1; i >= 0; i--)
            {
                if (IsNear(mouse, handles[i].Position, radius))
                {
                    return handles[i].Id;
                }
            }

            return fallbackId;
        }

        public static void DrawHandleSet(Rect viewRect, Vector2 center, PostProcessScreenSpaceHandle[] handles, int activeId, string hint)
        {
            if (handles != null)
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    PostProcessScreenSpaceHandle handle = handles[i];
                    if (!handle.ConnectToCenter)
                    {
                        continue;
                    }

                    DrawDashedLine(center, handle.Position, HandleBlack, handle.LineThickness + 2.0f, 8.0f, 5.0f);
                    DrawDashedLine(center, handle.Position, HandleWhite, handle.LineThickness, 8.0f, 5.0f);
                }

                for (int i = 0; i < handles.Length; i++)
                {
                    PostProcessScreenSpaceHandle handle = handles[i];
                    DrawHandle(handle, handle.Id == activeId);
                }
            }

            DrawHint(viewRect, hint);
        }

        public static void DrawDashedLine(Vector2 start, Vector2 end, Color color, float thickness = 1.0f, float dashLength = 7.0f, float gapLength = 5.0f)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Vector2 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.001f)
            {
                return;
            }

            Vector2 direction = delta / length;
            float step = Mathf.Max(1.0f, dashLength + gapLength);
            for (float distance = 0.0f; distance < length; distance += step)
            {
                Vector2 segmentStart = start + direction * distance;
                Vector2 segmentEnd = start + direction * Mathf.Min(distance + dashLength, length);
                DrawLine(segmentStart, segmentEnd, color, thickness);
            }
        }

        public static void DrawDashedCircle(Vector2 center, float radius, Color color, float thickness = 1.0f, float dashLength = 7.0f, float gapLength = 5.0f)
        {
            if (Event.current.type != EventType.Repaint || radius <= 0.001f)
            {
                return;
            }

            float circumference = Mathf.Max(1.0f, 2.0f * Mathf.PI * radius);
            int steps = Mathf.Clamp(Mathf.CeilToInt(circumference / Mathf.Max(1.0f, dashLength + gapLength)) * 2, 24, 192);
            bool drawSegment = true;
            Vector2 previous = center + new Vector2(radius, 0.0f);
            for (int i = 1; i <= steps; i++)
            {
                float angle = ((float)i / steps) * Mathf.PI * 2.0f;
                Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (drawSegment)
                {
                    DrawLine(previous, next, color, thickness);
                }

                previous = next;
                drawSegment = !drawSegment;
            }
        }

        public static void DrawLine(Vector2 start, Vector2 end, Color color, float thickness = 2.0f)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            float length = Vector2.Distance(start, end);
            if (length <= 0.001f)
            {
                return;
            }

            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg, start);
            GUI.DrawTexture(new Rect(start.x, start.y - thickness * 0.5f, length, thickness), Texture2D.whiteTexture);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        public static void DrawHandle(PostProcessScreenSpaceHandle handle, bool active = false)
        {
            if (handle.Kind == PostProcessScreenSpaceHandleKind.Angle || handle.Kind == PostProcessScreenSpaceHandleKind.Direction)
            {
                DrawRotationHandle(handle.Position, active);
                return;
            }

            if (handle.Kind == PostProcessScreenSpaceHandleKind.HorizontalScale || handle.Kind == PostProcessScreenSpaceHandleKind.VerticalScale)
            {
                DrawScaleHandle(handle.Position, handle.Kind == PostProcessScreenSpaceHandleKind.HorizontalScale, active);
                return;
            }

            DrawBoxHandle(handle.Position, active);
        }

        public static void DrawHandle(Vector2 center, string label, Color color, bool active = false)
        {
            DrawBoxHandle(center, active);
        }

        private static void DrawBoxHandle(Vector2 center, bool active)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            float size = active ? 11.0f : 9.0f;
            Rect shadow = new Rect(center.x - size * 0.5f - 2.0f, center.y - size * 0.5f - 2.0f, size + 4.0f, size + 4.0f);
            Rect outline = new Rect(center.x - size * 0.5f - 1.0f, center.y - size * 0.5f - 1.0f, size + 2.0f, size + 2.0f);
            Rect fill = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
            EditorGUI.DrawRect(shadow, HandleShadow);
            EditorGUI.DrawRect(outline, HandleBlack);
            EditorGUI.DrawRect(fill, HandleWhite);
            if (active)
            {
                DrawLine(new Vector2(center.x - 7.0f, center.y), new Vector2(center.x + 7.0f, center.y), HandleBlack, 1.0f);
                DrawLine(new Vector2(center.x, center.y - 7.0f), new Vector2(center.x, center.y + 7.0f), HandleBlack, 1.0f);
            }
        }

        private static void DrawScaleHandle(Vector2 center, bool horizontal, bool active)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            float width = horizontal ? 15.0f : 9.0f;
            float height = horizontal ? 9.0f : 15.0f;
            if (active)
            {
                width += 2.0f;
                height += 2.0f;
            }

            Rect shadow = new Rect(center.x - width * 0.5f - 2.0f, center.y - height * 0.5f - 2.0f, width + 4.0f, height + 4.0f);
            Rect outline = new Rect(center.x - width * 0.5f - 1.0f, center.y - height * 0.5f - 1.0f, width + 2.0f, height + 2.0f);
            Rect fill = new Rect(center.x - width * 0.5f, center.y - height * 0.5f, width, height);
            EditorGUI.DrawRect(shadow, HandleShadow);
            EditorGUI.DrawRect(outline, HandleBlack);
            EditorGUI.DrawRect(fill, HandleWhite);
            if (horizontal)
            {
                DrawLine(new Vector2(center.x - 5.0f, center.y), new Vector2(center.x + 5.0f, center.y), HandleBlack, 1.0f);
            }
            else
            {
                DrawLine(new Vector2(center.x, center.y - 5.0f), new Vector2(center.x, center.y + 5.0f), HandleBlack, 1.0f);
            }
        }

        private static void DrawRotationHandle(Vector2 center, bool active)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            float radius = active ? 12.0f : 10.0f;
            DrawArc(center, radius, -210.0f, 130.0f, HandleBlack, 4.0f);
            DrawArc(center, radius, -210.0f, 130.0f, HandleWhite, 2.0f);

            float endAngle = 130.0f * Mathf.Deg2Rad;
            Vector2 tip = center + new Vector2(Mathf.Cos(endAngle), Mathf.Sin(endAngle)) * radius;
            Vector2 tangent = new Vector2(-Mathf.Sin(endAngle), Mathf.Cos(endAngle));
            Vector2 normal = new Vector2(Mathf.Cos(endAngle), Mathf.Sin(endAngle));
            DrawLine(tip, tip - tangent * 6.0f - normal * 3.0f, HandleBlack, 4.0f);
            DrawLine(tip, tip + tangent * 1.0f - normal * 7.0f, HandleBlack, 4.0f);
            DrawLine(tip, tip - tangent * 6.0f - normal * 3.0f, HandleWhite, 2.0f);
            DrawLine(tip, tip + tangent * 1.0f - normal * 7.0f, HandleWhite, 2.0f);
        }

        private static void DrawArc(Vector2 center, float radius, float startDegrees, float endDegrees, Color color, float thickness)
        {
            int steps = 16;
            Vector2 previous = center + DegreeToVector(startDegrees) * radius;
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float angle = Mathf.Lerp(startDegrees, endDegrees, t);
                Vector2 next = center + DegreeToVector(angle) * radius;
                DrawLine(previous, next, color, thickness);
                previous = next;
            }
        }

        private static Vector2 DegreeToVector(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        public static void DrawHint(Rect viewRect, string text)
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(text))
            {
                return;
            }

            GUIStyle style = HintLabel;
            Vector2 size = style.CalcSize(new GUIContent(text));
            float width = Mathf.Min(size.x + 8.0f, Mathf.Max(80.0f, viewRect.width - 24.0f));
            Rect rect = new Rect(viewRect.x + 12.0f, viewRect.yMax - 24.0f, width, 16.0f);
            Color oldColor = GUI.color;
            GUI.color = HandleBlack;
            GUI.Label(new Rect(rect.x + 1.0f, rect.y + 1.0f, rect.width, rect.height), text, style);
            GUI.color = HandleWhite;
            GUI.Label(rect, text, style);
            GUI.color = oldColor;
        }

        public static void DrawViewFrame(Rect viewRect)
        {
        }

        private static void OnEditorUpdate()
        {
            if (onGUI == null)
            {
                Stop();
                return;
            }

            EnsureOverlay(false);
        }

        private static void OnOverlayGUI()
        {
            if (onGUI == null)
            {
                return;
            }

            Rect viewRect = GetGameViewImageRect();
            if (viewRect.width <= 1.0f || viewRect.height <= 1.0f)
            {
                return;
            }

            onGUI(viewRect, Event.current);
        }

        private static void EnsureOverlay(bool openGameView)
        {
            EditorWindow view = FindGameView(openGameView);
            if (view == null)
            {
                return;
            }

            if (gameView != view)
            {
                DetachOverlay();
                gameView = view;
            }

            if (overlay == null)
            {
                overlay = new IMGUIContainer(OnOverlayGUI)
                {
                    name = OverlayName,
                    pickingMode = PickingMode.Position,
                    focusable = true
                };
                overlay.style.position = Position.Absolute;
                overlay.style.left = 0.0f;
                overlay.style.top = 0.0f;
                overlay.style.right = 0.0f;
                overlay.style.bottom = 0.0f;
            }

            if (overlay.parent != gameView.rootVisualElement)
            {
                overlay.RemoveFromHierarchy();
                gameView.rootVisualElement.Add(overlay);
                overlay.BringToFront();
                overlay.Focus();
            }
        }

        private static void DetachOverlay()
        {
            if (overlay != null)
            {
                overlay.RemoveFromHierarchy();
                overlay = null;
            }
        }

        private static EditorWindow FindGameView(bool open)
        {
            Type type = GameViewType;
            if (type == null)
            {
                return null;
            }

            if (open)
            {
                EditorWindow opened = EditorWindow.GetWindow(type);
                if (opened != null)
                {
                    opened.Focus();
                }

                return opened;
            }

            UnityEngine.Object[] views = Resources.FindObjectsOfTypeAll(type);
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] is EditorWindow window)
                {
                    return window;
                }
            }

            return null;
        }

        private static Type GameViewType
        {
            get
            {
                if (gameViewType == null)
                {
                    gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                }

                return gameViewType;
            }
        }

        private static Rect GetGameViewImageRect()
        {
            Rect fallback = overlay != null
                ? new Rect(0.0f, 0.0f, Mathf.Max(1.0f, overlay.contentRect.width), Mathf.Max(1.0f, overlay.contentRect.height))
                : new Rect(0.0f, 0.0f, Mathf.Max(1.0f, gameView.position.width), Mathf.Max(1.0f, gameView.position.height));

            if (gameView == null || gameView.rootVisualElement == null)
            {
                return fallback;
            }

            Rect best = Rect.zero;
            float bestArea = 0.0f;
            FindLargestContentElement(gameView.rootVisualElement, gameView.rootVisualElement, ref best, ref bestArea);
            Rect host = bestArea > fallback.width * fallback.height * 0.25f ? best : fallback;
            return FitRectToGameAspect(host);
        }

        private static void FindLargestContentElement(VisualElement root, VisualElement element, ref Rect best, ref float bestArea)
        {
            if (element == null || element == overlay || element.name == OverlayName || !element.visible)
            {
                return;
            }

            if (element != root)
            {
                Rect rect = ToRootRect(root, element);
                float area = rect.width * rect.height;
                if (rect.width >= 64.0f && rect.height >= 64.0f && area > bestArea)
                {
                    best = rect;
                    bestArea = area;
                }
            }

            foreach (VisualElement child in element.Children())
            {
                FindLargestContentElement(root, child, ref best, ref bestArea);
            }
        }

        private static Rect ToRootRect(VisualElement root, VisualElement element)
        {
            Rect rootWorld = root.worldBound;
            Rect elementWorld = element.worldBound;
            return new Rect(
                elementWorld.x - rootWorld.x,
                elementWorld.y - rootWorld.y,
                elementWorld.width,
                elementWorld.height);
        }

        private static Rect FitRectToGameAspect(Rect host)
        {
            Vector2 gameSize = Handles.GetMainGameViewSize();
            if (gameSize.x <= 1.0f || gameSize.y <= 1.0f || host.width <= 1.0f || host.height <= 1.0f)
            {
                return host;
            }

            float targetAspect = gameSize.x / gameSize.y;
            float hostAspect = host.width / host.height;
            if (hostAspect > targetAspect)
            {
                float width = host.height * targetAspect;
                return new Rect(host.x + (host.width - width) * 0.5f, host.y, width, host.height);
            }

            float height = host.width / targetAspect;
            return new Rect(host.x, host.y + (host.height - height) * 0.5f, host.width, height);
        }

        private static GUIStyle HintLabel
        {
            get
            {
                if (hintLabel == null)
                {
                    hintLabel = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        fontSize = 10,
                        clipping = TextClipping.Clip
                    };
                    hintLabel.normal.textColor = HintText;
                }

                return hintLabel;
            }
        }
    }
}
