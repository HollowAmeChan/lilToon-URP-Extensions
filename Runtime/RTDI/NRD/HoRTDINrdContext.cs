#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.RayTracing;

namespace lilToon.URP.Extensions.RTDI.NRD
{
    // NRD core chooses passes/constants/pools; Unity owns and dispatches GPU resources.
    internal sealed class HoRTDINrdContext:IDisposable
    {
        private IntPtr instance;
        private readonly HoRTDINrdNative.InstanceDesc description;
        private readonly ComputeShader[] shaders;
        private readonly int[][] bindings;
        private readonly int[] kernels;
        private readonly uint[] identifiers={0};
        private readonly HoRTDINrdNative.DispatchDesc[] passes;
        private readonly HoRTDINrdNative.ResourceDesc[,] resources;
        private readonly int[] constantWords=new int[ConstantBytes/4];
        private readonly RTHandle[] permanent,transient;
        private readonly GraphicsBuffer[] constants=new GraphicsBuffer[3];
        private readonly Dictionary<uint,string> names=new();
        private static readonly int[] signalHistoryBindings={Shader.PropertyToID("_HoRTDINrdHistorySpec"),Shader.PropertyToID("_HoRTDINrdHistoryDiff"),Shader.PropertyToID("_HoRTDINrdHistorySpecFast"),Shader.PropertyToID("_HoRTDINrdHistoryDiffFast")};
        private int width,height;
        private const int ConstantBytes=65536;
        private readonly int constantStride;
        private readonly HoRTDINrdTiming timing=new();
        internal HoRTDINrdNative.RelaxSettings AppliedSettings {get;private set;}=HoRTDINrdNative.RelaxSettings.DenseDI();
        internal bool HasCutoutVisibility;
        #if UNITY_EDITOR
        // Isolated stage/settings A/B only; no serialized/runtime product option.
        internal Func<HoRTDINrdNative.RelaxSettings,HoRTDINrdNative.RelaxSettings> ValidationSettings;
        internal bool ValidationPreserveWorldHistory;
        #endif
        #if UNITY_EDITOR
        internal double? ValidationTimeSeconds;
        internal Action<CommandBuffer,int,RenderTargetIdentifier> ValidationSpecularStage;
        #endif
        internal float SetRenderTime(double time,out bool reset)
        {
            #if UNITY_EDITOR
            time=ValidationTimeSeconds??time;
            #endif
            reset=timing.Advance(time);return timing.Seconds*1000;
        }
        internal int LastDispatchCount {get;private set;}
        internal HoRTDINrdContext(ComputeShader[] shaders)
        {
            instance=HoRTDINrdNative.Create();description=HoRTDINrdNative.Read<HoRTDINrdNative.InstanceDesc>(HoRTDINrdNative.GetInstanceDesc(instance));
            try
            {
                if(description.pipelineCount!=HoRTDINrdManifest.Identifiers.Length||shaders==null||shaders.Length!=description.pipelineCount)
                    throw new InvalidOperationException("NRD shader library count mismatch");
                this.shaders=(ComputeShader[])shaders.Clone();bindings=new int[shaders.Length][];kernels=new int[shaders.Length];int maxResources=0;
                for(int i=0;i<shaders.Length;i++)
                {
                    if(shaders[i]==null)throw new InvalidOperationException("NRD Unity shader missing: "+i);
                    var pipeline=HoRTDINrdNative.Read<HoRTDINrdNative.PipelineDesc>(description.pipelines,i);
                    if(pipeline.identifier!=HoRTDINrdManifest.Identifiers[i])throw new InvalidOperationException("NRD shader permutation mismatch");
                    var resources=HoRTDINrdManifest.Resources[i];bindings[i]=new int[resources.Length];
                    for(int j=0;j<resources.Length;j++)bindings[i][j]=Shader.PropertyToID(resources[j]);
                    maxResources=Math.Max(maxResources,resources.Length);kernels[i]=shaders[i].FindKernel("main");
                }
                constantStride=((int)description.constantMax+255)&~255;
                if(constantStride<=0||constantStride>ConstantBytes)throw new InvalidOperationException("NRD constant stride exceeds storage");
                passes=new HoRTDINrdNative.DispatchDesc[ConstantBytes/constantStride];
                resources=new HoRTDINrdNative.ResourceDesc[passes.Length,maxResources];
                for(int i=0;i<constants.Length;i++)constants[i]=new GraphicsBuffer(GraphicsBuffer.Target.Constant,ConstantBytes/4,4){name="HoRTDI NRD constants "+i};
                permanent=new RTHandle[description.permanentCount];transient=new RTHandle[description.transientCount];
            }
            catch{Dispose();throw;}
        }
        internal void Prepare(int width,int height)
        {
            this.width=width;this.height=height;
            AllocatePool(permanent,description.permanent,"permanent");AllocatePool(transient,description.transient,"transient");
        }
        private void AllocatePool(RTHandle[] pool,IntPtr descriptors,string name)
        {
            for(int i=0;i<pool.Length;i++)
            {
                var texture=HoRTDINrdNative.Read<HoRTDINrdNative.TextureDesc>(descriptors,i);int factor=Math.Max(1,(int)texture.downsample);
                var format=Format(texture.format);
                if(!SystemInfo.IsFormatSupported(format,GraphicsFormatUsage.LoadStore))throw new NotSupportedException("NRD UAV format "+format);
                var desc=new RenderTextureDescriptor((width+factor-1)/factor,(height+factor-1)/factor)
                {graphicsFormat=format,depthStencilFormat=GraphicsFormat.None,msaaSamples=1,enableRandomWrite=true};
                RenderingUtils.ReAllocateIfNeeded(ref pool[i],desc,FilterMode.Point,TextureWrapMode.Clamp,name:$"HoRTDI NRD {name} {i}");
            }
        }
        private static GraphicsFormat Format(uint format)=>format switch
        {
            0=>GraphicsFormat.R8_UNorm,8=>GraphicsFormat.R8G8B8A8_UNorm,17=>GraphicsFormat.R16_SFloat,
            27=>GraphicsFormat.R16G16B16A16_SFloat,30=>GraphicsFormat.R32_SFloat,
            _=>throw new NotSupportedException("Unmapped pinned RELAX pool format "+format)
        };
        internal void Declare(RenderGraph graph,IUnsafeRenderGraphBuilder builder)
        {
            foreach(var texture in permanent)builder.UseTexture(graph.ImportTexture(texture),AccessFlags.ReadWrite);
            foreach(var texture in transient)builder.UseTexture(graph.ImportTexture(texture),AccessFlags.ReadWrite);
            foreach(var buffer in constants)builder.UseBuffer(graph.ImportBuffer(buffer),AccessFlags.ReadWrite);
        }
        internal void BindPrimaryGuide(CommandBuffer cmd,HoRTPrimarySurfaceInputs primary,bool compatibility,CameraData camera,GraphicsBuffer history,bool historyValid,GraphicsBuffer signalScale)
        {
            for(int index=4;index<=10;index++)
            {
                if(index!=4&&index!=9&&index!=10)continue;
                if(compatibility)HoRTPrimarySurfaceInputs.BindCompatibilityCompute(cmd,shaders[index],kernels[index],camera);
                else primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd),shaders[index],kernels[index]);
                if(index==4){cmd.SetComputeBufferParam(shaders[index],kernels[index],"_HoRTDIHistory",history);cmd.SetComputeIntParam(shaders[index],"_HoRTDINrdGuideHistoryValid",historyValid?1:0);cmd.SetComputeBufferParam(shaders[index],kernels[index],"_HoRTDINrdScale",signalScale);}
            }
        }
        internal void BindSignalHistory(CommandBuffer cmd,ComputeShader shader,int kernel,bool valid)
        {
            // Borrow the exact permanent inputs selected by the previous core
            // dispatch, rather than inventing pool indices or a second history.
            if(!valid)
            {
                foreach(int binding in signalHistoryBindings)cmd.SetComputeTextureParam(shader,kernel,binding,Texture2D.blackTexture);
                return;
            }
            int found=0;
            for(int i=0;i<LastDispatchCount;i++)if(passes[i].pipeline==4)
            {
                var manifest=HoRTDINrdManifest.Resources[4];
                for(int j=0;j<manifest.Length;j++)
                {
                    string name=manifest[j];int bit=name switch{"gHistory_Spec"=>1,"gHistory_Diff"=>2,"gHistory_SpecFast"=>4,"gHistory_DiffFast"=>8,_=>0};
                    if(bit==0)continue;
                    var resource=resources[i,j];
                    if(HoRTDINrdNative.ResourceName(resource.type)!="PERMANENT_POOL"||resource.poolIndex>=permanent.Length)throw new InvalidOperationException("NRD signal history is not a permanent pool input");
                    int binding=signalHistoryBindings[bit==1?0:bit==2?1:bit==4?2:3];
                    cmd.SetComputeTextureParam(shader,kernel,binding,permanent[resource.poolIndex].nameID);found|=bit;
                }
                break;
            }
            if(found!=15)throw new InvalidOperationException("NRD signal history inputs missing from pinned Temporal dispatch");
        }
        internal void Record(CommandBuffer cmd,HoRTDINrdNative.CommonSettings settings,Dictionary<string,RenderTargetIdentifier> external)
        {
            HoRTDINrdNative.Check(HoRTDINrdNative.SetCommonSettings(instance,ref settings),"SetCommonSettings");
            var desired=HoRTDINrdNative.RelaxSettings.DenseDI(timing.FilteredSeconds);
            // Final anyhit visibility can create sharp radiance discontinuities
            // on a smooth receiver. Keep their diffuse luminance gate tighter.
            if(HasCutoutVisibility)desired.diffPhi=.5f;
            bool diagnostic=false;
            #if UNITY_EDITOR
            if(ValidationSettings!=null){desired=ValidationSettings(desired);diagnostic=true;}
            #endif
            var previous=AppliedSettings;
            if(diagnostic||desired.diffPhi!=previous.diffPhi||desired.diffFrames!=previous.diffFrames||desired.specFrames!=previous.specFrames||desired.diffFast!=previous.diffFast||desired.specFast!=previous.specFast||desired.historyFixFrames!=previous.historyFixFrames)
            {HoRTDINrdNative.Check(HoRTDINrdNative.SetDenoiserSettings(instance,0,ref desired),"SetDenoiserSettings");AppliedSettings=desired;}
            HoRTDINrdNative.Check(HoRTDINrdNative.GetComputeDispatches(instance,identifiers,1,out var pointer,out uint count),"GetComputeDispatches");
            if(count*constantStride>ConstantBytes)throw new InvalidOperationException("NRD dispatch constants exceed per-frame storage");
            LastDispatchCount=0;if(count==0)return;
            int wordCount=(int)count*constantStride/4;Array.Clear(constantWords,0,wordCount);
            for(int i=0;i<count;i++)
            {
                passes[i]=HoRTDINrdNative.Read<HoRTDINrdNative.DispatchDesc>(pointer,i);var pass=passes[i];
                if(pass.pipeline>=shaders.Length||pass.resourceCount!=bindings[pass.pipeline].Length||pass.constantSize>constantStride||(pass.constantSize&3)!=0)
                    throw new InvalidOperationException("NRD dispatch layout mismatch");
                for(int j=0;j<pass.resourceCount;j++)resources[i,j]=HoRTDINrdNative.Read<HoRTDINrdNative.ResourceDesc>(pass.resources,j);
                // Copy current native data now; the next SDK call invalidates its pointers.
                if(pass.constantSize>0)Marshal.Copy(pass.constants,constantWords,i*constantStride/4,(int)pass.constantSize/4);
            }
            // SetBufferData snapshots the CPU array into the recorded command.
            var buffer=constants[settings.frameIndex%3];cmd.SetBufferData(buffer,constantWords,0,0,wordCount);
            bool diffuseWritten=false,specularWritten=false;
            for(int i=0;i<count;i++)
            {
                var pass=passes[i];var shader=shaders[pass.pipeline];int kernel=kernels[pass.pipeline];
                bool writesDiffuse=false,writesSpecular=false;
                #if UNITY_EDITOR
                RenderTargetIdentifier validationOutput=default;bool hasValidationOutput=false;
                #endif
                if(pass.constantSize>0)cmd.SetComputeConstantBufferParam(shader,"HoRTDINrdConstants",buffer,i*constantStride,((int)pass.constantSize+15)&~15);
                for(int j=0;j<pass.resourceCount;j++)
                {
                    var resource=resources[i,j];
                    // PrePass writes these outputs before Temporal reads them in
                    // this frame. A read before any current-frame write would
                    // require the old demodulated output and forbid in-place resolve.
                    if(resource.type==18||resource.type==19)
                    {
                        bool written=resource.type==18?diffuseWritten:specularWritten;
                        if(resource.descriptorType==0&&!written)throw new InvalidOperationException("NRD reads external radiance output before this frame writes it");
                        if(resource.descriptorType==1){if(resource.type==18)writesDiffuse=true;else writesSpecular=true;}
                    }
                    if(!names.TryGetValue(resource.type,out string name)){name=HoRTDINrdNative.ResourceName(resource.type);names.Add(resource.type,name);}
                    RenderTargetIdentifier texture;
                    if(name=="PERMANENT_POOL")texture=permanent[resource.poolIndex].nameID;
                    else if(name=="TRANSIENT_POOL")texture=transient[resource.poolIndex].nameID;
                    else if(!external.TryGetValue(name,out texture))throw new InvalidOperationException($"NRD external resource missing: {name}; descriptor={resource.descriptorType}, type={resource.type}, pool={resource.poolIndex}, pipeline={pass.pipeline}");
                    cmd.SetComputeTextureParam(shader,kernel,bindings[pass.pipeline][j],texture);
                    #if UNITY_EDITOR
                    if(ValidationSpecularStage!=null&&resource.descriptorType==1)
                    {
                        string binding=HoRTDINrdManifest.Resources[pass.pipeline][j];
                        if(binding=="gOut_Spec"||binding=="gOut_Spec_Variance"){validationOutput=texture;hasValidationOutput=true;}
                    }
                    #endif
                }
                cmd.DispatchCompute(shader,kernel,pass.gridWidth,pass.gridHeight,1);
                #if UNITY_EDITOR
                if(hasValidationOutput)ValidationSpecularStage(cmd,(int)pass.pipeline,validationOutput);
                #endif
                diffuseWritten|=writesDiffuse;specularWritten|=writesSpecular;
            }
            LastDispatchCount=(int)count;
        }
        public void Dispose()
        {
            if(instance!=IntPtr.Zero){HoRTDINrdNative.DestroyInstance(instance);instance=IntPtr.Zero;}
            if(permanent!=null)foreach(var texture in permanent)texture?.Release();
            if(transient!=null)foreach(var texture in transient)texture?.Release();
            foreach(var buffer in constants)buffer?.Dispose();
        }
    }
}
