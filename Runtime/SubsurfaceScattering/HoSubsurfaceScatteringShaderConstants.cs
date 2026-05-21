using UnityEngine;

namespace lilToon.URP.Extensions.SubsurfaceScattering
{
    internal static class HoSubsurfaceScatteringShaderConstants
    {
        public const string ShaderName = "Hidden/lilToon/URP/HoSubsurfaceScattering";
        public const string SourceTextureName = "_lilHoSSSSourceTexture";
        public const string DiffusionTextureName = "_lilHoSSSDiffusedTexture";
        public const string CompositeSourceTextureName = "_lilHoSSSCompositeSourceTexture";

        public static readonly int SourceTextureId = Shader.PropertyToID(SourceTextureName);
        public static readonly int DiffusionTextureId = Shader.PropertyToID(DiffusionTextureName);
        public static readonly int CompositeSourceTextureId = Shader.PropertyToID(CompositeSourceTextureName);
        public static readonly int ParamsId = Shader.PropertyToID("_lilHoSSSParams");
        public static readonly int GateParamsId = Shader.PropertyToID("_lilHoSSSGateParams");
        public static readonly int ColorId = Shader.PropertyToID("_lilHoSSSColor");
        public static readonly int DirectionId = Shader.PropertyToID("_lilHoSSSDirection");
        public static readonly int SourceTexelSizeId = Shader.PropertyToID("_lilHoSSSSourceTexelSize");
    }
}
