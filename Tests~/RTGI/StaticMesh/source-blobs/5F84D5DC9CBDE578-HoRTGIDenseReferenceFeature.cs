#pragma warning disable CS0618,CS0672
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.RayTracing;
using lilToon.URP.Extensions.RTGI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // Diagnostic only: a sample lattice over selected receivers, after the same
    // full-resolution primary/scene/light production. No reusable GI history.
    internal sealed class HoRTGIDenseReferenceFeature:ScriptableRendererFeature
    {
        [StructLayout(LayoutKind.Sequential)]internal struct PrimaryProbe{internal Vector4 originalWorld,rabWorld,normalDepth,albedo,errors0,errors1;}
        internal HoRTGIRendererFeature source;internal Camera targetCamera;internal bool collect;internal int batch;
        internal HoRTGIContext context;internal GraphicsBuffer pixels,probe,rayProbe;internal int samplesPerPoint,pointCount,lastLightCount,lastInstances,lastCutouts;
        internal bool scheduled;
        internal HoRTGIRendererFeature.PassSettings lastSettings;internal bool lastEnvironment;
        internal int lastLightingSignature;internal uint lastSceneRevision,lastSkyRevision;
        ComputeShader shader;RayTracingShader rayShader;ProbePass pass;
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        public override void Create()=>pass=new ProbePass{owner=this};
        internal void Initialize(int samples,Vector2Int[] receivers)
        {
            if(context!=null)throw new Exception("Dense reference already initialized");
            const string root="Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTGI/Shaders/";
            shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(root+"HoRTGIDenseReference.compute");rayShader=AssetDatabase.LoadAssetAtPath<RayTracingShader>(root+"HoRTGIDenseReference.raytrace");
            if(shader==null||rayShader==null)throw new Exception("Missing dense reference shader wrappers");
            shader.FindKernel("Final");shader.FindKernel("ProbeDensePrimary");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(message.message);
            samplesPerPoint=samples;pointCount=receivers.Length;
            pixels=new GraphicsBuffer(GraphicsBuffer.Target.Structured,pointCount,8);pixels.SetData(receivers);
            probe=new GraphicsBuffer(GraphicsBuffer.Target.Structured,pointCount,Marshal.SizeOf<PrimaryProbe>());
            rayProbe=new GraphicsBuffer(GraphicsBuffer.Target.Structured,pointCount*4,16);
            context=new HoRTGIContext();context.Prepare(samples,pointCount,1,GraphicsFormat.R32G32B32A32_SFloat);
        }
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {if(collect&&data.cameraData.camera==targetCamera)renderer.EnqueuePass(pass);}
        protected override void Dispose(bool disposing)
        {context?.Dispose();context=null;pixels?.Dispose();probe?.Dispose();rayProbe?.Dispose();pixels=probe=rayProbe=null;}
        sealed class ProbePass:ScriptableRenderPass
        {
            internal HoRTGIDenseReferenceFeature owner;
            internal ProbePass(){renderPassEvent=RenderPassEvent.AfterRenderingPostProcessing;}
            sealed class Data
            {
                internal HoRTPrimarySurfaceInputs primary;internal HoRTScene.Inputs scene;
                internal HoRTGIContext reference,main;internal HoRTGIRendererFeature.PassSettings settings;
                internal ComputeShader shader;internal RayTracingShader rayShader;internal GraphicsBuffer pixels,probe,rayProbe;
                internal int lightCount,seed;internal bool environment;internal TextureHandle sky;
                internal Vector4 skyParameters,skyTint,skyConstant;
            }
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var camera=frame.Get<UniversalCameraData>().camera;
                var contexts=(Dictionary<Camera,HoRTGIContext>)typeof(HoRTGIRendererFeature).GetField("contexts",Flags).GetValue(owner.source);
                if(!contexts.TryGetValue(camera,out var main))throw new Exception("Full camera GI context missing");
                var registry=(HoRTScene)typeof(HoRTGIRendererFeature).GetField("scene",Flags).GetValue(owner.source);
                var mainPass=typeof(HoRTGIRendererFeature).GetField("pass",Flags).GetValue(owner.source);
                var settings=(HoRTGIRendererFeature.PassSettings)mainPass.GetType().GetField("settings",Flags).GetValue(mainPass);
                int lightCount=(int)mainPass.GetType().GetField("lightCount",Flags).GetValue(mainPass);
                owner.lastLightCount=lightCount;owner.lastInstances=registry.InstanceCount;owner.lastCutouts=registry.CutoutCount;owner.lastSettings=settings;
                owner.lastLightingSignature=(int)mainPass.GetType().GetField("signature",Flags).GetValue(mainPass);owner.lastSceneRevision=registry.Revision;
                owner.context.PrepareVisibility(registry.CutoutCount>0);
                using(var builder=graph.AddUnsafePass<Data>("GI dense no-reuse one-bounce reference",out var d))
                {
                    d.primary=HoRTPrimarySurfaceInputs.Read(graph,frame,builder);d.scene=registry.Read(graph,builder);
                    d.reference=owner.context;d.main=main;d.settings=settings;d.lightCount=lightCount;d.seed=104729+owner.batch*8191;
                    d.shader=owner.shader;d.rayShader=owner.rayShader;d.pixels=owner.pixels;d.probe=owner.probe;d.rayProbe=owner.rayProbe;
                    var sky=frame.GetOrCreate<HoAtmosphereRenderGraphResources>();d.sky=TextureHandle.nullHandle;d.skyParameters=d.skyTint=d.skyConstant=Vector4.zero;
                    owner.lastSkyRevision=sky.revision;
                    d.environment=main.environmentSampling&&settings.useEnvironment&&sky.IsValid;owner.lastEnvironment=d.environment;
                    if(sky.IsValid){d.sky=sky.radianceTexture;d.skyParameters=sky.parameters;d.skyTint=sky.tint;d.skyConstant=sky.constantColor;builder.UseTexture(d.sky);}
                    foreach(var buffer in new[]{d.reference.reservoirs,d.reference.history,d.reference.secondary,d.reference.finalVisibility})builder.UseBuffer(graph.ImportBuffer(buffer),AccessFlags.ReadWrite);
                    foreach(var buffer in new[]{main.lights,main.history,main.environmentRis,d.pixels})builder.UseBuffer(graph.ImportBuffer(buffer));
                    if(main.environmentPdf!=null)builder.UseTexture(graph.ImportTexture(main.environmentPdf));
                    builder.UseBuffer(graph.ImportBuffer(d.probe),AccessFlags.WriteAll);builder.UseTexture(graph.ImportTexture(d.reference.diffuse),AccessFlags.WriteAll);
                    builder.UseBuffer(graph.ImportBuffer(d.rayProbe),AccessFlags.WriteAll);
                    builder.SetRenderFunc(static(Data value,UnsafeGraphContext ctx)=>Record(CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd),value));
                }
            }
            static void Record(CommandBuffer cmd,Data d)
            {
                var c=d.reference;var frame=new Vector4(c.width,c.height,d.lightCount,0);var settings=new Vector4(d.settings.secondaryLightSamples,0,0,d.settings.rayBias);
                var environment=new Vector4(HoRTGIContext.EnvironmentPdfSize,d.settings.secondaryEnvironmentSamples,d.environment?1:0,0);var ris=new Vector4(HoRTGIContext.RisTileSize,HoRTGIContext.RisTileCount,0,0);
                d.primary.Bind(CommandBufferHelpers.GetRasterCommandBuffer(cmd));
                d.scene.BuildAndBind(cmd,d.rayShader);cmd.SetRayTracingShaderPass(d.rayShader,"HoRTGISecondarySurface");
                cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIFrame",frame);cmd.SetRayTracingIntParam(d.rayShader,"_HoRTGIFrameIndex",d.seed);
                cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGISettings",settings);cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIFlags",new Vector4(0,0,1,c.alphaVisibility?1:0));
                cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIReservoirLayout",new Vector4(c.rowPitch,c.arrayPitch,0,0));
                cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIRaySettings",new Vector4(d.settings.rayBias,d.settings.maxDistance,d.settings.useEnvironment&&d.settings.includeDirectEnvironment?1:0,100000));
                cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIEnvironmentRISLayout",ris);cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIEnvironment",environment);cmd.SetRayTracingIntParam(d.rayShader,"_HoRTGIEnvironmentBrdfSamples",0);
                cmd.SetRayTracingVectorParam(d.rayShader,HoAtmosphereBindings.ParametersId,d.skyParameters);cmd.SetRayTracingVectorParam(d.rayShader,HoAtmosphereBindings.TintId,d.skyTint);cmd.SetRayTracingVectorParam(d.rayShader,HoAtmosphereBindings.ConstantId,d.skyConstant);
                if(d.sky.IsValid())cmd.SetRayTracingTextureParam(d.rayShader,HoAtmosphereBindings.RadianceId,d.sky);
                else cmd.SetRayTracingTextureParam(d.rayShader,HoAtmosphereBindings.RadianceId,CoreUtils.blackCubeTexture);
                cmd.SetRayTracingTextureParam(d.rayShader,"_HoRTGIEnvironmentPdf",d.main.environmentPdf!=null?d.main.environmentPdf.nameID:new RenderTargetIdentifier(Texture2D.blackTexture));
                cmd.SetRayTracingBufferParam(d.rayShader,"_HoGIDensePixels",d.pixels);cmd.SetRayTracingBufferParam(d.rayShader,"_HoRTGISecondary",c.secondary);cmd.SetRayTracingBufferParam(d.rayShader,"_HoRTGIReservoirs",c.reservoirs);
                cmd.SetRayTracingBufferParam(d.rayShader,"_HoRTGIHistory",c.history);cmd.SetRayTracingBufferParam(d.rayShader,"_HoRTGINeighbors",c.neighbors);
                cmd.SetRayTracingBufferParam(d.rayShader,"_HoRTGILights",d.main.lights);cmd.SetRayTracingBufferParam(d.rayShader,"_HoRTGIEnvironmentRIS",d.main.environmentRis);cmd.SetRayTracingBufferParam(d.rayShader,"_HoRTGIFinalVisibility",c.finalVisibility);
                cmd.SetRayTracingBufferParam(d.rayShader,"_HoGIDenseRayPrimary",d.rayProbe);cmd.DispatchRays(d.rayShader,"ProbeDenseRayPrimary",(uint)c.height,1,1);
                cmd.DispatchRays(d.rayShader,"GenerateSecondary",(uint)c.width,(uint)c.height,1);
                cmd.DispatchRays(d.rayShader,"ShadeSecondaryNative",(uint)c.width,(uint)c.height,1);
                if(c.alphaVisibility)cmd.DispatchRays(d.rayShader,"TraceFinalVisibility",(uint)c.width,(uint)c.height,1);
                int final=d.shader.FindKernel("Final");d.scene.BuildAndBind(cmd,d.shader,final);Bind(final);
                cmd.SetComputeBufferParam(d.shader,final,"_HoRTGIReservoirs",c.reservoirs);cmd.SetComputeBufferParam(d.shader,final,"_HoRTGISecondary",c.secondary);cmd.SetComputeBufferParam(d.shader,final,"_HoRTGIHistory",c.history);cmd.SetComputeBufferParam(d.shader,final,"_HoRTGIFinalVisibility",c.finalVisibility);cmd.SetComputeTextureParam(d.shader,final,"_HoRTGIDiffuse",c.diffuse.nameID);
                cmd.DispatchCompute(d.shader,final,(c.width+7)/8,(c.height+7)/8,1);
                int probe=d.shader.FindKernel("ProbeDensePrimary");Bind(probe);cmd.SetComputeBufferParam(d.shader,probe,"_HoGIDenseMainHistory",d.main.history);cmd.SetComputeBufferParam(d.shader,probe,"_HoGIDenseProbe",d.probe);cmd.DispatchCompute(d.shader,probe,(c.height+15)/16,1,1);
                void Bind(int kernel)
                {
                    d.primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd),d.shader,kernel);cmd.SetComputeBufferParam(d.shader,kernel,"_HoGIDensePixels",d.pixels);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTGIFrame",frame);cmd.SetComputeVectorParam(d.shader,"_HoRTGISettings",settings);cmd.SetComputeVectorParam(d.shader,"_HoRTGIFlags",new Vector4(0,0,1,c.alphaVisibility?1:0));cmd.SetComputeIntParam(d.shader,"_HoRTGIFrameIndex",d.seed);cmd.SetComputeVectorParam(d.shader,"_HoRTGIReservoirLayout",new Vector4(c.rowPitch,c.arrayPitch,0,0));
                }
            }
        }
    }
}
