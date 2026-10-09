#pragma warning disable CS0618,CS0672
using lilToon.URP.Extensions.Debugging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
namespace lilToon.URP.Extensions.RTDI
{
    public static class HoRTDIDebugViewInfo
    {
        public static readonly HoDebugViewInfo[] Views=HoDebugGrid.EnumViews<HoRTDIDebugMode>("RTDI","Hidden/lilToon/URP/RTDI/Debug",
            "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/HoRTDIDebug.shader");
    }
    internal sealed class HoRTDIDebugPass:HoDebugRenderPass
    {
        private static readonly int ModeId=Shader.PropertyToID("_HoRTDIDebugMode");
        private HoRTDIDebugMode mode;private Material material;private HoRTDIContext context;
        internal void Setup(HoRTDIDebugMode mode,Material material,HoRTDIContext context)
        {this.mode=mode;this.material=material;this.context=context;renderPassEvent=RenderPassEvent.AfterRenderingPostProcessing;}
        private sealed class Data {internal Material material;internal int mode;internal Vector2Int size;internal TextureHandle diffuse,specular,rawDiffuse,rawSpecular;}
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
        {
            var input=frame.GetOrCreate<HoRTDIRenderGraphResources>();if(!input.diffuse.IsValid()||!input.specular.IsValid())return;
            var output=CreateDebugTarget(graph,frame);
            using(var builder=graph.AddRasterRenderPass<Data>("Ho-RTDI debug",out var d))
            {
                d.material=material;d.mode=(int)mode;d.size=DebugSize(graph,output);d.diffuse=input.diffuse;d.specular=input.specular;
                d.rawDiffuse=input.rawDiffuse;d.rawSpecular=input.rawSpecular;
                builder.UseTexture(d.diffuse);builder.UseTexture(d.specular);builder.SetRenderAttachment(output,0,AccessFlags.Write);builder.AllowGlobalStateModification(true);
                builder.UseTexture(d.rawDiffuse);builder.UseTexture(d.rawSpecular);
                builder.SetRenderFunc(static(Data data,RasterGraphContext ctx)=>
                {
                    ctx.cmd.SetGlobalTexture("_HoRTDIDiffuseTexture",data.diffuse);ctx.cmd.SetGlobalTexture("_HoRTDISpecularTexture",data.specular);
                    ctx.cmd.SetGlobalTexture("_HoRTDIRawDiffuseTexture",data.rawDiffuse);ctx.cmd.SetGlobalTexture("_HoRTDIRawSpecularTexture",data.rawSpecular);
                    HoDebugGrid.Draw(ctx.cmd,data.material,data.mode,ModeId,HoRTDIDebugViewInfo.Views,data.size);
                });
            }
        }
        public override void Execute(ScriptableRenderContext rendering,ref RenderingData data)
        {
            var cmd=CommandBufferPool.Get("Ho-RTDI debug");CoreUtils.SetRenderTarget(cmd,DebugTarget);
            cmd.SetGlobalTexture("_HoRTDIDiffuseTexture",context.PublishedDiffuse.nameID);cmd.SetGlobalTexture("_HoRTDISpecularTexture",context.PublishedSpecular.nameID);
            cmd.SetGlobalTexture("_HoRTDIRawDiffuseTexture",context.diffuse.nameID);cmd.SetGlobalTexture("_HoRTDIRawSpecularTexture",context.specular.nameID);
            HoDebugGrid.Draw(CommandBufferHelpers.GetRasterCommandBuffer(cmd),material,(int)mode,ModeId,HoRTDIDebugViewInfo.Views,new Vector2Int(DebugTarget.rt.width,DebugTarget.rt.height));
            rendering.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);PublishCompatibility(data.cameraData.camera);
        }
    }
}
