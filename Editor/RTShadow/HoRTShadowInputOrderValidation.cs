#pragma warning disable CS0618
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTShadow
{
    public static partial class HoRTShadowValidation
    {
        private static bool inputOrderAudit;
        public static void RunInputOrder() { inputOrderAudit = true; nprIntegration = true; rawOnly = true; Run(); }
        public static void RunInputOrderCompatibility() { compatibility = true; RunInputOrder(); }
        private static void ValidateInputOrder()
        {
            var pipeline = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
            var data = (UniversalRendererData)new SerializedObject(pipeline).FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue;
            Check(HoRTShadowRendererFeatureEditor.DependencyWarnings(data, shadow).Count == 0, "Normal producer order has no dependency warning");
            data.rendererFeatures.Remove(shadow); data.rendererFeatures.Insert(data.rendererFeatures.IndexOf(sb), shadow); data.SetDirty();
            var warnings = HoRTShadowRendererFeatureEditor.DependencyWarnings(data, shadow);
            Check(warnings.Exists(value => value.Contains("Ho-SurfaceBuffer") && value.Contains("尚未产出")), "Inspector names the SB read-before-producer dependency");
            var invalid = Render(); bool allInvalid = true; foreach (var pixel in invalid) allInvalid &= pixel.b == 2;
            Check(allInvalid, "Misordered same-event SB reproduces all-red InvalidSurface raw output");
            data.rendererFeatures.Remove(shadow); data.rendererFeatures.Insert(data.rendererFeatures.IndexOf(sb) + 1, shadow); data.SetDirty();
            var restored = Render();
            Valid(restored, 32, 32, 0, bodyId, "SB before RTShadow restores current receiver shadow");
            Check(HoRTShadowRendererFeatureEditor.DependencyWarnings(data, shadow).Count == 0, "Fixed ordering clears Inspector dependency warning");
            sb.Settings.passEvent = RenderPassEvent.AfterRenderingOpaques;
            Check(HoRTShadowRendererFeatureEditor.DependencyWarnings(data, shadow).Exists(value => value.Contains("Ho-SurfaceBuffer") && value.Contains("单独调整列表位置无效")), "Inspector distinguishes late event from same-event list ordering");
            sb.Settings.passEvent = RenderPassEvent.BeforeRenderingOpaques;
            data.rendererFeatures.Remove(host); data.rendererFeatures.Add(host);
            Check(HoRTShadowRendererFeatureEditor.DependencyWarnings(data, shadow).Exists(value => value.Contains("pre-cull")), "Host pre-cull dependency remains a list-order constraint");
            data.rendererFeatures.Remove(host); data.rendererFeatures.Insert(0, host); data.SetDirty();
            Debug.Log("[HoRTShadowInputOrder] PASS actual red-frame reproduction / corrected GPU receiver / same-event list and late-event diagnosis / host pre-cull order");
        }
    }
}
