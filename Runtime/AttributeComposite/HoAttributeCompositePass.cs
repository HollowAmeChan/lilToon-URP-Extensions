#pragma warning disable CS0618, CS0672

using System.Collections.Generic;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.AttributeComposite
{
    /// <summary>
    /// AC 的产出趟：`SemanticResolve`（规划 §0.5）。本轮只有 object 来源，所以这一趟就是
    /// "OB 身份池 + 部件行标签 → 固定 lane 的 `(SemanticId, coverage)`"——
    /// 也就是以前角色特化自己烤的那张位平面，收上来变成所有消费者共用的一份。
    /// <para>
    /// 它发布 <see cref="HoAttributeCompositeRenderGraphResources"/>：Selection 池 + 身份池引用。
    /// </para>
    /// </summary>
    internal sealed class HoAttributeCompositePass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-AttributeComposite");

        private HoAttributeCompositeSettings settings;
        private Material resolveMaterial;
        private ComputeBuffer laneBuffer;
        private bool catalogUploaded;

        // 兼容（非 RenderGraph）路径的常驻目标：与 OB 的兼容路径同形。
        private readonly RTHandle[] selectionTargets = new RTHandle[HoAttributeCompositeShaderConstants.SelectionTexturesPerResolve];
        private readonly RenderTargetIdentifier[] selectionIdentifiers = new RenderTargetIdentifier[HoAttributeCompositeShaderConstants.SelectionTexturesPerResolve];

        private sealed class ResolvePassData
        {
            public TextureHandle identityId0Texture;
            public TextureHandle identityId1Texture;
            public TextureHandle identityCoverageTexture;
            public TextureHandle[] selectionTextures;
            public Material material;
            public int laneCount;

            /// <summary>SB 的语义 lane（单采样）：有的话每条 lane 按 catalog 的 sourceMode 与它合成。</summary>
            public bool surfaceEnabled;
            public TextureHandle surfaceOwnerTexture;
            public TextureHandle[] surfaceLaneTextures;
        }

        public void Setup(HoAttributeCompositeSettings settings, Material resolveMaterial)
        {
            this.settings = settings;
            this.resolveMaterial = resolveMaterial;
            renderPassEvent = settings != null ? settings.passEvent : RenderPassEvent.BeforeRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        public void Dispose()
        {
            laneBuffer?.Release();
            laneBuffer = null;
            catalogUploaded = false;
            ReleaseCompatibilityTargets();
            ResetGlobalState();
        }

        /// <summary>AC 不在 renderer 里 / 被关掉时把"有没有产出"归零，免得消费者读到上一帧的绑定。</summary>
        internal static void ResetGlobalState()
        {
            Shader.SetGlobalFloat(HoAttributeCompositeShaderConstants.ActiveId, 0.0f);
            Shader.SetGlobalFloat(HoAttributeCompositeShaderConstants.LaneCountId, 0.0f);
        }

        // ------------------------------------------------------------------ 兼容（非 RenderGraph）路径

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (settings == null || !settings.enabled)
            {
                return;
            }

            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            descriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
            for (int i = 0; i < selectionTargets.Length; i++)
            {
                RenderingUtils.ReAllocateIfNeeded(
                    ref selectionTargets[i],
                    descriptor,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: string.Format(HoAttributeCompositeShaderConstants.SelectionTextureFormat, i));
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings == null || !settings.enabled || resolveMaterial == null || selectionTargets[0] == null)
            {
                return;
            }

            UploadLaneCatalogIfNeeded();
            int laneCount = Mathf.Min(HoSemanticSchema.LaneCount, HoSemanticSchema.ResolvedLaneCount);
            if (laneCount <= 0)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                for (int i = 0; i < selectionIdentifiers.Length; i++)
                {
                    selectionIdentifiers[i] = selectionTargets[i].nameID;
                }

                // 身份池的全局名由 OB 的兼容路径设好（它排在本趟之前）；SB 的语义 lane 同理。
                SetSurfaceKeywords(resolveMaterial, HoSurfaceBufferSemanticPass.LastProduced);

                cmd.SetRenderTarget(selectionIdentifiers, selectionTargets[0].nameID);
                cmd.SetGlobalFloat(HoAttributeCompositeShaderConstants.LaneCountId, laneCount);
                cmd.SetGlobalFloat(HoAttributeCompositeShaderConstants.ActiveId, 1.0f);
                Blitter.BlitTexture(cmd, selectionTargets[0], new Vector4(1, 1, 0, 0), resolveMaterial, 0);
                for (int i = 0; i < selectionTargets.Length; i++)
                {
                    cmd.SetGlobalTexture(HoAttributeCompositeShaderConstants.SelectionTextureIds[i], selectionTargets[i].nameID);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void ReleaseCompatibilityTargets()
        {
            for (int i = 0; i < selectionTargets.Length; i++)
            {
                selectionTargets[i]?.Release();
                selectionTargets[i] = null;
            }
        }

        // ------------------------------------------------------------------ RenderGraph 路径

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || !settings.enabled || resolveMaterial == null)
            {
                return;
            }

            HoObjectBufferRenderGraphResources objectBufferResources = frameData.GetOrCreate<HoObjectBufferRenderGraphResources>();
            HoAttributeCompositeRenderGraphResources resources = frameData.GetOrCreate<HoAttributeCompositeRenderGraphResources>();
            if (!objectBufferResources.HasRequiredTextures)
            {
                // 没有身份池就没有语义可解压：不产出、不报错（OB 自己的诊断会说为什么没有）。
                return;
            }

            // SB 的语义 lane：有就逐 sample 合成（SurfaceOverride），没有就纯物体位（ObjectOnly）。
            HoSurfaceBufferRenderGraphResources surfaceResources = frameData.GetOrCreate<HoSurfaceBufferRenderGraphResources>();
            bool surfaceEnabled = surfaceResources.HasSemanticLanes && HoSurfaceBufferSemanticPass.LastProduced;

            int laneCount = Mathf.Min(HoSemanticSchema.LaneCount, HoSemanticSchema.ResolvedLaneCount);
            if (laneCount <= 0)
            {
                return;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            TextureDesc descriptor = CreateSelectionTextureDesc(cameraData.cameraTargetDescriptor);

            var selectionTextures = new TextureHandle[HoAttributeCompositeShaderConstants.SelectionTexturesPerResolve];
            for (int i = 0; i < selectionTextures.Length; i++)
            {
                descriptor.name = string.Format(HoAttributeCompositeShaderConstants.SelectionTextureFormat, i);
                selectionTextures[i] = renderGraph.CreateTexture(descriptor);
            }

            using (var builder = renderGraph.AddRasterRenderPass<ResolvePassData>("Ho-AC Selection Resolve", out ResolvePassData passData, ProfilingSampler))
            {
                passData.identityId0Texture = objectBufferResources.id0Texture;
                passData.identityId1Texture = objectBufferResources.id1Texture;
                passData.identityCoverageTexture = objectBufferResources.coverageTexture;
                passData.selectionTextures = selectionTextures;
                passData.material = resolveMaterial;
                passData.laneCount = laneCount;
                passData.surfaceEnabled = surfaceEnabled;
                passData.surfaceOwnerTexture = surfaceResources.semanticOwnerTexture;
                passData.surfaceLaneTextures = surfaceResources.semanticLaneTextures;

                builder.UseTexture(passData.identityId0Texture, AccessFlags.Read);
                builder.UseTexture(passData.identityId1Texture, AccessFlags.Read);
                builder.UseTexture(passData.identityCoverageTexture, AccessFlags.Read);
                if (surfaceEnabled)
                {
                    // 逐 sample 合成要真的读这几张：依赖显式声明，别靠全局名"看着像有"。
                    builder.UseTexture(passData.surfaceOwnerTexture, AccessFlags.Read);
                    for (int i = 0; i < passData.surfaceLaneTextures.Length; i++)
                    {
                        builder.UseTexture(passData.surfaceLaneTextures[i], AccessFlags.Read);
                    }
                }

                for (int i = 0; i < selectionTextures.Length; i++)
                {
                    builder.SetRenderAttachment(selectionTextures[i], i, AccessFlags.WriteAll);
                }

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ResolvePassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.Id0TextureId, data.identityId0Texture);
                    context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.Id1TextureId, data.identityId1Texture);
                    context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.CoverageTextureId, data.identityCoverageTexture);
                    context.cmd.SetGlobalFloat(HoAttributeCompositeShaderConstants.LaneCountId, data.laneCount);
                    context.cmd.SetGlobalFloat(HoAttributeCompositeShaderConstants.ActiveId, 1.0f);
                    SetSurfaceKeywords(data.material, data.surfaceEnabled);
                    if (data.surfaceEnabled)
                    {
                        context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.SemanticOwnerTextureId, data.surfaceOwnerTexture);
                        for (int i = 0; i < data.surfaceLaneTextures.Length; i++)
                        {
                            context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.GetSemanticLaneTextureId(i), data.surfaceLaneTextures[i]);
                        }
                    }

                    Blitter.BlitTexture(context.cmd, data.identityId0Texture, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            PublishResources(resources, selectionTextures, laneCount, objectBufferResources, surfaceResources);
        }

        /// <summary>把 Selection 池与身份池 / SB 数值面的引用一起发布（规划 §0.1：句柄是引用，依赖各自声明）。</summary>
        private static void PublishResources(
            HoAttributeCompositeRenderGraphResources resources,
            TextureHandle[] selectionTextures,
            int laneCount,
            HoObjectBufferRenderGraphResources objectBufferResources,
            HoSurfaceBufferRenderGraphResources surfaceResources)
        {
            for (int i = 0; i < resources.selectionTextures.Length; i++)
            {
                resources.selectionTextures[i] = i < selectionTextures.Length ? selectionTextures[i] : TextureHandle.nullHandle;
            }

            resources.laneCount = laneCount;
            resources.identityId0Texture = objectBufferResources.id0Texture;
            resources.identityId1Texture = objectBufferResources.id1Texture;
            resources.identityCoverageTexture = objectBufferResources.coverageTexture;
            // `HoAC_Attribute` 的来源：SB 的数值面（Classification + owner）。语义 lane 关掉也照样发布 ——
            // 属性合成与语义 lane 是两条独立的通路。
            resources.surfaceClassificationTexture = surfaceResources.classificationTexture;
            resources.surfaceMaterialTexture = surfaceResources.materialTexture;
            resources.surfaceReflectionTexture = surfaceResources.reflectionTexture;
            resources.surfaceOwnerTexture = surfaceResources.ownerTexture;
            resources.surfaceValid = surfaceResources.HasRequiredTextures;
        }

        /// <summary>
        /// 语义合成的变体开关：关掉 = **纯物体位**（没有 SB 语义 lane 时）。
        /// 单采样，所以只有一个关键字（采样数不再需要告诉 shader）。
        /// </summary>
        private static void SetSurfaceKeywords(Material material, bool surfaceEnabled)
        {
            if (material == null)
            {
                return;
            }

            if (surfaceEnabled)
            {
                material.EnableKeyword(HoAttributeCompositeShaderConstants.SurfaceKeyword);
            }
            else
            {
                material.DisableKeyword(HoAttributeCompositeShaderConstants.SurfaceKeyword);
            }
        }

        private static TextureDesc CreateSelectionTextureDesc(RenderTextureDescriptor cameraTextureDescriptor)
        {
            var descriptor = new TextureDesc(cameraTextureDescriptor.width, cameraTextureDescriptor.height)
            {
                format = GraphicsFormat.R8G8B8A8_UNorm,
                dimension = cameraTextureDescriptor.dimension,
                slices = cameraTextureDescriptor.volumeDepth,
                depthBufferBits = 0,
                msaaSamples = MSAASamples.None,
                clearBuffer = true,
                clearColor = Color.clear,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                bindTextureMS = false,
                useDynamicScale = cameraTextureDescriptor.useDynamicScale,
                useDynamicScaleExplicit = cameraTextureDescriptor.useDynamicScaleExplicit,
                vrUsage = cameraTextureDescriptor.vrUsage
            };
            return descriptor;
        }

        /// <summary>兼容路径的常驻目标在 feature 被关掉 / 相机切换时释放。</summary>
        public void ReleaseCompatibilityResources()
        {
            ReleaseCompatibilityTargets();
        }

        /// <summary>
        /// runtime catalog → GPU（规划 §5：像素纹理每帧生产，catalog 只在变脏时重建）。
        /// 表很小（每条 16 B × ≤16），重建时上传一次即可；**按 LaneIndex 落位**，
        /// 不是按声明顺序 —— lane 是传输位，声明顺序不保证等于 lane 号。
        /// </summary>
        internal void UploadLaneCatalogIfNeeded()
        {
            if (catalogUploaded)
            {
                return;
            }

            IReadOnlyList<HoSemanticEntry> declarations = HoSemanticSchema.Declarations;
            int laneCount = 0;
            for (int i = 0; i < declarations.Count; i++)
            {
                laneCount = Mathf.Max(laneCount, declarations[i].laneIndex + 1);
            }

            laneCount = Mathf.Min(laneCount, HoSemanticSchema.MaxLanes);
            if (laneCount <= 0)
            {
                return;
            }

            if (laneBuffer == null || laneBuffer.count != laneCount)
            {
                laneBuffer?.Release();
                laneBuffer = new ComputeBuffer(laneCount, 16, ComputeBufferType.Structured);
            }

            var lanes = new uint[laneCount * 4];
            for (int i = 0; i < declarations.Count; i++)
            {
                HoSemanticEntry entry = declarations[i];
                if (entry.laneIndex < 0 || entry.laneIndex >= laneCount)
                {
                    continue;
                }

                int offset = entry.laneIndex * 4;
                lanes[offset + 0] = (uint)Mathf.Clamp(entry.semanticId, 0, 255);
                lanes[offset + 1] = (uint)Mathf.Clamp(entry.objectTagBit, 0, 31);
                lanes[offset + 2] = (uint)entry.sourceMode;
                lanes[offset + 3] = 0u;
            }

            laneBuffer.SetData(lanes);
            Shader.SetGlobalBuffer(HoAttributeCompositeShaderConstants.LaneBufferId, laneBuffer);
            Shader.SetGlobalFloat(HoAttributeCompositeShaderConstants.LaneCountId, laneCount);
            catalogUploaded = true;
        }
    }
}
