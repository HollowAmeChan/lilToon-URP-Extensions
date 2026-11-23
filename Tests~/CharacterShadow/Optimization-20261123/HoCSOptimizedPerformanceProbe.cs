using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class HoCSOptimizedPerformanceProbe
{
    [Serializable] public class Measurement { public string label; public int atlas, width, height, samples; public double gpuMedianMs, gpuP90Ms, cpuMedianMs; }
    [Serializable] public class Report { public string gpu, api; public List<Measurement> cases = new(); }
    struct Test { public string label; public int atlas, width, height, mode, blockers, filters; public bool pcss, reference, sloped; public float softness; }
    static readonly List<Test> tests = new();
    static readonly List<double> gpu = new(), cpu = new();
    static Report report;
    static Material material;
    static RenderTexture atlas, target;
    static ProfilingSampler sampler;
    static int index, frame, empty;
    static double start;
    const string Output = "D:/Unity_Fork/lilToon-URP-Extensions/Temp~/CharacterShadowOptimization-20261123";
    public static void Run()
    {
        try
        {
            Directory.CreateDirectory(Output);
            ShaderUtil.allowAsyncCompilation = false;
            var shader = Shader.Find("Hidden/Ho-CS/OptimizedPerformanceProbe");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("benchmark shader unavailable");
            material = new Material(shader);
            report = new Report { gpu = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString() };
            QualitySettings.vSyncCount = 0;
            tests.Clear(); index = 0;
            foreach (int size in new[] {2048,4096})
            foreach (float softness in new[] {0f,0.002f,0.005f})
            for(int round=0;round<4;round++)
            {
                bool reference=round==0||round==3;
                tests.Add(new Test{label="flat-"+(reference?"baseline":"optimized")+"-"+softness+"-"+round,atlas=size,width=1280,height=720,mode=2,pcss=false,blockers=32,filters=64,reference=reference,softness=softness});
            }
            foreach (int size in new[] {2048,4096})
            for(int round=0;round<4;round++)
            {
                bool reference=round==0||round==3;
                tests.Add(new Test{label="sloped-"+(reference?"baseline":"optimized")+"-0.002-"+round,atlas=size,width=1280,height=720,mode=2,pcss=false,blockers=32,filters=64,reference=reference,sloped=true,softness=0.002f});
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
        Shader.SetGlobalVector("_HoCSPcssParams2",new Vector4(0.0005f,t.blockers,t.filters,t.softness));
        lilToon.URP.Extensions.CharacterShadow.HoCharacterShadowShaderContract.BindSamplingPattern(new Vector4(0.0005f,t.blockers,t.filters,t.softness));
        material.SetFloat("_BenchmarkMode",t.mode);
        material.SetVector("_ReceiverGradient",t.sloped ? new Vector4(0.2f,0.1f,0,0) : Vector4.zero);
        if(t.reference) material.EnableKeyword("HO_CS_REFERENCE_IMPLEMENTATION");else material.DisableKeyword("HO_CS_REFERENCE_IMPLEMENTATION");
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
            EditorApplication.QueuePlayerLoopUpdate();
            if(frame>12)
            {
                if(sampler.gpuSampleCount>0 && sampler.gpuElapsedTime>0){gpu.Add(sampler.gpuElapsedTime/sampler.gpuSampleCount/8.0);cpu.Add(sampler.cpuElapsedTime/Math.Max(1,sampler.cpuSampleCount));}
                else empty++;
            }
            var cmd=CommandBufferPool.Get("HoCS benchmark");
            using(new ProfilingScope(cmd,sampler)){for(int repeat=0;repeat<8;repeat++)cmd.Blit(null,target,material,1);}
            Graphics.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
            // Readback ensures batch mode submits actual GPU commands rather than measuring idle editor updates.
            var request=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1);request.WaitForCompletion();
            if(gpu.Count>=32 || frame>=100)
            {
                gpu.Sort();cpu.Sort();var t=tests[index];
                var m=new Measurement{label=t.label,atlas=t.atlas,width=t.width,height=t.height,samples=gpu.Count,
                    gpuMedianMs=gpu.Count>0?gpu[gpu.Count/2]:-1,gpuP90Ms=gpu.Count>0?gpu[(int)(gpu.Count*.9)]:-1,cpuMedianMs=cpu.Count>0?cpu[cpu.Count/2]:-1};
                report.cases.Add(m);File.WriteAllText(Output+"/sampling-math-ab.json",JsonUtility.ToJson(report,true));
                Debug.Log("HO_CS_BENCH_RESULT "+JsonUtility.ToJson(m));sampler.enableRecording=false;
                if(++index==tests.Count){Debug.Log("HO_CS_BENCH_DONE");EditorApplication.update-=Update;EditorApplication.Exit(0);return;}
                Begin();
            }
            if(EditorApplication.timeSinceStartup-start>300)throw new TimeoutException("benchmark timeout");
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.update-=Update;EditorApplication.Exit(1);}
    }
}
