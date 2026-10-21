#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.SurfaceBuffer;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // Isolated integration test; no validation feature appears in Add Renderer Feature.
    public static class HoRTGIValidation
    {
        private static readonly List<Object> objects=new();
        private static RenderPipelineAsset previousDefault,previousQuality;
        private static bool compatibility,unsupported,previousAsync;
        private static Camera camera;
        private static RenderTexture target;
        private static HoRTGIRendererFeature feature;
        private static HoRTDIRendererFeature direct;
        private static HoGeometryBufferRendererFeature gb;
        private static HoAtmosphereRendererFeature atmosphere;
        private static HoRTGIVolume volume;
        private static Material receiverMaterial,bounceMaterial;
        private static GameObject bounce;
        private static int tick;
        [StructLayout(LayoutKind.Sequential)]
        private struct Reservoir { public Vector3 position;public uint misc,radiance;public float weight;public uint normal;public float unused; }
        static bool legacyHistoryControl;static ComputeShader legacyHistoryShader;
        public static void RunLegacyHistoryControl(){legacyHistoryControl=true;Run();}
        public static void RunCompatibility(){compatibility=true;Run();}
        public static void RunUnsupported(){unsupported=true;Run();}
        public static void Run()
        {
            try
            {
                Debug.Log("[HoRTGIValidation] START "+SystemInfo.graphicsDeviceType);
                previousDefault=GraphicsSettings.defaultRenderPipeline;previousQuality=QualitySettings.renderPipeline;
                previousAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                gb=Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());gb.Settings.maxRenderQueue=(int)RenderQueue.GeometryLast;
                var sb=Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());sb.Settings.enableSemanticLanes=sb.Settings.enableCorrelatedSemantics=false;
                atmosphere=Keep(ScriptableObject.CreateInstance<HoAtmosphereRendererFeature>());atmosphere.Settings.source=HoAtmosphereSource.Constant;
                atmosphere.Settings.constantColor=new Color(.5f,.25f,.125f,1);
                feature=Keep(ScriptableObject.CreateInstance<HoRTGIRendererFeature>());if(legacyHistoryControl){legacyHistoryShader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/HoRTGIProductionLegacyControl/Legacy.compute");if(legacyHistoryShader==null)throw new Exception("Frozen legacy control missing");typeof(HoRTGIRendererFeature).GetField("shader",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(feature,legacyHistoryShader);}feature.Settings.temporal=false;feature.Settings.spatial=false;feature.Settings.secondaryLightSamples=1;
                feature.Settings.includeDirectEnvironment=true;feature.Settings.secondaryEnvironmentSamples=0;
                direct=Keep(ScriptableObject.CreateInstance<HoRTDIRendererFeature>());direct.Settings.useEnvironment=false;direct.Settings.temporal=false;direct.Settings.spatial=false;direct.Settings.initialSamples=1;direct.SetActive(false);
                renderer.rendererFeatures.Add(gb);renderer.rendererFeatures.Add(sb);renderer.rendererFeatures.Add(atmosphere);renderer.rendererFeatures.Add(direct);renderer.rendererFeatures.Add(feature);
                var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #else
                Require(!compatibility,"Compatibility symbol missing");
                #endif
                var holder=Keep(new GameObject("RTGI validation volume")).AddComponent<Volume>();holder.isGlobal=true;
                holder.sharedProfile=Keep(ScriptableObject.CreateInstance<VolumeProfile>());volume=holder.sharedProfile.Add<HoRTGIVolume>(true);
                holder.sharedProfile.Add<HoRTDIVolume>().enable.Override(true);
                camera=Keep(new GameObject("RTGI camera")).AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;
                camera.transform.position=new Vector3(0,0,-3);camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.allowHDR=true;camera.cullingMask=1;
                camera.GetUniversalAdditionalCameraData().volumeLayerMask=-1;
                target=Keep(new RenderTexture(64,64,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();
                var receiver=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));receiver.transform.localScale=new Vector3(2,2,1);
                receiverMaterial=Mat("lilToon");PrepareMaterial(receiverMaterial,Color.white);receiver.GetComponent<Renderer>().sharedMaterial=receiverMaterial;
                bounce=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));bounce.name="World-only secondary surface";bounce.layer=1;
                bounce.transform.position=new Vector3(0,0,-1);bounce.transform.rotation=Quaternion.Euler(0,180,0);bounce.transform.localScale=new Vector3(4000,4000,1);
                bounceMaterial=Mat("lilToon");PrepareMaterial(bounceMaterial,Color.white);bounce.GetComponent<Renderer>().sharedMaterial=bounceMaterial;bounce.SetActive(false);
                EditorApplication.update+=Tick;
            }
            catch(Exception error){Finish(error);}
        }
        private static void PrepareMaterial(Material material,Color color)
        {
            material.EnableKeyword("UNITY_UI_ALPHACLIP");material.SetFloat("_Cull",0);material.SetColor("_Color",color);material.SetTexture("_MainTex",Texture2D.whiteTexture);
            material.SetFloat("_Metallic",0);material.SetFloat("_Reflectance",0);material.SetFloat("_Smoothness",.5f);material.SetFloat("_UseEmission",0);material.SetFloat("_UseEmission2nd",0);
        }
        private static void Tick()
        {
            try
            {
                if(unsupported)
                {
                    RenderCamera();Require(feature.LastContext==null&&Shader.GetGlobalFloat("_HoRTGIActive")==0,"Unsupported platform allocated/published GI");
                    Debug.Log("[HoRTGIValidation] PASS unsupported platform: "+feature.Status);Finish(null);return;
                }
                if(++tick<3){Render();return;}
                Color sky=atmosphere.Settings.constantColor;
                var initial=Render();Debug.Log("[HoRTGIValidation] initial sky="+initial);NearRGB(initial,sky,"cosine environment integral",.02f);Near(initial.a,1,"valid receiver");
                var baseline=Read(target);feature.SetActive(false);RenderCamera();NearRGB(Read(target),baseline,"no automatic camera composite");feature.SetActive(true);
                var receiverColor=new Color(.25f,.5f,.75f,1);receiverMaterial.SetColor("_Color",receiverColor);
                if(QualitySettings.activeColorSpace==ColorSpace.Linear)receiverColor=receiverColor.linear;
                NearRGB(Render(),receiverColor*sky,"receiver diffuse albedo",.015f);
                receiverMaterial.SetFloat("_Metallic",1);NearRGB(Render(),Color.black,"metallic receiver has no diffuse GI",.002f);
                receiverMaterial.SetFloat("_Metallic",0);receiverMaterial.SetColor("_Color",Color.white);
                var cube=Keep(new Cubemap(8,TextureFormat.RGBAFloat,false));var colors=new Color[64];
                for(int i=0;i<colors.Length;i++)colors[i]=new Color(1.5f,.75f,.25f,1);
                for(int face=0;face<6;face++)cube.SetPixels(colors,(CubemapFace)face);cube.Apply();
                atmosphere.Settings.source=HoAtmosphereSource.RadianceCubemap;atmosphere.Settings.radianceCubemap=cube;
                NearRGB(Render(),colors[0],"actual HDR cube ray sampling",.035f);
                atmosphere.Settings.source=HoAtmosphereSource.SkyboxMaterial;atmosphere.Settings.skyboxMaterial=Mat("Hidden/lilToon/URP/Atmosphere/ValidationSky");
                atmosphere.Settings.resolution=32;atmosphere.Settings.skyboxMaterial.SetFloat("_TestGain",8);Color skyMean=Color.clear;
                for(int i=0;i<32;i++){Render();skyMean+=ReadMean(feature.LastContext.diffuse.rt);}skyMean/=32;
                Debug.Log("[HoRTGIValidation] captured directional sky mean="+skyMean);
                NearRGB(skyMean,new Color(4,4,8f/6,1),"Unity skybox directional HDR integral",.09f);
                atmosphere.Settings.source=HoAtmosphereSource.Constant;Render();
                feature.Settings.spatial=true;Render();
                if(legacyHistoryControl){var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var pass=typeof(HoRTGIRendererFeature).GetField("pass",flags).GetValue(feature);Require(ReferenceEquals(pass.GetType().GetField("shader",flags).GetValue(pass),legacyHistoryShader),"Legacy control shader was not actually bound");Debug.Log("[HoGIHistoryLegacy] BOUND frozen legacy Final");}
                var spatialOnly=new Reservoir[feature.LastContext.reservoirs.count];feature.LastContext.reservoirs.GetData(spatialOnly);
                int spatialAddress=(32/16)*feature.LastContext.rowPitch+(32/16)*256+(32%16)*16+32%16;
                Require((spatialOnly[feature.LastContext.arrayPitch+spatialAddress].misc&255)>1,"SDK spatial-only stage did not combine neighbors");
                feature.Settings.temporal=true;
                for(int i=0;i<8;i++)Render();NearRGB(ReadMean(feature.LastContext.diffuse.rt),sky,"SDK GI resampling environment mean",.04f);
                var reservoirs=new Reservoir[feature.LastContext.reservoirs.count];feature.LastContext.reservoirs.GetData(reservoirs);
                int address=(32/16)*feature.LastContext.rowPitch+(32/16)*256+(32%16)*16+32%16;
                Require((reservoirs[address].misc&255)>1,"SDK GI reservoir did not accumulate samples");
                feature.Settings.spatial=false;
                for(int i=0;i<3;i++)Render();feature.LastContext.reservoirs.GetData(reservoirs);int aged=0;
                for(int y=24;y<40;y++)for(int x=24;x<40;x++)
                {
                    int pointer=(y/16)*feature.LastContext.rowPitch+(x/16)*256+(y%16)*16+x%16;
                    if(((reservoirs[pointer].misc>>8)&255)>0&&(reservoirs[pointer].misc&255)>1)aged++;
                }
                Require(aged>128,"SDK temporal-only stage did not retain previous samples");
                bounce.SetActive(true);bounce.GetComponent<Renderer>().sharedMaterial=Mat("Universal Render Pipeline/Unlit");
                var blocked=Render();NearRGB(blocked,Color.black,"unsupported opaque world hit occludes sky",.002f);Near(blocked.a,1,"shadow remains valid");Require(!feature.LastContext.lastTemporal,"Geometry change did not reject history");
                bounce.GetComponent<Renderer>().sharedMaterial=bounceMaterial;
                bounceMaterial.SetFloat("_UseEmission",1);bounceMaterial.SetFloat("_EmissionBlend",1);bounceMaterial.SetColor("_EmissionColor",new Color(20,10,5,1));
                bounceMaterial.SetTexture("_EmissionMap",Texture2D.whiteTexture);bounceMaterial.SetTexture("_EmissionBlendMask",Texture2D.whiteTexture);
                NearRGB(Render(),new Color(20,10,5,1),"offscreen HDR emissive integral without firefly clamp",.4f);
                receiverMaterial.SetFloat("_UseEmission",1);receiverMaterial.SetColor("_EmissionColor",new Color(4,2,1,1));
                NearRGB(Render(),new Color(20,10,5,1),"primary emission is not GI",.4f);receiverMaterial.SetFloat("_UseEmission",0);
                for(int i=0;i<3;i++)Render();Require(feature.LastContext.lastTemporal,"Static scene history was not reused");
                bounceMaterial.SetColor("_EmissionColor",Color.white);NearRGB(Render(),Color.white,"material change resets radiance history",.02f);Require(!feature.LastContext.lastTemporal,"Material change reused stale radiance");
                var map=Keep(new Texture2D(1,1,TextureFormat.RGBAFloat,false,true));map.SetPixel(0,0,new Color(.2f,.4f,.6f,1));map.Apply();bounceMaterial.SetTexture("_EmissionMap",map);
                NearRGB(Render(),new Color(.2f,.4f,.6f,1),"secondary emission texture",.02f);Render();
                map.SetPixel(0,0,new Color(.6f,.4f,.2f,1));map.Apply();NearRGB(Render(),new Color(.6f,.4f,.2f,1),"texture content revision",.02f);Require(!feature.LastContext.lastTemporal,"Texture update reused history");
                bounceMaterial.SetFloat("_UseEmission",0);feature.Settings.geometryLayers=1<<1;
                var light=Keep(new GameObject("Secondary directional light")).AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(0,180,0);light.intensity=1;
                NearRGB(Render(),Color.white/Mathf.PI,"secondary RTXDI Lambert lighting",.018f);
                light.intensity=2;NearRGB(Render(),Color.white*(2/Mathf.PI),"secondary light change",.025f);Require(!feature.LastContext.lastTemporal,"Changed light reused history");light.enabled=false;
                bounce.SetActive(false);feature.Settings.geometryLayers=-1;Render();Render();
                atmosphere.Settings.constantColor=new Color(.1f,.2f,.3f,1);NearRGB(Render(),atmosphere.Settings.constantColor,"environment revision",.02f);Require(!feature.LastContext.lastTemporal,"Changed sky reused history");
                atmosphere.Settings.enabled=false;NearRGB(Render(),Color.black,"missing sky gives black",.002f);atmosphere.Settings.enabled=true;
                feature.Settings.useEnvironment=false;NearRGB(Render(),Color.black,"GI sky sampling toggle",.002f);
                // GI binds environment locally; disabling its use must not erase other consumers' input.
                Require(Shader.GetGlobalVector("_HoAtmosphereParameters").w>.5f,"GI overwrote atmosphere publication");feature.Settings.useEnvironment=true;Render();Render();
                camera.transform.position+=new Vector3(.1f,0,-.5f);Render();Require(feature.LastContext.lastTemporal,"Static-world camera reprojection was disabled");NearRGB(ReadMean(feature.LastContext.diffuse.rt),atmosphere.Settings.constantColor,"camera motion mean",.035f);
                volume.debugMode.value=HoRTGIDebugMode.Diffuse;var debug=Render();NearRGB(Read(target),debug,"diffuse debug",.015f);
                gb.Settings.renderScale=HoGeometryBufferRenderScale.Half;Near(Render().a,0,"invalid primary domain");
                volume.debugMode.value=HoRTGIDebugMode.Receiver;Render();NearRGB(Read(target),Color.red,"invalid receiver debug");
                gb.Settings.renderScale=HoGeometryBufferRenderScale.Full;Render();NearRGB(Read(target),Color.green,"valid receiver debug");volume.debugMode.value=HoRTGIDebugMode.Off;
                volume.enable.Override(false);RenderCamera();Require(Shader.GetGlobalFloat("_HoRTGIActive")==0,"Volume disable did not reset output");volume.enable.Override(true);Render();Require(!feature.LastContext.lastTemporal,"Re-enabled GI reused history");
                var directLight=Keep(new GameObject("DI/GI coexistence directional")).AddComponent<Light>();directLight.type=LightType.Directional;directLight.intensity=.5f;
                direct.SetActive(true);NearRGB(Render(),atmosphere.Settings.constantColor,"GI remains independent when DI is enabled",.025f);
                Require(direct.LastContext!=null&&Shader.GetGlobalFloat("_HoRTDIActive")>.5f&&Shader.GetGlobalFloat("_HoRTGIActive")>.5f,"DI/GI publication conflict");
                Near(Read(direct.LastContext.diffuse.rt).r,.5f/Mathf.PI,"DI remains independent when GI is enabled",.015f);
                var compute=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTGI/Shaders/HoRTGI.compute");
                foreach(var message in ShaderUtil.GetComputeShaderMessages(compute))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
                feature.Dispose();Require(Shader.GetGlobalFloat("_HoRTGIActive")==0,"Dispose did not reset output");
                Debug.Log("[HoRTGIValidation] PASS reference GI five stages, sky/emissive/secondary analytic energy, world occlusion, SDK temporal/spatial/history, independent output/debug; compatibility="+compatibility);Finish(null);
            }
            catch(Exception error){Finish(error);}
        }
        private static void RenderCamera(){VolumeManager.instance.Update(camera.transform,-1);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});}
        private static Color Render(){RenderCamera();Require(feature.LastContext!=null,"GI context missing: "+feature.Status);return Read(feature.LastContext.diffuse.rt);}
        private static Color Read(RenderTexture source)=>ReadArea(source,false);
        private static Color ReadMean(RenderTexture source)=>ReadArea(source,true);
        private static Color ReadArea(RenderTexture source,bool mean)
        {
            var previous=RenderTexture.active;var image=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
            try
            {
                RenderTexture.active=source;image.ReadPixels(new Rect(0,0,source.width,source.height),0,0);image.Apply();
                Color result=image.GetPixel(source.width/2,source.height/2);
                if(mean){result=Color.clear;for(int y=24;y<40;y++)for(int x=24;x<40;x++)result+=image.GetPixel(x,y);result/=256;}
                Require(float.IsFinite(result.r)&&float.IsFinite(result.g)&&float.IsFinite(result.b),"Non-finite GI output");return result;
            }
            finally{RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
        private static Material Mat(string name){var shader=Shader.Find(name);Require(shader!=null,"Missing shader "+name);return Keep(new Material(shader));}
        private static T Keep<T>(T value)where T:Object{objects.Add(value);return value;}
        private static void Near(float actual,float expected,string name,float tolerance=.01f)=>Require(Mathf.Abs(actual-expected)<tolerance,$"{name}: expected {expected}, got {actual}");
        private static void NearRGB(Color actual,Color expected,string name,float tolerance=.02f){Near(actual.r,expected.r,name+" R",tolerance);Near(actual.g,expected.g,name+" G",tolerance);Near(actual.b,expected.b,name+" B",tolerance);}
        private static void Require(bool value,string message){if(!value)throw new Exception(message);}
        private static void Finish(Exception error)
        {
            EditorApplication.update-=Tick;GraphicsSettings.defaultRenderPipeline=previousDefault;QualitySettings.renderPipeline=previousQuality;ShaderUtil.allowAsyncCompilation=previousAsync;
            for(int i=objects.Count-1;i>=0;i--)if(objects[i]!=null)Object.DestroyImmediate(objects[i]);objects.Clear();
            if(error!=null)Debug.LogException(error);EditorApplication.Exit(error==null?0:1);
        }
    }
}
