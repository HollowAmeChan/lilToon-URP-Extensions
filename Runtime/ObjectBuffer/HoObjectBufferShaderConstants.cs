using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    internal static class HoObjectBufferShaderConstants
    {
        public const string ShaderPassName = "HoObjectBuffer";
        // 表面色 / 材质数值的 pass（HoSurfaceBuffer*）属于 Ho-SurfaceBuffer，不在这里。

        public const string FallbackShaderName = "Hidden/lilToon/URP/ObjectBuffer/Fallback";
        public const string ResolveShaderName = "Hidden/lilToon/URP/ObjectBuffer/Resolve";
        public const string DebugShaderName = "Hidden/lilToon/URP/ObjectBuffer/DebugView";

        public const string ActiveName = "_HoObjectBufferActive";
        public const string ValidName = "_HoObjectBufferValid";
        public const string Id0TextureName = "_HoObjectBufferId0Texture";
        public const string Id1TextureName = "_HoObjectBufferId1Texture";
        public const string CoverageTextureName = "_HoObjectBufferCoverageTexture";
        public const string SelectionTextureName = "_HoObjectBufferSelectionTexture";
        /// <summary>部件行表（OB 架构 §1.2 的"条目表"）：全局名与文档统一，别再写成 Palette。</summary>
        public const string PartBufferName = "_HoObjectBufferEntries";
        public const string GroupBufferName = "_HoObjectBufferGroups";
        public const string SelectionBufferName = "_HoObjectBufferSelections";
        public const string PartCountName = "_HoObjectBufferPartCount";
        public const string SelectionCountName = "_HoObjectBufferSelectionCount";
        public const string SelectionLayerCountName = "_HoObjectBufferSelectionLayerCount";
        public const string RequestedSamplesName = "_HoObjectBufferRequestedSamples";
        public const string ActualSamplesName = "_HoObjectBufferActualSamples";
        public const string DebugModeName = "_HoObjectBufferDebugMode";

        public const string ResolveIdTextureMsName = "_HoObjectBufferResolveIdTextureMS";
        public const string ResolveSelectionTextureMsName = "_HoObjectBufferResolveSelectionTextureMS";
        public const string ResolveDepthTextureMsName = "_HoObjectBufferResolveDepthTextureMS";

        public const string Msaa2Keyword = "_HO_OBJECT_BUFFER_MSAA_2";
        public const string Msaa4Keyword = "_HO_OBJECT_BUFFER_MSAA_4";

        public static readonly int ActiveId = Shader.PropertyToID(ActiveName);
        public static readonly int ValidId = Shader.PropertyToID(ValidName);
        public static readonly int Id0TextureId = Shader.PropertyToID(Id0TextureName);
        public static readonly int Id1TextureId = Shader.PropertyToID(Id1TextureName);
        public static readonly int CoverageTextureId = Shader.PropertyToID(CoverageTextureName);
        public static readonly int SelectionTextureId = Shader.PropertyToID(SelectionTextureName);
        public static readonly int PartBufferId = Shader.PropertyToID(PartBufferName);
        public static readonly int GroupBufferId = Shader.PropertyToID(GroupBufferName);
        public static readonly int SelectionBufferId = Shader.PropertyToID(SelectionBufferName);
        public static readonly int PartCountId = Shader.PropertyToID(PartCountName);
        public static readonly int SelectionCountId = Shader.PropertyToID(SelectionCountName);
        public static readonly int SelectionLayerCountId = Shader.PropertyToID(SelectionLayerCountName);
        public static readonly int RequestedSamplesId = Shader.PropertyToID(RequestedSamplesName);
        public static readonly int ActualSamplesId = Shader.PropertyToID(ActualSamplesName);
        public static readonly int DebugModeId = Shader.PropertyToID(DebugModeName);
        public static readonly int ResolveIdTextureMsId = Shader.PropertyToID(ResolveIdTextureMsName);
        public static readonly int ResolveSelectionTextureMsId = Shader.PropertyToID(ResolveSelectionTextureMsName);
        public static readonly int ResolveDepthTextureMsId = Shader.PropertyToID(ResolveDepthTextureMsName);

        public static readonly ShaderTagId ShaderTagId = new ShaderTagId(ShaderPassName);
    }
}
