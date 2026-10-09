using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using lilToon.URP.Extensions.RTDI.NRD;
using UnityEditor;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug=UnityEngine.Debug;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static class HoRTDINrdRecordingValidation
    {
        [Serializable] private sealed class Measurement
        {public string device;public long allocationEventsPerRecord;public double microsecondsPerRecord;public int dispatches;}
        public static void RunBaseline()=>Run(false);
        public static void Run()=>Run(true);
        private static void Run(bool optimized)
        {
            bool previousProfiler=UnityEngine.Profiling.Profiler.enabled;
            try
            {
                Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");
                UnityEngine.Profiling.Profiler.enabled=true;
                ValidateAllocationCounter();
                ValidateUploadSnapshot();
                // RenderingUtils pool allocation requires a constructed URP.
                InitializePipeline();
                var shaders=new ComputeShader[15];
                for(int i=0;i<shaders.Length;i++)shaders[i]=AssetDatabase.LoadAssetAtPath<ComputeShader>($"Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/NRD/HoRTDINrd{i:00}.compute");
                using var context=new HoRTDINrdContext(shaders);context.Prepare(64,64);
                using var cmd=new CommandBuffer{name="NRD allocation measurement"};
                var external=new Dictionary<string,RenderTargetIdentifier>();
                foreach(string name in HoRTDINrdManifest.ResourceTypes)external[name]=new RenderTargetIdentifier(Texture2D.blackTexture);
                int frame=0;
                for(;frame<16;frame++){cmd.Clear();context.Record(cmd,Common(frame),external);}
                const int samples=128;var timer=new Stopwatch();
                using var allocation=StartAllocationRecorder();timer.Start();
                for(int i=0;i<samples;i++,frame++){cmd.Clear();context.Record(cmd,Common(frame),external);}
                timer.Stop();allocation.Stop();long events=allocation.Count==0?0:allocation.GetSample(0).Count;
                var result=new Measurement{device=SystemInfo.graphicsDeviceName,allocationEventsPerRecord=events/samples,microsecondsPerRecord=timer.Elapsed.TotalMilliseconds*1000/samples,dispatches=context.LastDispatchCount};
                Require(result.dispatches>0,"No actual NRD scheduling recorded");
                if(optimized)Require(result.allocationEventsPerRecord<4,"NRD recording still allocates frame temporaries");
                string path=Path.Combine(Directory.GetParent(Application.dataPath).FullName,optimized?"rtdi-nrd-recording-optimized.json":"rtdi-nrd-recording-baseline.json");
                File.WriteAllText(path,JsonUtility.ToJson(result,true));Debug.Log("[HoRTDINrdRecordingValidation] PASS "+JsonUtility.ToJson(result));
                cmd.Clear();EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
            finally{UnityEngine.Profiling.Profiler.enabled=previousProfiler;}
        }
        private static HoRTDINrdNative.CommonSettings Common(int frame)=>HoRTDINrdNative.CommonSettings.Create(64,64,Matrix4x4.identity,Matrix4x4.Perspective(60,1,.1f,50),Matrix4x4.identity,Matrix4x4.Perspective(60,1,.1f,50),frame,frame==0);
        private static ProfilerRecorder StartAllocationRecorder()=>ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GC.Alloc",1,ProfilerRecorderOptions.CollectOnlyOnCurrentThread|ProfilerRecorderOptions.SumAllSamplesInFrame);
        private static void ValidateAllocationCounter()
        {
            using var recorder=StartAllocationRecorder();var control=new byte[256*1024];GC.KeepAlive(control);recorder.Stop();
            // GC.Alloc Value is marker duration; Count is allocation events, not bytes.
            Require(recorder.Valid&&recorder.Count>0&&recorder.GetSample(0).Count>0,"GC allocation event counter failed the known-allocation control");
        }
        private static void InitializePipeline()
        {
            var holder=new GameObject("Recording benchmark initialization");var camera=holder.AddComponent<Camera>();camera.enabled=false;camera.transform.position=new Vector3(0,0,-3);
            var target=new RenderTexture(32,32,24);target.Create();
            try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});}
            finally{UnityEngine.Object.DestroyImmediate(holder);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
        private static void ValidateUploadSnapshot()
        {
            using var first=new GraphicsBuffer(GraphicsBuffer.Target.Structured,4,4);
            using var second=new GraphicsBuffer(GraphicsBuffer.Target.Structured,4,4);
            using var cmd=new CommandBuffer();var data=new[]{11,12,13,14};
            cmd.SetBufferData(first,data,0,0,4);for(int i=0;i<data.Length;i++)data[i]=21+i;
            cmd.SetBufferData(second,data,0,0,4);Array.Fill(data,99);
            Graphics.ExecuteCommandBuffer(cmd);var a=new int[4];var b=new int[4];first.GetData(a);second.GetData(b);
            for(int i=0;i<4;i++)Require(a[i]==11+i&&b[i]==21+i,"Deferred SetBufferData retained mutable CPU source instead of copying it");
        }
        private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    }
}
