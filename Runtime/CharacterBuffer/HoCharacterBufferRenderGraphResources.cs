#pragma warning disable CS0618, CS0672

using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.CharacterBuffer
{
    /// <summary>
    /// 消费者取句柄的入口（与 GeometryBuffer 同形）：生产端发布纹理，消费端
    /// <c>frameData.GetOrCreate&lt;HoCharacterBufferRenderGraphResources&gt;()</c> 后按需 <c>UseTexture(..., AccessFlags.Read)</c>。
    /// <para>
    /// **CB 只有身份与覆盖率**（决策 20）：<c>HasRequiredTextures</c> 要求 ID 层与覆盖率，
    /// 选择层按需。表面色与材质数值在 `Ho-SurfaceBuffer`，不在这里。
    /// </para>
    /// </summary>
    internal sealed class HoCharacterBufferRenderGraphResources : ContextItem
    {
        public TextureHandle id0Texture = TextureHandle.nullHandle;
        public TextureHandle id1Texture = TextureHandle.nullHandle;
        public TextureHandle coverageTexture = TextureHandle.nullHandle;
        public TextureHandle selectionTexture = TextureHandle.nullHandle;
        public TextureHandle depthTexture = TextureHandle.nullHandle;

        public bool HasRequiredTextures =>
            id0Texture.IsValid() && id1Texture.IsValid() && coverageTexture.IsValid();

        public override void Reset()
        {
            id0Texture = TextureHandle.nullHandle;
            id1Texture = TextureHandle.nullHandle;
            coverageTexture = TextureHandle.nullHandle;
            selectionTexture = TextureHandle.nullHandle;
            depthTexture = TextureHandle.nullHandle;
        }
    }
}
