using lilToon.URP.Extensions.Shadows;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    /// <summary>
    /// Camera-local surfaces at the Character Capture resolution. RGBA stores device depth,
    /// integer owner group, integer owner part and a validity bit. EyeSurface is optional.
    /// The two textures are separate from the shaded capture MRTs.
    /// </summary>
    public sealed class HoCharacterCaptureInputFrameData : ContextItem
    {
        public TextureHandle FaceSurface { get; internal set; }
        public TextureHandle EyeSurface { get; internal set; }
        // RGBA16F stores native realtime visibility, local cast, edge blend and native validity.
        public TextureHandle FaceVisibility { get; internal set; }
        public TextureHandle EyeVisibility { get; internal set; }
        public TextureHandle AdditionalVisibility { get; internal set; }
        public int AdditionalLayerCount { get; internal set; }
        internal HoAdditionalShadowFrameData AdditionalLights { get; set; }
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        public int CameraInstanceId { get; internal set; }

        public override void Reset()
        {
            FaceSurface = TextureHandle.nullHandle;
            EyeSurface = TextureHandle.nullHandle;
            FaceVisibility = TextureHandle.nullHandle;
            EyeVisibility = TextureHandle.nullHandle;
            AdditionalVisibility = TextureHandle.nullHandle;
            AdditionalLayerCount = 0;
            AdditionalLights = null;
            Width = 0;
            Height = 0;
            CameraInstanceId = 0;
        }
    }
}
