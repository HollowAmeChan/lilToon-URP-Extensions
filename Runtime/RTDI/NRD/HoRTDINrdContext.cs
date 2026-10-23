#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.NRD;
using lilToon.URP.Extensions.RayTracing;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace lilToon.URP.Extensions.RTDI.NRD
{
    // DI-only appearance/history policy over the shared NRD executor.
    internal sealed class HoRTDINrdContext:HoNrdContext
    {
        internal bool HasCutoutVisibility;
        private double lastLightingChange=double.NaN;
        internal bool StableDiffuseHistory {get;private set;}
        #if UNITY_EDITOR
        internal const uint SceneViewSettledSamples=64;
        internal uint SceneViewAccumulatedFrames {get;private set;}
        internal bool ValidationDisableSceneViewPreview,ValidationForceSceneViewPreview,ValidationUseJitteredProjection;
        internal bool ValidationPreserveWorldHistory,ValidationDisableStableDiffuseHistory,ValidationForceWorldReset,ValidationSkipVisibilityConfidence,ValidationTraceVisibilityConfidence;
        internal int ValidationVisibilityFilterPasses=2;
        internal Action<CommandBuffer,RenderTargetIdentifier> ValidationMotionCapture;
        #endif
        private static readonly int[] signalHistoryBindings={Shader.PropertyToID("_HoRTDINrdHistorySpec"),Shader.PropertyToID("_HoRTDINrdHistoryDiff"),Shader.PropertyToID("_HoRTDINrdHistorySpecFast"),Shader.PropertyToID("_HoRTDINrdHistoryDiffFast")};
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
            StableDiffuseHistory=currentSampleTime-lastLightingChange>=HoRTDINrdProfile.StableDiffusePeriod;
            return restart;
        }
        internal HoRTDINrdContext(ComputeShader[] shaders,uint denoiserType=14,string[] pipelineIdentifiers=null,string[][] pipelineResources=null,bool highPrecisionColorPool=false)
            :base(shaders,denoiserType,pipelineIdentifiers??HoRTDINrdManifest.Identifiers,pipelineResources??HoRTDINrdManifest.Resources,highPrecisionColorPool)
        {ApplySettings(HoRTDINrdProfile.DenseDI());}
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
                var manifest=pipelineResources[4];
                for(int j=0;j<manifest.Length;j++)
                {
                    string name=manifest[j];int bit=name switch{"gHistory_Spec"=>1,"gHistory_Diff"=>2,"gHistory_SpecFast"=>4,"gHistory_DiffFast"=>8,_=>0};
                    if(bit==0)continue;
                    var resource=resources[i,j];
                    if(HoNrdNative.ResourceName(resource.type)!="PERMANENT_POOL"||resource.poolIndex>=permanent.Length)throw new InvalidOperationException("NRD signal history is not a permanent pool input");
                    int binding=signalHistoryBindings[bit==1?0:bit==2?1:bit==4?2:3];
                    cmd.SetComputeTextureParam(shader,kernel,binding,permanent[resource.poolIndex].nameID);found|=bit;
                }
                break;
            }
            if(found!=15)throw new InvalidOperationException("NRD signal history inputs missing from pinned Temporal dispatch");
        }
        internal void Record(CommandBuffer cmd,HoNrdNative.CommonSettings settings,Dictionary<string,RenderTargetIdentifier> external,HoNrdNative.RelaxSettings? suppliedSettings=null)
        {
            #if UNITY_EDITOR
            if(SceneViewPreview)
                SceneViewAccumulatedFrames=settings.accumulationMode==2||!StableDiffuseHistory?0:Math.Min(SceneViewSettledSamples,SceneViewAccumulatedFrames+1);
            #endif
            var desired=suppliedSettings??HoRTDINrdProfile.DenseDI(timing.FilteredSeconds,StableDiffuseHistory);
            #if UNITY_EDITOR
            // Keep useful sample history even when a large Scene View renders
            // once a second. Keep the responsive history too: at zero samples
            // it is the current noisy frame, so HistoryClamping repeatedly
            // clips the long history against a stochastic 5x5 colour box.
            // Input changes still leave this profile and restart once;
            // game/Player response remains time based.
            if(!suppliedSettings.HasValue&&SceneViewPreview&&StableDiffuseHistory)
            {
                desired.diffFrames=Math.Max(30u,desired.diffFrames);desired.specFrames=Math.Max(30u,desired.specFrames);
                desired.diffFast=Math.Max(6u,desired.diffFast);desired.specFast=Math.Max(6u,desired.specFast);
            }
            #endif
            // Final anyhit visibility can create sharp radiance discontinuities
            // on a smooth receiver. Keep their diffuse luminance gate tighter.
            // Its rare visible light samples also need a wider history box;
            // keep the ordinary opaque profile unchanged.
            if(!suppliedSettings.HasValue&&HasCutoutVisibility){desired.diffPhi=1f;desired.fastSigma=4f;}
            bool diagnostic=suppliedSettings.HasValue;
            var previous=AppliedSettings;
            bool apply=diagnostic||desired.diffPhi!=previous.diffPhi||desired.fastSigma!=previous.fastSigma||desired.diffFrames!=previous.diffFrames||desired.specFrames!=previous.specFrames||desired.diffFast!=previous.diffFast||desired.specFast!=previous.specFast||desired.historyFixFrames!=previous.historyFixFrames;
            base.Record(cmd,settings,external,desired,apply);
        }

    }
}
