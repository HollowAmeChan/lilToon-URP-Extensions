using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.CharacterBuffer
{
    internal static class HoCharacterBufferShaderConstants
    {
        public const string ShaderPassName = "HoCharacterBuffer";
        public const string SurfaceShaderPassName = "HoCharacterBufferSurface";

        public const string ActiveName = "_HoCharacterBufferActive";
        public const string ValidName = "_HoCharacterBufferValid";
        public const string Id0TextureName = "_HoCharacterBufferId0Texture";
        public const string Id1TextureName = "_HoCharacterBufferId1Texture";
        public const string CoverageTextureName = "_HoCharacterBufferCoverageTexture";
        public const string SurfaceTextureName = "_HoCharacterBufferSurfaceTexture";
        public const string Material0TextureName = "_HoCharacterBufferMaterial0Texture";
        public const string SelectionTextureName = "_HoCharacterBufferSelectionTexture";
        public const string PartBufferName = "_HoCharacterBufferPalette";
        public const string CharacterBufferName = "_HoCharacterBufferCharacters";
        public const string SelectionBufferName = "_HoCharacterBufferSelections";
        public const string PartCountName = "_HoCharacterBufferPartCount";
        public const string SelectionCountName = "_HoCharacterBufferSelectionCount";
        public const string SelectionLayerCountName = "_HoCharacterBufferSelectionLayerCount";

        public const string ResolveIdTextureMsName = "_HoCharacterBufferResolveIdTextureMS";
        public const string ResolveSelectionTextureMsName = "_HoCharacterBufferResolveSelectionTextureMS";
        public const string ResolveDepthTextureMsName = "_HoCharacterBufferResolveDepthTextureMS";

        public const string Msaa2Keyword = "_HO_CHARACTER_BUFFER_MSAA_2";
        public const string Msaa4Keyword = "_HO_CHARACTER_BUFFER_MSAA_4";

        public static readonly int ActiveId = Shader.PropertyToID(ActiveName);
        public static readonly int ValidId = Shader.PropertyToID(ValidName);
        public static readonly int Id0TextureId = Shader.PropertyToID(Id0TextureName);
        public static readonly int Id1TextureId = Shader.PropertyToID(Id1TextureName);
        public static readonly int CoverageTextureId = Shader.PropertyToID(CoverageTextureName);
        public static readonly int SurfaceTextureId = Shader.PropertyToID(SurfaceTextureName);
        public static readonly int Material0TextureId = Shader.PropertyToID(Material0TextureName);
        public static readonly int SelectionTextureId = Shader.PropertyToID(SelectionTextureName);
        public static readonly int PartBufferId = Shader.PropertyToID(PartBufferName);
        public static readonly int CharacterBufferId = Shader.PropertyToID(CharacterBufferName);
        public static readonly int SelectionBufferId = Shader.PropertyToID(SelectionBufferName);
        public static readonly int PartCountId = Shader.PropertyToID(PartCountName);
        public static readonly int SelectionCountId = Shader.PropertyToID(SelectionCountName);
        public static readonly int SelectionLayerCountId = Shader.PropertyToID(SelectionLayerCountName);
        public static readonly int ResolveIdTextureMsId = Shader.PropertyToID(ResolveIdTextureMsName);
        public static readonly int ResolveSelectionTextureMsId = Shader.PropertyToID(ResolveSelectionTextureMsName);
        public static readonly int ResolveDepthTextureMsId = Shader.PropertyToID(ResolveDepthTextureMsName);

        public static readonly ShaderTagId ShaderTagId = new ShaderTagId(ShaderPassName);
        public static readonly ShaderTagId SurfaceShaderTagId = new ShaderTagId(SurfaceShaderPassName);
    }
}
