using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using lilToon.URP.Extensions.Debugging;
using lilToon.URP.Extensions.CharacterShadow;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
using Debug=UnityEngine.Debug;
#pragma warning disable CS0618
public static class HoCSCameraPerformanceProbe
{
    [Serializable] public class Result { public string label; public int resolution,samples; public double atlasMs,opaqueMs,cpuRenderMs; public string status; }
    [Serializable] public class Report {public string gpu,api;public int width=2560,height=1440;public List<Result> cases=new();}
    struct Test{public string label;public int size;public bool enabled,bypass,pcss,sm35;public float softness;public HoCharacterShadowPcssQuality quality;}
    static List<Test> tests=new();static int index,frame;static double start;
    static Camera camera;static RenderTexture target;static Material receiverMaterial;
    static HoCharacterShadowRendererFeature feature;static HoCharacterShadow subject;
    static ProfilingSampler atlasSampler,opaqueSampler;static Report report;
    static List<double> atlas=new(),opaque=new(),cpu=new();
    static string Output="D:/Unity_Fork/lilToon-URP-Extensions/Temp~/CharacterShadowPerf-20261123/receiver-target-comparison.json";
    public static void Run()
    {
        try
        {
            ShaderUtil.allowAsyncCompilation=false;
            QualitySettings.vSyncCount=0;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var data=ScriptableObject.CreateInstance<UniversalRendererData>();
            feature=ScriptableObject.CreateInstance<HoCharacterShadowRendererFeature>();
            data.rendererFeatures.Add(feature);
            var pipeline=UniversalRenderPipelineAsset.Create(data);pipeline.shadowDistance=50;pipeline.shadowCascadeCount=1;
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            var sun=new GameObject("sun").AddComponent<Light>();sun.type=LightType.Directional;sun.shadows=LightShadows.Hard;RenderSettings.sun=sun;
            camera=new GameObject("probe").AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1;
            camera.transform.position=new Vector3(0,0,-3);camera.transform.LookAt(Vector3.zero);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            target=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGB32);target.Create();
            var receiver=GameObject.CreatePrimitive(PrimitiveType.Quad);receiver.transform.localScale=Vector3.one*2;
            var renderer=receiver.GetComponent<MeshRenderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;
            receiverMaterial=new Material(Shader.Find("Hidden/Ho-CS/ReceiverPerformanceProbe"));renderer.sharedMaterial=receiverMaterial;
            var group=receiver.AddComponent<HoObjectBufferGroup>();group.parts.Add(new HoObjectBufferPartEntry{name="receiver",includeChildren=false,renderers=new Object[]{renderer}});group.Apply();
            subject=receiver.AddComponent<HoCharacterShadow>();subject.objectGroup=group;subject.center=Vector3.zero;subject.size=new Vector3(1.1f,1.1f,.5f);
            var caster=GameObject.CreatePrimitive(PrimitiveType.Cube);caster.transform.position=new Vector3(-.3f,0,-1);caster.transform.localScale=new Vector3(.6f,2,.2f);
            caster.GetComponent<MeshRenderer>().sharedMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));caster.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.ShadowsOnly;
            atlasSampler=HoDebugPerformanceRegistry.GetSampler("Ho-CharacterShadow");atlasSampler.enableRecording=true;
            var type=typeof(UniversalRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.Universal.URPProfileId");
            var get=typeof(ProfilingSampler).GetMethods().First(m=>m.Name=="Get"&&m.IsGenericMethodDefinition);
            opaqueSampler=(ProfilingSampler)get.MakeGenericMethod(type).Invoke(null,new[]{Enum.Parse(type,"DrawOpaqueObjects")});opaqueSampler.enableRecording=true;
            report=new Report{gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString()};
            foreach(int size in new[]{2048})
            foreach(bool sm35 in new[]{false,true})
            {
                Add("off",false,true,false,0,HoCharacterShadowPcssQuality.Low);
                Add("atlas-only",true,true,true,.002f,HoCharacterShadowPcssQuality.Ultra);
                Add("pcf-zero",true,false,false,0,HoCharacterShadowPcssQuality.Ultra);
                Add("pcss-low",true,false,true,.002f,HoCharacterShadowPcssQuality.Low);
                Add("pcss-ultra",true,false,true,.002f,HoCharacterShadowPcssQuality.Ultra);
                void Add(string label,bool enabled,bool bypass,bool pcss,float softness,HoCharacterShadowPcssQuality q)=>tests.Add(new Test{label=(sm35?"SM35 ":"SM45 ")+label,size=size,enabled=enabled,bypass=bypass,pcss=pcss,softness=softness,quality=q,sm35=sm35});
            }
            start=EditorApplication.timeSinceStartup;Begin();EditorApplication.update+=Update;
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void Begin()
    {
        frame=0;atlas.Clear();opaque.Clear();cpu.Clear();var t=tests[index];feature.SetActive(t.enabled);
        feature.Settings.resolution=(HoCharacterShadowResolution)t.size;feature.Settings.pcssQuality=t.quality;feature.Settings.pcssEnabled=t.pcss;
        feature.Settings.softnessRadius=t.softness;feature.Settings.pcssSoftness=.1f;feature.Settings.pcssBlockerSearchRadius=.11f;feature.Settings.pcssMaxPenumbraRadius=.076f;feature.Settings.pcssDepthBias=0;
        receiverMaterial.shader=Shader.Find(t.sm35?"Hidden/Ho-CS/ReceiverPerformanceProbeSM35":"Hidden/Ho-CS/ReceiverPerformanceProbe");
        if(t.bypass)receiverMaterial.EnableKeyword("HO_CS_BYPASS");else receiverMaterial.DisableKeyword("HO_CS_BYPASS");
        Debug.Log("HO_CS_CAMERA_BEGIN "+t.label+" "+t.size);
    }
    static void Update()
    {
        try
        {
            frame++;var t=tests[index];
            if(frame>18&&opaqueSampler.gpuSampleCount>0)
            {
                opaque.Add(opaqueSampler.gpuElapsedTime/opaqueSampler.gpuSampleCount);
                atlas.Add(t.enabled&&atlasSampler.gpuSampleCount>0?atlasSampler.gpuElapsedTime:0);
            }
            HoDebugPerformanceRegistry.RequestRecording();
            var timer=Stopwatch.StartNew();RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});timer.Stop();
            if(frame>18)cpu.Add(timer.Elapsed.TotalMilliseconds);
            var request=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1);request.WaitForCompletion();
            if(opaque.Count>=32||frame>=100)
            {
                atlas.Sort();opaque.Sort();cpu.Sort();
                var r=new Result{label=t.label,resolution=t.size,samples=opaque.Count,atlasMs=atlas.Count>0?atlas[atlas.Count/2]:-1,opaqueMs=opaque.Count>0?opaque[opaque.Count/2]:-1,cpuRenderMs=cpu[cpu.Count/2],status=HoCharacterShadowRendererFeature.LastCullStatus+";"+subject.status};
                report.cases.Add(r);Directory.CreateDirectory(Path.GetDirectoryName(Output));File.WriteAllText(Output,JsonUtility.ToJson(report,true));
                Debug.Log("HO_CS_CAMERA_RESULT "+JsonUtility.ToJson(r));
                if(++index==tests.Count){Debug.Log("HO_CS_CAMERA_DONE");EditorApplication.update-=Update;EditorApplication.Exit(0);return;}Begin();
            }
            if(EditorApplication.timeSinceStartup-start>240)throw new TimeoutException();
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.update-=Update;EditorApplication.Exit(1);}
    }
}
