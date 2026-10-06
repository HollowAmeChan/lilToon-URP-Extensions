#pragma warning disable CS0618, CS0672

using UnityEngine.Rendering;
using UnityEngine;
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
        public HoAttributeCompositeDemandSnapshot demand;

        /// <summary>引用：OB 身份池（ranked 4 层）。</summary>
        public TextureHandle identityId0Texture = TextureHandle.nullHandle;
        public TextureHandle identityId1Texture = TextureHandle.nullHandle;
        public TextureHandle identityCoverageTexture = TextureHandle.nullHandle;
        public TextureHandle rawIdentityId0Texture = TextureHandle.nullHandle;
        public TextureHandle rawIdentityId1Texture = TextureHandle.nullHandle;
        public TextureHandle rawIdentityCoverageTexture = TextureHandle.nullHandle;
        public TextureHandle outlineOwnerTexture = TextureHandle.nullHandle;
        public bool outlineInheritance;
        public TextureHandle[] rawSelectionTextures = { TextureHandle.nullHandle, TextureHandle.nullHandle, TextureHandle.nullHandle, TextureHandle.nullHandle };

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

        // Read-only semantic inputs for composition diagnostics. Distinct from numeric surface owner.
        public TextureHandle semanticOwnerTexture = TextureHandle.nullHandle;
        public TextureHandle[] semanticLaneTextures =
        {
            TextureHandle.nullHandle, TextureHandle.nullHandle, TextureHandle.nullHandle, TextureHandle.nullHandle
        };
        public bool semanticValid;
        public bool HasSurfaceSemantics => semanticValid && semanticOwnerTexture.IsValid() && semanticLaneTextures[0].IsValid();
        public TextureHandle semanticWrittenCoverageTexture = TextureHandle.nullHandle;
        public TextureHandle semanticWeightedCoverageTexture = TextureHandle.nullHandle;
        public TextureHandle semanticAssociationStatusTexture = TextureHandle.nullHandle;
        public bool correlatedSemantics;
        public string semanticPrecisionStatus;
        public bool HasCorrelatedSemantics => correlatedSemantics && semanticWrittenCoverageTexture.IsValid() &&
            semanticWeightedCoverageTexture.IsValid() && semanticAssociationStatusTexture.IsValid();

        // GB 引用。单采样时没有 coverage RT，以 NormalDepth.a 的有效深度作二值回退。
        public TextureHandle geometryCoverageTexture = TextureHandle.nullHandle;
        public TextureHandle geometryNormalDepthTexture = TextureHandle.nullHandle;
        public TextureHandle outlineCoverageTexture = TextureHandle.nullHandle;
        public TextureHandle outlineNormalDepthTexture = TextureHandle.nullHandle;
        public bool published;
        public bool HasGeometry => geometryNormalDepthTexture.IsValid();
        public bool HasOutline => outlineNormalDepthTexture.IsValid();
        public Vector4 InputFlags => new Vector4(HasIdentityPool ? 1 : 0, HasSelectionPool ? 1 : 0,
            HasGeometry ? 1 : 0, HasOutline ? 1 : 0);
        public Vector4 GeometryFlags => new Vector4(geometryCoverageTexture.IsValid() ? 1 : 0,
            outlineCoverageTexture.IsValid() ? 1 : 0, 0, 0);

        public string DescribeMissingInput(HoACQueryDescriptor query)
        {
            return query.DescribeMissingInput(published, InputFlags);
        }

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
            demand = null;
            identityId0Texture = TextureHandle.nullHandle;
            identityId1Texture = TextureHandle.nullHandle;
            identityCoverageTexture = TextureHandle.nullHandle;
            rawIdentityId0Texture = rawIdentityId1Texture = rawIdentityCoverageTexture = TextureHandle.nullHandle;
            outlineOwnerTexture = TextureHandle.nullHandle;
            outlineInheritance = false;
            for (int i = 0; i < rawSelectionTextures.Length; ++i) rawSelectionTextures[i] = TextureHandle.nullHandle;
            surfaceClassificationTexture = TextureHandle.nullHandle;
            surfaceMaterialTexture = TextureHandle.nullHandle;
            surfaceReflectionTexture = TextureHandle.nullHandle;
            surfaceOwnerTexture = TextureHandle.nullHandle;
            surfaceValid = false;
            semanticOwnerTexture = TextureHandle.nullHandle;
            for (int i = 0; i < semanticLaneTextures.Length; i++) semanticLaneTextures[i] = TextureHandle.nullHandle;
            semanticValid = false;
            semanticWrittenCoverageTexture = TextureHandle.nullHandle;
            semanticWeightedCoverageTexture = TextureHandle.nullHandle;
            semanticAssociationStatusTexture = TextureHandle.nullHandle;
            correlatedSemantics = false;
            semanticPrecisionStatus = null;
            geometryCoverageTexture = TextureHandle.nullHandle;
            geometryNormalDepthTexture = TextureHandle.nullHandle;
            outlineCoverageTexture = TextureHandle.nullHandle;
            outlineNormalDepthTexture = TextureHandle.nullHandle;
            published = false;
        }
    }
}
