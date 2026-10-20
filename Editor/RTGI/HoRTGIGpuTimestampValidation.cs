using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static class HoRTGIGpuTimestampValidation
    {
        const string Library="HoGIGpuProbe";
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr HoGpu_Event();
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int HoGpu_Base();
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int HoGpu_Status();
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int HoGpu_Allocate();
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int HoGpu_Poll(int index,out NativeResult result);
        [Serializable,StructLayout(LayoutKind.Sequential)]struct NativeResult{public ulong begin,end,frequency,targetFence,completedFence;public int error,ready;}
        [Serializable]sealed class Sample{public int slot,iterations;public ulong begin,end,frequency,targetFence,completedFence;public double milliseconds;}
        [Serializable]sealed class Report{public bool pass;public string error,gpu,api;public int initStatus,eventBase,nativeResultSize;public double emptyMedian,shortMedian,longMedian;public NativeResult failedSample;public Sample[] samples;}
        static readonly List<Sample> samples=new();
        static ComputeShader shader;
        static GraphicsBuffer output;
        static RenderTexture sync;
        static Texture2D pixel;
        static IntPtr callback,packet;
        static int eventBase,pending=-1,index,waits;
        static bool finished;
        static string directory;
        static Report report=new();
        public static void Run()
        {
            try
            {
                if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"))throw new Exception("Dedicated GI project required");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-gi-gpu-qualification"));Directory.CreateDirectory(directory);
                report.gpu=SystemInfo.graphicsDeviceName;report.api=SystemInfo.graphicsDeviceType.ToString();report.nativeResultSize=Marshal.SizeOf<NativeResult>();if(report.nativeResultSize!=48)throw new Exception("Native timestamp result ABI size mismatch");
                shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTGI/Shaders/HoRTGIGpuProbe.compute");if(shader==null)throw new Exception("Compute qualification shader missing");
                output=new GraphicsBuffer(GraphicsBuffer.Target.Structured,128*64,4);
                sync=new RenderTexture(1,1,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);sync.Create();pixel=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
                callback=HoGpu_Event();eventBase=HoGpu_Base();report.eventBase=eventBase;if(callback==IntPtr.Zero||eventBase<0)throw new Exception("Native Unity D3D12 event registration unavailable, status="+HoGpu_Status());
                using(var cmd=new CommandBuffer{name="GPU timestamp initialization"}){cmd.IssuePluginEventAndData(callback,eventBase,IntPtr.Zero);Graphics.ExecuteCommandBuffer(cmd);}Flush();
                EditorApplication.update+=Tick;
            }
            catch(Exception error){Finish(error);}
        }
        static void Flush()
        {
            var old=RenderTexture.active;try{RenderTexture.active=sync;pixel.ReadPixels(new Rect(0,0,1,1),0,0);pixel.Apply();}finally{RenderTexture.active=old;}
        }
        static void Tick()
        {
            try
            {
                report.initStatus=HoGpu_Status();if(report.initStatus<0)throw new Exception("Native initialization error "+report.initStatus);
                if(report.initStatus!=1){if(++waits>120)throw new Exception("Native timestamp init did not complete");return;}
                if(pending>=0)
                {
                    int state=HoGpu_Poll(pending,out var data);
                    if(state<0){report.failedSample=data;throw new Exception("Native timestamp sample failed: "+state+" slot="+pending+" fence="+data.completedFence+"/"+data.targetFence+" ticks="+data.begin+"/"+data.end);}
                    if(state==0){Flush();if(++waits>120)throw new Exception("GPU completion fence did not retire timestamp slot "+pending+" value="+data.completedFence+"/"+data.targetFence);return;}
                    if(Iterations(index)>0&&data.end<=data.begin){report.failedSample=data;throw new Exception("Nonempty GPU workload produced zero duration");}
                    samples.Add(new Sample{slot=pending,iterations=Iterations(index),begin=data.begin,end=data.end,frequency=data.frequency,targetFence=data.targetFence,completedFence=data.completedFence,milliseconds=1000.0*(data.end-data.begin)/data.frequency});
                    Marshal.FreeHGlobal(packet);packet=IntPtr.Zero;pending=-1;index++;waits=0;
                    if(index==24)
                    {
                        report.emptyMedian=Median(0);report.shortMedian=Median(128);report.longMedian=Median(2048);
                        if(!(report.longMedian>report.shortMedian*2&&report.longMedian>report.emptyMedian*3))throw new Exception("GPU timestamp qualification did not distinguish known loads: empty="+report.emptyMedian+" short="+report.shortMedian+" long="+report.longMedian);
                        report.pass=true;Finish(null);return;
                    }
                }
                pending=HoGpu_Allocate();if(pending<0)throw new Exception("No native query slot");packet=Marshal.AllocHGlobal(4);Marshal.WriteInt32(packet,pending);
                using(var cmd=new CommandBuffer{name="GPU timestamp known-load qualification"})
                {
                    cmd.IssuePluginEventAndData(callback,eventBase+1,packet);
                    int iterations=Iterations(index);if(iterations>0){int kernel=shader.FindKernel("Burn");cmd.SetComputeBufferParam(shader,kernel,"_Output",output);cmd.SetComputeIntParam(shader,"_Iterations",iterations);cmd.SetComputeIntParam(shader,"_Seed",index+1);cmd.DispatchCompute(shader,kernel,128,1,1);}
                    cmd.IssuePluginEventAndData(callback,eventBase+2,packet);cmd.IssuePluginEventAndData(callback,eventBase+3,packet);Graphics.ExecuteCommandBuffer(cmd);
                }
                Flush();
            }
            catch(Exception error){Finish(error);}
        }
        static int Iterations(int sample)=>sample%3==0?0:sample%3==1?128:2048;
        static double Median(int iterations){var values=samples.Where(x=>x.iterations==iterations).Select(x=>x.milliseconds).OrderBy(x=>x).ToArray();return (values[3]+values[4])*.5;}
        static void Finish(Exception error)
        {
            if(finished)return;finished=true;EditorApplication.update-=Tick;report.error=error?.ToString();report.pass=error==null&&report.pass;report.samples=samples.ToArray();
            if(directory!=null)File.WriteAllText(Path.Combine(directory,"report.json"),JsonUtility.ToJson(report,true));
            // A packet may still be referenced by an unretired callback after a
            // failure. Leave that tiny allocation to process exit in that case.
            output?.Dispose();sync?.Release();if(sync!=null)UnityEngine.Object.DestroyImmediate(sync);if(pixel!=null)UnityEngine.Object.DestroyImmediate(pixel);
            if(error!=null)Debug.LogException(error);Debug.Log("[HoGIGpuTimestamp] END pass="+report.pass+" samples="+samples.Count);EditorApplication.Exit(report.pass?0:1);
        }
    }
}
