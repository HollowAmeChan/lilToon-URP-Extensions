#pragma warning disable CS0618,CS0672
using System;
using lilToon.URP.Extensions.RayTracing;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.RTDI
{
    // Replace supported opaque receivers before transparents. The published
    // lighting already contains its BRDF; never multiply it by albedo again.
    internal sealed class HoRTDICompositePass:ScriptableRenderPass,IDisposable
    {
        private static readonly int DiffuseId=Shader.PropertyToID("_HoRTDICompositeDiffuse");
        private static readonly int SpecularId=Shader.PropertyToID("_HoRTDICompositeSpecular");
        private static readonly int DepthId=Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int WeightId=Shader.PropertyToID("_HoRTDICompositeWeight");
        private Material material;
        private HoRTDIContext lighting;
        private float weight;
        private RTHandle scratch;
        internal void Setup(Material material,HoRTDIContext lighting,float weight)
        {
            this.material=material;this.lighting=lighting;this.weight=Mathf.Clamp01(weight);
            renderPassEvent=RenderPassEvent.BeforeRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.Depth);
            requiresIntermediateTexture=true;
        }
        private sealed class Data
        {
            internal Material material;
            internal float weight;
            internal TextureHandle source,diffuse,specular,depth;
            internal HoRTPrimarySurfaceInputs primary;
        }
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
        {
            var resources=frame.Get<UniversalResourceData>();
            var input=frame.GetOrCreate<HoRTDIRenderGraphResources>();
            if(resources.isActiveTargetBackBuffer||!resources.activeColorTexture.IsValid()||!resources.cameraDepthTexture.IsValid()
                ||!input.diffuse.IsValid()||!input.specular.IsValid())return;
            var source=resources.activeColorTexture;
            var descriptor=graph.GetTextureDesc(source);descriptor.name="Ho-RTDI composite color";descriptor.depthBufferBits=0;
            var destination=graph.CreateTexture(descriptor);
            using(var builder=graph.AddRasterRenderPass<Data>("Ho-RTDI reference PBR composite",out var data))
            {
                data.material=material;data.weight=weight;data.source=source;data.diffuse=input.diffuse;data.specular=input.specular;
                data.depth=resources.cameraDepthTexture;data.primary=HoRTPrimarySurfaceInputs.Read(graph,frame,builder);
                builder.UseTexture(data.source);builder.UseTexture(data.diffuse);builder.UseTexture(data.specular);builder.UseTexture(data.depth);
                builder.SetRenderAttachment(destination,0,AccessFlags.WriteAll);builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static(Data d,RasterGraphContext ctx)=>
                {
                    d.primary.Bind(ctx.cmd);
                    ctx.cmd.SetGlobalTexture(DiffuseId,d.diffuse);ctx.cmd.SetGlobalTexture(SpecularId,d.specular);ctx.cmd.SetGlobalTexture(DepthId,d.depth);
                    ctx.cmd.SetGlobalFloat(WeightId,d.weight);
                    Blitter.BlitTexture(ctx.cmd,d.source,new Vector4(1,1,0,0),d.material,0);
                });
            }
            resources.cameraColor=destination;
        }
        public override void OnCameraSetup(CommandBuffer cmd,ref RenderingData data)
        {
            var descriptor=data.cameraData.cameraTargetDescriptor;descriptor.depthBufferBits=0;descriptor.msaaSamples=1;
            RenderingUtils.ReAllocateIfNeeded(ref scratch,descriptor,FilterMode.Point,TextureWrapMode.Clamp,name:"Ho-RTDI composite scratch");
        }
        public override void Execute(ScriptableRenderContext rendering,ref RenderingData data)
        {
            var source=data.cameraData.renderer.cameraColorTargetHandle;
            var cmd=CommandBufferPool.Get("Ho-RTDI reference PBR composite");
            HoRTPrimarySurfaceInputs.BindCompatibility(cmd,data.cameraData);
            cmd.SetGlobalTexture(DiffuseId,lighting.PublishedDiffuse.nameID);cmd.SetGlobalTexture(SpecularId,lighting.PublishedSpecular.nameID);
            // The copied camera depth is requested explicitly. A raster depth
            // attachment may be multisampled or currently bound for writing.
            // URP publishes this as a global texture, not a temporary RT ID.
            // The shader reads that binding directly in compatibility mode.
            cmd.SetGlobalFloat(WeightId,weight);
            Blitter.BlitCameraTexture(cmd,source,scratch,material,0);Blitter.BlitCameraTexture(cmd,scratch,source);
            rendering.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
        }
        public void Dispose(){scratch?.Release();scratch=null;}
    }
}
