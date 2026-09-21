#pragma warning disable CS0618, CS0672

using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.AttributeComposite
{
    /// <summary>
    /// 消费者取句柄的入口（与 OB / GeometryBuffer 同形）：AC 生产 <b>Selection 池</b>，
    /// 消费端 <c>frameData.GetOrCreate&lt;HoAttributeCompositeRenderGraphResources&gt;()</c> 后按需
    /// <c>UseTexture(..., AccessFlags.Read)</c>，并调 <c>HoAC_*</c> 查询（AC 架构 §0.1）。
    /// <para>
    /// **身份池与朝向只是引用**（SB 不写身份 ⇒ 不复制）：AC 把它们一并发布，消费者从这里取，
    /// 就不再直接依赖 OB 的 packing 与全局名。RenderGraph 的物理读依赖仍要各自声明。
    /// </para>
    /// </summary>
    internal sealed class HoAttributeCompositeRenderGraphResources : ContextItem
    {
        /// <summary>Selection 池：每张装 2 条 `(SemanticId, coverage)`；未产出的槽位是 nullHandle。</summary>
        public TextureHandle[] selectionTextures =
        {
            TextureHandle.nullHandle, TextureHandle.nullHandle, TextureHandle.nullHandle, TextureHandle.nullHandle
        };

        public int laneCount;

        /// <summary>引用：OB 身份池（ranked 4 层）。</summary>
        public TextureHandle identityId0Texture = TextureHandle.nullHandle;
        public TextureHandle identityId1Texture = TextureHandle.nullHandle;
        public TextureHandle identityCoverageTexture = TextureHandle.nullHandle;

        /// <summary>
        /// 引用：SB 的**数值面**（Classification 四通道 + 逐像素 owner）——`HoAC_Attribute` 的 surface 来源。
        /// AC 不复制这几张图：属性合成本轮是**读取时**做的（`valid ? surface : constant`），
        /// 只有真需要"一张合成属性图"时才落成 RT（AC 架构 §2 的 1~2 张）。
        /// </summary>
        public TextureHandle surfaceClassificationTexture = TextureHandle.nullHandle;
        public TextureHandle surfaceMaterialTexture = TextureHandle.nullHandle;
        public TextureHandle surfaceReflectionTexture = TextureHandle.nullHandle;
        public TextureHandle surfaceOwnerTexture = TextureHandle.nullHandle;

        /// <summary>SB 的数值面本帧有没有产出（没有的话 `HoAC_Attribute` 全是 constant 兜底）。</summary>
        public bool surfaceValid;

        public bool HasSelectionPool => laneCount > 0 && selectionTextures[0].IsValid();

        public bool HasIdentityPool =>
            identityId0Texture.IsValid() && identityId1Texture.IsValid() && identityCoverageTexture.IsValid();

        /// <summary>`HoAC_Attribute` 能不能拿到 surface 值（拿不到就是 constant 兜底，不是错值）。</summary>
        public bool HasSurfaceAttributes => surfaceValid && surfaceClassificationTexture.IsValid() && surfaceOwnerTexture.IsValid();

        public override void Reset()
        {
            for (int i = 0; i < selectionTextures.Length; i++)
            {
                selectionTextures[i] = TextureHandle.nullHandle;
            }

            laneCount = 0;
            identityId0Texture = TextureHandle.nullHandle;
            identityId1Texture = TextureHandle.nullHandle;
            identityCoverageTexture = TextureHandle.nullHandle;
            surfaceClassificationTexture = TextureHandle.nullHandle;
            surfaceMaterialTexture = TextureHandle.nullHandle;
            surfaceReflectionTexture = TextureHandle.nullHandle;
            surfaceOwnerTexture = TextureHandle.nullHandle;
            surfaceValid = false;
        }
    }
}
