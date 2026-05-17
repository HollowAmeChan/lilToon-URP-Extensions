using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal enum PostProcessScreenSpaceHandleKind
    {
        Point,
        Radius,
        Angle,
        Direction,
        HorizontalScale,
        VerticalScale
    }

    internal struct PostProcessScreenSpaceHandle
    {
        public int Id;
        public string Label;
        public Vector2 Position;
        public Color Color;
        public PostProcessScreenSpaceHandleKind Kind;
        public bool ConnectToCenter;
        public float LineAlpha;
        public float LineThickness;

        public PostProcessScreenSpaceHandle(
            int id,
            string label,
            Vector2 position,
            Color color,
            PostProcessScreenSpaceHandleKind kind,
            bool connectToCenter = true,
            float lineAlpha = 0.56f,
            float lineThickness = 2.0f)
        {
            Id = id;
            Label = label;
            Position = position;
            Color = color;
            Kind = kind;
            ConnectToCenter = connectToCenter;
            LineAlpha = lineAlpha;
            LineThickness = lineThickness;
        }
    }
}
