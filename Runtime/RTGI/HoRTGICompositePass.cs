#pragma warning disable CS0618,CS0672
using System;
using lilToon.URP.Extensions.RayTracing;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.RTGI
{
    // Same camera/depth/fog contract as DI's verified additive composition.
    internal sealed class HoRTGICompositePass:ScriptableRenderPass,IDisposable
    {
        static readonly int DiffuseId=Shader.PropertyToID("_HoRTGICompositeDiffuse");
        static readonly int DepthId=Shader.PropertyToID("_CameraDepthTexture");
        static readonly int IntensityId=Shader.PropertyToID("_HoRTGICompositeIntensity");
        Material material;
        HoRTGIContext lighting;
        float intensity;
        RTHandle scratch;
        internal void Setup(Material value,HoRTGIContext context,float amount)
        {
            material=value;lighting=context;intensity=amount;
            renderPassEvent=RenderPassEvent.BeforeRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.Depth);requiresIntermediateTexture=true;
        }
        sealed class Data
        {
            internal Material material;internal float intensity;
            internal TextureHandle source,diffuse,depth;
            internal HoRTPrimarySurfaceInputs primary;
        }
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
        {
            var resources=frame.Get<UniversalResourceData>();var input=frame.GetOrCreate<HoRTGIRenderGraphResources>();
            if(resources.isActiveTargetBackBuffer||!resources.activeColorTexture.IsValid()||!resources.cameraDepthTexture.IsValid()||!input.diffuse.IsValid())return;
            var descriptor=graph.GetTextureDesc(resources.activeColorTexture);descriptor.name="Ho-RTGI composite color";descriptor.depthBufferBits=0;
            var destination=graph.CreateTexture(descriptor);
            using(var builder=graph.AddRasterRenderPass<Data>("Ho-RTGI additive composite",out var data))
            {
                data.material=material;data.intensity=intensity;data.source=resources.activeColorTexture;data.diffuse=input.diffuse;data.depth=resources.cameraDepthTexture;
                data.primary=HoRTPrimarySurfaceInputs.Read(graph,frame,builder);
                builder.UseTexture(data.source);builder.UseTexture(data.diffuse);builder.UseTexture(data.depth);
                builder.SetRenderAttachment(destination,0,AccessFlags.WriteAll);builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static(Data d,RasterGraphContext ctx)=>
                {
                    d.primary.Bind(ctx.cmd);ctx.cmd.SetGlobalTexture(DiffuseId,d.diffuse);ctx.cmd.SetGlobalTexture(DepthId,d.depth);ctx.cmd.SetGlobalFloat(IntensityId,d.intensity);
                    Blitter.BlitTexture(ctx.cmd,d.source,new Vector4(1,1,0,0),d.material,0);
                });
            }
            resources.cameraColor=destination;
        }
        public override void OnCameraSetup(CommandBuffer cmd,ref RenderingData data)
        {
            var descriptor=data.cameraData.cameraTargetDescriptor;descriptor.depthBufferBits=0;descriptor.msaaSamples=1;
            RenderingUtils.ReAllocateIfNeeded(ref scratch,descriptor,FilterMode.Point,TextureWrapMode.Clamp,name:"Ho-RTGI composite scratch");
        }
        public override void Execute(ScriptableRenderContext rendering,ref RenderingData data)
        {
            var source=data.cameraData.renderer.cameraColorTargetHandle;var cmd=CommandBufferPool.Get("Ho-RTGI additive composite");
            HoRTPrimarySurfaceInputs.BindCompatibility(cmd,data.cameraData);
            cmd.SetGlobalTexture(DiffuseId,lighting.PublishedDiffuse.nameID);cmd.SetGlobalFloat(IntensityId,intensity);
            Blitter.BlitCameraTexture(cmd,source,scratch,material,0);Blitter.BlitCameraTexture(cmd,scratch,source);
            rendering.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
        }
        public void Dispose(){scratch?.Release();scratch=null;}
    }
}
