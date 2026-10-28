using System;
using lilToon.URP.Extensions.RTShadow;
using UnityEngine;
using UnityEngine.Rendering;
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
            shadow.Settings.terminatorCorrection=0;var original=Render();
            shadow.Settings.terminatorCorrection=1;var requested=Render();
            Check(Equal(original,requested),"Saved nonzero Feature correction cannot reactivate regressed displacement");
            var volume=Keep(new GameObject("Explicit saved correction override")).AddComponent<Volume>();volume.isGlobal=true;volume.priority=100;
            volume.sharedProfile=Keep(ScriptableObject.CreateInstance<VolumeProfile>());var controls=volume.sharedProfile.Add<HoRTShadowVolume>(false);controls.terminatorCorrection.Override(1);
            var overridden=Render();Check(Equal(original,overridden),"Saved nonzero Volume correction also remains safely inactive");
            Check(shadow.Settings.terminatorCorrection==1&&controls.terminatorCorrection.value==1,"User Feature and Volume values preserved without asset mutation");
            Check(shadow.LastEffectiveSettings.terminatorCorrection==0,"Runtime diagnostics report actual correction zero");
            Check(shadow.TryGetCompatibilityProduct(camera,light,out var product)&&product.TerminatorCorrection==0,"Published current product explicitly reports uncorrected geometry policy");
            Debug.Log("[HoRTShadowSafePolicy] PASS disabled displacement / saved Feature and Volume values / exact GPU reference / honest product metadata");
        }
    }
}
