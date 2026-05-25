// Compatibility-mode hooks are kept for projects that still run URP's non-RenderGraph path.
#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.MetadataBuffer;
using lilToon.URP.Extensions.GeometryBuffer;

namespace lilToon.URP.Extensions.SubsurfaceScattering
{
    internal static class HoSubsurfaceScatteringProfileShaderData
    {
        private static readonly Vector4[] ProfileIds = new Vector4[HoSubsurfaceScatteringSettings.MaxProfileCount];
        private static readonly Vector4[] ProfileDiffusionParams = new Vector4[HoSubsurfaceScatteringSettings.MaxProfileCount];
        private static readonly Vector4[] ProfileTransmissionParams = new Vector4[HoSubsurfaceScatteringSettings.MaxProfileCount];
        private static readonly Vector4[] ProfileShapeParams = new Vector4[HoSubsurfaceScatteringSettings.MaxProfileCount];

        public static void Set(Material material, HoSubsurfaceScatteringSettings settings)
        {
            Build(settings);
            material.SetVectorArray(HoSubsurfaceScatteringShaderConstants.ProfileIdsId, ProfileIds);
            material.SetVectorArray(HoSubsurfaceScatteringShaderConstants.ProfileDiffusionParamsId, ProfileDiffusionParams);
            material.SetVectorArray(HoSubsurfaceScatteringShaderConstants.ProfileTransmissionParamsId, ProfileTransmissionParams);
            material.SetVectorArray(HoSubsurfaceScatteringShaderConstants.ProfileShapeParamsId, ProfileShapeParams);
        }

        private static void Build(HoSubsurfaceScatteringSettings settings)
        {
            HoSubsurfaceScatteringProfileSettings[] profiles = settings.profiles;
            for (int i = 0; i < HoSubsurfaceScatteringSettings.MaxProfileCount; i++)
            {
                HoSubsurfaceScatteringProfileSettings profile = profiles != null && i < profiles.Length ? profiles[i] : null;
                bool enabled = profile != null && profile.enabled;
                int profileId = profile != null ? profile.profileId : i + 1;
                Color diffusionColor = profile != null ? profile.diffusionColor : settings.color;
                Color transmissionColor = profile != null ? profile.transmissionColor : settings.transmissionColor;

                ProfileIds[i] = new Vector4(
                    enabled ? Mathf.Clamp(profileId, 0, 255) : -1.0f,
                    enabled ? 1.0f : 0.0f,
                    0.0f,
                    0.0f);

                ProfileDiffusionParams[i] = new Vector4(
                    PackRadius(enabled ? profile.diffusionRadius : settings.radius, 24.0f),
                    enabled ? Mathf.Clamp01(profile.sourcePreserve) : Mathf.Clamp01(settings.sourcePreserve),
                    diffusionColor.r,
                    diffusionColor.g);

                ProfileTransmissionParams[i] = new Vector4(
                    enabled ? Mathf.Clamp01(profile.transmissionStrength) : Mathf.Clamp01(settings.transmissionStrength),
                    PackRadius(enabled ? profile.transmissionRadius : settings.transmissionRadius, 24.0f),
                    transmissionColor.r,
                    transmissionColor.g);

                ProfileShapeParams[i] = new Vector4(
                    enabled ? Mathf.Max(0.0f, profile.thicknessScale) : 1.0f,
                    diffusionColor.b,
                    transmissionColor.b,
                    diffusionColor.a);
            }
        }

        private static float PackRadius(float radius, float maxRadius)
        {
            float normalized = Mathf.Clamp01(Mathf.Max(0.0f, radius) / Mathf.Max(0.0001f, maxRadius));
            return normalized * normalized * maxRadius;
        }
    }

    [DisallowMultipleRendererFeature("Ho-SubsurfaceScattering")]
    public sealed class HoSubsurfaceScatteringRendererFeature : ScriptableRendererFeature
    {
        [SerializeField, InspectorName("HoSSS 设置")]
        private HoSubsurfaceScatteringSettings settings = new HoSubsurfaceScatteringSettings();

        private readonly HoSubsurfaceScatteringRenderTargets renderTargets = new HoSubsurfaceScatteringRenderTargets();
        private HoSubsurfaceScatteringSourcePass sourcePass;
        private HoSubsurfaceScatteringBlurPass horizontalBlurPass;
        private HoSubsurfaceScatteringBlurPass verticalBlurPass;
        private HoSubsurfaceScatteringTransmissionGatherPass transmissionGatherPass;
        private HoSubsurfaceScatteringTransmissionBlurPass transmissionHorizontalBlurPass;
        private HoSubsurfaceScatteringTransmissionBlurPass transmissionVerticalBlurPass;
        private HoSubsurfaceScatteringCompositePass compositePass;
        private HoSubsurfaceScatteringDebugPass debugPass;
        private Material material;
        private Shader materialShader;
        private Material debugMaterial;
        private Shader debugMaterialShader;
        private bool warnedMissingShader;
        private bool warnedMissingDebugShader;

        public HoSubsurfaceScatteringSettings Settings => settings;

        public override void Create()
        {
            settings?.ClampPassEvents();
            sourcePass = new HoSubsurfaceScatteringSourcePass();
            horizontalBlurPass = new HoSubsurfaceScatteringBlurPass("Ho-SubsurfaceScattering BlurX", new Vector2(1.0f, 0.0f));
            verticalBlurPass = new HoSubsurfaceScatteringBlurPass("Ho-SubsurfaceScattering BlurY", new Vector2(0.0f, 1.0f));
            transmissionGatherPass = new HoSubsurfaceScatteringTransmissionGatherPass();
            transmissionHorizontalBlurPass = new HoSubsurfaceScatteringTransmissionBlurPass("Ho-SubsurfaceScattering TransmissionX", new Vector2(1.0f, 0.0f));
            transmissionVerticalBlurPass = new HoSubsurfaceScatteringTransmissionBlurPass("Ho-SubsurfaceScattering TransmissionY", new Vector2(0.0f, 1.0f));
            compositePass = new HoSubsurfaceScatteringCompositePass();
            debugPass = new HoSubsurfaceScatteringDebugPass();
        }

        private void OnValidate()
        {
            settings?.ClampPassEvents();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            settings?.ClampPassEvents();
            EnsureMaterial();
            if (material == null)
            {
                return;
            }

            sourcePass?.Setup(settings, renderer.cameraColorTargetHandle, renderTargets, material);
            horizontalBlurPass?.Setup(settings, renderTargets, material, false);
            verticalBlurPass?.Setup(settings, renderTargets, material, true);
            transmissionGatherPass?.Setup(settings, renderTargets, material);
            transmissionHorizontalBlurPass?.Setup(settings, renderTargets, material, false);
            transmissionVerticalBlurPass?.Setup(settings, renderTargets, material, true);
            compositePass?.Setup(settings, renderer.cameraColorTargetHandle, renderTargets, material);
            if (IsDebugEnabled())
            {
                EnsureDebugMaterial();
                if (debugMaterial != null)
                {
                    debugPass?.Setup(settings, renderer.cameraColorTargetHandle, renderTargets, debugMaterial);
                }
            }
            else
            {
                ReleaseDebugMaterial();
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            settings?.ClampPassEvents();
            EnsureMaterial();
            if (material == null)
            {
                return;
            }

            sourcePass?.SetupRenderGraph(settings, renderTargets, material);
            horizontalBlurPass?.SetupRenderGraph(settings, renderTargets, material, false);
            verticalBlurPass?.SetupRenderGraph(settings, renderTargets, material, true);
            transmissionGatherPass?.SetupRenderGraph(settings, renderTargets, material);
            transmissionHorizontalBlurPass?.SetupRenderGraph(settings, renderTargets, material, false);
            transmissionVerticalBlurPass?.SetupRenderGraph(settings, renderTargets, material, true);
            compositePass?.SetupRenderGraph(settings, renderTargets, material);

            renderer.EnqueuePass(sourcePass);
            renderer.EnqueuePass(horizontalBlurPass);
            renderer.EnqueuePass(verticalBlurPass);
            renderer.EnqueuePass(transmissionGatherPass);
            renderer.EnqueuePass(transmissionHorizontalBlurPass);
            renderer.EnqueuePass(transmissionVerticalBlurPass);
            renderer.EnqueuePass(compositePass);

            if (IsDebugEnabled())
            {
                EnsureDebugMaterial();
                if (debugMaterial != null)
                {
                    debugPass?.SetupRenderGraph(settings, renderTargets, debugMaterial);
                    renderer.EnqueuePass(debugPass);
                }
            }
            else
            {
                ReleaseDebugMaterial();
            }
        }

        protected override void Dispose(bool disposing)
        {
            renderTargets.Release();
            CoreUtils.Destroy(material);
            ReleaseDebugMaterial();
            sourcePass = null;
            horizontalBlurPass = null;
            verticalBlurPass = null;
            transmissionGatherPass = null;
            transmissionHorizontalBlurPass = null;
            transmissionVerticalBlurPass = null;
            compositePass = null;
            debugPass = null;
            material = null;
            materialShader = null;
        }

        private bool ShouldRender(in RenderingData renderingData)
        {
            if (settings == null || !settings.enabled)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.SceneView)
            {
                return settings.renderInSceneView;
            }

            return cameraType == CameraType.Game;
        }

        private void EnsureMaterial()
        {
            Shader shader = settings != null && settings.shader != null
                ? settings.shader
                : Shader.Find(HoSubsurfaceScatteringShaderConstants.ShaderName);

            if (material != null && materialShader == shader)
            {
                return;
            }

            CoreUtils.Destroy(material);
            material = null;
            materialShader = shader;
            if (shader == null)
            {
                if (!warnedMissingShader)
                {
                    warnedMissingShader = true;
                    Debug.LogWarning($"HoSSS 不可用：找不到着色器 '{HoSubsurfaceScatteringShaderConstants.ShaderName}'。");
                }

                return;
            }

            material = CoreUtils.CreateEngineMaterial(shader);
        }

        private bool IsDebugEnabled()
        {
            return settings != null && settings.debugMode != HoSubsurfaceScatteringDebugMode.Off;
        }

        private void EnsureDebugMaterial()
        {
            if (!IsDebugEnabled())
            {
                return;
            }

            Shader shader = Shader.Find(HoSubsurfaceScatteringShaderConstants.DebugShaderName);
            if (debugMaterial != null && debugMaterialShader == shader)
            {
                return;
            }

            CoreUtils.Destroy(debugMaterial);
            debugMaterial = null;
            debugMaterialShader = shader;
            if (shader == null)
            {
                if (!warnedMissingDebugShader)
                {
                    warnedMissingDebugShader = true;
                    Debug.LogWarning($"HoSSS debug 已跳过：找不到调试着色器 '{HoSubsurfaceScatteringShaderConstants.DebugShaderName}'。");
                }

                return;
            }

            debugMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        private void ReleaseDebugMaterial()
        {
            CoreUtils.Destroy(debugMaterial);
            debugMaterial = null;
            debugMaterialShader = null;
        }
    }

    internal sealed class HoSubsurfaceScatteringSourcePass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-SubsurfaceScattering Source");
        private RTHandle cameraColorTarget;
        private HoSubsurfaceScatteringSettings settings;
        private HoSubsurfaceScatteringRenderTargets renderTargets;
        private Material material;

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle maskIdTexture;
            public TextureHandle normalDepthTexture;
            public TextureHandle surfaceDataTexture;
            public TextureHandle sssTexture;
            public Material material;
            public Vector4 sssParams;
            public Vector4 gateParams;
            public Vector4 color;
        }

        public void Setup(
            HoSubsurfaceScatteringSettings settings,
            RTHandle cameraColorTarget,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material)
        {
            this.settings = settings;
            this.cameraColorTarget = cameraColorTarget;
            this.renderTargets = renderTargets;
            this.material = material;
            renderPassEvent = settings.GetSourceRenderPassEvent();
        }

        public void SetupRenderGraph(
            HoSubsurfaceScatteringSettings settings,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.material = material;
            renderPassEvent = settings.GetSourceRenderPassEvent();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTargets.ReAllocateIfNeeded(renderingData.cameraData.cameraTargetDescriptor, settings);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                SetMaterialProperties(material, settings, renderTargets.SourceTexture);
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, renderTargets.SourceTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, material, 0);
                cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.SourceTextureId, renderTargets.SourceTexture.nameID);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            HoMetadataBufferRenderGraphResources metadataResources = frameData.GetOrCreate<HoMetadataBufferRenderGraphResources>();
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            HoSubsurfaceScatteringRenderGraphResources sssResources = frameData.GetOrCreate<HoSubsurfaceScatteringRenderGraphResources>();
            TextureHandle cameraColor = resourceData.activeColorTexture;
            if (!cameraColor.IsValid() || !metadataResources.HasRequiredTextures || !geometryResources.HasRequiredTextures)
            {
                return;
            }

            TextureHandle source = renderGraph.CreateTexture(HoSubsurfaceScatteringRenderGraphResources.CreateDescriptor(
                cameraData.cameraTargetDescriptor,
                settings,
                HoSubsurfaceScatteringShaderConstants.SourceTextureName));
            sssResources.sourceTexture = source;
            HoSubsurfaceScatteringProfileShaderData.Set(material, settings);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-SubsurfaceScattering Source", out PassData passData, ProfilingSampler))
            {
                passData.source = cameraColor;
                passData.maskIdTexture = metadataResources.maskIdTexture;
                passData.normalDepthTexture = geometryResources.normalDepthTexture;
                passData.surfaceDataTexture = metadataResources.surfaceDataTexture;
                passData.sssTexture = metadataResources.surfaceColorTexture;
                passData.material = material;
                passData.sssParams = CreateSssParams(settings, cameraData.cameraTargetDescriptor, source.GetDescriptor(renderGraph));
                passData.gateParams = CreateGateParams(settings);
                passData.color = settings.color;

                builder.UseTexture(cameraColor, AccessFlags.Read);
                builder.UseTexture(passData.maskIdTexture, AccessFlags.Read);
                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.surfaceDataTexture, AccessFlags.Read);
                builder.UseTexture(passData.sssTexture, AccessFlags.Read);
                builder.SetRenderAttachment(source, 0, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(source, HoSubsurfaceScatteringShaderConstants.SourceTextureId);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    SetMaterialProperties(data.material, data.sssParams, data.gateParams, data.color);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, data.maskIdTexture);
                    context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceDataTextureId, data.surfaceDataTexture);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceColorTextureId, data.sssTexture);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }
        }

        private static void SetMaterialProperties(Material material, HoSubsurfaceScatteringSettings settings, RTHandle sourceTarget)
        {
            SetMaterialProperties(material, CreateSssParams(settings, sourceTarget), CreateGateParams(settings), settings.color);
            HoSubsurfaceScatteringProfileShaderData.Set(material, settings);
        }

        private static void SetMaterialProperties(Material material, Vector4 sssParams, Vector4 gateParams, Vector4 color)
        {
            material.SetVector(HoSubsurfaceScatteringShaderConstants.ParamsId, sssParams);
            material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, gateParams);
            material.SetVector(HoSubsurfaceScatteringShaderConstants.ColorId, color);
        }

        private static Vector4 CreateSssParams(HoSubsurfaceScatteringSettings settings, RTHandle sourceTarget)
        {
            RenderTexture rt = sourceTarget != null ? sourceTarget.rt : null;
            float scaleCompensation = rt != null ? RTHandles.rtHandleProperties.currentViewportSize.x / Mathf.Max(1.0f, rt.width) : 1.0f;
            return CreateSssParams(settings, scaleCompensation);
        }

        private static Vector4 CreateSssParams(
            HoSubsurfaceScatteringSettings settings,
            RenderTextureDescriptor cameraTextureDescriptor,
            TextureDesc sourceDesc)
        {
            float scaleCompensation = sourceDesc.width > 0
                ? cameraTextureDescriptor.width / (float)sourceDesc.width
                : 1.0f;
            return CreateSssParams(settings, scaleCompensation);
        }

        private static Vector4 CreateSssParams(HoSubsurfaceScatteringSettings settings, float scaleCompensation)
        {
            return new Vector4(
                Mathf.Max(0.0f, settings.strength),
                PackRadius(settings.radius, 24.0f),
                Mathf.Clamp((int)settings.quality, 1.0f, 32.0f),
                Mathf.Max(1.0f, scaleCompensation));
        }

        private static float PackRadius(float radius, float maxRadius)
        {
            float normalized = Mathf.Clamp01(Mathf.Max(0.0f, radius) / Mathf.Max(0.0001f, maxRadius));
            return normalized * normalized * maxRadius;
        }

        private static Vector4 CreateGateParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(
                Mathf.Max(0.0001f, settings.depthTolerance),
                Mathf.Max(0.01f, settings.normalTolerance),
                Mathf.Clamp01(settings.sourcePreserve),
                0.0f);
        }

    }

    internal sealed class HoSubsurfaceScatteringBlurPass : ScriptableRenderPass
    {
        private const int BlurPassIndex = 1;
        private readonly ProfilingSampler blurProfilingSampler;
        private readonly string blurPassName;
        private readonly Vector2 direction;
        private HoSubsurfaceScatteringSettings settings;
        private HoSubsurfaceScatteringRenderTargets renderTargets;
        private Material material;
        private bool vertical;

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle destination;
            public TextureHandle maskIdTexture;
            public TextureHandle normalDepthTexture;
            public TextureHandle surfaceDataTexture;
            public Material material;
            public Vector4 sssParams;
            public Vector4 gateParams;
            public Vector4 direction;
        }

        public HoSubsurfaceScatteringBlurPass(string passName, Vector2 direction)
        {
            blurPassName = passName;
            this.direction = direction;
            blurProfilingSampler = new ProfilingSampler(passName);
        }

        public void Setup(
            HoSubsurfaceScatteringSettings settings,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material,
            bool vertical)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.material = material;
            this.vertical = vertical;
            renderPassEvent = vertical
                ? settings.GetVerticalBlurRenderPassEvent()
                : settings.GetHorizontalBlurRenderPassEvent();
        }

        public void SetupRenderGraph(
            HoSubsurfaceScatteringSettings settings,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material,
            bool vertical)
        {
            Setup(settings, renderTargets, material, vertical);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTargets.ReAllocateIfNeeded(renderingData.cameraData.cameraTargetDescriptor, settings);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null)
            {
                return;
            }

            RTHandle source = vertical ? renderTargets.DiffusionTexture : renderTargets.SourceTexture;
            RTHandle destination = vertical ? renderTargets.SourceTexture : renderTargets.DiffusionTexture;
            if (source == null || destination == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, blurProfilingSampler))
            {
                SetMaterialProperties(material, settings, destination);
                SetDirection(material);
                Blitter.BlitCameraTexture(cmd, source, destination, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, material, BlurPassIndex);
                cmd.SetGlobalTexture(vertical ? HoSubsurfaceScatteringShaderConstants.SourceTextureId : HoSubsurfaceScatteringShaderConstants.DiffusionTextureId, destination.nameID);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
            {
                return;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            HoMetadataBufferRenderGraphResources metadataResources = frameData.GetOrCreate<HoMetadataBufferRenderGraphResources>();
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            HoSubsurfaceScatteringRenderGraphResources sssResources = frameData.GetOrCreate<HoSubsurfaceScatteringRenderGraphResources>();
            TextureHandle source = vertical ? sssResources.diffusionTexture : sssResources.sourceTexture;
            if (!source.IsValid() || !metadataResources.HasRequiredTextures || !geometryResources.HasRequiredTextures)
            {
                return;
            }

            TextureHandle destination = renderGraph.CreateTexture(HoSubsurfaceScatteringRenderGraphResources.CreateDescriptor(
                cameraData.cameraTargetDescriptor,
                settings,
                vertical ? HoSubsurfaceScatteringShaderConstants.SourceTextureName : HoSubsurfaceScatteringShaderConstants.DiffusionTextureName));
            HoSubsurfaceScatteringProfileShaderData.Set(material, settings);

            if (vertical)
            {
                sssResources.sourceTexture = destination;
            }
            else
            {
                sssResources.diffusionTexture = destination;
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(blurPassName, out PassData passData, blurProfilingSampler))
            {
                passData.source = source;
                passData.destination = destination;
                passData.maskIdTexture = metadataResources.maskIdTexture;
                passData.normalDepthTexture = geometryResources.normalDepthTexture;
                passData.surfaceDataTexture = metadataResources.surfaceDataTexture;
                passData.material = material;
                passData.sssParams = CreateSssParams(settings, cameraData.cameraTargetDescriptor, destination.GetDescriptor(renderGraph));
                passData.gateParams = CreateGateParams(settings);
                passData.direction = new Vector4(direction.x, direction.y, 0.0f, 0.0f);

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(passData.maskIdTexture, AccessFlags.Read);
                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.surfaceDataTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(destination, vertical ? HoSubsurfaceScatteringShaderConstants.SourceTextureId : HoSubsurfaceScatteringShaderConstants.DiffusionTextureId);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.ParamsId, data.sssParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, data.gateParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.DirectionId, data.direction);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, data.maskIdTexture);
                    context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceDataTextureId, data.surfaceDataTexture);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, BlurPassIndex);
                });
            }
        }

        private void SetDirection(Material material)
        {
            material.SetVector(HoSubsurfaceScatteringShaderConstants.DirectionId, new Vector4(direction.x, direction.y, 0.0f, 0.0f));
        }

        private static void SetMaterialProperties(Material material, HoSubsurfaceScatteringSettings settings, RTHandle target)
        {
            material.SetVector(HoSubsurfaceScatteringShaderConstants.ParamsId, CreateSssParams(settings, target));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, CreateGateParams(settings));
            HoSubsurfaceScatteringProfileShaderData.Set(material, settings);
        }

        private static Vector4 CreateSssParams(HoSubsurfaceScatteringSettings settings, RTHandle target)
        {
            RenderTexture rt = target != null ? target.rt : null;
            float scaleCompensation = rt != null ? RTHandles.rtHandleProperties.currentViewportSize.x / Mathf.Max(1.0f, rt.width) : 1.0f;
            return new Vector4(Mathf.Max(0.0f, settings.strength), PackRadius(settings.radius, 24.0f), Mathf.Clamp((int)settings.quality, 1.0f, 32.0f), Mathf.Max(1.0f, scaleCompensation));
        }

        private static float PackRadius(float radius, float maxRadius)
        {
            float normalized = Mathf.Clamp01(Mathf.Max(0.0f, radius) / Mathf.Max(0.0001f, maxRadius));
            return normalized * normalized * maxRadius;
        }

        private static Vector4 CreateSssParams(
            HoSubsurfaceScatteringSettings settings,
            RenderTextureDescriptor cameraTextureDescriptor,
            TextureDesc targetDesc)
        {
            float scaleCompensation = targetDesc.width > 0
                ? cameraTextureDescriptor.width / (float)targetDesc.width
                : 1.0f;
            return new Vector4(Mathf.Max(0.0f, settings.strength), PackRadius(settings.radius, 24.0f), Mathf.Clamp((int)settings.quality, 1.0f, 32.0f), Mathf.Max(1.0f, scaleCompensation));
        }

        private static Vector4 CreateGateParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(Mathf.Max(0.0001f, settings.depthTolerance), Mathf.Max(0.01f, settings.normalTolerance), Mathf.Clamp01(settings.sourcePreserve), 0.0f);
        }
    }

    internal sealed class HoSubsurfaceScatteringTransmissionGatherPass : ScriptableRenderPass
    {
        private const int TransmissionGatherPassIndex = 2;
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-SubsurfaceScattering TransmissionGather");
        private HoSubsurfaceScatteringSettings settings;
        private HoSubsurfaceScatteringRenderTargets renderTargets;
        private Material material;

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle destination;
            public TextureHandle maskIdTexture;
            public TextureHandle normalDepthTexture;
            public TextureHandle surfaceDataTexture;
            public Material material;
            public Vector4 gateParams;
            public Vector4 transmissionParams;
            public Vector4 transmissionColor;
            public Vector4 transmissionShapeParams;
        }

        public void Setup(
            HoSubsurfaceScatteringSettings settings,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.material = material;
            renderPassEvent = settings.GetCompositeRenderPassEvent();
        }

        public void SetupRenderGraph(
            HoSubsurfaceScatteringSettings settings,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material)
        {
            Setup(settings, renderTargets, material);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTargets.ReAllocateIfNeeded(renderingData.cameraData.cameraTargetDescriptor, settings);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null || renderTargets.SourceTexture == null || renderTargets.TransmissionTexture == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                SetMaterialProperties(material, settings);
                cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.SourceTextureId, renderTargets.SourceTexture.nameID);
                Blitter.BlitCameraTexture(cmd, renderTargets.SourceTexture, renderTargets.TransmissionTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, material, TransmissionGatherPassIndex);
                cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.TransmissionTextureId, renderTargets.TransmissionTexture.nameID);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
            {
                return;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            HoMetadataBufferRenderGraphResources metadataResources = frameData.GetOrCreate<HoMetadataBufferRenderGraphResources>();
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            HoSubsurfaceScatteringRenderGraphResources sssResources = frameData.GetOrCreate<HoSubsurfaceScatteringRenderGraphResources>();
            TextureHandle source = sssResources.sourceTexture;
            if (!source.IsValid() || !metadataResources.HasRequiredTextures || !geometryResources.HasRequiredTextures)
            {
                return;
            }

            TextureHandle destination = renderGraph.CreateTexture(HoSubsurfaceScatteringRenderGraphResources.CreateDescriptor(
                cameraData.cameraTargetDescriptor,
                settings,
                HoSubsurfaceScatteringShaderConstants.TransmissionTextureName));
            sssResources.transmissionTexture = destination;
            HoSubsurfaceScatteringProfileShaderData.Set(material, settings);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-SubsurfaceScattering TransmissionGather", out PassData passData, ProfilingSampler))
            {
                passData.source = source;
                passData.destination = destination;
                passData.maskIdTexture = metadataResources.maskIdTexture;
                passData.normalDepthTexture = geometryResources.normalDepthTexture;
                passData.surfaceDataTexture = metadataResources.surfaceDataTexture;
                passData.material = material;
                passData.gateParams = CreateGateParams(settings);
                passData.transmissionParams = CreateTransmissionParams(settings);
                passData.transmissionColor = settings.transmissionColor;
                passData.transmissionShapeParams = CreateTransmissionShapeParams(settings);

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(passData.maskIdTexture, AccessFlags.Read);
                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.surfaceDataTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(destination, HoSubsurfaceScatteringShaderConstants.TransmissionTextureId);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    SetMaterialProperties(data.material, data.gateParams, data.transmissionParams, data.transmissionColor, data.transmissionShapeParams);
                    context.cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.SourceTextureId, data.source);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, data.maskIdTexture);
                    context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceDataTextureId, data.surfaceDataTexture);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, TransmissionGatherPassIndex);
                });
            }
        }

        private static void SetMaterialProperties(Material material, HoSubsurfaceScatteringSettings settings)
        {
            SetMaterialProperties(material, CreateGateParams(settings), CreateTransmissionParams(settings), settings.transmissionColor, CreateTransmissionShapeParams(settings));
            HoSubsurfaceScatteringProfileShaderData.Set(material, settings);
        }

        private static void SetMaterialProperties(Material material, Vector4 gateParams, Vector4 transmissionParams, Vector4 transmissionColor, Vector4 transmissionShapeParams)
        {
            material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, gateParams);
            material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionParamsId, transmissionParams);
            material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionColorId, transmissionColor);
            material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionShapeParamsId, transmissionShapeParams);
        }

        private static Vector4 CreateGateParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(Mathf.Max(0.0001f, settings.depthTolerance), Mathf.Max(0.01f, settings.normalTolerance), Mathf.Clamp01(settings.sourcePreserve), 0.0f);
        }

        private static Vector4 CreateTransmissionParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(
                Mathf.Clamp01(settings.transmissionStrength),
                PackRadius(settings.transmissionRadius, 24.0f),
                Mathf.Clamp(settings.transmissionSamples, 2, 32),
                Mathf.Clamp01(settings.transmissionMainLightDirection));
        }

        private static Vector4 CreateTransmissionShapeParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(
                Mathf.Max(0.0f, settings.transmissionDepthWeight),
                Mathf.Max(0.0f, settings.transmissionEdgeBoost),
                Mathf.Clamp01(settings.transmissionRimWeight),
                Mathf.Clamp01(settings.transmissionSmoothing));
        }

        private static float PackRadius(float radius, float maxRadius)
        {
            float normalized = Mathf.Clamp01(Mathf.Max(0.0f, radius) / Mathf.Max(0.0001f, maxRadius));
            return normalized * normalized * maxRadius;
        }
    }

    internal sealed class HoSubsurfaceScatteringTransmissionBlurPass : ScriptableRenderPass
    {
        private const int TransmissionBlurPassIndex = 3;
        private readonly ProfilingSampler blurProfilingSampler;
        private readonly string blurPassName;
        private readonly Vector2 direction;
        private HoSubsurfaceScatteringSettings settings;
        private HoSubsurfaceScatteringRenderTargets renderTargets;
        private Material material;
        private bool vertical;

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle destination;
            public TextureHandle maskIdTexture;
            public TextureHandle normalDepthTexture;
            public TextureHandle surfaceDataTexture;
            public Material material;
            public Vector4 gateParams;
            public Vector4 transmissionParams;
            public Vector4 transmissionShapeParams;
            public Vector4 direction;
        }

        public HoSubsurfaceScatteringTransmissionBlurPass(string passName, Vector2 direction)
        {
            blurPassName = passName;
            this.direction = direction;
            blurProfilingSampler = new ProfilingSampler(passName);
        }

        public void Setup(
            HoSubsurfaceScatteringSettings settings,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material,
            bool vertical)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.material = material;
            this.vertical = vertical;
            renderPassEvent = settings.GetCompositeRenderPassEvent();
        }

        public void SetupRenderGraph(
            HoSubsurfaceScatteringSettings settings,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material,
            bool vertical)
        {
            Setup(settings, renderTargets, material, vertical);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTargets.ReAllocateIfNeeded(renderingData.cameraData.cameraTargetDescriptor, settings);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null || renderTargets.TransmissionTexture == null || renderTargets.TransmissionTempTexture == null)
            {
                return;
            }

            RTHandle source = vertical ? renderTargets.TransmissionTempTexture : renderTargets.TransmissionTexture;
            RTHandle destination = vertical ? renderTargets.TransmissionTexture : renderTargets.TransmissionTempTexture;
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, blurProfilingSampler))
            {
                SetMaterialProperties(material, settings);
                material.SetVector(HoSubsurfaceScatteringShaderConstants.DirectionId, new Vector4(direction.x, direction.y, 0.0f, 0.0f));
                cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.SourceTextureId, source.nameID);
                Blitter.BlitCameraTexture(cmd, source, destination, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, material, TransmissionBlurPassIndex);
                cmd.SetGlobalTexture(vertical ? HoSubsurfaceScatteringShaderConstants.TransmissionTextureId : HoSubsurfaceScatteringShaderConstants.TransmissionTempTextureId, destination.nameID);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
            {
                return;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            HoMetadataBufferRenderGraphResources metadataResources = frameData.GetOrCreate<HoMetadataBufferRenderGraphResources>();
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            HoSubsurfaceScatteringRenderGraphResources sssResources = frameData.GetOrCreate<HoSubsurfaceScatteringRenderGraphResources>();
            TextureHandle source = vertical ? sssResources.transmissionTempTexture : sssResources.transmissionTexture;
            if (!source.IsValid() || !metadataResources.HasRequiredTextures || !geometryResources.HasRequiredTextures)
            {
                return;
            }

            TextureHandle destination = renderGraph.CreateTexture(HoSubsurfaceScatteringRenderGraphResources.CreateDescriptor(
                cameraData.cameraTargetDescriptor,
                settings,
                vertical ? HoSubsurfaceScatteringShaderConstants.TransmissionTextureName : HoSubsurfaceScatteringShaderConstants.TransmissionTempTextureName));
            HoSubsurfaceScatteringProfileShaderData.Set(material, settings);

            if (vertical)
            {
                sssResources.transmissionTexture = destination;
            }
            else
            {
                sssResources.transmissionTempTexture = destination;
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(blurPassName, out PassData passData, blurProfilingSampler))
            {
                passData.source = source;
                passData.destination = destination;
                passData.maskIdTexture = metadataResources.maskIdTexture;
                passData.normalDepthTexture = geometryResources.normalDepthTexture;
                passData.surfaceDataTexture = metadataResources.surfaceDataTexture;
                passData.material = material;
                passData.gateParams = CreateGateParams(settings);
                passData.transmissionParams = CreateTransmissionParams(settings);
                passData.transmissionShapeParams = CreateTransmissionShapeParams(settings);
                passData.direction = new Vector4(direction.x, direction.y, 0.0f, 0.0f);

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(passData.maskIdTexture, AccessFlags.Read);
                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.surfaceDataTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(destination, vertical ? HoSubsurfaceScatteringShaderConstants.TransmissionTextureId : HoSubsurfaceScatteringShaderConstants.TransmissionTempTextureId);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, data.gateParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionParamsId, data.transmissionParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionShapeParamsId, data.transmissionShapeParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.DirectionId, data.direction);
                    context.cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.SourceTextureId, data.source);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, data.maskIdTexture);
                    context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceDataTextureId, data.surfaceDataTexture);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, TransmissionBlurPassIndex);
                });
            }
        }

        private static void SetMaterialProperties(Material material, HoSubsurfaceScatteringSettings settings)
        {
            material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, CreateGateParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionParamsId, CreateTransmissionParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionShapeParamsId, CreateTransmissionShapeParams(settings));
            HoSubsurfaceScatteringProfileShaderData.Set(material, settings);
        }

        private static Vector4 CreateGateParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(Mathf.Max(0.0001f, settings.depthTolerance), Mathf.Max(0.01f, settings.normalTolerance), Mathf.Clamp01(settings.sourcePreserve), 0.0f);
        }

        private static Vector4 CreateTransmissionParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(
                Mathf.Clamp01(settings.transmissionStrength),
                PackRadius(settings.transmissionRadius, 24.0f),
                Mathf.Clamp(settings.transmissionSamples, 2, 32),
                Mathf.Clamp01(settings.transmissionMainLightDirection));
        }

        private static Vector4 CreateTransmissionShapeParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(
                Mathf.Max(0.0f, settings.transmissionDepthWeight),
                Mathf.Max(0.0f, settings.transmissionEdgeBoost),
                Mathf.Clamp01(settings.transmissionRimWeight),
                Mathf.Clamp01(settings.transmissionSmoothing));
        }

        private static float PackRadius(float radius, float maxRadius)
        {
            float normalized = Mathf.Clamp01(Mathf.Max(0.0f, radius) / Mathf.Max(0.0001f, maxRadius));
            return normalized * normalized * maxRadius;
        }
    }

    internal sealed class HoSubsurfaceScatteringCompositePass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-SubsurfaceScattering Composite");
        private RTHandle cameraColorTarget;
        private HoSubsurfaceScatteringSettings settings;
        private HoSubsurfaceScatteringRenderTargets renderTargets;
        private Material material;

        private sealed class PassData
        {
            public TextureHandle cameraColor;
            public TextureHandle sssTexture;
            public TextureHandle transmissionTexture;
            public TextureHandle maskIdTexture;
            public TextureHandle normalDepthTexture;
            public TextureHandle surfaceDataTexture;
            public Material material;
            public Vector4 sssParams;
            public Vector4 gateParams;
            public Vector4 color;
            public Vector4 transmissionParams;
            public Vector4 transmissionColor;
            public Vector4 transmissionShapeParams;
            public Vector4 compositeParams;
        }

        public void Setup(
            HoSubsurfaceScatteringSettings settings,
            RTHandle cameraColorTarget,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material)
        {
            this.settings = settings;
            this.cameraColorTarget = cameraColorTarget;
            this.renderTargets = renderTargets;
            this.material = material;
            renderPassEvent = settings.GetCompositeRenderPassEvent();
        }

        public void SetupRenderGraph(
            HoSubsurfaceScatteringSettings settings,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.material = material;
            renderPassEvent = settings.GetCompositeRenderPassEvent();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTargets.ReAllocateCompositeSource(renderingData.cameraData.cameraTargetDescriptor);
            ConfigureTarget(cameraColorTarget);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null || renderTargets.SourceTexture == null || renderTargets.TransmissionTexture == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                SetMaterialProperties(material, settings);
                cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.SourceTextureId, renderTargets.SourceTexture.nameID);
                cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.TransmissionTextureId, renderTargets.TransmissionTexture.nameID);
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, renderTargets.CompositeSourceTexture, 0, true);
                Blitter.BlitCameraTexture(cmd, renderTargets.CompositeSourceTexture, cameraColorTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, material, 4);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoMetadataBufferRenderGraphResources metadataResources = frameData.GetOrCreate<HoMetadataBufferRenderGraphResources>();
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            HoSubsurfaceScatteringRenderGraphResources sssResources = frameData.GetOrCreate<HoSubsurfaceScatteringRenderGraphResources>();
            TextureHandle cameraColor = resourceData.activeColorTexture;
            TextureHandle sssTexture = sssResources.sourceTexture;
            TextureHandle transmissionTexture = sssResources.transmissionTexture;
            if (!cameraColor.IsValid()
                || !sssTexture.IsValid()
                || !transmissionTexture.IsValid()
                || !metadataResources.HasRequiredTextures
                || !geometryResources.HasRequiredTextures)
            {
                return;
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(cameraColor);
            destinationDesc.name = "_lilHoSSSCompositeColor";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);
            HoSubsurfaceScatteringProfileShaderData.Set(material, settings);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-SubsurfaceScattering Composite", out PassData passData, ProfilingSampler))
            {
                passData.cameraColor = cameraColor;
                passData.sssTexture = sssTexture;
                passData.transmissionTexture = transmissionTexture;
                passData.maskIdTexture = metadataResources.maskIdTexture;
                passData.normalDepthTexture = geometryResources.normalDepthTexture;
                passData.surfaceDataTexture = metadataResources.surfaceDataTexture;
                passData.material = material;
                passData.sssParams = CreateSssParams(settings);
                passData.gateParams = CreateGateParams(settings);
                passData.color = settings.color;
                passData.transmissionParams = CreateTransmissionParams(settings);
                passData.transmissionColor = settings.transmissionColor;
                passData.transmissionShapeParams = CreateTransmissionShapeParams(settings);
                passData.compositeParams = CreateCompositeParams(settings);

                builder.UseTexture(cameraColor, AccessFlags.Read);
                builder.UseTexture(sssTexture, AccessFlags.Read);
                builder.UseTexture(transmissionTexture, AccessFlags.Read);
                builder.UseTexture(passData.maskIdTexture, AccessFlags.Read);
                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.surfaceDataTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.ParamsId, data.sssParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, data.gateParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.ColorId, data.color);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionParamsId, data.transmissionParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionColorId, data.transmissionColor);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionShapeParamsId, data.transmissionShapeParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.CompositeParamsId, data.compositeParams);
                    context.cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.SourceTextureId, data.sssTexture);
                    context.cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.TransmissionTextureId, data.transmissionTexture);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, data.maskIdTexture);
                    context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceDataTextureId, data.surfaceDataTexture);
                    Blitter.BlitTexture(context.cmd, data.cameraColor, new Vector4(1, 1, 0, 0), data.material, 4);
                });
            }

            resourceData.cameraColor = destination;
        }

        private static void SetMaterialProperties(Material material, HoSubsurfaceScatteringSettings settings)
        {
            material.SetVector(HoSubsurfaceScatteringShaderConstants.ParamsId, CreateSssParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, CreateGateParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.ColorId, settings.color);
            material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionParamsId, CreateTransmissionParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionColorId, settings.transmissionColor);
            material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionShapeParamsId, CreateTransmissionShapeParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.CompositeParamsId, CreateCompositeParams(settings));
            HoSubsurfaceScatteringProfileShaderData.Set(material, settings);
        }

        private static Vector4 CreateSssParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(Mathf.Max(0.0f, settings.strength), PackRadius(settings.radius, 24.0f), Mathf.Clamp((int)settings.quality, 1.0f, 32.0f), 0.0f);
        }

        private static Vector4 CreateGateParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(Mathf.Max(0.0001f, settings.depthTolerance), Mathf.Max(0.01f, settings.normalTolerance), Mathf.Clamp01(settings.sourcePreserve), 0.0f);
        }

        private static Vector4 CreateTransmissionParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(
                Mathf.Clamp01(settings.transmissionStrength),
                PackRadius(settings.transmissionRadius, 24.0f),
                Mathf.Clamp(settings.transmissionSamples, 2, 32),
                Mathf.Clamp01(settings.transmissionMainLightDirection));
        }

        private static float PackRadius(float radius, float maxRadius)
        {
            float normalized = Mathf.Clamp01(Mathf.Max(0.0f, radius) / Mathf.Max(0.0001f, maxRadius));
            return normalized * normalized * maxRadius;
        }

        private static Vector4 CreateTransmissionShapeParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(
                Mathf.Max(0.0f, settings.transmissionDepthWeight),
                Mathf.Max(0.0f, settings.transmissionEdgeBoost),
                Mathf.Clamp01(settings.transmissionRimWeight),
                Mathf.Clamp01(settings.transmissionSmoothing));
        }

        private static Vector4 CreateCompositeParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(
                Mathf.Clamp((float)settings.transmissionBlendMode, 0.0f, 4.0f),
                Mathf.Clamp01(settings.transmissionTintInjection),
                0.0f,
                0.0f);
        }
    }

    internal sealed class HoSubsurfaceScatteringRenderGraphResources : ContextItem
    {
        public TextureHandle sourceTexture = TextureHandle.nullHandle;
        public TextureHandle diffusionTexture = TextureHandle.nullHandle;
        public TextureHandle transmissionTexture = TextureHandle.nullHandle;
        public TextureHandle transmissionTempTexture = TextureHandle.nullHandle;

        public override void Reset()
        {
            sourceTexture = TextureHandle.nullHandle;
            diffusionTexture = TextureHandle.nullHandle;
            transmissionTexture = TextureHandle.nullHandle;
            transmissionTempTexture = TextureHandle.nullHandle;
        }

        public static TextureDesc CreateDescriptor(
            RenderTextureDescriptor cameraTextureDescriptor,
            HoSubsurfaceScatteringSettings settings,
            string name)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            TextureDesc descriptor = new TextureDesc(
                Mathf.Max(1, cameraTextureDescriptor.width / divisor),
                Mathf.Max(1, cameraTextureDescriptor.height / divisor));
            descriptor.name = name;
            descriptor.format = HoSubsurfaceScatteringRenderTargets.GetColorGraphicsFormat(cameraTextureDescriptor);
            descriptor.dimension = cameraTextureDescriptor.dimension;
            descriptor.slices = cameraTextureDescriptor.volumeDepth;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.clearBuffer = false;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.useDynamicScale = cameraTextureDescriptor.useDynamicScale;
            descriptor.useDynamicScaleExplicit = cameraTextureDescriptor.useDynamicScaleExplicit;
            descriptor.vrUsage = cameraTextureDescriptor.vrUsage;
            return descriptor;
        }
    }

    internal sealed class HoSubsurfaceScatteringRenderTargets
    {
        private RTHandle sourceTexture;
        private RTHandle diffusionTexture;
        private RTHandle transmissionTexture;
        private RTHandle transmissionTempTexture;
        private RTHandle compositeSourceTexture;

        public RTHandle SourceTexture => sourceTexture;
        public RTHandle DiffusionTexture => diffusionTexture;
        public RTHandle TransmissionTexture => transmissionTexture;
        public RTHandle TransmissionTempTexture => transmissionTempTexture;
        public RTHandle CompositeSourceTexture => compositeSourceTexture;

        public void ReAllocateIfNeeded(RenderTextureDescriptor cameraTextureDescriptor, HoSubsurfaceScatteringSettings settings)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            RenderTextureDescriptor descriptor = cameraTextureDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.graphicsFormat = GetColorGraphicsFormat(cameraTextureDescriptor);
            descriptor.msaaSamples = 1;
            descriptor.width = Mathf.Max(1, descriptor.width / divisor);
            descriptor.height = Mathf.Max(1, descriptor.height / divisor);

            RenderingUtils.ReAllocateIfNeeded(ref sourceTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoSubsurfaceScatteringShaderConstants.SourceTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref diffusionTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoSubsurfaceScatteringShaderConstants.DiffusionTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref transmissionTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoSubsurfaceScatteringShaderConstants.TransmissionTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref transmissionTempTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoSubsurfaceScatteringShaderConstants.TransmissionTempTextureName);
        }

        public void ReAllocateCompositeSource(RenderTextureDescriptor cameraTextureDescriptor)
        {
            cameraTextureDescriptor.depthBufferBits = 0;
            cameraTextureDescriptor.depthStencilFormat = GraphicsFormat.None;
            cameraTextureDescriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref compositeSourceTexture, cameraTextureDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoSubsurfaceScatteringShaderConstants.CompositeSourceTextureName);
        }

        public void Release()
        {
            sourceTexture?.Release();
            diffusionTexture?.Release();
            transmissionTexture?.Release();
            transmissionTempTexture?.Release();
            compositeSourceTexture?.Release();
            sourceTexture = null;
            diffusionTexture = null;
            transmissionTexture = null;
            transmissionTempTexture = null;
            compositeSourceTexture = null;
        }

        internal static GraphicsFormat GetColorGraphicsFormat(RenderTextureDescriptor cameraTextureDescriptor)
        {
            if (cameraTextureDescriptor.graphicsFormat != GraphicsFormat.None &&
                SystemInfo.IsFormatSupported(cameraTextureDescriptor.graphicsFormat, GraphicsFormatUsage.Render))
            {
                return cameraTextureDescriptor.graphicsFormat;
            }

            if (SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormatUsage.Render))
            {
                return GraphicsFormat.R16G16B16A16_SFloat;
            }

            return SystemInfo.GetGraphicsFormat(DefaultFormat.HDR);
        }
    }
}
