using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GeometryData
{
    [DisallowMultipleRendererFeature("Ho-GeometryData")]
    public sealed class HoGeometryDataRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private HoGeometryDataSettings settings = new HoGeometryDataSettings();
        public HoGeometryDataSettings Settings => settings;
        public Camera LastCamera { get; private set; }
        public int LastScheduledFrame { get; private set; } = -1;
        public string DebugStatus { get; private set; } = "预览关闭";
        private PublishPass pass;
        private HoGeometryTensionPass tensionPass;
        private HoGeometryDataDebugPass debugPass;
        private Material debugMaterial;
        private bool registered;
        public override void Create()
        {
            tensionPass?.ReleaseBorrowed();
            debugPass?.Dispose();
            pass=new PublishPass();tensionPass=new HoGeometryTensionPass();
            debugPass = new HoGeometryDataDebugPass();
            if(!registered){RenderPipelineManager.beginCameraRendering+=ResetCamera;RenderPipelineManager.endCameraRendering+=EndCamera;registered=true;}
        }
        private static void ResetCamera(ScriptableRenderContext context,Camera camera)
        {
            Shader.SetGlobalFloat(HoOutlineDataRegistry.AvailableId,0);
            Shader.SetGlobalFloat(HoTensionDataRegistry.AvailableId,0);
        }
        private void EndCamera(ScriptableRenderContext context,Camera camera) => tensionPass?.ReleaseBorrowed();
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData)
        {
            if (!settings.enabled) { DebugStatus = "运行关闭"; return; }
            LastCamera = renderingData.cameraData.camera; LastScheduledFrame = Time.frameCount;
            // Allocate Tension slots before building the outline table, so both tables cover the shared addressing range.
            HoTensionDataRegistry.Capture(out _,out _);
            renderer.EnqueuePass(pass);renderer.EnqueuePass(tensionPass);
            DebugStatus = "预览关闭";
            if (!ShouldDebug(renderingData.cameraData.camera)) return;
            if (debugMaterial == null)
            {
                var shader = Resources.Load<Shader>("HoGeometryDataDebug");
                if (shader == null || !shader.isSupported) { DebugStatus = "数据预览 Shader 不可用"; return; }
                debugMaterial = CoreUtils.CreateEngineMaterial(shader);
            }
            debugPass.Setup(settings, debugMaterial); renderer.EnqueuePass(debugPass);
            DebugStatus = "已安排数据预览";
        }
        private bool ShouldDebug(Camera camera) => settings.debugMode != HoGeometryDataDebugMode.Off && camera != null
            && (camera.cameraType == CameraType.SceneView ? settings.debugInSceneView
                : camera.cameraType == CameraType.Game && settings.debugInGameView);
#pragma warning disable CS0672, CS0618
        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (settings.enabled && ShouldDebug(renderingData.cameraData.camera))
                debugPass.SetupCompatibility(renderer.cameraColorTargetHandle);
        }
#pragma warning restore CS0672, CS0618
        protected override void Dispose(bool disposing)
        {
            if(registered){RenderPipelineManager.beginCameraRendering-=ResetCamera;RenderPipelineManager.endCameraRendering-=EndCamera;registered=false;}
            tensionPass?.ReleaseBorrowed();
            debugPass?.Dispose(); CoreUtils.Destroy(debugMaterial); debugMaterial = null;
            Shader.SetGlobalFloat(HoOutlineDataRegistry.AvailableId,0);
            Shader.SetGlobalFloat(HoTensionDataRegistry.AvailableId,0);
        }

        private sealed class PublishPass : ScriptableRenderPass
        {
            private sealed class Data { public BufferHandle entries,values;public int count; }
            public PublishPass(){renderPassEvent=RenderPassEvent.BeforeRendering;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
            {
                HoOutlineDataRegistry.Capture(out var entries,out var values);
                using(var builder=graph.AddRasterRenderPass<Data>("Ho-GD Outline Publish",out var data))
                {
                    data.entries=graph.ImportBuffer(entries);data.values=graph.ImportBuffer(values);data.count=HoOutlineDataRegistry.EntryCount;
                    builder.UseBuffer(data.entries,AccessFlags.Read);builder.UseBuffer(data.values,AccessFlags.Read);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (Data d,RasterGraphContext context)=>
                    {
                        context.cmd.SetGlobalBuffer(HoOutlineDataRegistry.EntriesId,d.entries);
                        context.cmd.SetGlobalBuffer(HoOutlineDataRegistry.ValuesId,d.values);
                        context.cmd.SetGlobalInt(HoOutlineDataRegistry.CountId,d.count);
                        context.cmd.SetGlobalFloat(HoOutlineDataRegistry.AvailableId,1);
                    });
                }
            }
#pragma warning disable CS0672, CS0618
            public override void Execute(ScriptableRenderContext context,ref RenderingData renderingData)
            {
                HoOutlineDataRegistry.Capture(out var entries,out var values);
                var cmd=CommandBufferPool.Get("Ho-GD Outline Publish");
                cmd.SetGlobalBuffer(HoOutlineDataRegistry.EntriesId,entries);cmd.SetGlobalBuffer(HoOutlineDataRegistry.ValuesId,values);
                cmd.SetGlobalInt(HoOutlineDataRegistry.CountId,HoOutlineDataRegistry.EntryCount);cmd.SetGlobalFloat(HoOutlineDataRegistry.AvailableId,1);
                context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
            }
#pragma warning restore CS0672, CS0618
        }
    }
}
