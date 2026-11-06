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
        private static readonly int DisplayId=Shader.PropertyToID("_HoRTGIDebugDisplay");
        private Vector4 display;
        private HoRTGIDebugMode mode;private Material material;private HoRTGIContext context;
        internal void Setup(HoRTGIDebugMode mode,Material material,HoRTGIContext context,float exposure=0,bool tonemap=false)
        {this.mode=mode;this.material=material;this.context=context;display=new Vector4(float.IsFinite(exposure)?Mathf.Clamp(exposure,-24,24):0,tonemap?1:0,0,0);renderPassEvent=RenderPassEvent.AfterRenderingPostProcessing;}
        private sealed class Data {internal Material material;internal int mode;internal Vector2Int size;internal TextureHandle diffuse,raw;internal Vector4 display;}
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
        {
            var input=frame.GetOrCreate<HoRTGIRenderGraphResources>();if(!input.diffuse.IsValid())return;
            var output=CreateDebugTarget(graph,frame);
            using(var builder=graph.AddRasterRenderPass<Data>("Ho-RTGI debug",out var d))
            {
                d.material=material;d.mode=(int)mode;d.size=DebugSize(graph,output);d.diffuse=input.diffuse;d.raw=input.rawDiffuse;
                d.display=display;
                if(d.raw.IsValid())builder.UseTexture(d.raw);
                builder.UseTexture(d.diffuse);builder.SetRenderAttachment(output,0,AccessFlags.Write);builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static(Data data,RasterGraphContext ctx)=>
                {
                    ctx.cmd.SetGlobalTexture("_HoRTGIDiffuseTexture",data.diffuse);
                    if(data.raw.IsValid())ctx.cmd.SetGlobalTexture("_HoRTGIRawDiffuseTexture",data.raw);
                    ctx.cmd.SetGlobalVector(DisplayId,data.display);
                    HoDebugGrid.Draw(ctx.cmd,data.material,data.mode,ModeId,HoRTGIDebugViewInfo.Views,data.size);
                });
            }
        }
        public override void Execute(ScriptableRenderContext rendering,ref RenderingData data)
        {
            var cmd=CommandBufferPool.Get("Ho-RTGI debug");CoreUtils.SetRenderTarget(cmd,DebugTarget);
            cmd.SetGlobalTexture("_HoRTGIDiffuseTexture",context.PublishedDiffuse.nameID);
            cmd.SetGlobalTexture("_HoRTGIRawDiffuseTexture",context.diffuse.nameID);
            cmd.SetGlobalVector(DisplayId,display);
            HoDebugGrid.Draw(CommandBufferHelpers.GetRasterCommandBuffer(cmd),material,(int)mode,ModeId,HoRTGIDebugViewInfo.Views,new Vector2Int(DebugTarget.rt.width,DebugTarget.rt.height));
            rendering.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);PublishCompatibility(data.cameraData.camera);
        }
    }
}
