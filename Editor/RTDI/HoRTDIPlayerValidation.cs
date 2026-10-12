using System;
using System.IO;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.SurfaceBuffer;
using lilToon.URP.Extensions.RTDI;
using lilToon.URP.Extensions.Atmosphere;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static class HoRTDIPlayerValidation
    {
        private static string ProjectRoot()
        {
            string root=Directory.GetParent(Application.dataPath).FullName;
            if(!root.Replace('\\','/').Contains("/.codex-research/"))throw new InvalidOperationException("Player validation is restricted to the isolated research project");return root;
        }
        public static void Prepare()
        {
            ProjectRoot();var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/jp.lilxyzw.liltoon.urp.extensions/package.json");
            string dst=Path.Combine(Application.dataPath,"RTDIPlayerProbe");Directory.CreateDirectory(dst);
            foreach(string file in new[]{"HoRTDIPlayerCaptureFeature.cs","HoRTDIPlayerProbe.cs","HoRTDIPlayerCapture.shader","HoRTDIPlayerGpuTiming.cs"})
                File.Copy(Path.Combine(package.resolvedPath,"Tests~/Player/RTDI",file),Path.Combine(dst,file),true);
            AssetDatabase.Refresh();Debug.Log("[HoRTDIPlayerValidation] PREPARED runtime probe templates");EditorApplication.Exit(0);
        }
        public static void Build()=>Build(false);
        public static void BuildCompatibility()=>Build(true);
        public static void BuildGpuTiming()=>Build(false,true);
        public static void BuildGpuTimingCompatibility()=>Build(true,true);
        private static void Build(bool compatibility,bool gpuTiming=false)
        {
            try
            {
                string root=ProjectRoot();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                const string assets="Assets/RTDIPlayerProbe/";
                var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,assets+"Renderer.asset");
                var gb=ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>();gb.Settings.maxRenderQueue=(int)RenderQueue.GeometryLast;
                var sb=ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>();sb.Settings.enableSemanticLanes=sb.Settings.enableCorrelatedSemantics=false;
                var di=ScriptableObject.CreateInstance<HoRTDIRendererFeature>();di.Settings.denoise=true;di.Settings.inSceneView=false;di.Create();
                var sky=ScriptableObject.CreateInstance<HoAtmosphereRendererFeature>();sky.Settings.source=HoAtmosphereSource.Constant;sky.Settings.constantColor=new Color(1,.5f,.25f,1);sky.Settings.enabled=false;sky.Create();
                var captureType=Type.GetType("HoRTDIPlayerCaptureFeature, Assembly-CSharp");var probeType=Type.GetType("HoRTDIPlayerProbe, Assembly-CSharp");
                if(captureType==null||probeType==null)throw new Exception("Run Prepare and allow script import before Build");
                var capture=(ScriptableRendererFeature)ScriptableObject.CreateInstance(captureType);
                var captureSerialized=new SerializedObject(capture);Set(captureSerialized,"captureShader",AssetDatabase.LoadAssetAtPath<Shader>(assets+"HoRTDIPlayerCapture.shader"));captureSerialized.ApplyModifiedPropertiesWithoutUndo();
                foreach(var feature in new ScriptableRendererFeature[]{sky,gb,sb,di,capture}){feature.name=feature.GetType().Name;AssetDatabase.AddObjectToAsset(feature,renderer);renderer.rendererFeatures.Add(feature);EditorUtility.SetDirty(feature);}
                EditorUtility.SetDirty(renderer);
                var serialized=new SerializedObject(di);if(serialized.FindProperty("shader").objectReferenceValue==null||serialized.FindProperty("nrdShaders").arraySize!=15)throw new Exception("DI shader serialization missing");
                if(serialized.FindProperty("finalVisibilityShader").objectReferenceValue==null||serialized.FindProperty("visibilityMaterialShader").objectReferenceValue==null)throw new Exception("DI alpha visibility shader references missing");
                if(serialized.FindProperty("debugShader").objectReferenceValue==null)throw new Exception("DI debug shader serialization missing");
                if(serialized.FindProperty("debugOverlayShader").objectReferenceValue==null)throw new Exception("DI debug grid shader serialization missing");
                for(int i=0;i<15;i++)if(serialized.FindProperty("nrdShaders").GetArrayElementAtIndex(i).objectReferenceValue==null)throw new Exception("NRD shader reference missing "+i);
                var pipeline=UniversalRenderPipelineAsset.Create(renderer);pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;AssetDatabase.CreateAsset(pipeline,assets+"Pipeline.asset");
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #endif
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D12,GraphicsDeviceType.Direct3D11});
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);PlayerSettings.defaultScreenWidth=PlayerSettings.defaultScreenHeight=64;PlayerSettings.runInBackground=true;
                var camera=new GameObject("Player probe camera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;camera.transform.position=new Vector3(0,0,-3);camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.cullingMask=1;camera.allowHDR=true;
                camera.GetUniversalAdditionalCameraData().volumeLayerMask=-1;
                var receiver=GameObject.CreatePrimitive(PrimitiveType.Quad);receiver.transform.localScale=new Vector3(2,2,1);
                var material=new Material(Shader.Find("lilToon"));material.EnableKeyword("UNITY_UI_ALPHACLIP");material.SetColor("_Color",Color.white);material.SetTexture("_MainTex",Texture2D.whiteTexture);material.SetFloat("_Cull",0);material.SetFloat("_Reflectance",0);material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",.5f);
                AssetDatabase.CreateAsset(material,assets+"Receiver.mat");receiver.GetComponent<Renderer>().sharedMaterial=material;
                var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.layer=1;blocker.transform.position=new Vector3(.75f,0,-.5f);blocker.transform.localScale=new Vector3(.4f,.4f,.2f);blocker.GetComponent<Renderer>().sharedMaterial=material;blocker.SetActive(false);
                var light=Point("White/Red light",new Vector3(1.5f,0,-1),Color.white);var blue=Point("Blue light",light.transform.position,Color.blue);blue.enabled=false;
                var volume=new GameObject("Global DI volume").AddComponent<Volume>();volume.isGlobal=true;var profile=ScriptableObject.CreateInstance<VolumeProfile>();var component=profile.Add<HoRTDIVolume>(true);AssetDatabase.CreateAsset(profile,assets+"Volume.asset");AssetDatabase.AddObjectToAsset(component,profile);EditorUtility.SetDirty(profile);EditorUtility.SetDirty(component);volume.sharedProfile=profile;
                var probe=new GameObject("Player DI validation").AddComponent(probeType);var so=new SerializedObject(probe);
                var cutoutShader=Shader.Find("Hidden/lilToonCutout");if(cutoutShader==null)throw new Exception("Player cutout source shader missing");
                var cutoutMaterial=new Material(cutoutShader);AssetDatabase.CreateAsset(cutoutMaterial,assets+"Cutout.asset");Set(so,"cutoutMaterial",cutoutMaterial);
                Set(so,"probeCamera",camera);Set(so,"feature",di);Set(so,"atmosphere",sky);Set(so,"capture",capture);Set(so,"light",light);Set(so,"blue",blue);Set(so,"blocker",blocker);Set(so,"receiver",receiver);Set(so,"surfaceMaterial",material);Set(so,"profile",profile);so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),assets+"Probe.unity");
                string output=gpuTiming?(compatibility?"PlayerGpuTimingCompatibility":"PlayerGpuTiming"):(compatibility?"PlayerBuildCompatibility":"PlayerBuild");
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{assets+"Probe.unity"},target=BuildTarget.StandaloneWindows64,locationPathName=Path.Combine(root,output,"HoRTDIProbe.exe"),options=gpuTiming?BuildOptions.Development:BuildOptions.None});
                if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Player build failed: "+report.summary.result);
                Debug.Log($"[HoRTDIPlayerValidation] BUILD PASS size={report.summary.totalSize} errors={report.summary.totalErrors}");EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static void Set(SerializedObject target,string name,UnityEngine.Object value)=>target.FindProperty(name).objectReferenceValue=value;
        private static Light Point(string name,Vector3 position,Color color){var light=new GameObject(name).AddComponent<Light>();light.type=LightType.Point;light.transform.position=position;light.color=color;light.intensity=4;light.range=100;return light;}
    }
}
