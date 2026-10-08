#pragma warning disable CS0618,CS0672
using lilToon.URP.Extensions.Debugging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
namespace lilToon.URP.Extensions.RTGI
{
    public static class HoRTGIDebugViewInfo
    {
        public static readonly HoDebugViewInfo[] Views=HoDebugGrid.EnumViews<HoRTGIDebugMode>("RTGI","Hidden/lilToon/URP/RTGI/Debug",
            "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTGI/Shaders/HoRTGIDebug.shader");
    }
    internal sealed class HoRTGIDebugPass:HoDebugRenderPass
    {
        private static readonly int ModeId=Shader.PropertyToID("_HoRTGIDebugMode");
        private HoRTGIDebugMode mode;private Material material;private HoRTGIContext context;
        internal void Setup(HoRTGIDebugMode mode,Material material,HoRTGIContext context)
        {this.mode=mode;this.material=material;this.context=context;renderPassEvent=RenderPassEvent.AfterRenderingPostProcessing;}
        private sealed class Data {internal Material material;internal int mode;internal Vector2Int size;internal TextureHandle diffuse;}
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
        {
            var input=frame.GetOrCreate<HoRTGIRenderGraphResources>();if(!input.diffuse.IsValid())return;
            var output=CreateDebugTarget(graph,frame);
            using(var builder=graph.AddRasterRenderPass<Data>("Ho-RTGI debug",out var d))
            {
                d.material=material;d.mode=(int)mode;d.size=DebugSize(graph,output);d.diffuse=input.diffuse;
                builder.UseTexture(d.diffuse);builder.SetRenderAttachment(output,0,AccessFlags.Write);builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static(Data data,RasterGraphContext ctx)=>
                {
                    ctx.cmd.SetGlobalTexture("_HoRTGIDiffuseTexture",data.diffuse);
                    HoDebugGrid.Draw(ctx.cmd,data.material,data.mode,ModeId,HoRTGIDebugViewInfo.Views,data.size);
                });
            }
        }
        public override void Execute(ScriptableRenderContext rendering,ref RenderingData data)
        {
            var cmd=CommandBufferPool.Get("Ho-RTGI debug");CoreUtils.SetRenderTarget(cmd,DebugTarget);
            cmd.SetGlobalTexture("_HoRTGIDiffuseTexture",context.diffuse.nameID);
            HoDebugGrid.Draw(CommandBufferHelpers.GetRasterCommandBuffer(cmd),material,(int)mode,ModeId,HoRTGIDebugViewInfo.Views,new Vector2Int(DebugTarget.rt.width,DebugTarget.rt.height));
            rendering.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);PublishCompatibility(data.cameraData.camera);
        }
    }
}
