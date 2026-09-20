#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    /// <summary>
    /// SB 的数值 pass：材质侧 `HoSurfaceBuffer` pass 一次写五张数值图 + owner（规划 §1 / §0.1）。
    /// <list type="bullet">
    /// <item>**自用深度**：不发布深度、也不读别人的深度（规划 §1.2）；opaque/cutout 写深度，保证"前表面"唯一。</item>
    /// <item>**透明不生产**：队列上限压在上不透明段末尾（多层透明没有唯一表面真值，<see cref="HoSurfaceBufferSettings.maxRenderQueue"/>）。</item>
    /// <item>**owner = RSUV 的低 16 bit**（与 OB 写的 `partId` 同一个值），所以 AC 能用它跟 OB 层 0 的 IdentityId 逐像素对齐。</item>
    /// </list>
    /// </summary>
    internal sealed class HoSurfaceBufferPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-SurfaceBuffer");

        private HoSurfaceBufferSettings settings;
        private FilteringSettings filteringSettings;
        private RenderStateBlock renderStateBlock;

        // 兼容（非 RenderGraph）路径的常驻目标。
        private RTHandle colorTexture;
        private RTHandle normalTexture;
        private RTHandle materialTexture;
        private RTHandle reflectionTexture;
        private RTHandle classificationTexture;
        private RTHandle ownerTexture;
        private RTHandle depthTexture;
        private readonly RenderTargetIdentifier[] colorIdentifiers = new RenderTargetIdentifier[HoSurfaceBufferShaderConstants.ValueAttachmentCount];

        private sealed class SurfacePassData
        {
            public RendererListHandle rendererList;
            public TextureHandle colorTexture;
            public TextureHandle normalTexture;
            public TextureHandle materialTexture;
            public TextureHandle reflectionTexture;
            public TextureHandle classificationTexture;
            public TextureHandle ownerTexture;
            public TextureHandle depthTexture;
        }

        public HoSurfaceBufferPass()
        {
            renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public void Setup(HoSurfaceBufferSettings settings, in FilteringSettings filteringSettings)
        {
            this.settings = settings;
            this.filteringSettings = filteringSettings;
            renderPassEvent = settings != null ? settings.passEvent : RenderPassEvent.BeforeRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        public void Dispose()
        {
            ReleaseCompatibilityResources();
        }

        public void ReleaseCompatibilityResources()
        {
            colorTexture?.Release();
            normalTexture?.Release();
            materialTexture?.Release();
            reflectionTexture?.Release();
            classificationTexture?.Release();
            ownerTexture?.Release();
            depthTexture?.Release();
            colorTexture = null;
            normalTexture = null;
            materialTexture = null;
            reflectionTexture = null;
            classificationTexture = null;
            ownerTexture = null;
            depthTexture = null;
        }

        internal static void ResetGlobalState()
        {
            Shader.SetGlobalFloat(HoSurfaceBufferShaderConstants.ActiveId, 0.0f);
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

            AllocateIfNeeded(ref colorTexture, descriptor, HoSurfaceBufferFormatUtility.GetColorGraphicsFormat(), HoSurfaceBufferShaderConstants.ColorTextureName);
            AllocateIfNeeded(ref normalTexture, descriptor, HoSurfaceBufferFormatUtility.GetUnormGraphicsFormat(), HoSurfaceBufferShaderConstants.NormalTextureName);
            AllocateIfNeeded(ref materialTexture, descriptor, HoSurfaceBufferFormatUtility.GetUnormGraphicsFormat(), HoSurfaceBufferShaderConstants.MaterialTextureName);
            AllocateIfNeeded(ref reflectionTexture, descriptor, HoSurfaceBufferFormatUtility.GetUnormGraphicsFormat(), HoSurfaceBufferShaderConstants.ReflectionTextureName);
            AllocateIfNeeded(ref classificationTexture, descriptor, HoSurfaceBufferFormatUtility.GetUnormGraphicsFormat(), HoSurfaceBufferShaderConstants.ClassificationTextureName);
            AllocateIfNeeded(ref ownerTexture, descriptor, HoSurfaceBufferFormatUtility.GetOwnerGraphicsFormat(), HoSurfaceBufferShaderConstants.OwnerTextureName);

            RenderTextureDescriptor depthDescriptor = descriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.None;
            depthDescriptor.depthStencilFormat = HoSurfaceBufferFormatUtility.GetDepthStencilFormat(renderingData.cameraData.cameraTargetDescriptor);
            RenderingUtils.ReAllocateIfNeeded(ref depthTexture, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoSurfaceBufferDepth");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings == null || !settings.enabled || colorTexture == null || depthTexture == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                colorIdentifiers[HoSurfaceBufferShaderConstants.ColorAttachment] = colorTexture.nameID;
                colorIdentifiers[HoSurfaceBufferShaderConstants.NormalAttachment] = normalTexture.nameID;
                colorIdentifiers[HoSurfaceBufferShaderConstants.MaterialAttachment] = materialTexture.nameID;
                colorIdentifiers[HoSurfaceBufferShaderConstants.ReflectionAttachment] = reflectionTexture.nameID;
                colorIdentifiers[HoSurfaceBufferShaderConstants.ClassificationAttachment] = classificationTexture.nameID;
                colorIdentifiers[HoSurfaceBufferShaderConstants.OwnerAttachment] = ownerTexture.nameID;

                cmd.SetRenderTarget(colorIdentifiers, depthTexture.nameID);
                cmd.ClearRenderTarget(RTClearFlags.ColorDepth, Color.clear, 1.0f, 0);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                DrawingSettings drawingSettings = new DrawingSettings(HoSurfaceBufferShaderConstants.ShaderTagId, new SortingSettings(renderingData.cameraData.camera) { criteria = SortingCriteria.CommonOpaque })
                {
                    perObjectData = renderingData.perObjectData,
                    enableDynamicBatching = renderingData.supportsDynamicBatching,
                    enableInstancing = true
                };

                cmd.SetGlobalFloat(HoSurfaceBufferShaderConstants.ActiveId, 1.0f);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings, ref renderStateBlock);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        // ------------------------------------------------------------------ RenderGraph 路径

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            ReleaseCompatibilityResources();
            if (settings == null || !settings.enabled)
            {
                return;
            }

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            TextureHandle color = CreateTexture(renderGraph, cameraData.cameraTargetDescriptor, HoSurfaceBufferFormatUtility.GetColorGraphicsFormat(), HoSurfaceBufferShaderConstants.ColorTextureName);
            TextureHandle normal = CreateTexture(renderGraph, cameraData.cameraTargetDescriptor, HoSurfaceBufferFormatUtility.GetUnormGraphicsFormat(), HoSurfaceBufferShaderConstants.NormalTextureName);
            TextureHandle material = CreateTexture(renderGraph, cameraData.cameraTargetDescriptor, HoSurfaceBufferFormatUtility.GetUnormGraphicsFormat(), HoSurfaceBufferShaderConstants.MaterialTextureName);
            TextureHandle reflection = CreateTexture(renderGraph, cameraData.cameraTargetDescriptor, HoSurfaceBufferFormatUtility.GetUnormGraphicsFormat(), HoSurfaceBufferShaderConstants.ReflectionTextureName);
            TextureHandle classification = CreateTexture(renderGraph, cameraData.cameraTargetDescriptor, HoSurfaceBufferFormatUtility.GetUnormGraphicsFormat(), HoSurfaceBufferShaderConstants.ClassificationTextureName);
            TextureHandle owner = CreateTexture(renderGraph, cameraData.cameraTargetDescriptor, HoSurfaceBufferFormatUtility.GetOwnerGraphicsFormat(), HoSurfaceBufferShaderConstants.OwnerTextureName);

            TextureDesc depthDescriptor = new TextureDesc(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height)
            {
                name = "_HoSurfaceBufferDepth",
                format = GraphicsFormat.None,
                depthBufferBits = DepthBits.Depth24,
                msaaSamples = MSAASamples.None,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useDynamicScale = cameraData.cameraTargetDescriptor.useDynamicScale,
                vrUsage = cameraData.cameraTargetDescriptor.vrUsage
            };
            TextureHandle depth = renderGraph.CreateTexture(depthDescriptor);

            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                new System.Collections.Generic.List<ShaderTagId> { HoSurfaceBufferShaderConstants.ShaderTagId },
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonOpaque);
            RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);

            using (var builder = renderGraph.AddRasterRenderPass<SurfacePassData>("Ho-SurfaceBuffer Surface", out SurfacePassData passData, ProfilingSampler))
            {
                passData.rendererList = renderGraph.CreateRendererList(rendererListParams);
                passData.colorTexture = color;
                passData.normalTexture = normal;
                passData.materialTexture = material;
                passData.reflectionTexture = reflection;
                passData.classificationTexture = classification;
                passData.ownerTexture = owner;
                passData.depthTexture = depth;

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(color, HoSurfaceBufferShaderConstants.ColorAttachment, AccessFlags.WriteAll);
                builder.SetRenderAttachment(normal, HoSurfaceBufferShaderConstants.NormalAttachment, AccessFlags.WriteAll);
                builder.SetRenderAttachment(material, HoSurfaceBufferShaderConstants.MaterialAttachment, AccessFlags.WriteAll);
                builder.SetRenderAttachment(reflection, HoSurfaceBufferShaderConstants.ReflectionAttachment, AccessFlags.WriteAll);
                builder.SetRenderAttachment(classification, HoSurfaceBufferShaderConstants.ClassificationAttachment, AccessFlags.WriteAll);
                builder.SetRenderAttachment(owner, HoSurfaceBufferShaderConstants.OwnerAttachment, AccessFlags.WriteAll);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(color, HoSurfaceBufferShaderConstants.ColorTextureId);
                builder.SetGlobalTextureAfterPass(normal, HoSurfaceBufferShaderConstants.NormalTextureId);
                builder.SetGlobalTextureAfterPass(material, HoSurfaceBufferShaderConstants.MaterialTextureId);
                builder.SetGlobalTextureAfterPass(reflection, HoSurfaceBufferShaderConstants.ReflectionTextureId);
                builder.SetGlobalTextureAfterPass(classification, HoSurfaceBufferShaderConstants.ClassificationTextureId);
                builder.SetGlobalTextureAfterPass(owner, HoSurfaceBufferShaderConstants.OwnerTextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (SurfacePassData data, RasterGraphContext context) =>
                {
                    // 清屏：五张数值图清 0、owner 清 0（"没人写"的唯一表示），深度清成远平面。
                    // 附件是 WriteAll，所以这里清完直接画，不需要额外的 clear pass。
                    context.cmd.ClearRenderTarget(RTClearFlags.ColorDepth, Color.clear, 1.0f, 0);
                    context.cmd.SetGlobalFloat(HoSurfaceBufferShaderConstants.ActiveId, 1.0f);
                    context.cmd.DrawRendererList(data.rendererList);
                });
            }

            HoSurfaceBufferRenderGraphResources resources = frameData.GetOrCreate<HoSurfaceBufferRenderGraphResources>();
            resources.colorTexture = color;
            resources.normalTexture = normal;
            resources.materialTexture = material;
            resources.reflectionTexture = reflection;
            resources.classificationTexture = classification;
            resources.ownerTexture = owner;

        }

        private static TextureHandle CreateTexture(RenderGraph renderGraph, RenderTextureDescriptor cameraTextureDescriptor, GraphicsFormat format, string name)
        {
            TextureDesc descriptor = new TextureDesc(cameraTextureDescriptor.width, cameraTextureDescriptor.height)
            {
                name = name,
                format = format,
                dimension = cameraTextureDescriptor.dimension,
                slices = cameraTextureDescriptor.volumeDepth,
                depthBufferBits = 0,
                // 数值图单采样：材质值属于最近的那个面（规划 §1.2），MSAA resolve 会把两个面平均掉。
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
