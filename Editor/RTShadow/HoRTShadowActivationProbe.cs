#pragma warning disable CS0618,CS0672
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
namespace lilToon.URP.Extensions.Editor.RTShadow
{
    [SupportedOnRenderer()]
    internal sealed class HoRTShadowActivationProbe:ScriptableRendererFeature
    {
        internal Material material;internal RTHandle target;internal bool poison;
        private Poison before;private Observe after;
        public override void Create(){before=new Poison{owner=this};after=new Observe{owner=this};}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data){if(poison)renderer.EnqueuePass(before);renderer.EnqueuePass(after);}
        private sealed class Poison:ScriptableRenderPass
        {
            internal HoRTShadowActivationProbe owner;internal Poison(){renderPassEvent=RenderPassEvent.BeforeRenderingOpaques;}
            private sealed class Data{}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {using(var builder=graph.AddUnsafePass<Data>("Inject stale RTShadow activation",out _)){builder.AllowGlobalStateModification(true);builder.AllowPassCulling(false);builder.SetRenderFunc(static(Data d,UnsafeGraphContext c)=>c.cmd.SetGlobalFloat(Shader.PropertyToID("_HoRTShadowActive"),1));}}
            public override void Execute(ScriptableRenderContext context,ref RenderingData data)
            {var cmd=CommandBufferPool.Get();cmd.SetGlobalFloat("_HoRTShadowActive",1);context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);}
        }
        private sealed class Observe:ScriptableRenderPass
        {
            internal HoRTShadowActivationProbe owner;internal Observe(){renderPassEvent=(RenderPassEvent)((int)RenderPassEvent.BeforeRenderingOpaques+1);}
            private sealed class Data{internal Material material;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {using(var builder=graph.AddRasterRenderPass<Data>("Observe actual GPU RTShadow activation",out var d)){d.material=owner.material;builder.SetRenderAttachment(graph.ImportTexture(owner.target),0,AccessFlags.Write);builder.AllowPassCulling(false);builder.SetRenderFunc(static(Data d,RasterGraphContext c)=>c.cmd.DrawProcedural(Matrix4x4.identity,d.material,0,MeshTopology.Triangles,3,1));}}
            public override void OnCameraSetup(CommandBuffer cmd,ref RenderingData data){ConfigureTarget(owner.target);}
            public override void Execute(ScriptableRenderContext context,ref RenderingData data)
            {var cmd=CommandBufferPool.Get();CoreUtils.SetRenderTarget(cmd,owner.target);cmd.DrawProcedural(Matrix4x4.identity,owner.material,0,MeshTopology.Triangles,3,1);context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);}
        }
    }
}
