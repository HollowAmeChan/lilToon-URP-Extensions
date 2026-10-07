#pragma warning disable CS0618, CS0672

using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using lilToon.URP.Extensions.AttributeComposite;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    /// <summary>
    /// 消费者取句柄的入口。对外发布六张数值图（含独立 Emission）+ owner。
    /// <list type="bullet">
    /// <item>`Color` / `Normal` / `Material` / `Reflection` / `Classification`：表面数值，采样一律 Point（SB 架构 §1.2）；</item>
    /// <item>`OwnerTexture`：与 OB 关联的 16-bit IdentityId；0 = 无有效身份，保留现有 AC 对齐规则。</item>
    /// <item>`Emission`：RGB 为独立 HDR 贡献，A 为本次表面的 writer-validity，与 OB 是否登记无关。
    /// 全局采样还须检查 Active；RenderGraph 消费须检查本相机句柄有效性并声明读依赖。</item>
    /// </list>
    /// </summary>
    internal sealed class HoSurfaceBufferRenderGraphResources : ContextItem
    {
        public TextureHandle colorTexture = TextureHandle.nullHandle;
        // RGB: dedicated emission component, linear HDR. A: surface writer (not coverage/OB identity).
        public TextureHandle emissionTexture = TextureHandle.nullHandle;
        public TextureHandle normalTexture = TextureHandle.nullHandle;
        public TextureHandle materialTexture = TextureHandle.nullHandle;
        public TextureHandle reflectionTexture = TextureHandle.nullHandle;
        public TextureHandle classificationTexture = TextureHandle.nullHandle;
        public TextureHandle ownerTexture = TextureHandle.nullHandle;

        // ---------------------------------------------------------------- 语义 lane（单采样；只给 AC）

        /// <summary>逐像素的 owner（16-bit IdentityId；两个字节）。AC 用它跟 OB 层 0 对齐。</summary>
        public TextureHandle semanticOwnerTexture = TextureHandle.nullHandle;

        /// <summary>4 张 RGBA8，每张两条 `(SemanticId, value)`：lane 0/1、2/3、4/5、6/7。</summary>
        public TextureHandle[] semanticLaneTextures =
        {
            TextureHandle.nullHandle, TextureHandle.nullHandle, TextureHandle.nullHandle, TextureHandle.nullHandle
        };

        public bool HasRequiredTextures =>
            colorTexture.IsValid()
            && emissionTexture.IsValid()
            && normalTexture.IsValid()
            && materialTexture.IsValid()
            && reflectionTexture.IsValid()
            && classificationTexture.IsValid()
            && ownerTexture.IsValid();

        /// <summary>语义 lane 是否本帧产出（关掉开关 / 平台不够时是 false；AC 必须据此回落）。</summary>
        public bool HasSemanticLanes => semanticOwnerTexture.IsValid() && semanticLaneTextures[0].IsValid();

        public TextureHandle semanticWrittenCoverageTexture = TextureHandle.nullHandle;
        public TextureHandle semanticWeightedCoverageTexture = TextureHandle.nullHandle;
        public TextureHandle semanticAssociationStatusTexture = TextureHandle.nullHandle;
        public HoSemanticSampleDomain semanticSampleDomain;
        public string semanticPrecisionStatus = "Not recorded";
        public bool HasCorrelatedSemantics => semanticSampleDomain.IsValid && semanticWrittenCoverageTexture.IsValid() &&
            semanticWeightedCoverageTexture.IsValid() && semanticAssociationStatusTexture.IsValid();

        public override void Reset()
        {
            colorTexture = TextureHandle.nullHandle;
            emissionTexture = TextureHandle.nullHandle;
            normalTexture = TextureHandle.nullHandle;
            materialTexture = TextureHandle.nullHandle;
            reflectionTexture = TextureHandle.nullHandle;
            classificationTexture = TextureHandle.nullHandle;
            ownerTexture = TextureHandle.nullHandle;
            semanticOwnerTexture = TextureHandle.nullHandle;
            semanticWrittenCoverageTexture = TextureHandle.nullHandle;
            semanticWeightedCoverageTexture = TextureHandle.nullHandle;
            semanticAssociationStatusTexture = TextureHandle.nullHandle;
            semanticSampleDomain = default;
            semanticPrecisionStatus = "Not recorded";
            for (int i = 0; i < semanticLaneTextures.Length; i++)
            {
                semanticLaneTextures[i] = TextureHandle.nullHandle;
            }
        }
    }
}
