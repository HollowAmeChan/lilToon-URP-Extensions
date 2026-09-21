#pragma warning disable CS0618, CS0672

using System.Collections.Generic;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    /// <summary>
    /// SB 的**语义 lane pass**：材质侧 `HoSurfaceSemantic` 一趟写 owner + 8 条 lane（SB 架构 §0.4 / §0.3.7）。
    /// <list type="bullet">
    /// <item>**与数值 pass 分开**：这一趟是 5 个 MRT（owner + 4 张 lane 图），数值那趟是 6 个，
    /// 附件集合不同，只能两趟。</item>
    /// <item>**单采样**：lane 逐像素写 `(SemanticId, value)`，一张 RGBA8 装两条 lane；`SemanticId = 0` 是未写，
    /// `SemanticId = 声明值 &amp; value = 0` 是明确写 0。逐 sample 的细分形态见 `Setup` 的说明。</item>
    /// <item>**SB 只覆盖 OB 语义**：材质侧先按 palette 表读自己 renderer 的物体位，只写它真有的那几位；
    /// lane → SemanticId / 物体位掩码从 `HoSemanticSchema` 按全局上传（OB/SB/AC 共用一份声明）。</item>
    /// </list>
    /// </summary>
    internal sealed class HoSurfaceBufferSemanticPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-SurfaceBuffer Semantic");
        private static readonly Vector4[] LaneIds = new Vector4[2];
        private static readonly Vector4[] LaneTagMasks = new Vector4[2];

        private HoSurfaceBufferSettings settings;
        private FilteringSettings filteringSettings;
        private RenderStateBlock renderStateBlock;

        /// <summary>本帧这趟有没有产出（AC 的兼容路径拿不到纹理句柄，只能问这里）。</summary>
        internal static bool LastProduced { get; private set; }

        // 兼容（非 RenderGraph）路径的常驻目标。
        private RTHandle ownerTexture;
        private readonly RTHandle[] laneTextures = new RTHandle[HoSurfaceBufferShaderConstants.SemanticLaneTextureCount];
        private RTHandle depthTexture;
        private readonly RenderTargetIdentifier[] colorIdentifiers = new RenderTargetIdentifier[HoSurfaceBufferShaderConstants.SemanticAttachmentCount];

        private sealed class SemanticPassData
        {
            public RendererListHandle rendererList;
            public TextureHandle ownerTexture;
            public TextureHandle laneTexture0;
            public TextureHandle laneTexture1;
            public TextureHandle laneTexture2;
            public TextureHandle laneTexture3;
            public TextureHandle depthTexture;
            public Vector4 laneIds0;
            public Vector4 laneIds1;
            public Vector4 laneTagMasks0;
            public Vector4 laneTagMasks1;
        }

        public HoSurfaceBufferSemanticPass()
        {
            renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public void Setup(HoSurfaceBufferSettings settings, in FilteringSettings filteringSettings)
        {
            this.settings = settings;
            this.filteringSettings = filteringSettings;
            // 语义 lane 的 shader 读 OB 身份表（`HoObjectBufferLoadPart(partId).tags`），所以 OB 的
            // palette 必须先建好并绑上。OB feature 不在这条链上（或没启用）时没人建，D3D12 会直接
            // 跳过 draw（"requires a buffer (SRV) _HoObjectBufferEntries"），D3D11 则静默读到 0。
            HoObjectBufferRegistry.EnsureBuilt();
            // **单采样**：语义 lane 这一轮按像素走。逐 sample 的细分（同一材质内部的眼白 / 虹膜）
            // 等真有消费者要时再上，而且形态必须是"SB 自己 resolve 出单采样 lane 再发布"——
            // 读端永远只读单采样：让消费者按 `Texture2DMS` + `Load` 读，坐标 / 采样数 / bindMS
            // 任何一处对不上都会静默读出邻域或旧 sample（本轮踩过：池子整片均匀、无形状）。
            renderPassEvent = settings != null ? settings.passEvent : RenderPassEvent.BeforeRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        public void Dispose()
        {
            ReleaseCompatibilityResources();
        }

        public void ReleaseCompatibilityResources()
        {
            ownerTexture?.Release();
            for (int i = 0; i < laneTextures.Length; i++)
            {
                laneTextures[i]?.Release();
                laneTextures[i] = null;
            }

            depthTexture?.Release();
            ownerTexture = null;
            depthTexture = null;
        }

        internal static void ResetGlobalState()
        {
            LastProduced = false;
            Shader.SetGlobalFloat(HoSurfaceBufferShaderConstants.SemanticActiveId, 0.0f);
        }

        /// <summary>
        /// 按 `HoSemanticSchema` 摊平成 lane → (SemanticId, 物体位掩码)。
        /// **不缓存**：schema 将来变成可编辑资产后，缓存会悄悄变旧（一次 8×8 比较，不值得冒这个风险）。
        /// </summary>
        private static void BuildLaneUniforms()
        {
            IReadOnlyList<HoSemanticEntry> declarations = HoSemanticSchema.Declarations;
            int laneCount = HoSurfaceBufferShaderConstants.SemanticLaneCount;
            for (int lane = 0; lane < laneCount; lane++)
            {
                float semanticId = 0.0f;
                for (int i = 0; i < declarations.Count; i++)
                {
                    if (declarations[i].laneIndex == lane)
                    {
                        semanticId = declarations[i].semanticId;
                        break;
                    }
                }

                float tagMask = HoSemanticSchema.ObjectTagMaskForLane(lane);
                int group = lane / 4;
                int slot = lane % 4;
                LaneIds[group][slot] = semanticId;
                LaneTagMasks[group][slot] = tagMask;
            }
        }

        private static void ApplyGlobalState(CommandBuffer cmd)
        {
            BuildLaneUniforms();
            cmd.SetGlobalVector(HoSurfaceBufferShaderConstants.SemanticLaneIdsId0, LaneIds[0]);
            cmd.SetGlobalVector(HoSurfaceBufferShaderConstants.SemanticLaneIdsId1, LaneIds[1]);
            cmd.SetGlobalVector(HoSurfaceBufferShaderConstants.SemanticLaneTagMasksId0, LaneTagMasks[0]);
            cmd.SetGlobalVector(HoSurfaceBufferShaderConstants.SemanticLaneTagMasksId1, LaneTagMasks[1]);
            cmd.SetGlobalFloat(HoSurfaceBufferShaderConstants.SemanticActiveId, 1.0f);
        }

        // ------------------------------------------------------------------ 兼容（非 RenderGraph）路径

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (settings == null || !settings.enabled || !settings.enableSemanticLanes)
            {
                return;
            }

            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            // 单采样（见 Setup 的说明）：不再按多重采样纹理绑定。
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;

            AllocateIfNeeded(ref ownerTexture, descriptor, HoSurfaceBufferFormatUtility.GetOwnerGraphicsFormat(), HoSurfaceBufferShaderConstants.SemanticOwnerTextureName);
            for (int i = 0; i < laneTextures.Length; i++)
            {
                AllocateIfNeeded(ref laneTextures[i], descriptor, HoSurfaceBufferFormatUtility.GetUnormGraphicsFormat(),
                    string.Format(HoSurfaceBufferShaderConstants.SemanticLaneTextureNameFormat, i));
            }

            RenderTextureDescriptor depthDescriptor = descriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.None;
            depthDescriptor.depthStencilFormat = HoSurfaceBufferFormatUtility.GetDepthStencilFormat(renderingData.cameraData.cameraTargetDescriptor);
            // 自用深度只当附件、没人读它 ⇒ 不按多重采样纹理绑（颜色的那几张才需要 bindMS）。
            depthDescriptor.bindMS = false;
            RenderingUtils.ReAllocateIfNeeded(ref depthTexture, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoSurfaceSemanticDepth");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            LastProduced = false;
            if (settings == null || !settings.enabled || !settings.enableSemanticLanes || ownerTexture == null || depthTexture == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                colorIdentifiers[HoSurfaceBufferShaderConstants.SemanticOwnerAttachment] = ownerTexture.nameID;
                for (int i = 0; i < laneTextures.Length; i++)
                {
                    colorIdentifiers[HoSurfaceBufferShaderConstants.SemanticLaneAttachmentBase + i] = laneTextures[i].nameID;
                }

                cmd.SetRenderTarget(colorIdentifiers, depthTexture.nameID);
                cmd.ClearRenderTarget(RTClearFlags.ColorDepth, Color.clear, 1.0f, 0);
                ApplyGlobalState(cmd);
                // 兼容路径的句柄是常驻 RTHandle，AC 那趟只认全局名 —— 在这里绑好。
                cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.SemanticOwnerTextureId, ownerTexture.nameID);
                for (int i = 0; i < laneTextures.Length; i++)
                {
                    cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.GetSemanticLaneTextureId(i), laneTextures[i].nameID);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                LastProduced = true;

                DrawingSettings drawingSettings = new DrawingSettings(HoSurfaceBufferShaderConstants.SemanticShaderTagId, new SortingSettings(renderingData.cameraData.camera) { criteria = SortingCriteria.CommonOpaque })
                {
                    perObjectData = renderingData.perObjectData,
                    enableDynamicBatching = renderingData.supportsDynamicBatching,
                    enableInstancing = true
                };

                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings, ref renderStateBlock);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        // ------------------------------------------------------------------ RenderGraph 路径

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            ReleaseCompatibilityResources();
            LastProduced = false;
            if (settings == null || !settings.enabled || !settings.enableSemanticLanes)
            {
                return;
            }

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            // 单采样（见 Setup 的说明）。
            MSAASamples msaaSamples = MSAASamples.None;

            TextureHandle owner = CreateTexture(renderGraph, cameraData.cameraTargetDescriptor, HoSurfaceBufferFormatUtility.GetOwnerGraphicsFormat(), HoSurfaceBufferShaderConstants.SemanticOwnerTextureName, msaaSamples);
            TextureHandle[] lanes = new TextureHandle[HoSurfaceBufferShaderConstants.SemanticLaneTextureCount];
            for (int i = 0; i < lanes.Length; i++)
            {
                lanes[i] = CreateTexture(renderGraph, cameraData.cameraTargetDescriptor, HoSurfaceBufferFormatUtility.GetUnormGraphicsFormat(),
                    string.Format(HoSurfaceBufferShaderConstants.SemanticLaneTextureNameFormat, i), msaaSamples);
            }

            TextureDesc depthDescriptor = new TextureDesc(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height)
            {
                name = "_HoSurfaceSemanticDepth",
                format = GraphicsFormat.None,
                depthBufferBits = DepthBits.Depth24,
                msaaSamples = msaaSamples,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useDynamicScale = cameraData.cameraTargetDescriptor.useDynamicScale,
                vrUsage = cameraData.cameraTargetDescriptor.vrUsage
            };
            TextureHandle depth = renderGraph.CreateTexture(depthDescriptor);

            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                new List<ShaderTagId> { HoSurfaceBufferShaderConstants.SemanticShaderTagId },
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonOpaque);
            RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);

            BuildLaneUniforms();

            using (var builder = renderGraph.AddRasterRenderPass<SemanticPassData>("Ho-SurfaceBuffer Semantic", out SemanticPassData passData, ProfilingSampler))
            {
                passData.rendererList = renderGraph.CreateRendererList(rendererListParams);
                passData.ownerTexture = owner;
                passData.laneTexture0 = lanes[0];
                passData.laneTexture1 = lanes[1];
                passData.laneTexture2 = lanes[2];
                passData.laneTexture3 = lanes[3];
                passData.depthTexture = depth;
                passData.laneIds0 = LaneIds[0];
                passData.laneIds1 = LaneIds[1];
                passData.laneTagMasks0 = LaneTagMasks[0];
                passData.laneTagMasks1 = LaneTagMasks[1];

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(owner, HoSurfaceBufferShaderConstants.SemanticOwnerAttachment, AccessFlags.WriteAll);
                for (int i = 0; i < lanes.Length; i++)
                {
                    builder.SetRenderAttachment(lanes[i], HoSurfaceBufferShaderConstants.SemanticLaneAttachmentBase + i, AccessFlags.WriteAll);
                }

                builder.SetRenderAttachmentDepth(depth, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(owner, HoSurfaceBufferShaderConstants.SemanticOwnerTextureId);
                for (int i = 0; i < lanes.Length; i++)
                {
                    builder.SetGlobalTextureAfterPass(lanes[i], HoSurfaceBufferShaderConstants.GetSemanticLaneTextureId(i));
                }

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (SemanticPassData data, RasterGraphContext context) =>
                {
                    // 清屏：owner 清 0（"没人写"），lane 清 0（SemanticId = 0 = 未写）。深度清成远平面。
                    context.cmd.ClearRenderTarget(RTClearFlags.ColorDepth, Color.clear, 1.0f, 0);
                    context.cmd.SetGlobalVector(HoSurfaceBufferShaderConstants.SemanticLaneIdsId0, data.laneIds0);
                    context.cmd.SetGlobalVector(HoSurfaceBufferShaderConstants.SemanticLaneIdsId1, data.laneIds1);
                    context.cmd.SetGlobalVector(HoSurfaceBufferShaderConstants.SemanticLaneTagMasksId0, data.laneTagMasks0);
                    context.cmd.SetGlobalVector(HoSurfaceBufferShaderConstants.SemanticLaneTagMasksId1, data.laneTagMasks1);
                    context.cmd.SetGlobalFloat(HoSurfaceBufferShaderConstants.SemanticActiveId, 1.0f);
                    context.cmd.DrawRendererList(data.rendererList);
                });
            }

            LastProduced = true;

            HoSurfaceBufferRenderGraphResources resources = frameData.GetOrCreate<HoSurfaceBufferRenderGraphResources>();
            resources.semanticOwnerTexture = owner;
            for (int i = 0; i < lanes.Length; i++)
            {
                resources.semanticLaneTextures[i] = lanes[i];
            }
        }

        private static TextureHandle CreateTexture(RenderGraph renderGraph, RenderTextureDescriptor cameraTextureDescriptor, GraphicsFormat format, string name, MSAASamples msaaSamples)
        {
            TextureDesc descriptor = new TextureDesc(cameraTextureDescriptor.width, cameraTextureDescriptor.height)
            {
                name = name,
                format = format,
                dimension = cameraTextureDescriptor.dimension,
                slices = cameraTextureDescriptor.volumeDepth,
                depthBufferBits = 0,
                msaaSamples = msaaSamples,
                clearBuffer = true,
                clearColor = Color.clear,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                // 单采样（`msaaSamples = None`）：`bindTextureMS` 必须是 0，否则会被当成多重采样
                // 纹理绑到非多重采样采样器上，整张贴图被摘掉（与 OB 的写法同一条纪律）。
                bindTextureMS = msaaSamples != MSAASamples.None,
                useDynamicScale = cameraTextureDescriptor.useDynamicScale,
                useDynamicScaleExplicit = cameraTextureDescriptor.useDynamicScaleExplicit,
                vrUsage = cameraTextureDescriptor.vrUsage
            };
            return renderGraph.CreateTexture(descriptor);
        }

        private static void AllocateIfNeeded(ref RTHandle target, RenderTextureDescriptor descriptor, GraphicsFormat format, string name)
        {
            RenderTextureDescriptor targetDescriptor = descriptor;
            targetDescriptor.graphicsFormat = format;
            RenderingUtils.ReAllocateIfNeeded(ref target, targetDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: name);
        }
    }
}
