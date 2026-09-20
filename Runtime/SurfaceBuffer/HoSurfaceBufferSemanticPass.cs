#pragma warning disable CS0618, CS0672

using System.Collections.Generic;
using lilToon.URP.Extensions.AttributeComposite;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    /// <summary>
    /// SB 的**语义 lane pass**：材质侧 `HoSurfaceSemantic` 一趟写 owner + 8 条 lane（规划 §0.4 / §0.3.7）。
    /// <list type="bullet">
    /// <item>**与数值 pass 分开**：lane 必须保留 MSAA sample（一个像素里眼白与虹膜是两个 sample），
    /// 而数值图是单采样，附件不能混用。两趟各有一张自用 MSAA 深度。</item>
    /// <item>**逐 sample 写 `(SemanticId, value)`**：一张 RGBA8 装两条 lane；`SemanticId = 0` 是未写，
    /// `SemanticId = 声明值 &amp; value = 0` 是明确写 0。</item>
    /// <item>**SB 只覆盖 OB 语义**：材质侧先按 palette 表读自己 renderer 的物体位，只写它真有的那几位；
    /// lane → SemanticId / 物体位掩码从 `HoSemanticSchema` 按全局上传（OB/SB/AC 共用一份声明）。</item>
    /// <item>MSAA 采样数跟相机走：相机关了 AA 就是 1 sample（语义退化成逐像素，合成口径不变）。</item>
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
        private int semanticSampleCount = 1;

        /// <summary>本帧这趟有没有产出 + 实际采样数（AC 的兼容路径拿不到纹理句柄，只能问这里）。</summary>
        internal static bool LastProduced { get; private set; }

        internal static int LastActualSampleCount { get; private set; } = 1;

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
            public int sampleCount;
        }

        public HoSurfaceBufferSemanticPass()
        {
            renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public void Setup(HoSurfaceBufferSettings settings, in FilteringSettings filteringSettings, int semanticSampleCount)
        {
            this.settings = settings;
            this.filteringSettings = filteringSettings;
            this.semanticSampleCount = Mathf.Max(1, semanticSampleCount);
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
            LastActualSampleCount = 1;
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

        private static void ApplyGlobalState(CommandBuffer cmd, int sampleCount)
        {
            BuildLaneUniforms();
            cmd.SetGlobalVector(HoSurfaceBufferShaderConstants.SemanticLaneIdsId0, LaneIds[0]);
            cmd.SetGlobalVector(HoSurfaceBufferShaderConstants.SemanticLaneIdsId1, LaneIds[1]);
            cmd.SetGlobalVector(HoSurfaceBufferShaderConstants.SemanticLaneTagMasksId0, LaneTagMasks[0]);
            cmd.SetGlobalVector(HoSurfaceBufferShaderConstants.SemanticLaneTagMasksId1, LaneTagMasks[1]);
            cmd.SetGlobalFloat(HoSurfaceBufferShaderConstants.SemanticSampleCountId, Mathf.Max(1, sampleCount));
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
            // **自建 MSAA，与相机 AA 解耦**：一个像素里可能有"眼白 + 虹膜"两个 sample（规划 §0.4）。
            descriptor.msaaSamples = semanticSampleCount;
            // **bindMS 必须为真**：这几张图在 AC 的 resolve 里是按 `Texture2DMS` 声明并逐 sample Load 的，
            // 非 MSAA 绑定会让 Unity 直接把它从多重采样采样器上摘掉（"Disabling to avoid undefined behavior"）。
            // 与 OB 的 MSAA 目标同一个写法。
            descriptor.bindMS = semanticSampleCount > 1;

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
                ApplyGlobalState(cmd, semanticSampleCount);
                // 兼容路径的句柄是常驻 RTHandle，AC 那趟只认全局名 —— 在这里绑好。
                cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.SemanticOwnerTextureId, ownerTexture.nameID);
                for (int i = 0; i < laneTextures.Length; i++)
                {
                    cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.GetSemanticLaneTextureId(i), laneTextures[i].nameID);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                LastProduced = true;
                LastActualSampleCount = semanticSampleCount;

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

            // **自建 MSAA，与相机 AA 解耦**：采样数来自设置（实际值由 feature 问过平台）。
            MSAASamples msaaSamples = ToMSAASamples(semanticSampleCount);

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
                passData.sampleCount = semanticSampleCount;

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
                    context.cmd.SetGlobalFloat(HoSurfaceBufferShaderConstants.SemanticSampleCountId, (float)data.sampleCount);
                    context.cmd.SetGlobalFloat(HoSurfaceBufferShaderConstants.SemanticActiveId, 1.0f);
                    context.cmd.DrawRendererList(data.rendererList);
                });
            }

            LastProduced = true;
            LastActualSampleCount = semanticSampleCount;

            HoSurfaceBufferRenderGraphResources resources = frameData.GetOrCreate<HoSurfaceBufferRenderGraphResources>();
            resources.semanticOwnerTexture = owner;
            for (int i = 0; i < lanes.Length; i++)
            {
                resources.semanticLaneTextures[i] = lanes[i];
            }
        }

        private static MSAASamples ToMSAASamples(int sampleCount)
        {
            switch (sampleCount)
            {
                case 2:
                    return MSAASamples.MSAA2x;
                case 4:
                    return MSAASamples.MSAA4x;
                case 8:
                    return MSAASamples.MSAA8x;
                default:
                    return MSAASamples.None;
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
                // 这几张图要按 `Texture2DMS` 读（AC 的 resolve 逐 sample Load）：bindMS 必须跟采样数一致，
                // 否则会被当成非多重采样纹理、从多重采样采样器上摘掉（与 OB 的 MSAA 目标同一个写法）。
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
