#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.SurfaceBuffer;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static class HoRTDIValidation
    {
        private static readonly List<Object> objects=new();
        private static RenderPipelineAsset previousDefault,previousQuality;
        private static bool compatibility,previousAsync,unsupported;
        private static Camera camera;
        private static RenderTexture target;
        private static HoRTDIRendererFeature feature;
        private static HoGeometryBufferRendererFeature gb;
        private static HoRTDIVolume volume;
        private static Light light;
        private static Material surfaceMaterial;
        private static GameObject blocker;
        private static int tick;
        [StructLayout(LayoutKind.Sequential)]
        private struct Reservoir {public uint light,uv,visibility,age;public float target,weight;}
        public static void RunCompatibility(){compatibility=true;Run();}
        public static void RunUnsupported(){unsupported=true;Run();}
        public static void Run()
        {
            try
            {
                Debug.Log("[HoRTDIValidation] START "+SystemInfo.graphicsDeviceType);
                previousDefault=GraphicsSettings.defaultRenderPipeline;previousQuality=QualitySettings.renderPipeline;
                previousAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                gb=Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());gb.Settings.maxRenderQueue=(int)RenderQueue.GeometryLast;
                var sb=Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());sb.Settings.enableSemanticLanes=sb.Settings.enableCorrelatedSemantics=false;
                feature=Keep(ScriptableObject.CreateInstance<HoRTDIRendererFeature>());feature.Settings.temporal=false;feature.Settings.spatial=false;feature.Settings.initialSamples=1;
                renderer.rendererFeatures.Add(gb);renderer.rendererFeatures.Add(sb);renderer.rendererFeatures.Add(feature);
                var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #else
                Require(!compatibility,"Compatibility symbol missing");
                #endif
                var holder=Keep(new GameObject("RTDI validation volume")).AddComponent<Volume>();holder.isGlobal=true;
                holder.sharedProfile=Keep(ScriptableObject.CreateInstance<VolumeProfile>());volume=holder.sharedProfile.Add<HoRTDIVolume>(true);
                camera=Keep(new GameObject("RTDI validation camera")).AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1;
                camera.transform.position=new Vector3(0,0,-3);camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.aspect=1;camera.allowHDR=true;camera.GetUniversalAdditionalCameraData().volumeLayerMask=-1;
                target=Keep(new RenderTexture(64,64,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();
                var surface=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));surface.transform.localScale=new Vector3(2,2,1);
                var material=Mat("lilToon");material.EnableKeyword("UNITY_UI_ALPHACLIP");material.SetFloat("_Cull",0);material.SetColor("_Color",Color.white);material.SetTexture("_MainTex",Texture2D.whiteTexture);
                surfaceMaterial=material;
                material.SetFloat("_Reflectance",0);material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",.5f);surface.GetComponent<Renderer>().sharedMaterial=material;
                light=Point(new Vector3(1.5f,0,-1),Color.white,4);
                blocker=Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));blocker.transform.position=new Vector3(.75f,0,-.5f);blocker.transform.localScale=new Vector3(.4f,.4f,.2f);
                blocker.GetComponent<Renderer>().sharedMaterial=Mat("Universal Render Pipeline/Unlit");blocker.SetActive(false);
                EditorApplication.update+=Tick;
            }
            catch(Exception error){Finish(error);}
        }
        private static void Tick()
        {
            try
            {
                if(unsupported)
                {
                    RenderCamera();Require(feature.LastContext==null&&Shader.GetGlobalFloat("_HoRTDIActive")==0,"unsupported platform allocated/published DI");
                    Debug.Log("[HoRTDIValidation] PASS unsupported platform: "+feature.Status);Finish(null);return;
                }
                if(++tick<3){Render();return;}
                var single=Render();float expected=Expected(light);Near(single.r,expected,"single-light reference",.015f);Near(single.a,1,"receiver valid",.01f);
                var baseline=Read(target);feature.SetActive(false);RenderCamera();NearRGB(Read(target),baseline,"no automatic camera composite");feature.SetActive(true);
                blocker.SetActive(true);var shadow=Render();Near(shadow.r,0,"world visibility",.002f);Near(shadow.a,1,"shadow is still a valid receiver",.01f);
                blocker.SetActive(false);Near(Render().r,expected,"occluder removed",.015f);
                light.shadows=LightShadows.None;blocker.SetActive(true);Near(Render().r,0,"RT visibility independent of raster shadow switch",.002f);blocker.SetActive(false);light.shadows=LightShadows.Hard;
                surfaceMaterial.SetFloat("_Metallic",1);Near(Render().r,0,"metal has no diffuse response",.002f);Require(Read(feature.LastContext.specular.rt).r>0,"reference GGX specular output missing");surfaceMaterial.SetFloat("_Metallic",0);
                light.enabled=false;var directional=Keep(new GameObject("RTDI directional")).AddComponent<Light>();directional.type=LightType.Directional;directional.intensity=.5f;
                Near(Render().r,.5f/Mathf.PI,"directional delta reference",.012f);directional.enabled=false;
                var spot=Point(new Vector3(0,0,-2),Color.white,4);spot.type=LightType.Spot;spot.spotAngle=60;spot.innerSpotAngle=30;
                Near(Render().r,Expected(spot),"spot on-axis",.012f);spot.transform.rotation=Quaternion.Euler(0,90,0);Near(Render().r,0,"spot outside cone",.002f);spot.enabled=false;
                surfaceMaterial.SetFloat("_UseEmission",1);surfaceMaterial.SetColor("_EmissionColor",new Color(10,4,2,1));Near(Render().r,0,"emission is not direct lighting",.002f);surfaceMaterial.SetFloat("_UseEmission",0);light.enabled=true;
                light.intensity=0;Near(Render().r,0,"zero intensity",.002f);light.intensity=4;
                var red=Point(new Vector3(-1.5f,0,-1),Color.red,3);var blue=Point(new Vector3(0,1.5f,-1),Color.blue,3);
                feature.Settings.initialSamples=8;Color average=Color.clear;
                for(int i=0;i<32;i++){Render();average+=ReadMean(feature.LastContext.diffuse.rt);}average/=32;
                Color total=Color.clear;
                for(int y=24;y<40;y++)for(int x=24;x<40;x++)total+=new Color(Expected(light,x,y)+Expected(red,x,y),Expected(light,x,y),Expected(light,x,y)+Expected(blue,x,y),1);
                total/=256;Debug.Log("[HoRTDIValidation] multi-light mean="+average+" reference="+total);
                NearRGB(average,total,"multi-light SDK initial sampling",.035f);
                feature.Settings.temporal=true;feature.Settings.spatial=true;
                for(int i=0;i<6;i++)Render();
                var data=new Reservoir[feature.LastContext.reservoirs.count];feature.LastContext.reservoirs.GetData(data);
                int address=(32/16)*feature.LastContext.rowPitch+(32/16)*256+(32%16)*16+32%16;
                Require((data[address].visibility>>18)>1,"reference temporal/spatial did not accumulate samples");
                red.enabled=blue.enabled=false;for(int i=0;i<3;i++)Render();
                blocker.SetActive(true);Near(Render().r,0,"geometry change rejects temporal history",.003f);blocker.SetActive(false);
                feature.Settings.spatial=false;light.intensity=2;Near(Render().r,Expected(light),"light change resets history",.015f);
                volume.debugMode.value=HoRTDIDebugMode.Combined;var debug=Render();NearRGB(Read(target),debug,"debug presentation",.01f);volume.debugMode.value=HoRTDIDebugMode.Off;
                gb.Settings.renderScale=HoGeometryBufferRenderScale.Half;Near(Render().a,0,"domain mismatch receiver invalid",.01f);
                volume.debugMode.value=HoRTDIDebugMode.Receiver;Render();NearRGB(Read(target),Color.red,"invalid receiver debug",.01f);
                gb.Settings.renderScale=HoGeometryBufferRenderScale.Full;Render();NearRGB(Read(target),Color.green,"valid receiver debug",.01f);volume.debugMode.value=HoRTDIDebugMode.Off;
                volume.enable.Override(false);RenderCamera();Require(Shader.GetGlobalFloat("_HoRTDIActive")==0,"volume disabled reset");volume.enable.Override(true);Render();
                camera.transform.position+=new Vector3(.1f,0,-.5f);Render();Require(feature.LastContext.lastTemporal,"static-world camera movement should use reference reprojection");
                Color moved=ReadMean(feature.LastContext.diffuse.rt);float movedReference=0;for(int y=24;y<40;y++)for(int x=24;x<40;x++)movedReference+=Expected(light,x,y);movedReference/=256;
                Near(moved.r,movedReference,"camera reprojection mean",.025f);
                var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/HoRTDI.compute");
                foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
                feature.Dispose();Require(Shader.GetGlobalFloat("_HoRTDIActive")==0,"dispose reset");
                Debug.Log("[HoRTDIValidation] PASS reference RTXDI initial/temporal/spatial/shade, analytic energy, world visibility, multi-light mean, history/reset, independent output/debug; API="+SystemInfo.graphicsDeviceType+"; compatibility="+compatibility);
                Finish(null);
            }
            catch(Exception error){Finish(error);}
        }
        private static Light Point(Vector3 position,Color color,float intensity)
        {var result=Keep(new GameObject("RTDI point light")).AddComponent<Light>();result.type=LightType.Point;result.transform.position=position;result.color=color;result.intensity=intensity;result.range=100;result.shadows=LightShadows.Hard;return result;}
        private static float Expected(Light source,int x=32,int y=32)
        {
            var ray=camera.ViewportPointToRay(new Vector3((x+.5f)/64,(y+.5f)/64,0));var position=ray.origin+ray.direction*(-ray.origin.z/ray.direction.z);var delta=source.transform.position-position;float d2=delta.sqrMagnitude;
            float fade=Mathf.Clamp01(1-Mathf.Pow(d2/(source.range*source.range),2));return source.intensity/d2*(-delta.normalized.z)/Mathf.PI*fade*fade;
        }
        private static void RenderCamera(){VolumeManager.instance.Update(camera.transform,-1);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});}
        private static Color Render(){RenderCamera();Require(feature.LastContext!=null,"DI context missing: "+feature.Status);return Read(feature.LastContext.diffuse.rt);}
        private static Color Read(RenderTexture source)
        {
            var previous=RenderTexture.active;var image=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
            try{RenderTexture.active=source;image.ReadPixels(new Rect(0,0,source.width,source.height),0,0);image.Apply();return image.GetPixel(source.width/2,source.height/2);}
            finally{RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
        private static Color ReadMean(RenderTexture source)
        {
            var previous=RenderTexture.active;var image=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
            try{RenderTexture.active=source;image.ReadPixels(new Rect(0,0,source.width,source.height),0,0);image.Apply();Color result=Color.clear;for(int y=24;y<40;y++)for(int x=24;x<40;x++)result+=image.GetPixel(x,y);return result/256;}
            finally{RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
        private static Material Mat(string name){var shader=Shader.Find(name);Require(shader!=null,"Missing shader "+name);return Keep(new Material(shader));}
        private static T Keep<T>(T value)where T:Object{objects.Add(value);return value;}
        private static void Near(float actual,float expected,string label,float tolerance=.02f)=>Require(Mathf.Abs(actual-expected)<tolerance,label+": expected "+expected+", got "+actual);
        private static void NearRGB(Color actual,Color expected,string label,float tolerance=.02f){Near(actual.r,expected.r,label+" R",tolerance);Near(actual.g,expected.g,label+" G",tolerance);Near(actual.b,expected.b,label+" B",tolerance);}
        private static void Require(bool value,string message){if(!value)throw new Exception(message);}
        private static void Finish(Exception error)
        {
            EditorApplication.update-=Tick;GraphicsSettings.defaultRenderPipeline=previousDefault;QualitySettings.renderPipeline=previousQuality;ShaderUtil.allowAsyncCompilation=previousAsync;
            for(int i=objects.Count-1;i>=0;i--)if(objects[i]!=null)Object.DestroyImmediate(objects[i]);objects.Clear();
            if(error!=null)Debug.LogException(error);EditorApplication.Exit(error==null?0:1);
        }
    }
}
