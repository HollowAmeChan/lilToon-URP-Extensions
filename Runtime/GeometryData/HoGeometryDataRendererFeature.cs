using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GeometryData
{
    [DisallowMultipleRendererFeature("Ho-GeometryData")]
    public sealed class HoGeometryDataRendererFeature : ScriptableRendererFeature
    {
        private PublishPass pass;
        private bool registered;
        public override void Create()
        {
            pass=new PublishPass();
            if(!registered){RenderPipelineManager.beginCameraRendering+=ResetCamera;registered=true;}
        }
        private static void ResetCamera(ScriptableRenderContext context,Camera camera)
            => Shader.SetGlobalFloat(HoOutlineDataRegistry.AvailableId,0);
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData) => renderer.EnqueuePass(pass);
        protected override void Dispose(bool disposing)
        {
            if(registered){RenderPipelineManager.beginCameraRendering-=ResetCamera;registered=false;}
            Shader.SetGlobalFloat(HoOutlineDataRegistry.AvailableId,0);
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
