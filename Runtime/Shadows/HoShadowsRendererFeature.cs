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

        private Material resolveMaterial;
        private NativeMainShadowPass resolvePass;
        private Material characterMaterial;
        private HoCharacterScreenShadowPass characterPass;
        private Material additionalMaterial;
        private HoAdditionalScreenShadowPass additionalPass;

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
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            var camera = cameraData.camera;
            if (!(renderer is UniversalRenderer)
                || camera == null
                || cameraData.cameraType != CameraType.Game
                || cameraData.renderType != CameraRenderType.Base
                || cameraData.xrRendering
                || cameraData.cameraTargetDescriptor.msaaSamples > 1
                || camera.allowDynamicResolution
                || camera.rect != new Rect(0, 0, 1, 1)
                || UniversalRenderer.IsOffscreenDepthTexture(ref cameraData))
                return;

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
