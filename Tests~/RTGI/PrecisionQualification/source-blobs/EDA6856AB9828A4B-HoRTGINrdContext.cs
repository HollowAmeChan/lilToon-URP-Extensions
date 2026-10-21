#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.RTDI.NRD;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.RTGI.NRD
{
    // Independent original RELAX_DIFFUSE instance. Share the verified ABI/pool
    // dispatcher, not DenseDI kernels, guide histories or appearance policy.
    internal sealed class HoRTGINrdContext:IDisposable
    {
        internal const float SignalScale=64;
        internal readonly HoRTDINrdContext core;
        internal RTHandle signal,normal,viewZ,output,filtered;
        internal bool historyValid,lastReset;
        internal Matrix4x4 previousView=Matrix4x4.identity,previousProjection=Matrix4x4.identity;
        readonly Dictionary<string,RenderTargetIdentifier> external=new();
        int width,height;
        float seconds=1f/60;
        #if UNITY_EDITOR
        internal double? ValidationTimeSeconds;
        // Isolated unit-domain comparison. Physical raw is unchanged; changing
        // the NRD unit invalidates only its own history before recording.
        internal float? ValidationSignalScale;
        float validationAppliedScale=SignalScale;
        #endif
        internal float AppliedSignalScale
        {
            get
            {
                #if UNITY_EDITOR
                return validationAppliedScale;
                #else
                return SignalScale;
                #endif
            }
        }
        internal HoRTGINrdContext(ComputeShader[] shaders)
        {core=new HoRTDINrdContext(shaders,HoRTGINrdManifest.DenoiserType,HoRTGINrdManifest.Identifiers,HoRTGINrdManifest.Resources,highPrecisionColorPool:true);}
        internal void Prepare(int w,int h)
        {
            if(w!=width||h!=height){historyValid=false;width=w;height=h;}
            var desc=new RenderTextureDescriptor(w,h){graphicsFormat=GraphicsFormat.R16G16B16A16_SFloat,depthStencilFormat=GraphicsFormat.None,msaaSamples=1,enableRandomWrite=true};
            RenderingUtils.ReAllocateIfNeeded(ref signal,desc,FilterMode.Point,TextureWrapMode.Clamp,name:"HoRTGI NRD radiance/hit distance");
            RenderingUtils.ReAllocateIfNeeded(ref normal,desc,FilterMode.Point,TextureWrapMode.Clamp,name:"HoRTGI NRD normal/roughness");
            RenderingUtils.ReAllocateIfNeeded(ref output,desc,FilterMode.Point,TextureWrapMode.Clamp,name:"HoRTGI NRD demodulated output");
            RenderingUtils.ReAllocateIfNeeded(ref filtered,desc,FilterMode.Point,TextureWrapMode.Clamp,name:"HoRTGI filtered diffuse");
            desc.graphicsFormat=GraphicsFormat.R32_SFloat;
            RenderingUtils.ReAllocateIfNeeded(ref viewZ,desc,FilterMode.Point,TextureWrapMode.Clamp,name:"HoRTGI NRD viewZ");
            core.Prepare(w,h);
            external["IN_MV"]=new RenderTargetIdentifier(Texture2D.blackTexture);
            external["IN_NORMAL_ROUGHNESS"]=normal.nameID;external["IN_VIEWZ"]=viewZ.nameID;
            external["IN_DIFF_RADIANCE_HITDIST"]=signal.nameID;external["OUT_DIFF_RADIANCE_HITDIST"]=output.nameID;
            external["IN_DIFF_CONFIDENCE"]=new RenderTargetIdentifier(Texture2D.whiteTexture);
            external["IN_DISOCCLUSION_THRESHOLD_MIX"]=new RenderTargetIdentifier(Texture2D.blackTexture);
            external["OUT_VALIDATION"]=filtered.nameID;
        }
        internal void Declare(RenderGraph graph,IUnsafeRenderGraphBuilder builder)
        {
            core.Declare(graph,builder);
            foreach(var texture in new[]{signal,normal,viewZ,output,filtered})builder.UseTexture(graph.ImportTexture(texture),AccessFlags.ReadWrite);
        }
        internal void BindFinal(CommandBuffer cmd,ComputeShader shader,int kernel)
        {
            #if UNITY_EDITOR
            float next=ValidationSignalScale??SignalScale;
            if(!float.IsFinite(next)||next<=0)throw new ArgumentOutOfRangeException(nameof(ValidationSignalScale));
            if(next!=validationAppliedScale){validationAppliedScale=next;historyValid=false;}
            #endif
            cmd.SetComputeFloatParam(shader,"_HoRTGINrdSignalScale",AppliedSignalScale);
            cmd.SetComputeTextureParam(shader,kernel,"_HoRTGINrdSignal",signal.nameID);
            cmd.SetComputeTextureParam(shader,kernel,"_HoRTGINrdNormal",normal.nameID);
            cmd.SetComputeTextureParam(shader,kernel,"_HoRTGINrdViewZ",viewZ.nameID);
        }
        internal void Record(CommandBuffer cmd,Matrix4x4 view,Matrix4x4 projection,int frame,bool reset,double renderTime,bool preview)
        {
            #if UNITY_EDITOR
            core.SceneViewPreview=preview;core.ValidationTimeSeconds=ValidationTimeSeconds;
            #endif
            float deltaMs=core.SetRenderTime(renderTime,out bool timeReset);
            if(float.IsFinite(deltaMs)&&deltaMs>0)
            {
                float sample=Mathf.Clamp(deltaMs*.001f,1f/240,1);
                seconds+= (sample-seconds)*(1-Mathf.Exp(-sample/.1f));
            }
            lastReset=reset||timeReset||!historyValid;
            var common=HoRTDINrdNative.CommonSettings.Create(width,height,view,projection,historyValid?previousView:view,historyValid?previousProjection:projection,frame,lastReset);
            common.timeDelta=deltaMs;
            var settings=ReferenceSettings(preview?1f/60:seconds);
            core.Record(cmd,common,external,settings);
            previousView=view;previousProjection=projection;historyValid=true;
        }
        // Pinned NRDSettings.h defaults, with accumulation periods converted to
        // observed unscaled seconds. Scene View uses the reference sample budget.
        internal static HoRTDINrdNative.RelaxSettings ReferenceSettings(float delta)
        {
            uint frames=(uint)Mathf.Clamp(Mathf.RoundToInt(.5f/delta),2,255);
            uint fast=(uint)Mathf.Clamp(Mathf.RoundToInt(.1f/delta),1,(int)frames-1);
            return new HoRTDINrdNative.RelaxSettings
            {
                acceleration=.3f,spatialSigma=4.5f,temporalSigma=.5f,reset=.5f,
                diffFrames=frames,specFrames=frames,diffFast=fast,specFast=fast,historyFixFrames=Math.Min(3u,fast-1),
                baseStride=14,alternateStride=14,historyNormalPower=8,fastSigma=2,diffPrepass=30,specPrepass=50,
                minHitWeight=.1f,varianceHistory=3,diffPhi=2,specPhi=1,lobeFraction=.5f,roughFraction=.15f,specSlack=.15f,atrous=5,
                depthThreshold=.003f,lumRelax=.5f,normalRelax=.3f,roughRelax=1,minDiffMaterial=4,minSpecMaterial=4,roughEdge=1
            };
        }
        public void Dispose(){core.Dispose();signal?.Release();normal?.Release();viewZ?.Release();output?.Release();filtered?.Release();}
    }
}
