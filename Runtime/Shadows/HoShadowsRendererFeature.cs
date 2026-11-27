using lilToon.URP.Extensions.CharacterShadow;
using lilToon.URP.Extensions.RTShadow;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Shadows
{
    public sealed class HoNativeMainShadowFrameData : ContextItem
    {
        public TextureHandle Visibility { get; internal set; }
        public TextureHandle SurfaceDepth { get; internal set; }
        public TextureHandle CharacterVisibility { get; internal set; }
        public int CameraInstanceId { get; internal set; }
        public bool IsValid => Visibility.IsValid();

        public override void Reset()
        {
            Visibility = TextureHandle.nullHandle;
            SurfaceDepth = TextureHandle.nullHandle;
            CharacterVisibility = TextureHandle.nullHandle;
            CameraInstanceId = 0;
        }
    }

    [DisallowMultipleRendererFeature("Ho Shadows")]
    public sealed class HoShadowsRendererFeature : ScriptableRendererFeature
    {
        [SerializeField, Range(0, 16)] private int additionalLightBudget = 4;
        [SerializeField] private bool earlyTransparentShadows;
        [SerializeField] private bool transparentEyeShadows;
        [SerializeField] private bool weightedOitShadows;
        [SerializeField] private bool managedCharacterShadows;
        [SerializeField] private HoShadowsCharacterSettings characterSettings = new();
        [SerializeField] private bool rayTracedShadows;
        [SerializeField] private HoRTShadowSettings rayTracedShadowSettings = new();
        [SerializeField] private RayTracingShader rayTracedShadowProgram;
        [SerializeField] private Shader rayVisibilityMaterial;
        [SerializeField] private ComputeShader rayShadowReconstruction;
        [SerializeField] private ComputeShader[] rayShadowReconstructionPasses;
        [SerializeField] private Shader rayShadowDebugShader;

        private Material resolveMaterial;
        private NativeMainShadowPass resolvePass;
        private Material characterMaterial;
        private HoCharacterScreenShadowPass characterPass;
        private Material additionalMaterial;
        private HoAdditionalScreenShadowPass additionalPass;
        private HoOpaqueScreenShadowBindingPass bindOpaque;
        private HoOpaqueScreenShadowBindingPass clearOpaque;
        private HoRTShadowProducer rayProducer;
        private Material rayResolveMaterial;
        private HoRayTracedScreenShadowPass rayResolvePass;
        private Material transparentMaterial;
        private Material transparentAdditionalMaterial;
        private HoTransparentShadowSurfacePass transparentSurface;
        private HoTransparentScreenShadowPass transparentResolve;
        private HoTransparentScreenShadowPass clearTransparent;
        private HoTransparentShadowSurfacePass earlyTransparentSurface;
        private HoTransparentScreenShadowPass earlyTransparentResolve;
        private HoTransparentScreenShadowPass clearEarlyTransparent;
        private HoWeightedOitShadowSetupPass weightedOitSetup;
        private HoCharacterShadowAtlasProducer characterProducer;
        private HoCharacterShadowSettings characterProducerSettings;

        public bool RayTracedShadowsEnabled
        {
            get => rayTracedShadows;
            set => rayTracedShadows = value;
        }

        public HoRTShadowSettings RayTracedShadowSettings => rayTracedShadowSettings;

        public bool EarlyTransparentShadowsEnabled
        {
            get => earlyTransparentShadows;
            set => earlyTransparentShadows = value;
        }

        public bool TransparentEyeShadowsEnabled
        {
            get => transparentEyeShadows;
            set => transparentEyeShadows = value;
        }

        public bool WeightedOitShadowsEnabled
        {
            get => weightedOitShadows;
            set => weightedOitShadows = value;
        }

        public bool ManagedCharacterShadowsEnabled
        {
            get => managedCharacterShadows;
            set => managedCharacterShadows = value;
        }

        public HoShadowsCharacterSettings CharacterSettings => characterSettings;

        public override void Create()
        {
            CoreUtils.Destroy(resolveMaterial);
            Shader shader = Resources.Load<Shader>("HoNativeMainShadowResolve");
            resolveMaterial = shader != null ? CoreUtils.CreateEngineMaterial(shader) : null;
            resolvePass = resolveMaterial != null ? new NativeMainShadowPass(resolveMaterial) : null;
            CoreUtils.Destroy(characterMaterial);
            Shader characterShader = Resources.Load<Shader>("HoCharacterScreenShadowResolve");
            characterMaterial = characterShader != null ? CoreUtils.CreateEngineMaterial(characterShader) : null;
            characterPass = characterMaterial != null ? new HoCharacterScreenShadowPass(characterMaterial) : null;
            CoreUtils.Destroy(additionalMaterial);
            Shader additionalShader = Resources.Load<Shader>("HoAdditionalScreenShadowResolve");
            additionalMaterial = additionalShader != null ? CoreUtils.CreateEngineMaterial(additionalShader) : null;
            additionalPass = additionalMaterial != null ? new HoAdditionalScreenShadowPass(additionalMaterial) : null;
            bindOpaque = new HoOpaqueScreenShadowBindingPass(false);
            clearOpaque = new HoOpaqueScreenShadowBindingPass(true);
            ReleaseRayTracedShadows();
            ReleaseLayeredShadows();
            ReleaseCharacterProducer();
        }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            if (!rayTracedShadows || rayTracedShadowSettings == null || !rayTracedShadowSettings.enabled)
            {
                ReleaseRayTracedShadows();
                return;
            }
            if (!SystemInfo.supportsRayTracing || !SupportsOpaqueCamera(renderer, cameraData))
                return;

            if (rayProducer == null)
            {
                Shader shader = Resources.Load<Shader>("HoRayTracedScreenShadowResolve");
                if (shader == null)
                    return;
                rayResolveMaterial = CoreUtils.CreateEngineMaterial(shader);
                rayResolvePass = new HoRayTracedScreenShadowPass(rayResolveMaterial);
                rayProducer = new HoRTShadowProducer(this);
                rayProducer.ConfigureForManaged(rayTracedShadowSettings);
                rayProducer.ConfigureManagedShaders(rayTracedShadowProgram, rayVisibilityMaterial, rayShadowReconstruction,
                    rayShadowReconstructionPasses, rayShadowDebugShader);
            }
            rayProducer.OnCameraPreCull(renderer, in cameraData);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!SupportsOpaqueCamera(renderer, renderingData.cameraData))
                return;

            if (managedCharacterShadows && characterSettings != null)
            {
                if (HoCharacterShadow.Active.Count != 0)
                {
                    if (characterProducer == null)
                    {
                        characterProducerSettings = new HoCharacterShadowSettings();
                        characterProducer = new HoCharacterShadowAtlasProducer(filterPaddingTexels: 2.0f);
                        characterProducer.Create();
                    }
                    characterSettings.SetAtlasSettings(characterProducerSettings);
                    characterProducer.AddRenderPasses(renderer, ref renderingData, characterProducerSettings);
                }
            }
            else
                ReleaseCharacterProducer();

            if (resolvePass != null && renderingData.shadowData.supportsMainLightShadows
                && renderingData.lightData.mainLightIndex >= 0)
            {
                renderer.EnqueuePass(resolvePass);
                if (characterPass != null)
                    renderer.EnqueuePass(characterPass);
            }
            if (additionalPass != null && additionalLightBudget > 0 && renderingData.shadowData.supportsAdditionalLightShadows)
            {
                additionalPass.Budget = Mathf.Min(additionalLightBudget, 16);
                renderer.EnqueuePass(additionalPass);
            }
            if (rayTracedShadows && rayProducer != null)
            {
                rayProducer.AddRenderPasses(renderer, ref renderingData);
                if (rayProducer.ScheduledLightCount > 0)
                    renderer.EnqueuePass(rayResolvePass);
            }
            if (bindOpaque != null)
            {
                renderer.EnqueuePass(bindOpaque);
                renderer.EnqueuePass(clearOpaque);
            }
            var universal = (UniversalRenderer)renderer;
            bool early = earlyTransparentShadows
                && universal.TryGetEarlyTransparentShadowLayerMask(in renderingData.cameraData, out _);
            if ((early || transparentEyeShadows || weightedOitShadows) && universal.shadowTransparentReceive && SetupLayeredShadows())
            {
                if (early)
                {
                    renderer.EnqueuePass(earlyTransparentSurface);
                    renderer.EnqueuePass(earlyTransparentResolve);
                    renderer.EnqueuePass(clearEarlyTransparent);
                }
                if (weightedOitShadows) renderer.EnqueuePass(weightedOitSetup);
                if (transparentEyeShadows)
                {
                    renderer.EnqueuePass(transparentSurface);
                    renderer.EnqueuePass(transparentResolve);
                    renderer.EnqueuePass(clearTransparent);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(resolveMaterial);
            resolveMaterial = null;
            resolvePass = null;
            CoreUtils.Destroy(characterMaterial);
            characterMaterial = null;
            characterPass = null;
            CoreUtils.Destroy(additionalMaterial);
            additionalMaterial = null;
            additionalPass = null;
            bindOpaque = null;
            clearOpaque = null;
            ReleaseRayTracedShadows();
            ReleaseLayeredShadows();
            ReleaseCharacterProducer();
        }

        private static bool SupportsOpaqueCamera(ScriptableRenderer renderer, CameraData cameraData)
        {
            var camera = cameraData.camera;
            return renderer is UniversalRenderer && camera != null
                && IsSurfaceCamera(cameraData.cameraType) && cameraData.renderType == CameraRenderType.Base
                && !cameraData.xrRendering && cameraData.cameraTargetDescriptor.msaaSamples <= 1
                && !camera.allowDynamicResolution && camera.rect == new Rect(0, 0, 1, 1)
                && !UniversalRenderer.IsOffscreenDepthTexture(ref cameraData);
        }

        private static bool IsSurfaceCamera(CameraType type) =>
            type == CameraType.Game || type == CameraType.SceneView || type == CameraType.Reflection;

        private void ReleaseRayTracedShadows()
        {
            if (rayProducer != null)
            {
                rayProducer.Dispose();
                rayProducer = null;
            }
            CoreUtils.Destroy(rayResolveMaterial);
            rayResolveMaterial = null;
            rayResolvePass = null;
        }

        private bool SetupLayeredShadows()
        {
            if (transparentResolve != null)
                return true;
            var shader = Resources.Load<Shader>("HoTransparentScreenShadowResolve");
            if (shader == null)
                return false;
            transparentMaterial = CoreUtils.CreateEngineMaterial(shader);
            if (additionalMaterial != null)
            {
                transparentAdditionalMaterial = CoreUtils.CreateEngineMaterial(additionalMaterial.shader);
                transparentAdditionalMaterial.EnableKeyword("_HO_TRANSPARENT_SHADOW_SURFACE");
            }
            transparentSurface = new HoTransparentShadowSurfacePass();
            transparentResolve = new HoTransparentScreenShadowPass(transparentMaterial, transparentAdditionalMaterial);
            clearTransparent = new HoTransparentScreenShadowPass(null, clear: true);
            earlyTransparentSurface = new HoTransparentShadowSurfacePass(HoLayeredShadowScope.OpaqueQueue);
            earlyTransparentResolve = new HoTransparentScreenShadowPass(transparentMaterial, transparentAdditionalMaterial,
                scope: HoLayeredShadowScope.OpaqueQueue);
            clearEarlyTransparent = new HoTransparentScreenShadowPass(null, clear: true, scope: HoLayeredShadowScope.OpaqueQueue);
            weightedOitSetup = new HoWeightedOitShadowSetupPass(transparentResolve);
            return true;
        }

        private void ReleaseLayeredShadows()
        {
            CoreUtils.Destroy(transparentMaterial);
            CoreUtils.Destroy(transparentAdditionalMaterial);
            transparentMaterial = null;
            transparentAdditionalMaterial = null;
            transparentSurface = null;
            transparentResolve = null;
            clearTransparent = null;
            earlyTransparentSurface = null;
            earlyTransparentResolve = null;
            clearEarlyTransparent = null;
            weightedOitSetup = null;
        }

        private void ReleaseCharacterProducer()
        {
            if (characterProducer == null)
                return;
            characterProducer.Dispose();
            characterProducer = null;
            characterProducerSettings = null;
        }

        private sealed class NativeMainShadowPass : ScriptableRenderPass
        {
            private static readonly int DepthId = Shader.PropertyToID("_HoNativeMainShadowDepth");
            private static readonly int AtlasId = Shader.PropertyToID("_HoNativeMainShadowAtlas");
            private static readonly int CascadeCountId = Shader.PropertyToID("_HoNativeMainCascadeCount");

            private readonly Material material;

            private sealed class PassData
            {
                public Material material;
                public TextureHandle depth;
                public TextureHandle atlas;
                public int cascadeCount;
            }

            public NativeMainShadowPass(Material material)
            {
                this.material = material;
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var lights = frameData.Get<UniversalLightData>();
                var rendering = frameData.Get<UniversalRenderingData>();
                var shadows = frameData.Get<UniversalShadowData>();
                TextureHandle depth = resources.cameraDepthTexture;
                TextureHandle atlas = resources.mainShadowsTexture;
                int index = lights.mainLightIndex;

                if (!depth.IsValid() || !atlas.IsValid() || !shadows.supportsMainLightShadows
                    || index < 0 || index >= lights.visibleLights.Length)
                    return;
                var main = lights.visibleLights[index];
                if (main.lightType != LightType.Directional || main.light == null
                    || main.light.shadows == LightShadows.None || main.light.shadowStrength <= 0
                    || !rendering.cullResults.GetShadowCasterBounds(index, out _))
                    return;

                var desc = depth.GetDescriptor(graph);
                desc.name = "Ho Native Main Shadow Visibility";
                desc.depthBufferBits = DepthBits.None;
                desc.colorFormat = GraphicsFormat.R16_SFloat;
                desc.msaaSamples = MSAASamples.None;
                desc.bindTextureMS = false;
                desc.filterMode = FilterMode.Point;
                desc.wrapMode = TextureWrapMode.Clamp;
                desc.clearBuffer = false;

                TextureHandle output = graph.CreateTexture(desc);
                using var builder = graph.AddRasterRenderPass<PassData>("Ho Native Main Shadow Resolve", out var pass);
                pass.material = material;
                pass.depth = depth;
                pass.atlas = atlas;
                pass.cascadeCount = shadows.mainLightShadowCascadesCount;
                builder.UseTexture(depth, AccessFlags.Read);
                builder.UseTexture(atlas, AccessFlags.Read);
                builder.SetRenderAttachment(output, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    var properties = context.renderGraphPool.GetTempMaterialPropertyBlock();
                    properties.SetTexture(DepthId, (Texture)data.depth);
                    properties.SetTexture(AtlasId, (Texture)data.atlas);
                    properties.SetFloat(CascadeCountId, data.cascadeCount);
                    CoreUtils.DrawFullScreen(context.cmd, data.material, properties);
                });

                var product = frameData.GetOrCreate<HoNativeMainShadowFrameData>();
                product.Visibility = output;
                product.SurfaceDepth = depth;
                product.CameraInstanceId = frameData.Get<UniversalCameraData>().camera.GetInstanceID();
            }
        }
    }
}
