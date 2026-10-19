#pragma warning disable CS0618
using System;
using System.IO;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceCompositeBlend;
        public static void RunCompositeBlend(){forceCompositeBlend=true;RunComposite();}
        public static void RunCompositeBlendCompatibility(){compatibility=true;RunCompositeBlend();}
        private static float CompositeDecode(float v)=>v<=.04045f?v/12.92f:Mathf.Pow((v+.055f)/1.055f,2.4f);
        private static Color AddExpected(Color original,Color direct,float amount,float transmission=1)
        {
            if(QualitySettings.activeColorSpace==ColorSpace.Gamma)
            {
                var linear=new Color(CompositeDecode(original.r),CompositeDecode(original.g),CompositeDecode(original.b),original.a);
                var result=CompositeDisplay(linear+direct*(amount*transmission));result.a=original.a;return result;
            }
            var sum=original+direct*(amount*transmission);sum.a=original.a;return sum;
        }
        private static Color Direct()=>Read(feature.LastContext.PublishedDiffuse.rt)+Read(feature.LastContext.PublishedSpecular.rt);
        private static void ValidateCompositeBlend()
        {
            Require(!feature.Settings.composite&&feature.Settings.compositeMode==HoRTDICompositeMode.Replace&&feature.Settings.additiveIntensity==.25f,"Composite defaults changed");
            const string legacyPath="Assets/RTDIPlayerProbe/LegacyPartialRenderer.asset";
            Require(File.Exists(legacyPath)&&!File.ReadAllText(legacyPath).Contains("compositeMode:"),"Legacy Player fixture missing/already migrated");
            HoRTDIRendererFeature legacy=null;foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(legacyPath))if(asset is HoRTDIRendererFeature f)legacy=f;
            Require(legacy!=null&&legacy.Settings.compositeMode==HoRTDICompositeMode.Replace&&legacy.Settings.additiveIntensity==.25f,"Old renderer changed blend mode/default gain");
            compositeChecks.Add("old serialized renderer retains replacement and additive default .25");
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.14f,.21f,1);
            light.type=LightType.Directional;light.transform.rotation=Quaternion.identity;light.intensity=2;light.shadows=LightShadows.None;
            feature.Settings.useEnvironment=false;surfaceMaterial.SetFloat("_Reflectance",0);surfaceMaterial.SetColor("_Color",new Color(.25f,.5f,.8f,1));
            Render();var original=Read(target);var raw=Direct();
            feature.Settings.compositeMode=HoRTDICompositeMode.Additive;feature.Settings.compositeWeight=0;feature.Settings.composite=true;
            foreach(float gain in new[]{0f,.25f,1f,2f})
            {
                feature.Settings.additiveIntensity=gain;Render();
                CompositeNear(Read(target),AddExpected(original,Direct(),gain),"linear HDR addition gain="+gain,.006f);
                NearRGB(Direct(),raw,"Composition gain changed raw lighting",.001f);
                Near(Read(target).a,original.a,"Additive camera alpha changed",.001f);
            }
            feature.Settings.additiveIntensity=2;feature.Settings.compositeMode=HoRTDICompositeMode.Replace;feature.Settings.compositeWeight=.5f;Render();
            CompositeNear(Read(target),Color.Lerp(original,CompositePublished(),.5f),"replacement crossfade independent of additive gain");
            feature.Settings.compositeMode=HoRTDICompositeMode.Additive;feature.Settings.additiveIntensity=.25f;

            surfaceMaterial.SetFloat("_UseEmission",1);surfaceMaterial.SetColor("_EmissionColor",new Color(4,1,.25f,1));
            light.intensity=0;feature.Settings.composite=false;Render();var emissionOriginal=Read(target);
            feature.Settings.composite=true;Render();CompositeNear(Read(target),emissionOriginal,"additive does not duplicate emission in zero direct light",.006f);
            light.intensity=2;feature.Settings.composite=false;Render();var emissionLit=Read(target);
            feature.Settings.composite=true;Render();CompositeNear(Read(target),AddExpected(emissionLit,Direct(),.25f),"additive retains emission once with direct light",.008f);
            surfaceMaterial.SetFloat("_UseEmission",0);

            light.type=LightType.Point;light.transform.position=new Vector3(1.5f,0,-1);light.intensity=4;blocker.SetActive(true);
            feature.Settings.composite=false;Render();var oldShadow=Read(target);feature.Settings.composite=true;Render();
            CompositeNear(Read(target),oldShadow,"zero RT visibility preserves original artistic shadow/lighting",.001f);
            blocker.SetActive(false);light.type=LightType.Directional;light.transform.rotation=Quaternion.identity;light.intensity=2;
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=Color.red;RenderSettings.fogStartDistance=0;RenderSettings.fogEndDistance=6;
            feature.Settings.composite=false;Render();var fogged=Read(target);feature.Settings.composite=true;Render();
            CompositeNear(Read(target),AddExpected(fogged,Direct(),.25f,1-(3-camera.nearClipPlane)/6),"added light fog transmission without duplicate fog colour",.008f);
            RenderSettings.fogEndDistance=1;feature.Settings.composite=false;Render();var fullFog=Read(target);
            feature.Settings.composite=true;Render();CompositeNear(Read(target),fullFog,"fully fogged receiver receives no additive light",.002f);RenderSettings.fog=false;

            primaryReceiver.transform.localScale=Vector3.one*.5f;feature.Settings.composite=false;Render();var background=CompositePixels(target)[4*64+4];
            feature.Settings.composite=true;Render();CompositeNear(CompositePixels(target)[4*64+4],background,"additive preserves background");primaryReceiver.transform.localScale=new Vector3(2,2,1);
            gb.Settings.renderScale=HoGeometryBufferRenderScale.Half;feature.Settings.composite=false;Render();var invalid=Read(target);
            feature.Settings.composite=true;Render();CompositeNear(Read(target),invalid,"additive rejects mismatched primary domain");gb.Settings.renderScale=HoGeometryBufferRenderScale.Full;
            var foreground=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));foreground.transform.position=new Vector3(0,0,-.8f);foreground.transform.localScale=Vector3.one*.5f;
            var fg=Mat("Universal Render Pipeline/Unlit");fg.SetColor("_BaseColor",new Color(.1f,.8f,.2f,1));foreground.GetComponent<Renderer>().sharedMaterial=fg;
            feature.Settings.composite=false;Render();var foregroundOriginal=Read(target);feature.Settings.composite=true;Render();
            CompositeNear(Read(target),foregroundOriginal,"additive protects foreground Unlit depth");foreground.SetActive(false);

            var transparent=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));transparent.transform.position=new Vector3(0,0,-.9f);transparent.transform.localScale=Vector3.one*.5f;
            var overlay=Mat("Universal Render Pipeline/Unlit");overlay.SetColor("_BaseColor",new Color(.8f,.1f,.2f,.5f));overlay.SetFloat("_Surface",1);
            overlay.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);overlay.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);overlay.SetInt("_ZWrite",0);
            overlay.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");overlay.SetOverrideTag("RenderType","Transparent");overlay.renderQueue=3000;transparent.GetComponent<Renderer>().sharedMaterial=overlay;
            transparent.SetActive(false);feature.Settings.composite=false;Render();var opaqueOriginal=Read(target);feature.Settings.composite=true;Render();var opaqueAdded=Read(target);
            transparent.SetActive(true);feature.Settings.composite=false;Render();var overlayOriginal=Read(target);feature.Settings.composite=true;Render();
            CompositeNear(Read(target),overlayOriginal+(opaqueAdded-opaqueOriginal)*.5f,"transparent blends after additive composite",.008f);transparent.SetActive(false);

            feature.Settings.denoise=true;light.type=LightType.Point;light.transform.position=new Vector3(1.5f,0,-1);light.color=Color.red;light.intensity=4;
            var second=Point(new Vector3(-1.5f,0,-1),Color.blue,4);for(int i=0;i<96;i++)Render();
            Require(feature.LastContext.nrd!=null,"Additive NRD fixture unavailable");feature.Settings.composite=false;Render();var filteredOriginal=Read(target);
            feature.Settings.composite=true;Render();CompositeNear(Read(target),AddExpected(filteredOriginal,Direct(),.25f),"additive consumes filtered publication",.008f);
            feature.Settings.compositeMode=HoRTDICompositeMode.Replace;feature.Settings.compositeWeight=1;Render();
            feature.Settings.compositeMode=HoRTDICompositeMode.Additive;Render();Require(!feature.LastContext.lastNrdReset,"Switching only blend mode reset NRD history");
            CompositeNear(Read(target),AddExpected(filteredOriginal,Direct(),.25f),"blend switch preserves accumulation",.008f);
            volume.enable.Override(false);RenderCamera();var disabled=Read(target);feature.SetActive(false);RenderCamera();
            CompositeNear(Read(target),disabled,"volume disable removes additive composition");feature.SetActive(true);volume.enable.Override(true);
            feature.Settings.compositeMode=HoRTDICompositeMode.Replace;feature.Settings.compositeWeight=1;SaveCompositePresentation(second);
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/HoRTDIComposite.shader");
            foreach(var message in ShaderUtil.GetShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
            File.WriteAllText("rtdi-composite-blend-"+(compatibility?"compat":"rg")+".json",JsonUtility.ToJson(new CompositeReport{pass=true,compatibility=compatibility,colorSpace=QualitySettings.activeColorSpace.ToString(),checks=compositeChecks.ToArray()},true));
            Debug.Log("[HoRTDICompositeBlend] PASS "+compositeChecks.Count+" checks; "+QualitySettings.activeColorSpace+"; compatibility="+compatibility);
        }
    }
}
