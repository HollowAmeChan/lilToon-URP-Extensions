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
        private double currentSampleTime,lastLightingChange=double.NaN;
        internal bool StableDiffuseHistory {get;private set;}
        #if UNITY_EDITOR
        // A slow or intermittently repainted Scene View is not a paused game.
        internal bool SceneViewPreview;
        internal const uint SceneViewSettledSamples=64;
        internal uint SceneViewAccumulatedFrames {get;private set;}
        internal bool ValidationDisableSceneViewPreview;
        internal bool ValidationForceSceneViewPreview,ValidationUseJitteredProjection;
        // Isolated stage/settings A/B only; no serialized/runtime product option.
        internal Func<HoRTDINrdNative.RelaxSettings,HoRTDINrdNative.RelaxSettings> ValidationSettings;
        internal bool ValidationPreserveWorldHistory;
        internal bool ValidationDisableStableDiffuseHistory;
        internal bool ValidationForceWorldReset,ValidationSkipVisibilityConfidence;
        internal bool ValidationTraceVisibilityConfidence;
        internal int ValidationVisibilityFilterPasses=2;
        #endif
        #if UNITY_EDITOR
        internal double? ValidationTimeSeconds;
        internal Action<CommandBuffer,int,RenderTargetIdentifier> ValidationSpecularStage;
        internal Action<CommandBuffer,int,RenderTargetIdentifier> ValidationDiffuseStage;
        internal Action<CommandBuffer,RenderTargetIdentifier> ValidationMotionCapture;
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
        internal bool ObserveLightingStability(bool changed)
        {
            #if UNITY_EDITOR
            if(ValidationDisableStableDiffuseHistory){StableDiffuseHistory=false;lastLightingChange=currentSampleTime;return false;}
            #endif
            // Restart once when leaving the settled profile. Reusing its long
            // accumulated colour with a newly shortened budget retains a stale
            // first response; ongoing motion continues through normal confidence.
            bool restart=StableDiffuseHistory&&changed;
            if(!double.IsFinite(currentSampleTime)){lastLightingChange=double.NaN;StableDiffuseHistory=false;return restart;}
            if(changed||double.IsNaN(lastLightingChange)||currentSampleTime<lastLightingChange)lastLightingChange=currentSampleTime;
            // Retain the existing responsive profile while the camera, scene,
            // or actual light snapshot changes, and while its short history settles.
            StableDiffuseHistory=currentSampleTime-lastLightingChange>=HoRTDINrdNative.RelaxSettings.StableDiffusePeriod;
            return restart;
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
            #if UNITY_EDITOR
            if(SceneViewPreview)
                SceneViewAccumulatedFrames=settings.accumulationMode==2||!StableDiffuseHistory?0:Math.Min(SceneViewSettledSamples,SceneViewAccumulatedFrames+1);
            #endif
            HoRTDINrdNative.Check(HoRTDINrdNative.SetCommonSettings(instance,ref settings),"SetCommonSettings");
            var desired=HoRTDINrdNative.RelaxSettings.DenseDI(timing.FilteredSeconds,StableDiffuseHistory);
            #if UNITY_EDITOR
            // Keep useful sample history even when a large Scene View renders
            // once a second. Keep the responsive history too: at zero samples
            // it is the current noisy frame, so HistoryClamping repeatedly
            // clips the long history against a stochastic 5x5 colour box.
            // Input changes still leave this profile and restart once;
            // game/Player response remains time based.
            if(SceneViewPreview&&StableDiffuseHistory)
            {
                desired.diffFrames=Math.Max(30u,desired.diffFrames);desired.specFrames=Math.Max(30u,desired.specFrames);
                desired.diffFast=Math.Max(6u,desired.diffFast);desired.specFast=Math.Max(6u,desired.specFast);
            }
            #endif
            // Final anyhit visibility can create sharp radiance discontinuities
            // on a smooth receiver. Keep their diffuse luminance gate tighter.
            // Its rare visible light samples also need a wider history box;
            // keep the ordinary opaque profile unchanged.
            if(HasCutoutVisibility){desired.diffPhi=1f;desired.fastSigma=4f;}
            bool diagnostic=false;
            #if UNITY_EDITOR
            if(ValidationSettings!=null){desired=ValidationSettings(desired);diagnostic=true;}
            #endif
            var previous=AppliedSettings;
            if(diagnostic||desired.diffPhi!=previous.diffPhi||desired.fastSigma!=previous.fastSigma||desired.diffFrames!=previous.diffFrames||desired.specFrames!=previous.specFrames||desired.diffFast!=previous.diffFast||desired.specFast!=previous.specFast||desired.historyFixFrames!=previous.historyFixFrames)
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
                    if(ValidationDiffuseStage!=null&&resource.descriptorType==1)
                    {
                        string binding=HoRTDINrdManifest.Resources[pass.pipeline][j];
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
            if(instance!=IntPtr.Zero){HoRTDINrdNative.DestroyInstance(instance);instance=IntPtr.Zero;}
            if(permanent!=null)foreach(var texture in permanent)texture?.Release();
            if(transient!=null)foreach(var texture in transient)texture?.Release();
            foreach(var buffer in constants)buffer?.Dispose();
        }
    }
}
