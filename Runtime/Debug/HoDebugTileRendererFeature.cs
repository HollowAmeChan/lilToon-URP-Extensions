#pragma warning disable CS0618, CS0672

using lilToon.URP.Extensions.GeometryBuffer;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Debugging
{
    // Keep the type and script GUID so existing renderer assets remain valid.
    [DisallowMultipleRendererFeature("Ho-DebugTile (External Systems)")]
    public sealed class HoDebugTileRendererFeature : ScriptableRendererFeature
    {
        public const string NoneViewId = "";
        public const string AllRegisteredViewId = "__AllRegistered";
        [SerializeField] private bool enabledForGameView = true;
        [SerializeField] private bool enabledForSceneView = true;
        [SerializeField] private string selectedDebugViewId = NoneViewId;
        private HoSystemDebugPass pass;
        private Material material;
        private bool warnedMissingShader;

        public override void Create()
        {
            pass?.ReleaseDebugResources();
            pass = new HoSystemDebugPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType type = renderingData.cameraData.cameraType;
            if (!((enabledForGameView && type == CameraType.Game) || (enabledForSceneView && type == CameraType.SceneView))) return;
            int mode = selectedDebugViewId == AllRegisteredViewId ? HoDebugGrid.All : 0;
            foreach (var view in HoDebugViewRegistry.SystemViews)
                if (view.ViewId == selectedDebugViewId) mode = view.ModeValue;
            if (mode == 0) { pass?.ReleaseDebugResources(); return; }
            if (material == null)
            {
                var shader = Shader.Find(HoDebugTileShaderConstants.ShaderName);
                if (shader == null)
                {
                    if (!warnedMissingShader) Debug.LogWarning("External system debug shader is missing.");
                    warnedMissingShader = true;
                    return;
                }
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            pass.Setup(material, mode);
            pass.Enqueue(renderer);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.ReleaseDebugResources();
            pass = null;
            CoreUtils.Destroy(material);
            material = null;
        }

        private sealed class HoSystemDebugPass : HoDebugRenderPass
        {
            private Material material;
            private int mode;
            private sealed class PassData
            {
                public Material material;
                public TextureHandle geometry;
                public int mode;
                public Vector2Int size;
            }

            public void Setup(Material value, int selectedMode)
            {
                material = value;
                mode = selectedMode;
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var geometry = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>().normalDepthTexture;
                if (!geometry.IsValid()) return;
                TextureHandle destination = CreateDebugTarget(graph, frameData);
                using (var builder = graph.AddRasterRenderPass<PassData>("Ho-External System Debug", out var data))
                {
                    data.material = material; data.geometry = geometry; data.mode = mode;
                    data.size = DebugSize(graph, destination);
                    builder.UseTexture(geometry, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                    {
                        ctx.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, d.geometry);
                        HoDebugGrid.Draw(ctx.cmd, d.material, d.mode, HoDebugTileShaderConstants.ModeId, HoDebugViewRegistry.SystemViews, d.size, integerMode: true);
                    });
                }
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (Shader.GetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId) == null) return;
                var cmd = CommandBufferPool.Get("Ho-External System Debug");
                HoDebugGrid.Draw(CommandBufferHelpers.GetRasterCommandBuffer(cmd), material, mode,
                    HoDebugTileShaderConstants.ModeId, HoDebugViewRegistry.SystemViews, new Vector2Int(DebugTarget.rt.width, DebugTarget.rt.height), integerMode: true);
                PublishCompatibility(renderingData.cameraData.camera);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
