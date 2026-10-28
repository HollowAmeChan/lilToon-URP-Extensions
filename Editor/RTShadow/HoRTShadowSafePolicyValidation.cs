using System;
using lilToon.URP.Extensions.RTShadow;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace lilToon.URP.Extensions.Editor.RTShadow
{
    public static partial class HoRTShadowValidation
    {
        private static bool safePolicyAudit;
        public static void RunSafePolicy(){safePolicyAudit=true;nprIntegration=true;rawOnly=true;Run();}
        public static void RunSafePolicyCompatibility(){compatibility=true;RunSafePolicy();}
        private static void ValidateSafePolicy()
        {
            Check(!HoRTShadowRendererFeature.SupportsTerminatorCorrection,"Known character regression is unavailable in production");
            Check(HoRTShadowRendererFeature.ProductionSuspended,"Entire regressed producer is suspended, including zero-correction path");
            shadow.Settings.debugMode=HoRTShadowDebugMode.All;shadow.Settings.debugInGameView=true;
            shadow.Settings.terminatorCorrection=0;Render(false);var original=Read(target);
            shadow.Settings.terminatorCorrection=1;Render(false);var requested=Read(target);
            Check(Equal(original,requested),"Saved nonzero Feature correction cannot reactivate regressed displacement");
            var volume=Keep(new GameObject("Explicit saved correction override")).AddComponent<Volume>();volume.isGlobal=true;volume.priority=100;
            volume.sharedProfile=Keep(ScriptableObject.CreateInstance<VolumeProfile>());var controls=volume.sharedProfile.Add<HoRTShadowVolume>(false);controls.terminatorCorrection.Override(1);
            Render(false);var overridden=Read(target);Check(Equal(original,overridden),"Saved nonzero Volume correction also remains safely inactive");
            Check(shadow.Settings.terminatorCorrection==1&&controls.terminatorCorrection.value==1,"User Feature and Volume values preserved without asset mutation");
            Check(shadow.LastEffectiveSettings.terminatorCorrection==0,"Runtime diagnostics report actual correction zero");
            Check(!shadow.TryGetCompatibilityProduct(camera,light,out _)&&shadow.LastOutput==null,"Suspended producer exposes no current product or stale output");
            Check(Shader.GetGlobalFloat("_HoRTShadowActive")==0,"NPR activation is zero so material uses ordinary fallback");
            Check(!shadow.LastEffectiveSettings.enabled&&shadow.Settings.enabled,"Actual production disabled without changing saved master switch");
            Check(shadow.LastEffectiveSettings.debugMode==HoRTShadowDebugMode.Off&&shadow.Settings.debugMode==HoRTShadowDebugMode.All,"Suspension also prevents saved debug mode replacing the camera image");
            var renderer=(UniversalRendererData)new SerializedObject(GraphicsSettings.defaultRenderPipeline).FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue;
            var probe=Keep(ScriptableObject.CreateInstance<HoRTShadowActivationProbe>());
            probe.material=Keep(new Material(AssetDatabase.LoadAssetAtPath<Shader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTShadow/HoRTShadowActivationProbe.shader")));
            var texture=Keep(new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear));texture.Create();probe.target=RTHandles.Alloc(texture);
            renderer.rendererFeatures.Insert(renderer.rendererFeatures.IndexOf(shadow),probe);renderer.SetDirty();
            probe.poison=true;shadow.SetActive(false);Render(false);
            Check(Read(texture)[0].r==1,"Positive GPU control observes injected activation when clearing pass is absent");
            shadow.SetActive(true);Render(false);
            Check(Read(texture)[0].r==0,"Explicit stale GPU activation before suspended pass is cleared before downstream shaders");
            probe.target.Release();probe.target=null;
            Debug.Log("[HoRTShadowSafePolicy] PASS entire producer suspended / NPR fallback / no stale products / saved Feature and Volume values preserved");
        }
    }
}
