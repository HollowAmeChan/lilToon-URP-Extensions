#pragma warning disable CS0618,CS0672
using lilToon.URP.Extensions.RTDI;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Copied into the isolated Player project only, not an addable package feature.
public sealed class HoRTDIPlayerCaptureFeature:ScriptableRendererFeature
{
    public Shader captureShader;
    private Material material;
    private RTHandle diffuse,raw,specular,rawSpecular,motion;
    public bool CaptureMotion;
    public int MotionCaptureCount;
    public RenderTexture Motion=>motion?.rt;
    private CapturePass pass;
    public RenderTexture Diffuse=>diffuse?.rt;
    public RenderTexture Raw=>raw?.rt;
    public RenderTexture Specular=>specular?.rt;
    public RenderTexture RawSpecular=>rawSpecular?.rt;
    public override void Create()
    {
        CoreUtils.Destroy(material);material=captureShader!=null?CoreUtils.CreateEngineMaterial(captureShader):null;
        var desc=new RenderTextureDescriptor(64,64){graphicsFormat=GraphicsFormat.R16G16B16A16_SFloat,depthStencilFormat=GraphicsFormat.None,msaaSamples=1};
        RenderingUtils.ReAllocateIfNeeded(ref diffuse,desc,FilterMode.Point,TextureWrapMode.Clamp,name:"Player filtered capture");
        RenderingUtils.ReAllocateIfNeeded(ref raw,desc,FilterMode.Point,TextureWrapMode.Clamp,name:"Player raw capture");
        RenderingUtils.ReAllocateIfNeeded(ref specular,desc,FilterMode.Point,TextureWrapMode.Clamp,name:"Player specular capture");
        RenderingUtils.ReAllocateIfNeeded(ref rawSpecular,desc,FilterMode.Point,TextureWrapMode.Clamp,name:"Player raw specular capture");
        desc.graphicsFormat=GraphicsFormat.R16G16_SFloat;RenderingUtils.ReAllocateIfNeeded(ref motion,desc,FilterMode.Point,TextureWrapMode.Clamp,name:"Player motion capture");
        pass=new CapturePass(this);
    }
    public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
    {pass.ConfigureInput(CaptureMotion?ScriptableRenderPassInput.Motion:ScriptableRenderPassInput.None);renderer.EnqueuePass(pass);}
    protected override void Dispose(bool disposing){diffuse?.Release();raw?.Release();specular?.Release();rawSpecular?.Release();motion?.Release();CoreUtils.Destroy(material);}
    private sealed class CapturePass:ScriptableRenderPass
    {
        private readonly HoRTDIPlayerCaptureFeature owner;
        public CapturePass(HoRTDIPlayerCaptureFeature owner){this.owner=owner;renderPassEvent=RenderPassEvent.AfterRenderingPostProcessing;}
        private sealed class Data {internal TextureHandle diffuse,raw,specular,rawSpecular,outDiffuse,outRaw,outSpecular,outRawSpecular,motion,outMotion;internal HoRTDIPlayerCaptureFeature owner;}
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
        {
            var input=frame.GetOrCreate<HoRTDIRenderGraphResources>();if(!input.diffuse.IsValid())return;
            using(var builder=graph.AddUnsafePass<Data>("Player DI capture",out var data))
            {
                data.diffuse=input.diffuse;data.raw=input.rawDiffuse;data.specular=input.specular;data.rawSpecular=input.rawSpecular;
                data.outDiffuse=graph.ImportTexture(owner.diffuse);data.outRaw=graph.ImportTexture(owner.raw);data.outSpecular=graph.ImportTexture(owner.specular);data.outRawSpecular=graph.ImportTexture(owner.rawSpecular);
                builder.UseTexture(data.diffuse);builder.UseTexture(data.raw);builder.UseTexture(data.specular);builder.UseTexture(data.rawSpecular);
                builder.UseTexture(data.outDiffuse,AccessFlags.WriteAll);builder.UseTexture(data.outRaw,AccessFlags.WriteAll);builder.UseTexture(data.outSpecular,AccessFlags.WriteAll);builder.UseTexture(data.outRawSpecular,AccessFlags.WriteAll);
                if(owner.CaptureMotion)
                {data.owner=owner;data.motion=frame.Get<UniversalResourceData>().motionVectorColor;data.outMotion=graph.ImportTexture(owner.motion);if(data.motion.IsValid())builder.UseTexture(data.motion);builder.UseTexture(data.outMotion,AccessFlags.WriteAll);}
                builder.AllowPassCulling(false);builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static(Data d,UnsafeGraphContext ctx)=>
                {
                    var cmd=CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    cmd.Blit((RenderTargetIdentifier)d.diffuse,(RenderTargetIdentifier)d.outDiffuse);
                    cmd.Blit((RenderTargetIdentifier)d.raw,(RenderTargetIdentifier)d.outRaw);
                    cmd.Blit((RenderTargetIdentifier)d.specular,(RenderTargetIdentifier)d.outSpecular);
                    cmd.Blit((RenderTargetIdentifier)d.rawSpecular,(RenderTargetIdentifier)d.outRawSpecular);
                    if(d.motion.IsValid()){cmd.CopyTexture(d.motion,d.outMotion);d.owner.MotionCaptureCount++;}
                });
            }
        }
        public override void Execute(ScriptableRenderContext context,ref RenderingData data)
        {
            var cmd=CommandBufferPool.Get("Player DI capture");
            // Global shader bindings are not temporary RT identifiers.
            cmd.Blit(Texture2D.blackTexture,owner.diffuse.nameID,owner.material,0);
            cmd.Blit(Texture2D.blackTexture,owner.raw.nameID,owner.material,1);
            cmd.Blit(Texture2D.blackTexture,owner.specular.nameID,owner.material,2);
            cmd.Blit(Texture2D.blackTexture,owner.rawSpecular.nameID,owner.material,3);
            if(owner.CaptureMotion){cmd.Blit(Texture2D.blackTexture,owner.motion.nameID,owner.material,4);owner.MotionCaptureCount++;}
            context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
        }
    }
}
