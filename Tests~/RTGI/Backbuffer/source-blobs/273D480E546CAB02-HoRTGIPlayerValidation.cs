using System;
using System.IO;
using System.Linq;
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
        public static void BuildCutout()=>Build(false,true);
        public static void BuildCutoutCompatibility()=>Build(true,true);
        public static void BuildSkinned()=>Build(false,true,true);
        public static void BuildSkinnedCompatibility()=>Build(true,true,true);
        public static void BuildSkinnedBaseVertex()=>Build(false,true,true,"BaseVertex",true);
        public static void BuildSkinnedBaseVertexCompatibility()=>Build(true,true,true,"BaseVertex",true);
        public static void BuildAutomatic()=>Build(false,true,true,"Automatic",true,true,true,true);
        public static void BuildAutomaticCompatibility()=>Build(true,true,true,"Automatic",true,true,true,true);
        public static void BuildBackbuffer()=>Build(false,true,true,"Backbuffer",true,true,true,true,true);
        public static void BuildBackbufferCompatibility()=>Build(true,true,true,"Backbuffer",true,true,true,true,true);
        public static void BuildPrecisionOutput()=>Build(false,true,true,"PrecisionOutput",true,true,true);
        public static void BuildPrecisionOutputCompatibility()=>Build(true,true,true,"PrecisionOutput",true,true,true);
        public static void BuildPrecisionPool()=>Build(false,true,true,"PrecisionPool",true,true,true);
        public static void BuildPrecisionPoolCompatibility()=>Build(true,true,true,"PrecisionPool",true,true,true);
        public static void BuildHistoryFeedback()=>Build(false,true,true,"HistoryFeedback",true,true);
        public static void BuildHistoryFeedbackCompatibility()=>Build(true,true,true,"HistoryFeedback",true,true);
        public static void BuildSkinnedNoGpuProbe()=>Build(false,true,true,"NoGpuProbe");
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
        static void Build(bool compatibility,bool cutout=false,bool skinned=false,string suffix="",bool baseVertex=false,bool historyProbe=false,bool precisionProbe=false,bool automatic=false,bool backbuffer=false)
        {
            try
            {
                string root=Directory.GetParent(Application.dataPath).FullName;
                if(!root.Replace('\\','/').EndsWith("/.codex-research/RTGIValidation"))throw new Exception("Dedicated GI project required");
                var probeType=Type.GetType((backbuffer?"HoRTGIBackbufferProbe":automatic?"HoRTGIAutomaticProbe":"HoRTGIRuntimeProbe")+", Assembly-CSharp");if(probeType==null)throw new Exception("Copy the selected Tests~/RTGI/Dynamic probe into isolated Assets before launching Build");
                string assets=precisionProbe?("Assets/RTGIPlayerProbe"+suffix+(compatibility?"Compatibility/":"/")):historyProbe?(compatibility?"Assets/RTGIPlayerProbeHistoryFeedbackCompatibility/":"Assets/RTGIPlayerProbeHistoryFeedback/"):baseVertex?"Assets/RTGIPlayerProbeBaseVertex/":"Assets/RTGIPlayerProbe/";Directory.CreateDirectory(Path.Combine(root,assets));AssetDatabase.Refresh();
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
                PlayerSettings.defaultScreenWidth=PlayerSettings.defaultScreenHeight=backbuffer?512:64;PlayerSettings.runInBackground=true;
                var camera=new GameObject("GI Player camera").AddComponent<Camera>();camera.enabled=automatic;camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;camera.transform.position=new Vector3(0,0,-3);camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.cullingMask=1;camera.allowHDR=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.GetUniversalAdditionalCameraData().volumeLayerMask=-1;
                if(automatic&&!backbuffer){var automaticTarget=new RenderTexture(64,64,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);automaticTarget.Create();AssetDatabase.CreateAsset(automaticTarget,assets+"AutomaticTarget.renderTexture");camera.targetTexture=automaticTarget;}
                var receiver=Quad("Receiver",Vector3.zero,new Vector3(2,2,1),0,Color.white,Color.black,assets+"Receiver.mat");
                var red=Quad("Offscreen red emitter",new Vector3(-1,0,-1),new Vector3(2,4,1),1,Color.black,Color.red,assets+"Red.mat");red.transform.rotation=Quaternion.Euler(0,180,0);
                var blue=Quad("Offscreen blue emitter",new Vector3(1,0,-1),new Vector3(2,4,1),1,Color.black,Color.blue,assets+"Blue.mat");blue.transform.rotation=Quaternion.Euler(0,180,0);
                Material cutoutMaterial=null,skyMaterial=null;Renderer cutoutRenderer=null;
                if(cutout)
                {
                    var shell=Quad("Serialized excluded sky shell",new Vector3(0,0,-.25f),Vector3.one*4000,1,Color.black,Color.black,assets+"ExcludedSky.mat");skyMaterial=shell.GetComponent<Renderer>().sharedMaterial;
                    gi.Settings.excludedRayMaterials=new[]{skyMaterial};EditorUtility.SetDirty(gi);
                    var panel=GameObject.CreatePrimitive(PrimitiveType.Quad);panel.name="Player GI Cutout panel";panel.layer=1;panel.transform.position=new Vector3(0,0,-.5f);panel.transform.localScale=Vector3.one*4000;
                    cutoutMaterial=new Material(Shader.Find("Hidden/lilToonCutout"));cutoutMaterial.SetOverrideTag("RenderType","TransparentCutout");cutoutMaterial.renderQueue=2450;cutoutMaterial.SetFloat("_Cull",0);cutoutMaterial.SetFloat("_Cutoff",.5f);cutoutMaterial.SetFloat("_AlphaMaskMode",0);cutoutMaterial.SetFloat("_Reflectance",0);cutoutMaterial.SetFloat("_Metallic",0);cutoutMaterial.SetFloat("_Smoothness",0);cutoutMaterial.SetFloat("_UseEmission",0);cutoutMaterial.SetFloat("_UseEmission2nd",0);cutoutMaterial.SetTexture("_MainTex",Texture2D.whiteTexture);cutoutMaterial.SetColor("_Color",new Color(0,0,0,0));AssetDatabase.CreateAsset(cutoutMaterial,assets+"Cutout.mat");cutoutRenderer=panel.GetComponent<Renderer>();cutoutRenderer.sharedMaterial=cutoutMaterial;
                }
                Transform blueBone=null;SkinnedMeshRenderer blueSkin=null;
                if(skinned){MakeSkin(red,false,assets+"RedSkin.asset",out _,baseVertex);blueSkin=MakeSkin(blue,true,assets+"BlueSkin.asset",out blueBone,baseVertex);cutoutRenderer=MakeSkin(cutoutRenderer.gameObject,false,assets+"CutoutSkin.asset",out _,baseVertex);}
                var holder=new GameObject("GI Player Volume").AddComponent<Volume>();holder.isGlobal=true;var profile=ScriptableObject.CreateInstance<VolumeProfile>();var component=profile.Add<HoRTGIVolume>();BoundScript(component);component.enable.Override(true);AssetDatabase.CreateAsset(profile,assets+"Volume.asset");AssetDatabase.AddObjectToAsset(component,profile);EditorUtility.SetDirty(profile);EditorUtility.SetDirty(component);holder.sharedProfile=profile;
                var probe=new GameObject("GI Runtime Probe").AddComponent(probeType);var so=new SerializedObject(probe);
                Set(so,"probeCamera",camera);Set(so,"feature",gi);Set(so,"redMaterial",red.GetComponent<Renderer>().sharedMaterial);Set(so,"blueWall",blue.transform);Set(so,"profile",profile);so.ApplyModifiedPropertiesWithoutUndo();
                if(cutout){Set(so,"cutoutMaterial",cutoutMaterial);Set(so,"cutoutRenderer",cutoutRenderer);Set(so,"excludedSky",skyMaterial);so.ApplyModifiedPropertiesWithoutUndo();}
                if(skinned){Set(so,"blueBone",blueBone);Set(so,"blueSkin",blueSkin);so.ApplyModifiedPropertiesWithoutUndo();}
                var baseVertexProperty=so.FindProperty("baseVertexProbe");if(baseVertex&&baseVertexProperty==null)throw new Exception("Copy the updated runtime probe before base-vertex build");if(baseVertexProperty!=null){baseVertexProperty.boolValue=baseVertex;so.ApplyModifiedPropertiesWithoutUndo();}
                var historyProperty=so.FindProperty("historyProbe");if(historyProbe&&historyProperty==null)throw new Exception("Copy the updated runtime history probe before build");if(historyProperty!=null){historyProperty.boolValue=historyProbe;so.ApplyModifiedPropertiesWithoutUndo();}
                var precisionProperty=so.FindProperty("precisionProbe");if(precisionProbe&&precisionProperty==null)throw new Exception("Copy the precision runtime probe before build");if(precisionProperty!=null){precisionProperty.boolValue=precisionProbe;so.ApplyModifiedPropertiesWithoutUndo();}
                AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),assets+"Probe.unity");
                string output=compatibility?"PlayerGIDynamicCompatibility":"PlayerGIDynamic";
                if(cutout)output=compatibility?"PlayerGICutoutCompatibility":"PlayerGICutout";
                if(skinned)output=compatibility?"PlayerGISkinnedCompatibility":"PlayerGISkinned";
                output+=suffix;
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
        static SkinnedMeshRenderer MakeSkin(GameObject obj,bool morph,string assetPath,out Transform bone,bool baseVertex=false)
        {
            var renderer=obj.GetComponent<MeshRenderer>();var material=renderer.sharedMaterial;var mesh=UnityEngine.Object.Instantiate(obj.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(renderer);UnityEngine.Object.DestroyImmediate(obj.GetComponent<MeshFilter>());
            if(baseVertex)
            {
                var positions=mesh.vertices;var normals=mesh.normals;var tangents=mesh.tangents;var uv=mesh.uv;var indices=mesh.GetIndices(0,false);
                mesh.vertices=positions.Concat(positions).ToArray();mesh.normals=normals.Concat(normals).ToArray();mesh.tangents=tangents.Concat(tangents).ToArray();
                var allUV=new Vector2[mesh.vertexCount];Array.Copy(uv,0,allUV,uv.Length,uv.Length);mesh.uv=mesh.uv2=allUV;mesh.SetTriangles(indices,0,false,positions.Length);mesh.RecalculateBounds();
            }
            bone=new GameObject(obj.name+" bone").transform;bone.SetParent(obj.transform,false);mesh.bindposes=new[]{Matrix4x4.identity};var weights=new BoneWeight[mesh.vertexCount];for(int i=0;i<weights.Length;i++)weights[i]=new BoneWeight{boneIndex0=0,weight0=1};mesh.boneWeights=weights;
            if(morph){var delta=new Vector3[mesh.vertexCount];for(int i=0;i<delta.Length;i++)delta[i]=Vector3.back*2;mesh.AddBlendShapeFrame("GI Player emitter move",100,delta,new Vector3[delta.Length],new Vector3[delta.Length]);}
            AssetDatabase.CreateAsset(mesh,assetPath);mesh.UploadMeshData(true);EditorUtility.SetDirty(mesh);
            var skin=obj.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.sharedMaterial=material;skin.bones=new[]{bone};skin.rootBone=bone;skin.updateWhenOffscreen=false;return skin;
        }
        static GameObject Quad(string name,Vector3 position,Vector3 scale,int layer,Color albedo,Color emission,string path)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Quad);obj.name=name;obj.layer=layer;obj.transform.position=position;obj.transform.localScale=scale;
            var material=new Material(Shader.Find("lilToon"));material.EnableKeyword("UNITY_UI_ALPHACLIP");material.SetFloat("_Cull",0);material.SetFloat("_AsUnlit",1);material.SetColor("_Color",albedo);material.SetTexture("_MainTex",Texture2D.whiteTexture);material.SetFloat("_Reflectance",0);material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",0);material.SetFloat("_UseEmission",emission.maxColorComponent>0?1:0);material.SetFloat("_UseEmission2nd",0);material.SetColor("_EmissionColor",emission);material.SetFloat("_EmissionMainStrength",0);
            AssetDatabase.CreateAsset(material,path);obj.GetComponent<Renderer>().sharedMaterial=material;return obj;
        }
    }
}
