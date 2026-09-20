using lilToon.URP.Extensions.ObjectBuffer;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    internal sealed partial class HoCharacterSpecializationPass
    {
        // 角色语义位平面（规划 §1.2 的"物体位"）：OB 身份池给出「这个像素是哪些部件、各占多少覆盖率」，
        // 部件行表给出「这些部件带哪些标签」，两者一乘就是**已经是抗锯齿连续场**的语义位。
        // 消费端（合成 / 脸色扩散源 / 两条轮廓源）都只点采样这两张图，所以每帧一趟、全分辨率一次。
        //
        // 这里刻意**不做**模糊：从前那套"给二值位做 box blur 伪造抗锯齿"的前提（MetadataBuffer 的
        // objectCustom 是单采样 0/1 位）在 OB 口径下不成立 —— 覆盖率本身就是 MSAA resolve 的结果，
        // 再滤波只会把已经正确的边缘搅糊（规划 §0.3.x：不再把 bit 通道当普通 UNORM 值过滤）。
        private static void AddObjectSemanticPass(
            RenderGraph renderGraph,
            string passName,
            Material material,
            TextureHandle objectBufferId0Texture,
            TextureHandle objectBufferId1Texture,
            TextureHandle objectBufferCoverageTexture,
            TextureHandle destinationLowTexture,
            TextureHandle destinationHighTexture)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ObjectSemanticPassData>(passName, out ObjectSemanticPassData passData, ProfilingSampler))
            {
                passData.objectBufferId0Texture = objectBufferId0Texture;
                passData.objectBufferId1Texture = objectBufferId1Texture;
                passData.objectBufferCoverageTexture = objectBufferCoverageTexture;
                passData.destinationLowTexture = destinationLowTexture;
                passData.destinationHighTexture = destinationHighTexture;
                passData.material = material;

                builder.UseTexture(passData.objectBufferId0Texture, AccessFlags.Read);
                builder.UseTexture(passData.objectBufferId1Texture, AccessFlags.Read);
                builder.UseTexture(passData.objectBufferCoverageTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destinationLowTexture, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(destinationHighTexture, 1, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ObjectSemanticPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.Id0TextureId, data.objectBufferId0Texture);
                    context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.Id1TextureId, data.objectBufferId1Texture);
                    context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.CoverageTextureId, data.objectBufferCoverageTexture);
                    Blitter.BlitTexture(context.cmd, data.objectBufferId0Texture, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }
        }
    }
}
