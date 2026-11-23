using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class HoCSPerformanceProbe
{
    [Serializable] public class Measurement { public string label; public int atlas, width, height, samples; public double gpuMedianMs, gpuP90Ms, cpuMedianMs; }
    [Serializable] public class Report { public string gpu, api; public List<Measurement> cases = new(); }
    struct Test { public string label; public int atlas, width, height, mode, blockers, filters; public bool pcss; }
    static readonly List<Test> tests = new();
    static readonly List<double> gpu = new(), cpu = new();
    static Report report;
    static Material material;
    static RenderTexture atlas, target;
    static ProfilingSampler sampler;
    static int index, frame, empty;
    static double start;
    const string Output = "D:/Unity_Fork/lilToon-URP-Extensions/Temp~/CharacterShadowPerf-20261123";
    public static void Run()
    {
        try
        {
            Directory.CreateDirectory(Output);
            ShaderUtil.allowAsyncCompilation = false;
            var shader = Shader.Find("Hidden/Ho-CS/PerformanceProbe");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("benchmark shader unavailable");
            material = new Material(shader);
            report = new Report { gpu = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString() };
            QualitySettings.vSyncCount = 0;
            foreach (int size in new[] {1024,2048,4096})
            foreach (int width in new[] {1280,2560})
            {
                int height = width * 9 / 16;
                Add("constant",0,false,0,0); Add("point-depth",1,false,0,0);
                Add("pcf-low",2,false,8,16); Add("pcf-ultra",2,false,32,64);
                Add("pcss-low",2,true,8,16); Add("pcss-ultra",2,true,32,64);
                void Add(string label,int mode,bool pcss,int b,int f) => tests.Add(new Test{label=label,atlas=size,width=width,height=height,mode=mode,pcss=pcss,blockers=b,filters=f});
            }
            start = EditorApplication.timeSinceStartup;
            Begin();
            EditorApplication.update += Update;
        }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static void Begin()
    {
        var t=tests[index]; frame=empty=0;gpu.Clear();cpu.Clear();
        if(atlas != null){atlas.Release();UnityEngine.Object.DestroyImmediate(atlas);}
        if(target != null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
        atlas=new RenderTexture(t.atlas,t.atlas,0,RenderTextureFormat.RFloat,RenderTextureReadWrite.Linear);atlas.Create();
        target=new RenderTexture(t.width,t.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();
        Graphics.Blit(null,atlas,material,0);
        Shader.SetGlobalTexture("_HoCSAtlas",atlas);
        Shader.SetGlobalVector("_HoCSAtlasSize",new Vector4(1f/t.atlas,1f/t.atlas,t.atlas,t.atlas));
        Shader.SetGlobalVector("_HoCSPcssParams",new Vector4(t.pcss?1:0,Mathf.Tan(Mathf.Deg2Rad),0.02f,0.04f));
        Shader.SetGlobalVector("_HoCSPcssParams2",new Vector4(0.0005f,t.blockers,t.filters,0.005f));
        material.SetFloat("_BenchmarkMode",t.mode);
        material.DisableKeyword("HO_BENCH_BASELINE"); material.DisableKeyword("HO_BENCH_POINT");
        if(t.mode==0)material.EnableKeyword("HO_BENCH_BASELINE");
        if(t.mode==1)material.EnableKeyword("HO_BENCH_POINT");
        sampler=new ProfilingSampler("HoCSBench"+index);sampler.enableRecording=true;
        Debug.Log("HO_CS_BENCH_BEGIN "+index+" "+t.label+" "+t.atlas+" "+t.width);
    }
    static void Update()
    {
        try
        {
            frame++;
            if(frame>12)
            {
                if(sampler.gpuSampleCount>0 && sampler.gpuElapsedTime>0){gpu.Add(sampler.gpuElapsedTime/sampler.gpuSampleCount);cpu.Add(sampler.cpuElapsedTime/Math.Max(1,sampler.cpuSampleCount));}
                else empty++;
            }
            var cmd=CommandBufferPool.Get("HoCS benchmark");
            using(new ProfilingScope(cmd,sampler))cmd.Blit(null,target,material,1);
            Graphics.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
            // Readback ensures batch mode submits actual GPU commands rather than measuring idle editor updates.
            var request=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1);request.WaitForCompletion();
            if(gpu.Count>=32 || frame>=100)
            {
                gpu.Sort();cpu.Sort();var t=tests[index];
                var m=new Measurement{label=t.label,atlas=t.atlas,width=t.width,height=t.height,samples=gpu.Count,
                    gpuMedianMs=gpu.Count>0?gpu[gpu.Count/2]:-1,gpuP90Ms=gpu.Count>0?gpu[(int)(gpu.Count*.9)]:-1,cpuMedianMs=cpu.Count>0?cpu[cpu.Count/2]:-1};
                report.cases.Add(m);File.WriteAllText(Output+"/sampling.json",JsonUtility.ToJson(report,true));
                Debug.Log("HO_CS_BENCH_RESULT "+JsonUtility.ToJson(m));sampler.enableRecording=false;
                if(++index==tests.Count){Debug.Log("HO_CS_BENCH_DONE");EditorApplication.update-=Update;EditorApplication.Exit(0);return;}
                Begin();
            }
            if(EditorApplication.timeSinceStartup-start>240)throw new TimeoutException("benchmark timeout");
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.update-=Update;EditorApplication.Exit(1);}
    }
}
