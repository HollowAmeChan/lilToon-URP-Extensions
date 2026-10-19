#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.RTDI;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static partial class HoRTGICompositeValidation
    {
        const int Size=64;
        static readonly List<UnityEngine.Object> owned=new();
        static readonly List<string> checks=new();
        static Camera camera;
        static RenderTexture target;
        static Texture2D image;
        static HoRTGIRendererFeature gi;
        static HoRTDIRendererFeature di;
        static HoGeometryBufferRendererFeature gb;
        static HoRTGIVolume volume;
        static Material receiverMaterial;
        static GameObject receiver;
        static RenderPipelineAsset oldDefault,oldQuality;
        static bool compatibility,oldAsync,finished;
        static int warmup,frames;
        static string output,failure;
        static Color original,added;
        static T Keep<T>(T obj) where T:UnityEngine.Object{owned.Add(obj);return obj;}
        public static void RunCompatibility(){compatibility=true;Run();}
        public static void Run()
        {
            try
            {
                if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"))throw new Exception("Dedicated isolated GI project required");
                oldDefault=GraphicsSettings.defaultRenderPipeline;oldQuality=QualitySettings.renderPipeline;oldAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
                output=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-gi-composite-"+(compatibility?"compat":"rg")));Directory.CreateDirectory(output);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);RenderSettings.fog=false;
                var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                gb=Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());gb.Settings.maxRenderQueue=(int)RenderQueue.GeometryLast;
                var sb=Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());sb.Settings.enableSemanticLanes=sb.Settings.enableCorrelatedSemantics=false;
                var sky=Keep(ScriptableObject.CreateInstance<HoAtmosphereRendererFeature>());sky.Settings.source=HoAtmosphereSource.Constant;sky.Settings.constantColor=new Color(.4f,.2f,.1f,1);
                di=Keep(ScriptableObject.CreateInstance<HoRTDIRendererFeature>());di.Settings.useEnvironment=false;di.Settings.temporal=di.Settings.spatial=false;di.Settings.initialSamples=1;di.SetActive(false);
                gi=Keep(ScriptableObject.CreateInstance<HoRTGIRendererFeature>());gi.Settings.temporal=gi.Settings.spatial=false;gi.Settings.secondaryEnvironmentSamples=0;gi.Settings.includeDirectEnvironment=true;
                renderer.rendererFeatures.Add(gb);renderer.rendererFeatures.Add(sb);renderer.rendererFeatures.Add(sky);renderer.rendererFeatures.Add(di);renderer.rendererFeatures.Add(gi);
                var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #else
                Require(!compatibility,"Compatibility symbol");
                #endif
                camera=Keep(new GameObject("GI composition camera")).AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;
                camera.transform.position=new Vector3(0,0,-3);camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.allowHDR=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.1f,.15f,.7f);
                camera.GetUniversalAdditionalCameraData().volumeLayerMask=-1;
                target=Keep(new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();image=Keep(new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true));
                var holder=Keep(new GameObject("GI appearance volume")).AddComponent<Volume>();holder.isGlobal=true;holder.sharedProfile=Keep(ScriptableObject.CreateInstance<VolumeProfile>());volume=holder.sharedProfile.Add<HoRTGIVolume>();
                holder.sharedProfile.Add<HoRTDIVolume>().enable.Override(true);
                receiver=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));receiver.transform.localScale=new Vector3(2,2,1);
                receiverMaterial=Mat("lilToon");receiverMaterial.EnableKeyword("UNITY_UI_ALPHACLIP");receiverMaterial.SetFloat("_Cull",0);receiverMaterial.SetFloat("_AsUnlit",1);
                receiverMaterial.SetColor("_Color",new Color(.2f,.4f,.6f,1));receiverMaterial.SetTexture("_MainTex",Texture2D.whiteTexture);receiverMaterial.SetFloat("_Reflectance",0);receiverMaterial.SetFloat("_Metallic",0);
                receiverMaterial.SetFloat("_UseEmission",0);receiverMaterial.SetFloat("_UseEmission2nd",0);receiver.GetComponent<Renderer>().sharedMaterial=receiverMaterial;
                Application.logMessageReceived+=OnLog;EditorApplication.update+=Tick;
            }
            catch(Exception error){Finish(error);}
        }
        static Material Mat(string shader){var value=Shader.Find(shader);if(value==null)throw new Exception("Missing shader "+shader);return Keep(new Material(value));}
        static void OnLog(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)failure=message;}
        static Color Read(RenderTexture texture,int x=32,int y=32)
        {
            var previous=RenderTexture.active;try{RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,Size,Size),0,0);image.Apply();return image.GetPixel(x,y);}finally{RenderTexture.active=previous;}
        }
        static Color Render()
        {
            if(failure!=null)throw new Exception("Render diagnostic: "+failure);
            VolumeManager.instance.Update(camera.transform,camera.GetUniversalAdditionalCameraData().volumeLayerMask);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});frames++;
            return Read(target);
        }
        static Color GI()=>Read(gi.LastContext.diffuse.rt);
        static Color DI(){if(di.LastContext==null)throw new Exception("Companion DI was not produced: "+di.Status);return Read(di.LastContext.PublishedDiffuse.rt)+Read(di.LastContext.PublishedSpecular.rt);}
        static float Decode(float v)=>v<=.04045f?v/12.92f:Mathf.Pow((v+.055f)/1.055f,2.4f);
        static float Encode(float v)=>v<=.0031308f?v*12.92f:1.055f*Mathf.Pow(v,1/2.4f)-.055f;
        static Color Expected(Color basis,Color light,float amount,float transmission=1)
        {
            Color result;
            if(QualitySettings.activeColorSpace==ColorSpace.Gamma)
            {
                result=new Color(Decode(basis.r),Decode(basis.g),Decode(basis.b),basis.a)+light*(amount*transmission);
                result=new Color(Encode(result.r),Encode(result.g),Encode(result.b),basis.a);
            }
            else{result=basis+light*(amount*transmission);result.a=basis.a;}
            return result;
        }
        static void Require(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);Debug.Log("[HoGIComposite] CHECK "+name);}
        static void Near(Color actual,Color expected,string name,float tolerance=.006f)
        {Require(Mathf.Abs(actual.r-expected.r)<tolerance&&Mathf.Abs(actual.g-expected.g)<tolerance&&Mathf.Abs(actual.b-expected.b)<tolerance,name+" actual="+actual+" expected="+expected);}
        static void Save(string name){Read(target);File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());File.WriteAllBytes(Path.Combine(output,name+".exr"),image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));}
        static void Tick()
        {
            if(warmup++<3){try{Render();}catch(Exception error){Finish(error);}return;}
            EditorApplication.update-=Tick;
            try
            {
                if(materialExclusion){ValidateMaterialExclusion();Finish(null);return;}
                Require(!gi.Settings.composite&&gi.Settings.intensity==.25f,"Legacy/new defaults retain opt-in composition and gain .25");
                original=Render();Save("before");Require(GI().a>.9f&&GI().r>0,"Dedicated GI remains produced with composition off");
                gi.Settings.composite=true;gi.Settings.intensity=1;added=Render();Near(added,Expected(original,GI(),1),"Actual camera adds same-frame GI without repeating albedo");Save("added");
                gi.Settings.intensity=.25f;Near(Render(),Expected(original,GI(),.25f),"Independent additive intensity .25");
                gi.Settings.intensity=0;Near(Render(),original,"Zero intensity preserves original",.002f);
                gi.Settings.intensity=.25f;gi.Settings.composite=false;volume.composite.Override(true);volume.intensity.Override(.5f);
                Near(Render(),Expected(original,GI(),.5f),"Volume enables composition and controls actual gain");Require(!gi.Settings.composite&&gi.Settings.intensity==.25f,"Volume does not write back shared Feature defaults");
                gi.Settings.composite=true;volume.composite.Override(false);Near(Render(),original,"Volume can disable Feature-default composition");
                volume.composite.overrideState=volume.intensity.overrideState=false;Near(Render(),Expected(original,GI(),.25f),"Unchecked Volume values restore Feature defaults");
                gi.Settings.temporal=true;Render();Render();gi.Settings.intensity=.7f;Render();Require(gi.LastContext.lastTemporal,"Appearance gain does not reset GI reservoir history");gi.Settings.temporal=false;
                receiverMaterial.SetFloat("_UseEmission",1);receiverMaterial.SetColor("_EmissionColor",new Color(.3f,.1f,.2f,1));
                gi.Settings.composite=false;var emissive=Render();gi.Settings.composite=true;Near(Render(),Expected(emissive,GI(),.7f),"Existing primary emission is not added again");receiverMaterial.SetFloat("_UseEmission",0);
                gi.Settings.intensity=.25f;RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.2f,.3f,.4f,1);RenderSettings.fogStartDistance=0;RenderSettings.fogEndDistance=6;
                gi.Settings.composite=false;var fogged=Render();gi.Settings.composite=true;Near(Render(),Expected(fogged,GI(),.25f,1-(3-camera.nearClipPlane)/6),"Fog attenuates added GI without repeating fog color",.008f);
                RenderSettings.fogEndDistance=1;gi.Settings.composite=false;var fullFog=Render();gi.Settings.composite=true;Near(Render(),fullFog,"Full fog has no additional light",.002f);RenderSettings.fog=false;
                receiver.transform.localScale=Vector3.one*.5f;gi.Settings.composite=false;Render();var background=Read(target,4,4);gi.Settings.composite=true;Render();Near(Read(target,4,4),background,"Background is preserved");Require(Mathf.Abs(Read(target,4,4).a-background.a)<.001f,"Background alpha is preserved");receiver.transform.localScale=new Vector3(2,2,1);
                gb.Settings.renderScale=HoGeometryBufferRenderScale.Half;gi.Settings.composite=false;var invalid=Render();gi.Settings.composite=true;Near(Render(),invalid,"Mismatched primary domain does not receive GI");gb.Settings.renderScale=HoGeometryBufferRenderScale.Full;
                var foreground=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));foreground.transform.position=new Vector3(0,0,-.8f);foreground.transform.localScale=Vector3.one*.5f;
                var fg=Mat("Universal Render Pipeline/Unlit");fg.SetColor("_BaseColor",new Color(.1f,.8f,.2f,1));foreground.GetComponent<Renderer>().sharedMaterial=fg;
                gi.Settings.composite=false;var foregroundOriginal=Render();gi.Settings.composite=true;Near(Render(),foregroundOriginal,"Forward-only foreground protects actual opaque depth");foreground.SetActive(false);
                ValidateTransparent();ValidateBounce();ValidateCombined();ValidateCameraIsolation();
                camera.rect=new Rect(0,0,.5f,1);gi.Settings.composite=false;Render();var partial=Read(target,16,32);gi.Settings.composite=true;Render();Near(Read(target,16,32),partial,"Partial viewport bypass preserves its original pixels");camera.rect=new Rect(0,0,1,1);
                gi.Settings.composite=false;var unchanged=Render();gi.Settings.composite=true;volume.enable.Override(false);Near(Render(),unchanged,"Volume production disable preserves camera");Require(Shader.GetGlobalFloat("_HoRTGIActive")==0,"Volume production disable clears publication");
                Require(ShaderUtil.GetShaderMessages(Shader.Find("Hidden/lilToon/URP/RTGI/Composite")).Length==0,"Composite shader has no warnings/errors");
                Finish(null);
            }
            catch(Exception error){Finish(error);}
        }
        static void ValidateTransparent()
        {
            var transparent=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));transparent.transform.position=new Vector3(0,0,-.9f);transparent.transform.localScale=Vector3.one*.5f;
            var overlay=Mat("Universal Render Pipeline/Unlit");overlay.SetColor("_BaseColor",new Color(.8f,.1f,.2f,.5f));overlay.SetFloat("_Surface",1);overlay.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);overlay.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);overlay.SetInt("_ZWrite",0);
            overlay.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");overlay.SetOverrideTag("RenderType","Transparent");overlay.renderQueue=3000;transparent.GetComponent<Renderer>().sharedMaterial=overlay;
            transparent.SetActive(false);gi.Settings.composite=false;var opaque=Render();gi.Settings.composite=true;var opaqueAdded=Render();
            transparent.SetActive(true);gi.Settings.composite=false;var overlayOriginal=Render();gi.Settings.composite=true;Near(Render(),overlayOriginal+(opaqueAdded-opaque)*.5f,"Transparent draws after GI composition",.008f);transparent.SetActive(false);
        }
        static void ValidateCombined()
        {
            var light=Keep(new GameObject("DI companion light")).AddComponent<Light>();light.type=LightType.Directional;light.color=new Color(.1f,.2f,.3f);light.intensity=1;light.transform.rotation=Quaternion.identity;
            di.SetActive(true);di.Settings.composite=false;gi.Settings.composite=false;var basis=Render();
            di.Settings.compositeMode=HoRTDICompositeMode.Additive;di.Settings.additiveIntensity=1;di.Settings.composite=true;var directAdded=Render();Near(directAdded,Expected(basis,DI(),1),"DI companion composes independently");
            gi.Settings.composite=true;Near(Render(),Expected(directAdded,GI(),.25f),"DI plus GI consume the current composed camera color",.008f);
            di.SetActive(false);light.enabled=false;
        }
        static void ValidateBounce()
        {
            var bounce=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));bounce.name="Offscreen red wall";bounce.layer=1;
            bounce.transform.position=new Vector3(1.25f,0,-1);bounce.transform.rotation=Quaternion.Euler(0,90,0);bounce.transform.localScale=new Vector3(2,3,1);
            var material=Keep(new Material(receiverMaterial));material.SetColor("_Color",new Color(.8f,.1f,.05f,1));bounce.GetComponent<Renderer>().sharedMaterial=material;
            gi.Settings.includeDirectEnvironment=false;gi.Settings.secondaryEnvironmentSamples=4;gi.Settings.temporal=gi.Settings.spatial=true;gi.Settings.intensity=3;gi.Settings.composite=false;
            var basis=Render();Save("bounce-before");for(int i=0;i<16;i++)Render();
            gi.Settings.composite=true;var result=Render();var indirect=GI();
            Require(indirect.r>.0001f&&indirect.r>indirect.g*2,"World-only red wall produces colored indirect light with direct sky excluded");
            float redDelta=QualitySettings.activeColorSpace==ColorSpace.Gamma?Decode(result.r)-Decode(basis.r):result.r-basis.r;
            Require(redDelta>indirect.r*3*.8f,"Secondary red bounce actually brightens the normal camera");
            Near(result,Expected(basis,indirect,3),"Actual scene camera consumes secondary sky bounce without primary sky duplication",.008f);Save("bounce-added");
            bounce.SetActive(false);gi.Settings.includeDirectEnvironment=true;gi.Settings.secondaryEnvironmentSamples=0;gi.Settings.temporal=gi.Settings.spatial=false;gi.Settings.intensity=.25f;
        }
        static void ValidateCameraIsolation()
        {
            var other=Keep(new GameObject("GI alternate camera")).AddComponent<Camera>();other.CopyFrom(camera);other.enabled=false;other.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation);
            var otherTarget=Keep(new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));otherTarget.Create();
            var local=Keep(new GameObject("Alternate camera volume")).AddComponent<Volume>();local.gameObject.layer=7;local.isGlobal=true;local.sharedProfile=Keep(ScriptableObject.CreateInstance<VolumeProfile>());
            var parameters=local.sharedProfile.Add<HoRTGIVolume>();parameters.composite.Override(false);parameters.intensity.Override(.8f);
            var oldMask=camera.GetUniversalAdditionalCameraData().volumeLayerMask;camera.GetUniversalAdditionalCameraData().volumeLayerMask=1;other.GetUniversalAdditionalCameraData().volumeLayerMask=1<<7;
            Color Other()
            {
                var oldCamera=camera;var oldTarget=target;try{camera=other;target=otherTarget;return Render();}finally{camera=oldCamera;target=oldTarget;}
            }
            gi.Settings.composite=false;var basisA=Render();for(int i=0;i<3;i++)Other();var basisB=Other();gi.Settings.composite=true;parameters.composite.Override(true);
            Near(Render(),Expected(basisA,GI(),.25f),"Camera A retains its own Volume gain");var contextA=gi.LastContext;
            var otherAdded=Other();var otherGI=GI();Require(otherGI.a>.9f&&otherGI.r>0,"Camera B has valid nonzero GI input");Near(otherAdded,Expected(basisB,otherGI,.8f),"Camera B uses its independent Volume gain");Require(!ReferenceEquals(contextA,gi.LastContext),"Two cameras own independent GI contexts");
            Near(Render(),Expected(basisA,GI(),.25f),"Camera A remains correct after camera B");Require(gi.Settings.intensity==.25f,"Camera-specific Volume gains preserve shared asset intensity");
            local.gameObject.SetActive(false);camera.GetUniversalAdditionalCameraData().volumeLayerMask=oldMask;
        }
        static void Finish(Exception error)
        {
            if(finished)return;finished=true;EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;RenderSettings.fog=false;
            if(error!=null)Debug.LogException(error);
            if(output!=null)File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(new Report{pass=error==null,compatibility=compatibility,colorSpace=QualitySettings.activeColorSpace.ToString(),failure=error?.ToString(),frames=frames,original=original,added=added,checks=checks.ToArray()},true));
            GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;ShaderUtil.allowAsyncCompilation=oldAsync;
            foreach(var obj in owned.AsEnumerable().Reverse())if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);
            Debug.Log("[HoGIComposite] END pass="+(error==null));EditorApplication.Exit(error==null?0:1);
        }
        [Serializable] sealed class Report {public bool pass,compatibility;public string colorSpace,failure;public int frames;public Color original,added;public string[] checks;}
    }
}
