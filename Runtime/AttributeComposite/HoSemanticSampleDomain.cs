using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.AttributeComposite
{
    /// <summary>Association key for producer-side semantic reduction. Camera MSAA is not the sampling authority.</summary>
    internal struct HoSemanticSampleDomain
    {
        public int cameraId, frame, width, height, slices, samples;
        public TextureDimension dimension;
        public bool dynamicScale;
        public Rect viewport;
        public Matrix4x4 view, projection;

        public bool IsValid => cameraId != 0 && width > 0 && height > 0 && (samples == 1 || samples == 2 || samples == 4);
        public bool Matches(HoSemanticSampleDomain other) => IsValid && other.IsValid &&
            cameraId == other.cameraId && frame == other.frame && width == other.width && height == other.height &&
            slices == other.slices && samples == other.samples && dimension == other.dimension &&
            dynamicScale == other.dynamicScale && viewport.Equals(other.viewport) && view.Equals(other.view) && projection.Equals(other.projection);
    }
}
