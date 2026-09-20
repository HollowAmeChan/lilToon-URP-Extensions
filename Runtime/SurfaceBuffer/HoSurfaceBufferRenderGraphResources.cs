#pragma warning disable CS0618, CS0672

using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    /// <summary>
    /// 消费者取句柄的入口（与 OB / AC / GeometryBuffer 同形）。对外发布**五张数值图 + owner**：
    /// <list type="bullet">
    /// <item>`Color` / `Normal` / `Material` / `Reflection` / `Classification`：表面数值，采样一律 Point（规划 §1.2）；</item>
    /// <item>`OwnerTexture`：这个像素的前表面是谁（16-bit IdentityId，0 = 没有 writer）——**pixel validity 的唯一判据**：
    /// 数值图里的 0 始终是合法值，只有 owner 能区分"写了 0"和"没人写"（规划 §0.1 / §4.8）。</item>
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

        public bool HasRequiredTextures =>
            colorTexture.IsValid()
            && normalTexture.IsValid()
            && materialTexture.IsValid()
            && reflectionTexture.IsValid()
            && classificationTexture.IsValid()
            && ownerTexture.IsValid();

        public override void Reset()
        {
            colorTexture = TextureHandle.nullHandle;
            normalTexture = TextureHandle.nullHandle;
            materialTexture = TextureHandle.nullHandle;
            reflectionTexture = TextureHandle.nullHandle;
            classificationTexture = TextureHandle.nullHandle;
            ownerTexture = TextureHandle.nullHandle;
        }
    }
}
