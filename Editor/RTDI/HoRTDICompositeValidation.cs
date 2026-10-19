#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceComposite;
        private static readonly List<string> compositeChecks=new();
        public static void RunComposite()
        {
            Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");
            forceComposite=true;Run();
        }
        public static void RunCompositeCompatibility(){compatibility=true;RunComposite();}
        public static void ConfigureCompositeLinear()
        {Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");PlayerSettings.colorSpace=ColorSpace.Linear;AssetDatabase.SaveAssets();EditorApplication.Exit(0);}
        public static void ConfigureCompositeGamma()
        {Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");PlayerSettings.colorSpace=ColorSpace.Gamma;AssetDatabase.SaveAssets();EditorApplication.Exit(0);}
        private static float CompositeSRGB(float value)=>value<=.0031308f?value*12.9232102f:1.055f*Mathf.Pow(value,1f/2.4f)-.055f;
        private static Color CompositeDisplay(Color value)=>new Color(CompositeSRGB(value.r),CompositeSRGB(value.g),CompositeSRGB(value.b),value.a);

        private static Color[] CompositePixels(RenderTexture source)
        {
            var previous=RenderTexture.active;RenderTexture.active=source;
            var image=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
            image.ReadPixels(new Rect(0,0,source.width,source.height),0,0);image.Apply();
            var result=image.GetPixels();Object.DestroyImmediate(image);RenderTexture.active=previous;return result;
        }
        private static Color CompositePublished()
        {
            var c=feature.LastContext;
            var result=Read(c.PublishedDiffuse.rt)+Read(c.PublishedSpecular.rt);
            if(QualitySettings.activeColorSpace==ColorSpace.Gamma)result=CompositeDisplay(result);
            return result;
        }
        private static void CompositeNear(Color actual,Color expected,string label,float tolerance=.004f)
        {
            NearRGB(actual,expected,label,tolerance);compositeChecks.Add(label);Debug.Log("[HoRTDIComposite] "+label+" actual="+actual+" expected="+expected);
        }
        private static void ValidateComposite()
        {
            if(forceCompositeBlend){ValidateCompositeBlend();return;}
            Require(!feature.Settings.composite&&feature.Settings.compositeWeight==1,"legacy composite defaults changed");
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.14f,.21f,1);
            light.type=LightType.Directional;light.transform.rotation=Quaternion.identity;light.intensity=2;light.shadows=LightShadows.None;
            feature.Settings.useEnvironment=false;surfaceMaterial.SetFloat("_Reflectance",0);
            surfaceMaterial.SetFloat("_Metallic",0);surfaceMaterial.SetColor("_Color",Color.white);
            Render();var original=Read(target);feature.SetActive(false);RenderCamera();
            CompositeNear(Read(target),original,"default disabled leaves original camera color");feature.SetActive(true);
            feature.Settings.composite=true;Render();var full=Read(target);
            CompositeNear(full,CompositePublished(),"camera receives published direct light");
            var analytic=new Color(2/Mathf.PI,2/Mathf.PI,2/Mathf.PI,1);
            if(QualitySettings.activeColorSpace==ColorSpace.Gamma)analytic=CompositeDisplay(analytic);
            CompositeNear(full,analytic,"independent directional Lambert energy",.006f);
            Require(Mathf.Abs(full.r-original.r)>.025f,"composite did not materially change normal camera output");
            feature.Settings.compositeWeight=.5f;Render();CompositeNear(Read(target),Color.Lerp(original,full,.5f),"weight crossfades rather than adding duplicate lighting");
            feature.Settings.compositeWeight=0;Render();CompositeNear(Read(target),original,"zero weight restores original camera color");
            feature.Settings.compositeWeight=1;
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=Color.red;
            RenderSettings.fogStartDistance=0;RenderSettings.fogEndDistance=6;
            Render();CompositeNear(Read(target),Color.Lerp(full,Color.red,(3-camera.nearClipPlane)/6),"reference lighting receives camera fog once",.006f);
            RenderSettings.fog=false;
            surfaceMaterial.SetColor("_Color",new Color(.25f,.5f,.8f,1));Render();
            CompositeNear(Read(target),CompositePublished(),"colored albedo is not multiplied twice");
            surfaceMaterial.SetFloat("_Metallic",1);surfaceMaterial.SetFloat("_Reflectance",.5f);Render();
            Require(Read(feature.LastContext.PublishedSpecular.rt).maxColorComponent>.001f,"metal specular fixture is black");
            CompositeNear(Read(target),CompositePublished(),"specular material response reaches camera");
            surfaceMaterial.SetFloat("_Metallic",0);surfaceMaterial.SetFloat("_Reflectance",0);surfaceMaterial.SetColor("_Color",Color.white);

            // Move the blocker off the view ray while retaining its shadow on the receiver.
            light.type=LightType.Point;light.transform.position=new Vector3(1.5f,0,-1);light.intensity=4;
            blocker.SetActive(true);Render();CompositeNear(Read(target),Color.black,"valid world shadow replaces raster lighting");
            surfaceMaterial.SetFloat("_UseEmission",1);surfaceMaterial.SetColor("_EmissionColor",new Color(4,1,.25f,1));Render();
            var emission=Read(target);Require(emission.r>1&&emission.g>.2f,"HDR emission disappeared in a world shadow");
            blocker.SetActive(false);light.intensity=0;Render();CompositeNear(Read(target),emission,"emission survives zero direct illumination",.008f);
            surfaceMaterial.SetFloat("_UseEmission",0);Render();CompositeNear(Read(target),Color.black,"valid zero direct light does not keep old ambient");
            light.intensity=4;

            primaryReceiver.transform.localScale=new Vector3(1.2f,1.2f,1);Render();var sky=CompositePixels(target)[4*64+4];
            feature.Settings.composite=false;Render();CompositeNear(CompositePixels(target)[4*64+4],sky,"background remains unchanged");
            feature.Settings.composite=true;primaryReceiver.transform.localScale=new Vector3(2,2,1);
            gb.Settings.renderScale=HoGeometryBufferRenderScale.Half;Render();var invalid=Read(target);
            feature.Settings.composite=false;Render();CompositeNear(Read(target),invalid,"invalid sample domain preserves original camera color");
            gb.Settings.renderScale=HoGeometryBufferRenderScale.Full;feature.Settings.composite=true;

            var foreground=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));foreground.name="forward-only opaque foreground";
            foreground.transform.position=new Vector3(0,0,-.8f);foreground.transform.localScale=Vector3.one*.5f;
            var foregroundMaterial=Mat("Universal Render Pipeline/Unlit");foregroundMaterial.SetColor("_BaseColor",new Color(.1f,.8f,.2f,1));
            foreground.GetComponent<Renderer>().sharedMaterial=foregroundMaterial;
            feature.Settings.composite=false;Render();var foregroundOriginal=Read(target);
            feature.Settings.composite=true;Render();CompositeNear(Read(target),foregroundOriginal,"forward-only opaque foreground is not overwritten");
            foreground.SetActive(false);

            var transparent=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));transparent.name="transparent overlay";
            transparent.transform.position=new Vector3(0,0,-.9f);transparent.transform.localScale=Vector3.one*.5f;
            var transparentMaterial=Mat("Universal Render Pipeline/Unlit");transparentMaterial.SetColor("_BaseColor",new Color(.8f,.1f,.2f,.5f));
            transparentMaterial.SetFloat("_Surface",1);transparentMaterial.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);
            transparentMaterial.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);transparentMaterial.SetInt("_ZWrite",0);
            transparentMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");transparentMaterial.SetOverrideTag("RenderType","Transparent");
            transparentMaterial.renderQueue=3000;transparent.GetComponent<Renderer>().sharedMaterial=transparentMaterial;
            transparent.SetActive(false);Render();var opaqueComposite=Read(target);
            feature.Settings.composite=false;Render();var opaqueOriginal=Read(target);
            transparent.SetActive(true);Render();var transparentOriginal=Read(target);
            feature.Settings.composite=true;Render();
            var transparentExpected=transparentOriginal+(opaqueComposite-opaqueOriginal)*.5f;
            CompositeNear(Read(target),transparentExpected,"transparent rendering follows compositing",.008f);
            transparent.SetActive(false);

            feature.Settings.denoise=true;light.color=Color.red;
            var second=Point(new Vector3(-1.5f,0,-1),Color.blue,4);
            for(int i=0;i<96;i++)Render();
            Require(feature.LastContext.nrd!=null,"RELAX unavailable in composite fixture");
            CompositeNear(Read(target),CompositePublished(),"camera consumes filtered publication rather than raw");
            volume.enable.Override(false);RenderCamera();var disabled=Read(target);
            feature.SetActive(false);RenderCamera();CompositeNear(Read(target),disabled,"volume disabled does not reuse stale composition");
            feature.SetActive(true);volume.enable.Override(true);Render();CompositeNear(Read(target),CompositePublished(),"reenabling reconstructs a valid camera composite");
            SaveCompositePresentation(second);
            var report=new CompositeReport{compatibility=compatibility,colorSpace=QualitySettings.activeColorSpace.ToString(),
                checks=compositeChecks.ToArray(),pass=true};
            File.WriteAllText("rtdi-composite-"+(compatibility?"compat":"rg")+".json",JsonUtility.ToJson(report,true));
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/HoRTDIComposite.shader");
            foreach(var message in ShaderUtil.GetShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
            Debug.Log("[HoRTDIComposite] PASS "+compositeChecks.Count+" checks, normal camera output and saved presentation");
        }
        [Serializable]private sealed class CompositeReport
        {public bool pass,compatibility;public string colorSpace;public string[] checks;}

        private static void SaveCompositePresentation(Light second)
        {
            // An actual normal-camera capture: debug stays Off throughout.
            Require(volume.debugMode.value==lilToon.URP.Extensions.RTDI.HoRTDIDebugMode.Off,"presentation uses a debug view");
            var directory=Path.GetFullPath("rtdi-composite-images");Directory.CreateDirectory(directory);
            var oldTarget=target;target=Keep(new RenderTexture(384,256,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();
            camera.aspect=1.5f;camera.orthographicSize=1.5f;
            primaryReceiver.transform.localScale=new Vector3(5,3,1);
            light.transform.position=new Vector3(1.5f,.6f,-1.5f);second.transform.position=new Vector3(-1.5f,.2f,-1.2f);
            light.intensity=second.intensity=6;
            surfaceMaterial.SetColor("_Color",new Color(.55f,.65f,.85f,1));surfaceMaterial.SetFloat("_Reflectance",.5f);
            var sphere=Keep(GameObject.CreatePrimitive(PrimitiveType.Sphere));sphere.transform.position=new Vector3(-.65f,-.25f,-.6f);
            sphere.transform.localScale=Vector3.one*.8f;sphere.GetComponent<Renderer>().sharedMaterial=surfaceMaterial;
            var sphere2=Keep(GameObject.CreatePrimitive(PrimitiveType.Sphere));sphere2.transform.position=new Vector3(.65f,.2f,-.65f);
            sphere2.transform.localScale=Vector3.one*.9f;var metal=Mat("lilToon");metal.SetColor("_Color",new Color(.8f,.65f,.3f,1));
            metal.SetFloat("_Metallic",.8f);metal.SetFloat("_Reflectance",.5f);metal.SetFloat("_Smoothness",.65f);sphere2.GetComponent<Renderer>().sharedMaterial=metal;
            for(int i=0;i<96;i++)Render();
            SaveCompositeImage(Path.Combine(directory,(compatibility?"compat":"rg")+"-after.png"));
            feature.Settings.composite=false;Render();SaveCompositeImage(Path.Combine(directory,(compatibility?"compat":"rg")+"-before.png"));
            if(forceCompositeBlend)
            {
                feature.Settings.composite=true;feature.Settings.compositeMode=HoRTDICompositeMode.Additive;feature.Settings.additiveIntensity=.25f;
                Render();SaveCompositeImage(Path.Combine(directory,(compatibility?"compat":"rg")+"-additive.png"));
            }
            target=oldTarget;
        }
        private static void SaveCompositeImage(string path)
        {
            var pixels=CompositePixels(target);
            for(int i=0;i<pixels.Length;i++)
            {
                var c=pixels[i];
                if(QualitySettings.activeColorSpace==ColorSpace.Gamma)c=c.linear;
                c=new Color(c.r/(1+c.r),c.g/(1+c.g),c.b/(1+c.b),1).gamma;pixels[i]=c;
            }
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,true);image.SetPixels(pixels);image.Apply();
            File.WriteAllBytes(path,image.EncodeToPNG());Object.DestroyImmediate(image);
        }
    }
}
