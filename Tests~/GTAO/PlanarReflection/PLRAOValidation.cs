using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using lilToon.URP.Extensions.GTAO;
using lilToon.URP.Extensions.PlanarReflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

public static class PLRAOValidation
{
    static readonly List<Object> owned=new();
    static Camera camera;
    static HoPlanarReflectionSurface surface;
    static HoGTAORendererFeature gtao;
    static Texture seed;
    static float available=1;
    static bool publish;
    static RenderTexture target;
    static int checks;
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../TestResults/HoPLRAOValidation",SystemInfo.graphicsDeviceType.ToString()));
    public static void Run()
    {
        bool async=ShaderUtil.allowAsyncCompilation;
        var oldDefault=GraphicsSettings.defaultRenderPipeline;
        var oldQuality=QualitySettings.renderPipeline;
        Texture oldAO=Shader.GetGlobalTexture("_HoAOTexture");
        float oldAvailable=Shader.GetGlobalFloat("_HoAOAvailable");
        try
        {
            if(!Application.isBatchMode || !Application.dataPath.Contains("Temp~"))throw new InvalidOperationException("Use the isolated batch project.");
            ShaderUtil.allowAsyncCompilation=false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
            gtao=Keep(ScriptableObject.CreateInstance<HoGTAORendererFeature>());
            gtao.Create();
            gtao.Settings.enabled=false;
            renderer.rendererFeatures.Add(gtao);
            RenderPipelineManager.beginCameraRendering+=Seed;
            var plr=Keep(ScriptableObject.CreateInstance<HoPlanarReflectionRendererFeature>());
            plr.Create();
            renderer.rendererFeatures.Add(plr);
            var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));
            pipeline.msaaSampleCount=1;pipeline.supportsHDR=true;
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            camera=Keep(new GameObject("PLR source camera")).AddComponent<Camera>();camera.enabled=false;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            camera.transform.position=new Vector3(0,2,-5);camera.transform.LookAt(new Vector3(0,1,0));
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            target=Keep(new RenderTexture(128,128,24,RenderTextureFormat.ARGBHalf));target.Create();
            var actor=Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));actor.name="AO consumer";
            actor.transform.position=new Vector3(0,1.1f,0);
            actor.GetComponent<Renderer>().sharedMaterial=Keep(new Material(Shader.Find("Hidden/Ho-Validation/PLRAOConsumer")));
            var floor=Keep(GameObject.CreatePrimitive(PrimitiveType.Plane));floor.name="PLR surface";
            floor.transform.localScale=Vector3.one*2;
            var floorMaterial=Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            floorMaterial.SetColor("_BaseColor",Color.black);
            floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
            surface=floor.AddComponent<HoPlanarReflectionSurface>();
            Set("resolution",128);
            bool baseline=Environment.CommandLine.Contains("-plrAOBaseline");
            seed=Solid(1);Render(); // Complete renderer creation before injecting a previous camera's AO.
            foreach(bool oblique in new[]{false,true})
            foreach(float value in new[]{0f,.25f,1f})
            {
                Set("useObliqueClipPlane",oblique);seed=Solid(value);publish=false;
                Render();
                var reflection=(RenderTexture)typeof(HoPlanarReflectionSurface).GetField("reflectionCameraTexture",Private).GetValue(surface);
                Require(reflection!=null,"Production PLR did not capture a reflection.");
                float peak=Peak(reflection);
                float main=Peak(target);
                Debug.Log($"PLR_AO_CASE baseline={baseline} oblique={oblique} seed={value} reflectedPeak={peak:R} sourcePeak={main:R}");
                if(baseline) Require(Mathf.Abs(peak-value)<.03f,"Baseline no longer reproduces inherited AO.");
                else Require(peak>.9f,"Reflection sampled another camera's AO.");
                if(value<1) Require(Mathf.Abs(main-value)<.03f,"Reflection did not restore parent AO state.");
                Save(reflection,$"{(baseline?"before":"after")}-oblique-{oblique}-seed-{value}.png");
            }
            available=0;seed=Solid(0);Render();
            Require(Peak(target)>.9f,"Unavailable parent AO did not retain its neutral fallback.");
            available=1;publish=true;Render();
            Require(Peak(target)>.9f,"Main camera publish-only AO no longer reaches opaque materials.");
            Debug.Log($"PLR_AO_VALIDATION_{(baseline?"BASELINE":"PASSED")} api={SystemInfo.graphicsDeviceType} checks={checks}");
        }
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);return;}
        finally
        {
            RenderPipelineManager.beginCameraRendering-=Seed;
            GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;
            for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)Object.DestroyImmediate(owned[i]);owned.Clear();
            Shader.SetGlobalTexture("_HoAOTexture",oldAO);Shader.SetGlobalFloat("_HoAOAvailable",oldAvailable);
            ShaderUtil.allowAsyncCompilation=async;
        }
        EditorApplication.Exit(0);
    }
    static void Seed(ScriptableRenderContext context,Camera value)
    {
        if(value!=camera)return;
        Shader.SetGlobalTexture("_HoAOTexture",seed);
        Shader.SetGlobalFloat("_HoAOAvailable",available);
        var command=CommandBufferPool.Get("Seed previous camera AO");
        command.SetGlobalTexture("_HoAOTexture",seed);
        command.SetGlobalFloat("_HoAOAvailable",available);
        context.ExecuteCommandBuffer(command);
        CommandBufferPool.Release(command);
        gtao.Settings.enabled=publish;gtao.Settings.publishAOTextureOnly=publish;
    }
    static void Render()=>RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=target});
    static Texture Solid(float v){var t=Keep(new Texture2D(1,1,TextureFormat.RGBAFloat,false,true));t.SetPixel(0,0,new Color(v,v,v,1));t.Apply();return t;}
    static Texture2D Read(RenderTexture texture)
    {
        var previous=RenderTexture.active;RenderTexture.active=texture;
        var image=new Texture2D(texture.width,texture.height,TextureFormat.RGBAFloat,false,true);
        image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();RenderTexture.active=previous;return image;
    }
    static float Peak(RenderTexture texture){var image=Read(texture);float max=0;foreach(var c in image.GetPixels())max=Mathf.Max(max,c.r);Object.DestroyImmediate(image);return max;}
    static void Save(RenderTexture texture,string name){Directory.CreateDirectory(Output);var image=Read(texture);File.WriteAllBytes(Path.Combine(Output,name),image.EncodeToPNG());Object.DestroyImmediate(image);}
    static T Keep<T>(T value)where T:Object{owned.Add(value);return value;}
    static void Set(string name,object value)=>typeof(HoPlanarReflectionSurface).GetField(name,Private).SetValue(surface,value);
    static void Require(bool condition,string message){checks++;if(!condition)throw new InvalidOperationException(message);}
}
