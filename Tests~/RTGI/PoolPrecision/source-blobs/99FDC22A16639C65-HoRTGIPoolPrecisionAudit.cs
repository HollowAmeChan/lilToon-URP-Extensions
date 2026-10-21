using System;
using System.Collections.Generic;
using System.Reflection;
using lilToon.URP.Extensions.RTDI.NRD;
using lilToon.URP.Extensions.RTGI.NRD;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    internal sealed class HoRTGIPoolPrecisionAudit:IDisposable
    {
        [Serializable] internal sealed class Pool
        { public string category,format;public int index,width,height;public uint nativeFormat;public long bytes,baselineBytes; }
        [Serializable] internal sealed class Binding
        { public int pipeline,dispatch,poolIndex;public string binding,resourceType;public uint descriptorType; }
        [Serializable] internal sealed class Report
        {
            public bool pass,copyBitExact;public int copiedComponents,changedPools;public string mode,signalFormat,outputFormat;
            public long poolBytes,baselinePoolBytes;public float signalScale;
            public Pool[] pools;public Binding[] bindings;
        }
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        readonly HoRTGINrdContext context;readonly ComputeShader copy;readonly int kernel;
        readonly string mode;readonly bool bitExact;int copiedComponents;
        internal HoRTGIPoolPrecisionAudit(HoRTGINrdContext context,string mode)
        {
            if(mode!="baseline"&&mode!="rgba32")throw new ArgumentException("Unknown precision policy");
            this.context=context;this.mode=mode;
            copy=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTGI/Shaders/HoRTGIPrecisionCopy.compute");
            if(copy==null)throw new Exception("Precision copy shader missing");kernel=copy.FindKernel("CopyFloat4");copy.FindKernel("Seed");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(copy))
                if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(message.message);
            bitExact=ValidateCopy();
            context.core.ValidationPoolFormat=SelectFormat;context.historyValid=false;
        }
        GraphicsFormat SelectFormat(string category,int index,GraphicsFormat original)
        {return mode=="rgba32"&&original==GraphicsFormat.R16G16B16A16_SFloat?GraphicsFormat.R32G32B32A32_SFloat:original;}
        internal void Copy(CommandBuffer cmd,RenderTargetIdentifier source,RenderTexture destination)
        {
            if(destination.graphicsFormat!=GraphicsFormat.R32G32B32A32_SFloat)throw new Exception("Audit destination loses float32 precision");
            cmd.SetComputeIntParams(copy,"_Size",destination.width,destination.height);
            cmd.SetComputeTextureParam(copy,kernel,"_Source",source);cmd.SetComputeTextureParam(copy,kernel,"_Dest",destination);
            cmd.DispatchCompute(copy,kernel,(destination.width+7)/8,(destination.height+7)/8,1);
        }
        bool ValidateCopy()
        {
            var values=new Vector4[16];for(int i=0;i<values.Length;i++)values[i]=new Vector4((i+1)*1e-12f,(i+1)*1e-9f,(i+1)*.00001f,(i+1)*123.125f);
            using var data=new GraphicsBuffer(GraphicsBuffer.Target.Structured,values.Length,16);data.SetData(values);
            var desc=new RenderTextureDescriptor(16,1){graphicsFormat=GraphicsFormat.R32G32B32A32_SFloat,enableRandomWrite=true,msaaSamples=1};
            var source=new RenderTexture(desc);var destination=new RenderTexture(desc);source.Create();destination.Create();
            try
            {
                using var cmd=new CommandBuffer{name="GI precision copy tiny-value self-test"};int seed=copy.FindKernel("Seed");
                cmd.SetComputeIntParams(copy,"_Size",16,1);cmd.SetComputeBufferParam(copy,seed,"_Data",data);cmd.SetComputeTextureParam(copy,seed,"_Dest",source);cmd.DispatchCompute(copy,seed,2,1,1);
                Copy(cmd,source,destination);Graphics.ExecuteCommandBuffer(cmd);
                var request=AsyncGPUReadback.Request(destination);request.WaitForCompletion();if(request.hasError)throw new Exception("Precision copy readback failed");
                var actual=request.GetData<Vector4>();if(actual.Length!=values.Length)throw new Exception("Precision copy shape changed");
                for(int i=0;i<values.Length;i++)for(int c=0;c<4;c++)if(BitConverter.SingleToInt32Bits(actual[i][c])!=BitConverter.SingleToInt32Bits(values[i][c]))throw new Exception("Float32 stage copy lost tiny values");
                copiedComponents=64;return true;
            }
            finally{source.Release();destination.Release();UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(destination);}
        }
        internal Report Snapshot()
        {
            var core=context.core;var description=(HoRTDINrdNative.InstanceDesc)typeof(HoRTDINrdContext).GetField("description",Flags).GetValue(core);
            var result=new Report{mode=mode,copyBitExact=bitExact,copiedComponents=copiedComponents,signalScale=context.AppliedSignalScale,signalFormat=context.signal.rt.graphicsFormat.ToString(),outputFormat=context.output.rt.graphicsFormat.ToString()};
            var pools=new List<Pool>();
            foreach(string category in new[]{"permanent","transient"})
            {
                var handles=(RTHandle[])typeof(HoRTDINrdContext).GetField(category,Flags).GetValue(core);IntPtr descriptors=category=="permanent"?description.permanent:description.transient;
                for(int i=0;i<handles.Length;i++)
                {
                    var native=HoRTDINrdNative.Read<HoRTDINrdNative.TextureDesc>(descriptors,i);var texture=handles[i].rt;
                    long bytes=(long)texture.width*texture.height*GraphicsFormatUtility.GetBlockSize(texture.graphicsFormat);
                    long baseline=(long)texture.width*texture.height*(native.format==27?8:native.format==30?4:native.format==17?2:native.format==8?4:native.format==0?1:throw new Exception("Unmapped native pool format"));
                    var expected=native.format==27?(mode=="rgba32"?GraphicsFormat.R32G32B32A32_SFloat:GraphicsFormat.R16G16B16A16_SFloat):texture.graphicsFormat;
                    if(texture.graphicsFormat!=expected)throw new Exception("Pool format selector was not applied");
                    if(bytes!=baseline)result.changedPools++;result.poolBytes+=bytes;result.baselinePoolBytes+=baseline;
                    pools.Add(new Pool{category=category,index=i,nativeFormat=native.format,format=texture.graphicsFormat.ToString(),width=texture.width,height=texture.height,bytes=bytes,baselineBytes=baseline});
                }
            }
            var passes=(HoRTDINrdNative.DispatchDesc[])typeof(HoRTDINrdContext).GetField("passes",Flags).GetValue(core);
            var resources=(HoRTDINrdNative.ResourceDesc[,])typeof(HoRTDINrdContext).GetField("resources",Flags).GetValue(core);var bindings=new List<Binding>();
            for(int i=0;i<core.LastDispatchCount;i++)for(int j=0;j<passes[i].resourceCount;j++)
            {
                var r=resources[i,j];string name=HoRTDINrdNative.ResourceName(r.type);
                if(name=="PERMANENT_POOL"||name=="TRANSIENT_POOL")bindings.Add(new Binding{pipeline=(int)passes[i].pipeline,dispatch=i,binding=HoRTGINrdManifest.Resources[passes[i].pipeline][j],descriptorType=r.descriptorType,resourceType=name,poolIndex=(int)r.poolIndex});
            }
            result.pools=pools.ToArray();result.bindings=bindings.ToArray();result.pass=bitExact&&result.signalScale==64&&(mode=="baseline"?result.changedPools==0:result.changedPools>0);
            if(!result.pass)throw new Exception("Precision experiment contract failed");return result;
        }
        public void Dispose(){context.core.ValidationPoolFormat=null;context.historyValid=false;}
    }
    public static partial class HoRTGICastleValidation
    {
        public static void RunPoolPrecisionQualification()=>Run(false,64,denseScheduled:true,validationTag:"pool-precision-qualification",poolPrecision:"rgba32");
        public static void RunPoolPrecisionBaseline()=>Run(false,256,true,true,256,256,moments:true,spatialAudit:true,denseScheduled:true,validationTag:"pool-precision-baseline",poolPrecision:"baseline");
        public static void RunPoolPrecisionFloat()=>Run(false,256,true,true,256,256,moments:true,spatialAudit:true,denseScheduled:true,validationTag:"pool-precision-rgba32",poolPrecision:"rgba32");
    }
}
