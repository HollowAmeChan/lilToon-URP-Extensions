#pragma warning disable CS0618, CS0672

using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    /// <summary>
    /// 消费者取句柄的入口（与 OB / AC / GeometryBuffer 同形）。对外发布**五张数值图 + owner**：
    /// <list type="bullet">
    /// <item>`Color` / `Normal` / `Material` / `Reflection` / `Classification`：表面数值，采样一律 Point（SB 架构 §1.2）；</item>
    /// <item>`OwnerTexture`：这个像素的前表面是谁（16-bit IdentityId，0 = 没有 writer）——**pixel validity 的唯一判据**：
    /// 数值图里的 0 始终是合法值，只有 owner 能区分"写了 0"和"没人写"（SB 架构 §0.1 / §4.8）。</item>
    /// </list>
    /// </summary>
    internal sealed class HoSurfaceBufferRenderGraphResources : ContextItem
    {
        public TextureHandle colorTexture = TextureHandle.nullHandle;
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
            && normalTexture.IsValid()
            && materialTexture.IsValid()
            && reflectionTexture.IsValid()
            && classificationTexture.IsValid()
            && ownerTexture.IsValid();

        /// <summary>语义 lane 是否本帧产出（关掉开关 / 平台不够时是 false；AC 必须据此回落）。</summary>
        public bool HasSemanticLanes => semanticOwnerTexture.IsValid() && semanticLaneTextures[0].IsValid();

        public override void Reset()
        {
            colorTexture = TextureHandle.nullHandle;
            normalTexture = TextureHandle.nullHandle;
            materialTexture = TextureHandle.nullHandle;
            reflectionTexture = TextureHandle.nullHandle;
            classificationTexture = TextureHandle.nullHandle;
            ownerTexture = TextureHandle.nullHandle;
            semanticOwnerTexture = TextureHandle.nullHandle;
            for (int i = 0; i < semanticLaneTextures.Length; i++)
            {
                semanticLaneTextures[i] = TextureHandle.nullHandle;
            }
        }
    }
}
