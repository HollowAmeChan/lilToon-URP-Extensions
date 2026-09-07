#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.GeometryBuffer;

namespace lilToon.URP.Extensions.GTAO
{
    [DisallowMultipleRendererFeature("Ho-GTAO")]
    public sealed class HoGTAORendererFeature : ScriptableRendererFeature
    {
        [SerializeField, InspectorName("Ho-GTAO 设置")]
        private HoGTAOSettings settings = new HoGTAOSettings();

        private HoGTAOPass pass;
        private Material material;
        private Shader shader;
        private HoGTAOHistory history;
        private bool resetRegistered;

        public HoGTAOSettings Settings => settings;

        public override void Create()
        {
            pass = new HoGTAOPass();
            history = new HoGTAOHistory();
            RenderPipelineManager.beginCameraRendering += ResetGlobalState;
            resetRegistered = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings == null || !settings.enabled)
            {
                return;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
            {
                return;
            }

            Shader currentShader = settings.shader != null ? settings.shader : Shader.Find(HoGTAOShaderConstants.ShaderName);
            if (currentShader == null)
            {
                return;
            }

            if (material == null || shader != currentShader)
            {
                CoreUtils.Destroy(material);
                shader = currentShader;
                material = CoreUtils.CreateEngineMaterial(shader);
            }

            pass.Setup(settings, material, history);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            if (resetRegistered)
            {
                RenderPipelineManager.beginCameraRendering -= ResetGlobalState;
                resetRegistered = false;
            }

            CoreUtils.Destroy(material);
            material = null;
            shader = null;
            history?.Dispose();
            history = null;
            pass = null;
        }

        private static void ResetGlobalState(ScriptableRenderContext context, Camera camera)
        {
            Shader.SetGlobalTexture(HoGTAOShaderConstants.AOTextureId, Texture2D.whiteTexture);
        }
    }

    internal sealed class HoGTAOHistory : System.IDisposable
    {
        private RTHandle previous;
        private RTHandle next;
        private bool valid;

        public RTHandle Previous => previous;
        public RTHandle Next => next;
        public bool Valid => valid;

        public void Ensure(int width, int height)
        {
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm,
                depthBufferBits = 0,
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = false
            };

            RenderingUtils.ReAllocateIfNeeded(ref previous, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoGTAOHistoryPrevTex");
            RenderingUtils.ReAllocateIfNeeded(ref next, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoGTAOHistoryNextTex");
            valid = false;
        }

        public void MarkValid() => valid = true;

        public void Swap()
        {
            RTHandle temp = previous;
            previous = next;
            next = temp;
        }

        public void Dispose()
        {
            previous?.Release();
            next?.Release();
            previous = null;
            next = null;
            valid = false;
        }
    }

    internal sealed class HoGTAOPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-GTAO");
        private static readonly int DebugModeId = Shader.PropertyToID("_HoGTAODebugMode");
        private static readonly int HistoryBlendId = Shader.PropertyToID("_HoGTAOHistoryBlend");

        private sealed class GenerateData
        {
            public Material material;
            public TextureHandle normalDepth;
            public TextureHandle output;
            public int debugMode;
        }

        private sealed class TemporalData
        {
            public Material material;
            public TextureHandle current;
            public TextureHandle previous;
            public TextureHandle output;
            public bool useHistory;
        }

        private sealed class BlitData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle destination;
            public int passIndex;
        }

        private HoGTAOSettings settings;
        private Material material;
        private HoGTAOHistory history;
        private int historyWidth;
        private int historyHeight;

        public void Setup(HoGTAOSettings settings, Material material, HoGTAOHistory history)
        {
            this.settings = settings;
            this.material = material;
            this.history = history;
            // GeometryBuffer is above Ho-GTAO at the same injection point.
            // Keep this transport probe on that fixed event while the shared
            // resource contract is being validated.
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || material == null || history == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoGeometryBufferRenderGraphResources geometry = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            if (!resourceData.activeColorTexture.IsValid() || !geometry.normalDepthTexture.IsValid())
            {
                return;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            int width = Mathf.Max(1, cameraData.cameraTargetDescriptor.width);
            int height = Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
            if (history.Previous == null || width != historyWidth || height != historyHeight)
            {
                history.Ensure(width, height);
                historyWidth = width;
                historyHeight = height;
            }

            HoGTAORenderGraphResources gtao = frameData.GetOrCreate<HoGTAORenderGraphResources>();
            TextureDesc currentDesc = new TextureDesc(width, height)
            {
                name = "_HoGTAOCurrentTex",
                format = GraphicsFormat.R8G8B8A8_UNorm,
                depthBufferBits = 0,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            TextureHandle current = renderGraph.CreateTexture(currentDesc);

            using (var builder = renderGraph.AddRasterRenderPass<GenerateData>("Ho-GTAO Generate", out GenerateData data, ProfilingSampler))
            {
                data.material = material;
                data.normalDepth = geometry.normalDepthTexture;
                data.output = current;
                data.debugMode = (int)settings.debugMode;
                builder.UseTexture(data.normalDepth, AccessFlags.Read);
                builder.SetRenderAttachment(data.output, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (GenerateData passData, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(DebugModeId, passData.debugMode);
                    Blitter.BlitTexture(context.cmd, passData.normalDepth, new Vector4(1, 1, 0, 0), passData.material, 0);
                });
            }

            if (settings.debugMode != HoGTAODebugMode.Off)
            {
                // Keep the semantic resource pointed at the generated AO. The
                // debug camera-color copy is only a presentation surface; exposing
                // that copy to DebugTile made it display a stale whole-frame image.
                gtao.aoTexture = current;
                RecordBlit(renderGraph, frameData, current, resourceData.activeColorTexture, 3, "Ho-GTAO Debug Output");
                return;
            }

            TextureHandle previous = renderGraph.ImportTexture(history.Previous);
            TextureHandle next = renderGraph.ImportTexture(history.Next);
            using (var builder = renderGraph.AddRasterRenderPass<TemporalData>("Ho-GTAO Temporal", out TemporalData data, ProfilingSampler))
            {
                data.material = material;
                data.current = current;
                data.previous = previous;
                data.output = next;
                data.useHistory = history.Valid;
                builder.UseTexture(data.current, AccessFlags.Read);
                builder.UseTexture(data.previous, AccessFlags.Read);
                builder.SetRenderAttachment(data.output, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (TemporalData passData, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HistoryBlendId, passData.useHistory ? 0.5f : 0.0f);
                    context.cmd.SetGlobalTexture(HoGTAOShaderConstants.AoInputTexId, passData.current);
                    context.cmd.SetGlobalTexture(HoGTAOShaderConstants.HistoryPrevTexId, passData.previous);
                    Blitter.BlitTexture(context.cmd, passData.current, new Vector4(1, 1, 0, 0), passData.material, 1);
                });
            }

            history.Swap();
            history.MarkValid();
            gtao.aoTexture = RecordBlit(renderGraph, frameData, next, resourceData.activeColorTexture, 2, "Ho-GTAO Output");
        }

        private TextureHandle RecordBlit(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source, TextureHandle cameraColor, int passIndex, string passName)
        {
            TextureDesc destinationDesc = renderGraph.GetTextureDesc(cameraColor);
            destinationDesc.name = passName == "Ho-GTAO Output" ? "_HoAOTexture" : "_HoGTAODebugColor";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<BlitData>(passName, out BlitData data, ProfilingSampler))
            {
                data.material = material;
                data.source = source;
                data.destination = destination;
                data.passIndex = passIndex;
                builder.UseTexture(data.source, AccessFlags.Read);
                builder.SetRenderAttachment(data.destination, 0, AccessFlags.WriteAll);
                if (passIndex == 2)
                {
                    builder.SetGlobalTextureAfterPass(data.destination, HoGTAOShaderConstants.AOTextureId);
                }
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (BlitData passData, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, passData.source, new Vector4(1, 1, 0, 0), passData.material, passData.passIndex);
                });
            }

            if (passIndex != 2)
            {
                frameData.Get<UniversalResourceData>().cameraColor = destination;
            }
            return destination;
        }
    }
}
