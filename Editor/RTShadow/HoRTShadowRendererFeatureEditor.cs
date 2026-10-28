using System.Collections.Generic;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTShadow;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTShadow
{
    [CustomEditor(typeof(HoRTShadowRendererFeature)), CanEditMultipleObjects]
    internal sealed class HoRTShadowRendererFeatureEditor : UnityEditor.Editor
    {
        private static readonly Color RuntimeColor = new(.46f, .64f, .92f), QualityColor = new(.42f, .72f, .58f),
            DebugColor = new(.86f, .62f, .38f), AdvancedColor = new(.62f, .58f, .78f);
        private static bool runtime = true, lights = true, quality = true, debug, advanced, status = true;
        private SerializedProperty settings;
        public override void OnInspectorGUI()
        {
            serializedObject.Update(); settings = serializedObject.FindProperty("settings");
            EditorGUILayout.HelpBox("需要 Ho-RT、同分辨率单采样 GB / SB 与 D3D12。接收角色在 Ho-RTShadow Receiver 中声明；本处设默认值，Ho-RTShadow Volume 可逐相机覆盖质量及调试。", MessageType.Info);
            if (targets.Length == 1)
            {
                var path = AssetDatabase.GetAssetPath(target);
                var renderer = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path) as ScriptableRendererData;
                foreach (string warning in DependencyWarnings(renderer, (HoRTShadowRendererFeature)target))
                    EditorGUILayout.HelpBox(warning, MessageType.Error);
            }
            if (Header(ref runtime, "生产", "总开关 / 相机", RuntimeColor))
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            { Draw("enabled", "生产总开关"); Draw("inSceneView", "Scene View 中生产"); }
            if (Header(ref lights, "灯与接收域", LilUrpEditorSectionGui.EnumName(Find("lightSelection")), RuntimeColor))
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Draw("lightSelection", "请求灯范围"); Draw("lightLayers", "灯所在层"); Draw("geometryLayers", "投射物层");
                Draw("excludedRayMaterials", "排除投射材质");
                EditorGUILayout.HelpBox("场景声明灯：在 Light 上添加 Ho-RTShadow Light。全部支持灯会按灯数增加贴图和追踪成本。方向光支持有限角度；点 / 聚光当前仅 delta 硬影。cookie、烘焙灯和受限 caster linking 会明确拒绝。", MessageType.None);
            }
            if (Header(ref quality, "重建质量", LilUrpEditorSectionGui.EnumName(Find("reconstruction")), QualityColor))
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Draw("reconstruction", "阴影重建"); Draw("angularDiameter", "默认角直径（度）");
                EditorGUILayout.HelpBox("Ho-RTShadow Light 的逐灯角度优先于这里和 Volume 的默认值。0 度为二值硬影；增加样本不能给零角度光源制造半影。", MessageType.None);
                // Keep both budgets visible: a Volume can choose a different mode.
                Draw("samplesPerPixel", "原始参考样本数"); Draw("singleFrameEstimates", "无历史同帧估计数"); Draw("samplingSeed", "采样种子");
                EditorGUILayout.HelpBox("原始参考只使用原始样本数。SIGMA 无历史逐次追踪并独立降噪后平均，成本随估计数增加，不等待应用帧；SIGMA 稳定预览每请求一次，输入变化保守重置历史。", MessageType.None);
            }
            if (Header(ref debug, "调试", "Volume 可覆盖", DebugColor))
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Draw("debugMode", "调试模式"); Draw("debugLight", "调试灯槽"); Draw("debugDistance", "距离显示上限（米）");
                Draw("debugInSceneView", "Scene View 调试"); Draw("debugInGameView", "Game View 调试");
                EditorGUILayout.HelpBox("灯槽对应下面运行状态的当前生产列表；灯集合变化时会重排。紫色表示该槽没有当前产品。All 平铺六种视图，最终 AA 后显示。Raw 在多估计模式中是最后一个真实单样本，差值不是误差真值。", MessageType.None);
            }
            if (Header(ref advanced, "高级", "射线 / Shader", AdvancedColor))
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Draw("rayBias", "射线偏移（米）"); Draw("maxDistance", "射线距离上限（米）");
                foreach (string field in new[] { "shader", "visibilityMaterialShader", "sigmaShader", "sigmaPipelines", "debugShader" })
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(field), true);
            }
            serializedObject.ApplyModifiedProperties();
            if (targets.Length == 1) DrawStatus((HoRTShadowRendererFeature)target);
        }
        private void DrawStatus(HoRTShadowRendererFeature feature)
        {
            if (!Header(ref status, "运行状态", feature.Status, AdvancedColor)) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("当前相机", feature.LastCamera != null ? feature.LastCamera.name : "暂无帧");
                EditorGUILayout.LabelField("生产状态", feature.Status, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("生产数表示已调度；逐像素是否有效请看状态视图。", EditorStyles.wordWrappedMiniLabel);
                var effective = feature.LastEffectiveSettings;
                if (effective != null)
                {
                    EditorGUILayout.LabelField("Volume 覆盖字段", feature.VolumeOverrideCount.ToString());
                    EditorGUILayout.LabelField("当前请求质量", $"{effective.reconstruction} / Raw {effective.samplesPerPixel} / 无历史 {effective.singleFrameEstimates}");
                    EditorGUILayout.LabelField("当前默认角度", effective.angularDiameter.ToString("0.####") + "°");
                    EditorGUILayout.LabelField("当前调试", effective.debugMode + " / slot " + effective.debugLight);
                }
                foreach (var item in feature.LightDiagnostics)
                {
                    EditorGUILayout.LabelField($"Slot {item.Slot} · ID {item.LightId}", item.Light != null ? item.Light.name : "已销毁");
                    EditorGUILayout.LabelField("  实际输出", $"{item.Width}×{item.Height} / {item.Reconstruction} / history {item.HistoryFrame}" + (item.HistoryReset ? " / reset" : ""));
                    EditorGUILayout.LabelField("  实际请求", $"{item.AngularDiameter:0.####}° / samples {item.Samples} / estimates {item.Estimates} / receivers {item.Receivers}");
                }
                foreach (var item in feature.UnavailableProducts)
                    EditorGUILayout.HelpBox($"{(item.Light != null ? item.Light.name : "已销毁")} · ID {item.LightId} · {item.Status}\n{item.Reason}", MessageType.Warning);
                EditorGUILayout.HelpBox("状态图：黑=未请求，绿=有效，红=主表面无效，蓝=灯域外，橙=射线预算不足，紫=查询错误 / 缺产品。有效像素的 V=0 才是完全遮挡。", MessageType.None);
            }
        }
        internal static List<string> DependencyWarnings(ScriptableRendererData renderer, HoRTShadowRendererFeature feature)
        {
            var warnings = new List<string>();
            if (renderer == null) return warnings;
            var features = renderer.rendererFeatures;
            int consumer = features.IndexOf(feature);
            if (consumer < 0) return warnings;
            int host = features.FindIndex(item => item is HoRTRendererFeature && item.isActive);
            if (host < 0) warnings.Add("缺少已启用的 Ho-RT。请添加公共宿主并放在 RTShadow 前面。");
            else if (host > consumer) warnings.Add("Ho-RT 必须位于 RTShadow 前面：pre-cull 请求依赖列表顺序，修改 Pass Event 不能代替宿主排序。");
            int gb = features.FindIndex(item => item is HoGeometryBufferRendererFeature value && item.isActive && value.Settings.enabled);
            int sb = features.FindIndex(item => item is HoSurfaceBufferRendererFeature value && item.isActive && value.Settings.enabled);
            if (gb < 0) warnings.Add("缺少已启用的 Ho-GeometryBuffer；RTShadow 需要当前相机的 GB 主表面输入。");
            else
            {
                var settings = ((HoGeometryBufferRendererFeature)features[gb]).Settings;
                CheckProducer("Ho-GeometryBuffer", gb, settings.passEvent);
                if (settings.renderScale != HoGeometryBufferRenderScale.Full)
                    warnings.Add("Ho-GeometryBuffer 的 Render Scale 必须为 Full：缩小的 GB 与 SB / RTShadow 不属于同一像素采样域。");
            }
            if (sb < 0) warnings.Add("缺少已启用的 Ho-SurfaceBuffer；RTShadow 需要当前相机的 SB 表面、深度和 owner。");
            else CheckProducer("Ho-SurfaceBuffer", sb, ((HoSurfaceBufferRendererFeature)features[sb]).Settings.passEvent);
            return warnings;

            void CheckProducer(string name, int producer, RenderPassEvent eventValue)
            {
                if (eventValue > RenderPassEvent.BeforeRenderingOpaques)
                    warnings.Add(name + " 的 Pass Event 晚于 RTShadow 的 BeforeRenderingOpaques；请先修正生产时机，单独调整列表位置无效。");
                else if (eventValue == RenderPassEvent.BeforeRenderingOpaques && producer > consumer)
                    warnings.Add(name + " 排在 RTShadow 后面，且两者同为 BeforeRenderingOpaques，RTShadow 读取时输入尚未产出。请把 RTShadow 移到 " + name + " 下方。");
            }
        }
        private SerializedProperty Find(string name) => settings.FindPropertyRelative(name);
        private void Draw(string name, string label)
        { var property = Find(name); EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip), true); }
        private static bool Header(ref bool expanded, string title, string summary, Color color) => LilUrpEditorSectionGui.DrawSectionHeader(ref expanded, title, summary, color);
    }
}
