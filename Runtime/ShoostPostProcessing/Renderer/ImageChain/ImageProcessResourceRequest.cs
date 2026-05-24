namespace lilToon.URP.Extensions.PostProcessing
{
    internal readonly struct ImageProcessResourceRequest
    {
        public readonly ImageProcessResourceKind Kind;
        public readonly string Reason;

        public ImageProcessResourceRequest(ImageProcessResourceKind kind, string reason)
        {
            Kind = kind;
            Reason = reason;
        }

        public bool IsSemanticInput =>
            Kind == ImageProcessResourceKind.AovInput ||
            Kind == ImageProcessResourceKind.MaterialBuffer ||
            Kind == ImageProcessResourceKind.GeometryBuffer ||
            Kind == ImageProcessResourceKind.ShadowCast;
    }
}
