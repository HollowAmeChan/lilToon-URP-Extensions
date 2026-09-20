using lilToon.URP.Extensions.AttributeComposite;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    internal sealed partial class HoCharacterSpecializationPass
    {
        // 角色特化的语义源 = **AC 的 Selection 池**（规划 §9.2：AC 不给每个消费者烤图，
        // 要烤的自己用 API 烤、图记在自己名下）。这一趟只做布局转置：
        // AC 的池是 `(SemanticId, coverage)` 固定 lane，这里转成"每通道一个语义"的位平面，
        // 因为下游要在同一张图上按 texel 抽很多次（前发投影的半影、眼透的羽化）。
        //
        // 它**不再解码 OB 的身份池与部件行表**，也不再自己判"这个物体带哪些位"。
        private static void AddObjectSemanticPass(
            RenderGraph renderGraph,
            string passName,
            Material material,
            HoAttributeCompositeRenderGraphResources acResources,
            TextureHandle destinationLowTexture,
            TextureHandle destinationHighTexture)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ObjectSemanticPassData>(passName, out ObjectSemanticPassData passData, ProfilingSampler))
            {
                passData.selectionTextures = acResources.selectionTextures;
                passData.destinationLowTexture = destinationLowTexture;
                passData.destinationHighTexture = destinationHighTexture;
                passData.material = material;

                for (int i = 0; i < acResources.selectionTextures.Length; i++)
                {
                    if (acResources.selectionTextures[i].IsValid())
                    {
                        builder.UseTexture(acResources.selectionTextures[i], AccessFlags.Read);
                    }
                }

                builder.SetRenderAttachment(destinationLowTexture, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(destinationHighTexture, 1, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ObjectSemanticPassData data, RasterGraphContext context) =>
                {
                    for (int i = 0; i < data.selectionTextures.Length; i++)
                    {
                        if (data.selectionTextures[i].IsValid())
                        {
                            context.cmd.SetGlobalTexture(HoAttributeCompositeShaderConstants.SelectionTextureIds[i], data.selectionTextures[i]);
                        }
                    }

                    Blitter.BlitTexture(context.cmd, data.selectionTextures[0], new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }
        }
    }
}
