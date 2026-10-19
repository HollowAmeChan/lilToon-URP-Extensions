using System;
using System.IO;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static class HoRTGIPlayerValidation
    {
        public static void Build()=>Build(false);
        public static void BuildCompatibility()=>Build(true);
        public static void AuditScripts()
        {
            try
            {
                foreach(var type in new[]{typeof(HoRTGIRendererFeature),typeof(HoGeometryBufferRendererFeature),typeof(HoSurfaceBufferRendererFeature),typeof(HoRTGIVolume)})
                {
                    var value=ScriptableObject.CreateInstance(type);var from=MonoScript.FromScriptableObject(value);
                    string[] guids=AssetDatabase.FindAssets(type.Name+" t:MonoScript");Debug.Log("[HoGIScriptAudit] type="+type.FullName+" from="+(from==null?"NULL":AssetDatabase.GetAssetPath(from)));
                    foreach(string guid in guids){string path=AssetDatabase.GUIDToAssetPath(guid);var script=AssetDatabase.LoadAssetAtPath<MonoScript>(path);Debug.Log("[HoGIScriptAudit] path="+path+" asset="+(script==null?"NULL":script.name)+" class="+script?.GetClass()?.FullName);}
                    UnityEngine.Object.DestroyImmediate(value);
                }
                EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void ReimportScripts()
        {
            foreach(string guid in AssetDatabase.FindAssets("t:MonoScript",new[]{"Packages/jp.lilxyzw.liltoon.urp.extensions"}))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid),ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AuditScripts();
        }
        static void Build(bool compatibility)
        {
            try
            {
                string root=Directory.GetParent(Application.dataPath).FullName;
                if(!root.Replace('\\','/').EndsWith("/.codex-research/RTGIValidation"))throw new Exception("Dedicated GI project required");
                var probeType=Type.GetType("HoRTGIRuntimeProbe, Assembly-CSharp");if(probeType==null)throw new Exception("Copy Tests~/RTGI/Dynamic/HoRTGIRuntimeProbe.cs into isolated Assets before launching Build");
                const string assets="Assets/RTGIPlayerProbe/";Directory.CreateDirectory(Path.Combine(Application.dataPath,"RTGIPlayerProbe"));AssetDatabase.Refresh();
                // Own isolated outputs only.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);RenderSettings.fog=false;
                var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,assets+"Renderer.asset");
                var gb=ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>();gb.Settings.maxRenderQueue=(int)RenderQueue.GeometryLast;
                var sb=ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>();sb.Settings.enableSemanticLanes=sb.Settings.enableCorrelatedSemantics=false;
                var gi=ScriptableObject.CreateInstance<HoRTGIRendererFeature>();gi.Settings.denoise=true;gi.Settings.composite=true;gi.Settings.intensity=1;gi.Settings.useEnvironment=false;gi.Settings.inSceneView=false;gi.Create();
                foreach(var feature in new ScriptableRendererFeature[]{gb,sb,gi}){BoundScript(feature);feature.name=feature.GetType().Name;AssetDatabase.AddObjectToAsset(feature,renderer);renderer.rendererFeatures.Add(feature);EditorUtility.SetDirty(feature);}EditorUtility.SetDirty(renderer);
                var serialized=new SerializedObject(gi);
                foreach(string name in new[]{"shader","rayShader","secondaryMaterialShader","compositeShader"})if(serialized.FindProperty(name).objectReferenceValue==null)throw new Exception("Missing serialized GI shader: "+name);
                var library=serialized.FindProperty("nrdShaders");if(library.arraySize!=15)throw new Exception("Missing GI NRD library");for(int i=0;i<15;i++)if(library.GetArrayElementAtIndex(i).objectReferenceValue==null)throw new Exception("Missing GI NRD shader "+i);
                var pipeline=UniversalRenderPipelineAsset.Create(renderer);pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;AssetDatabase.CreateAsset(pipeline,assets+"Pipeline.asset");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #endif
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D12,GraphicsDeviceType.Direct3D11});PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone,ManagedStrippingLevel.Disabled);
                PlayerSettings.defaultScreenWidth=PlayerSettings.defaultScreenHeight=64;PlayerSettings.runInBackground=true;
                var camera=new GameObject("GI Player camera").AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;camera.transform.position=new Vector3(0,0,-3);camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.cullingMask=1;camera.allowHDR=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.GetUniversalAdditionalCameraData().volumeLayerMask=-1;
                var receiver=Quad("Receiver",Vector3.zero,new Vector3(2,2,1),0,Color.white,Color.black,assets+"Receiver.mat");
                var red=Quad("Offscreen red emitter",new Vector3(-1,0,-1),new Vector3(2,4,1),1,Color.black,Color.red,assets+"Red.mat");red.transform.rotation=Quaternion.Euler(0,180,0);
                var blue=Quad("Offscreen blue emitter",new Vector3(1,0,-1),new Vector3(2,4,1),1,Color.black,Color.blue,assets+"Blue.mat");blue.transform.rotation=Quaternion.Euler(0,180,0);
                var holder=new GameObject("GI Player Volume").AddComponent<Volume>();holder.isGlobal=true;var profile=ScriptableObject.CreateInstance<VolumeProfile>();var component=profile.Add<HoRTGIVolume>();BoundScript(component);component.enable.Override(true);AssetDatabase.CreateAsset(profile,assets+"Volume.asset");AssetDatabase.AddObjectToAsset(component,profile);EditorUtility.SetDirty(profile);EditorUtility.SetDirty(component);holder.sharedProfile=profile;
                var probe=new GameObject("GI Runtime Probe").AddComponent(probeType);var so=new SerializedObject(probe);
                Set(so,"probeCamera",camera);Set(so,"feature",gi);Set(so,"redMaterial",red.GetComponent<Renderer>().sharedMaterial);Set(so,"blueWall",blue.transform);Set(so,"profile",profile);so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),assets+"Probe.unity");
                string output=compatibility?"PlayerGIDynamicCompatibility":"PlayerGIDynamic";
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{assets+"Probe.unity"},target=BuildTarget.StandaloneWindows64,locationPathName=Path.Combine(root,output,"HoRTGIProbe.exe"),options=BuildOptions.None});
                if(report.summary.result!=BuildResult.Succeeded)throw new Exception("GI Player build failed: "+report.summary.result);
                Debug.Log("[HoGIPlayerBuild] PASS size="+report.summary.totalSize+" errors="+report.summary.totalErrors);EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        static void Set(SerializedObject obj,string field,UnityEngine.Object value)=>obj.FindProperty(field).objectReferenceValue=value;
        static void BoundScript(ScriptableObject obj)
        {
            var script=MonoScript.FromScriptableObject(obj);
            if(script==null||script.GetClass()!=obj.GetType())throw new Exception("Script import/class mapping unavailable for serialized Player asset: "+obj.GetType().FullName);
        }
        static GameObject Quad(string name,Vector3 position,Vector3 scale,int layer,Color albedo,Color emission,string path)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Quad);obj.name=name;obj.layer=layer;obj.transform.position=position;obj.transform.localScale=scale;
            var material=new Material(Shader.Find("lilToon"));material.EnableKeyword("UNITY_UI_ALPHACLIP");material.SetFloat("_Cull",0);material.SetFloat("_AsUnlit",1);material.SetColor("_Color",albedo);material.SetTexture("_MainTex",Texture2D.whiteTexture);material.SetFloat("_Reflectance",0);material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",0);material.SetFloat("_UseEmission",emission.maxColorComponent>0?1:0);material.SetFloat("_UseEmission2nd",0);material.SetColor("_EmissionColor",emission);material.SetFloat("_EmissionMainStrength",0);
            AssetDatabase.CreateAsset(material,path);obj.GetComponent<Renderer>().sharedMaterial=material;return obj;
        }
    }
}
