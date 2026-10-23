#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
namespace lilToon.URP.Extensions.NRD
{
    // Shared NRD execution/ownership. Channel profiles and histories stay with callers.
    internal class HoNrdContext:IDisposable
    {
        protected IntPtr instance;
        protected readonly HoNrdNative.InstanceDesc description;
        protected readonly ComputeShader[] shaders;
        protected readonly int[][] bindings;
        protected readonly int[] kernels;
        protected readonly string[][] pipelineResources;
        protected readonly uint[] identifiers={0};
        protected readonly HoNrdNative.DispatchDesc[] passes;
        protected readonly HoNrdNative.ResourceDesc[,] resources;
        protected readonly int[] constantWords=new int[ConstantBytes/4];
        protected readonly RTHandle[] permanent,transient;
        protected readonly bool highPrecisionColorPool;
        protected readonly GraphicsBuffer[] constants=new GraphicsBuffer[3];
        protected readonly Dictionary<uint,string> names=new();
        private int width,height;
        private const int ConstantBytes=65536;
        protected readonly int constantStride;
        protected readonly HoNrdTiming timing=new();
        protected double currentSampleTime;
        internal HoNrdNative.RelaxSettings AppliedSettings {get;private set;}
        internal int LastDispatchCount {get;private set;}
        #if UNITY_EDITOR
        internal bool SceneViewPreview;
        internal double? ValidationTimeSeconds;
        internal Func<string,int,GraphicsFormat,GraphicsFormat> ValidationPoolFormat;
        internal Func<HoNrdNative.RelaxSettings,HoNrdNative.RelaxSettings> ValidationSettings;
        internal Action<CommandBuffer,int,RenderTargetIdentifier> ValidationSpecularStage,ValidationDiffuseStage;
        #endif
        internal float SetRenderTime(double time,out bool reset)
        {
            #if UNITY_EDITOR
            time=ValidationTimeSeconds??time;
            #endif
            bool preserveLongInterval=false;
            #if UNITY_EDITOR
            preserveLongInterval=SceneViewPreview;
            #endif
            currentSampleTime=time;reset=timing.Advance(time,preserveLongInterval);return timing.Seconds*1000;
        }
        internal HoNrdContext(ComputeShader[] shaders,uint denoiserType,string[] pipelineIdentifiers,string[][] pipelineResources,bool highPrecisionColorPool=false)
        {
            var expected=pipelineIdentifiers??throw new ArgumentNullException(nameof(pipelineIdentifiers));
            this.pipelineResources=pipelineResources??throw new ArgumentNullException(nameof(pipelineResources));
            this.highPrecisionColorPool=highPrecisionColorPool;
            instance=HoNrdNative.Create(denoiserType);description=HoNrdNative.Read<HoNrdNative.InstanceDesc>(HoNrdNative.GetInstanceDesc(instance));
            try
            {
                if(description.pipelineCount!=expected.Length||this.pipelineResources.Length!=expected.Length||shaders==null||shaders.Length!=description.pipelineCount)
                    throw new InvalidOperationException("NRD shader library count mismatch");
                this.shaders=(ComputeShader[])shaders.Clone();bindings=new int[shaders.Length][];kernels=new int[shaders.Length];int maxResources=0;
                for(int i=0;i<shaders.Length;i++)
                {
                    if(shaders[i]==null)throw new InvalidOperationException("NRD Unity shader missing: "+i);
                    var pipeline=HoNrdNative.Read<HoNrdNative.PipelineDesc>(description.pipelines,i);
                    if(pipeline.identifier!=expected[i])throw new InvalidOperationException("NRD shader permutation mismatch");
                    var resources=this.pipelineResources[i];bindings[i]=new int[resources.Length];
                    for(int j=0;j<resources.Length;j++)bindings[i][j]=Shader.PropertyToID(resources[j]);
                    maxResources=Math.Max(maxResources,resources.Length);kernels[i]=shaders[i].FindKernel("main");
                }
                constantStride=((int)description.constantMax+255)&~255;
                if(constantStride<=0||constantStride>ConstantBytes)throw new InvalidOperationException("NRD constant stride exceeds storage");
                passes=new HoNrdNative.DispatchDesc[ConstantBytes/constantStride];
                resources=new HoNrdNative.ResourceDesc[passes.Length,maxResources];
                for(int i=0;i<constants.Length;i++)constants[i]=new GraphicsBuffer(GraphicsBuffer.Target.Constant,ConstantBytes/4,4){name="Ho NRD constants "+i};
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
                var texture=HoNrdNative.Read<HoNrdNative.TextureDesc>(descriptors,i);int factor=Math.Max(1,(int)texture.downsample);
                var nativeFormat=Format(texture.format);
                var format=highPrecisionColorPool&&nativeFormat==GraphicsFormat.R16G16B16A16_SFloat?GraphicsFormat.R32G32B32A32_SFloat:nativeFormat;
                #if UNITY_EDITOR
                if(ValidationPoolFormat!=null)format=ValidationPoolFormat(name,i,nativeFormat);
                #endif
                if(!SystemInfo.IsFormatSupported(format,GraphicsFormatUsage.LoadStore))throw new NotSupportedException("NRD UAV format "+format);
                var desc=new RenderTextureDescriptor((width+factor-1)/factor,(height+factor-1)/factor)
                {graphicsFormat=format,depthStencilFormat=GraphicsFormat.None,msaaSamples=1,enableRandomWrite=true};
                RenderingUtils.ReAllocateIfNeeded(ref pool[i],desc,FilterMode.Point,TextureWrapMode.Clamp,name:$"Ho NRD {name} {i}");
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
        protected void ApplySettings(HoNrdNative.RelaxSettings desired)
        {HoNrdNative.Check(HoNrdNative.SetDenoiserSettings(instance,0,ref desired),"SetDenoiserSettings");AppliedSettings=desired;}
        internal void Record(CommandBuffer cmd,HoNrdNative.CommonSettings settings,Dictionary<string,RenderTargetIdentifier> external,HoNrdNative.RelaxSettings desired,bool applySettings=true)
        {
            HoNrdNative.Check(HoNrdNative.SetCommonSettings(instance,ref settings),"SetCommonSettings");
            #if UNITY_EDITOR
            if(ValidationSettings!=null){desired=ValidationSettings(desired);applySettings=true;}
            #endif
            if(applySettings)ApplySettings(desired);
            HoNrdNative.Check(HoNrdNative.GetComputeDispatches(instance,identifiers,1,out var pointer,out uint count),"GetComputeDispatches");
            if(count*constantStride>ConstantBytes)throw new InvalidOperationException("NRD dispatch constants exceed per-frame storage");
            LastDispatchCount=0;if(count==0)return;
            int wordCount=(int)count*constantStride/4;Array.Clear(constantWords,0,wordCount);
            for(int i=0;i<count;i++)
            {
                passes[i]=HoNrdNative.Read<HoNrdNative.DispatchDesc>(pointer,i);var pass=passes[i];
                if(pass.pipeline>=shaders.Length||pass.resourceCount!=bindings[pass.pipeline].Length||pass.constantSize>constantStride||(pass.constantSize&3)!=0)
                    throw new InvalidOperationException("NRD dispatch layout mismatch");
                for(int j=0;j<pass.resourceCount;j++)resources[i,j]=HoNrdNative.Read<HoNrdNative.ResourceDesc>(pass.resources,j);
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
                RenderTargetIdentifier validationOutput=default,validationDiffuse=default;bool hasValidationOutput=false,hasValidationDiffuse=false;
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
                    if(!names.TryGetValue(resource.type,out string name)){name=HoNrdNative.ResourceName(resource.type);names.Add(resource.type,name);}
                    RenderTargetIdentifier texture;
                    if(name=="PERMANENT_POOL")texture=permanent[resource.poolIndex].nameID;
                    else if(name=="TRANSIENT_POOL")texture=transient[resource.poolIndex].nameID;
                    else if(!external.TryGetValue(name,out texture))throw new InvalidOperationException($"NRD external resource missing: {name}; descriptor={resource.descriptorType}, type={resource.type}, pool={resource.poolIndex}, pipeline={pass.pipeline}");
                    cmd.SetComputeTextureParam(shader,kernel,bindings[pass.pipeline][j],texture);
                    #if UNITY_EDITOR
                    if(ValidationSpecularStage!=null&&resource.descriptorType==1)
                    {
                        string binding=pipelineResources[pass.pipeline][j];
                        if(binding=="gOut_Spec"||binding=="gOut_Spec_Variance"){validationOutput=texture;hasValidationOutput=true;}
                    }
                    if(ValidationDiffuseStage!=null&&resource.descriptorType==1)
                    {
                        string binding=pipelineResources[pass.pipeline][j];
                        if(binding=="gOut_Diff"||binding=="gOut_Diff_Variance"){validationDiffuse=texture;hasValidationDiffuse=true;}
                    }
                    #endif
                }
                cmd.DispatchCompute(shader,kernel,pass.gridWidth,pass.gridHeight,1);
                #if UNITY_EDITOR
                if(hasValidationOutput)ValidationSpecularStage(cmd,(int)pass.pipeline,validationOutput);
                if(hasValidationDiffuse)ValidationDiffuseStage(cmd,(int)pass.pipeline,validationDiffuse);
                #endif
                diffuseWritten|=writesDiffuse;specularWritten|=writesSpecular;
            }
            LastDispatchCount=(int)count;
        }
        public void Dispose()
        {
            if(instance!=IntPtr.Zero){HoNrdNative.DestroyInstance(instance);instance=IntPtr.Zero;}
            if(permanent!=null)foreach(var texture in permanent)texture?.Release();
            if(transient!=null)foreach(var texture in transient)texture?.Release();
            foreach(var buffer in constants)buffer?.Dispose();
        }
    }
}
