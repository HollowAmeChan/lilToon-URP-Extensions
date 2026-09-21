using lilToon.URP.Extensions.Editor;
using lilToon.URP.Extensions.ShadowCast;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.ShadowCast
{
    [CustomEditor(typeof(HoShadowCastRendererFeature))]
    internal sealed class HoShadowCastRendererFeatureEditor : UnityEditor.Editor
    {
        private const int BeforeRenderingPrePassesValue = 150;

        private static readonly Color SettingsColor = new Color(0.45f, 0.64f, 0.96f);

        private static bool showSettings;
        private static bool showCapacity;
        private static bool showAtlas;
        private static bool showPcss;
        private static bool showSecondDirectional;
        private static bool showRuntime;

        private SerializedProperty settingsProperty;

        private void OnEnable()
        {
            settingsProperty = serializedObject.FindProperty("settings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "收集当前相机的 URP 可见灯；URP 主光跳过（仍走原生阴影）。",
                MessageType.Info);

            if (settingsProperty == null)
            {
                DrawDefaultInspector();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawSettings();
            DrawCapacity();
            DrawAtlas();
            DrawPcss();
            DrawSecondDirectional();
            DrawRuntimeStatus();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSettings()
        {
            SerializedProperty enabled = settingsProperty.FindPropertyRelative("enabled");
            SerializedProperty collectVisibleLights = settingsProperty.FindPropertyRelative("collectVisibleLights");
            string summary = enabled != null && enabled.boolValue
                ? collectVisibleLights != null && collectVisibleLights.boolValue ? "开 / 收集" : "开 / 不收集"
                : "关";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSettings, "RendererFeature", summary, SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(enabled, "启用");
                SerializedProperty passEvent = settingsProperty.FindPropertyRelative("passEvent");
                DrawProperty(passEvent, "渲染时机");
                DrawProperty(collectVisibleLights, "收集可见灯光");
                DrawProperty(settingsProperty.FindPropertyRelative("lightLayerMask"), "灯光对象图层");
                DrawProperty(settingsProperty.FindPropertyRelative("lightRenderingLayerMask"), "灯光渲染层");
                DrawProperty(settingsProperty.FindPropertyRelative("casterLayerMask"), "投影物对象图层");
                DrawProperty(settingsProperty.FindPropertyRelative("casterRenderingLayerMask"), "投影物渲染层");
                DrawProperty(settingsProperty.FindPropertyRelative("shadowStrength"), "全局阴影强度");
                DrawProperty(settingsProperty.FindPropertyRelative("punctualShadowStrength"), "点/聚光阴影强度");
                DrawProperty(settingsProperty.FindPropertyRelative("punctualShadowFadeSpeed"), "点/聚光淡出速度");
                DrawProperty(settingsProperty.FindPropertyRelative("debugMode"), "调试模式");

                if (passEvent != null && passEvent.intValue < BeforeRenderingPrePassesValue)
                {
                    EditorGUILayout.HelpBox("ShadowCast 不应早于 URP 内置阴影阶段执行。运行时会自动钳制到 BeforeRenderingPrePasses。", MessageType.Info);
                }
            }
        }

        private void DrawCapacity()
        {
            SerializedProperty lightCapacity = settingsProperty.FindPropertyRelative("lightCapacity");
            HoShadowCastLightCapacity capacity = HoShadowCastShaderContract.ClampCapacity(lightCapacity != null ? lightCapacity.intValue : 0);
            HoShadowCastCapacityLimits limits = HoShadowCastShaderContract.GetLimits(capacity);
            int atlasSize = GetIntSummaryValue("atlasSize", 4096);
            int spotResolution = GetIntSummaryValue("spotResolution", 512);
            int pointFaceResolution = GetIntSummaryValue("pointFaceResolution", 512);
            int spotSlices = HoShadowCastShaderContract.GetSliceCapacity(atlasSize, spotResolution);
            int pointSlices = HoShadowCastShaderContract.GetSliceCapacity(atlasSize, pointFaceResolution);
            int pointLights = HoShadowCastShaderContract.GetPointLightCapacity(capacity, atlasSize, pointFaceResolution);
            string summary = limits.LightCount + " 灯 / ≤" + Mathf.Max(spotSlices, pointSlices) + " 片";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showCapacity, "容量", summary, SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(lightCapacity, "多光容量档");
                EditorGUILayout.LabelField("附加灯上限", limits.LightCount + " 盏（逐像素采样与循环上限）");
                EditorGUILayout.LabelField("全局数组长度", HoShadowCastShaderContract.ArrayLights + " 灯 / " + HoShadowCastShaderContract.ArraySlices + " 片（固定，与档位无关）");
                EditorGUILayout.LabelField("聚光切片", spotResolution + "px → 约 " + spotSlices + " 片（≈ " + Mathf.Min(spotSlices, limits.LightCount) + " 盏）");
                EditorGUILayout.LabelField("点光切片", pointFaceResolution + "px → 约 " + pointSlices + " 片（≈ " + pointLights + " 盏点光）");
                EditorGUILayout.LabelField("图集", atlasSize + "px（切片数按图集尺寸与分辨率动态计算，硬上限 " + HoShadowCastShaderContract.ArraySlices + " 片）");

                EditorGUILayout.HelpBox(
                    "数组长度是会话级缓存：改过它之后出现 \"exceeds previous array size\" 就重启一次 Unity。",
                    MessageType.Warning);
            }
        }

        private void DrawAtlas()
        {
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showAtlas, "图集", GetIntSummary("atlasSize", "px"), SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(settingsProperty.FindPropertyRelative("atlasSize"), "图集尺寸");
                DrawProperty(settingsProperty.FindPropertyRelative("spotResolution"), "聚光灯分辨率");
                DrawProperty(settingsProperty.FindPropertyRelative("pointFaceResolution"), "点光单面分辨率");
                DrawProperty(settingsProperty.FindPropertyRelative("directionalResolution"), "方向光分辨率");
                DrawProperty(settingsProperty.FindPropertyRelative("directionalNearPlane"), "方向光近裁面");
                DrawProperty(settingsProperty.FindPropertyRelative("directionalShadowSize"), "方向光阴影尺寸");
                DrawProperty(settingsProperty.FindPropertyRelative("directionalShadowDepth"), "方向光阴影深度");
            }
        }

        private void DrawPcss()
        {
            SerializedProperty enabled = settingsProperty.FindPropertyRelative("pcssEnabled");
            string summary = enabled != null && enabled.boolValue ? "开" : "关";
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showPcss, "PCSS", summary, SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(enabled, "启用 PCSS");
                DrawProperty(settingsProperty.FindPropertyRelative("pcssQuality"), "质量");
                DrawProperty(settingsProperty.FindPropertyRelative("punctualPcssSoftness"), "点/聚光柔化");
                DrawProperty(settingsProperty.FindPropertyRelative("secondDirectionalPcssSoftness"), "第二方向光柔化");
                DrawProperty(settingsProperty.FindPropertyRelative("pcssBlockerSearchRadius"), "遮挡搜索半径");
                DrawProperty(settingsProperty.FindPropertyRelative("pcssMaxPenumbraRadius"), "最大半影半径");
                DrawProperty(settingsProperty.FindPropertyRelative("pcssDepthBias"), "深度偏移");
            }
        }

        private void DrawSecondDirectional()
        {
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSecondDirectional, "第二方向光", GetIntSummary("secondDirectionalAtlasSize", "px"), SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(settingsProperty.FindPropertyRelative("secondDirectionalShadowStrength"), "强度");
                DrawProperty(settingsProperty.FindPropertyRelative("secondDirectionalAtlasSize"), "图集尺寸");
                DrawProperty(settingsProperty.FindPropertyRelative("secondDirectionalCascadeCount"), "级联数量");
                DrawProperty(settingsProperty.FindPropertyRelative("secondDirectionalMaxDistance"), "最大距离");
                DrawProperty(settingsProperty.FindPropertyRelative("secondDirectionalShadowDepth"), "阴影深度");
                DrawProperty(settingsProperty.FindPropertyRelative("secondDirectionalCascadeSplits"), "级联切分");
            }
        }

        private void DrawRuntimeStatus()
        {
            HoShadowCastRuntimeDiagnosticSnapshot snapshot = HoShadowCastRuntimeDiagnostics.CurrentSnapshot;
            string summary = snapshot.IsValid
                ? snapshot.LightCount + " 灯 / " + snapshot.SliceCount + " 片"
                : "暂无帧";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行状态", summary, SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!snapshot.IsValid)
                {
                    EditorGUILayout.HelpBox("尚未记录 ShadowCast 运行帧。进入 Play Mode，或让使用该 RendererFeature 的 Scene/Game camera 渲染一帧。", MessageType.Info);
                    return;
                }

                EditorGUILayout.LabelField("帧", snapshot.FrameCount.ToString());
                EditorGUILayout.LabelField("路径", snapshot.Path);
                EditorGUILayout.LabelField("相机", snapshot.CameraName);
                EditorGUILayout.LabelField("来源", snapshot.Source);
                EditorGUILayout.LabelField("可见灯光", snapshot.VisibleLightCount.ToString());
                EditorGUILayout.LabelField("候选灯光", snapshot.CandidateCount + " 检查, " + snapshot.SkippedCandidateCount + " 跳过");
                EditorGUILayout.LabelField("点/聚光图集", FormatAtlas(snapshot.HasFrame, snapshot.LightCount, snapshot.SliceCount, snapshot.AtlasSize));
                EditorGUILayout.LabelField("第二方向光", FormatSecondDirectional(snapshot));

                DrawAcceptedLights(snapshot.AcceptedLights);
                DrawSkippedLights(snapshot.SkippedLights, snapshot.SkippedCandidateCount);
            }
        }

        private static string FormatAtlas(bool active, int lightCount, int sliceCount, int atlasSize)
        {
            return active
                ? lightCount + " 灯, " + sliceCount + " 片, " + atlasSize + "px"
                : "未激活";
        }

        private static string FormatSecondDirectional(HoShadowCastRuntimeDiagnosticSnapshot snapshot)
        {
            return snapshot.HasSecondDirectionalFrame
                ? snapshot.SecondDirectionalLightCount + " 灯, " + snapshot.SecondDirectionalSliceCount + " 片, " + snapshot.SecondDirectionalCascadeCount + " 级联, " + snapshot.SecondDirectionalAtlasSize + "px"
                : "未激活";
        }

        private static void DrawAcceptedLights(HoShadowCastRuntimeDiagnosticLight[] lights)
        {
            if (lights == null || lights.Length == 0)
            {
                return;
            }

            EditorGUILayout.Space(3.0f);
            EditorGUILayout.LabelField("已接收灯光", EditorStyles.boldLabel);
            for (int i = 0; i < lights.Length; i++)
            {
                HoShadowCastRuntimeDiagnosticLight light = lights[i];
                EditorGUILayout.LabelField(
                    light.Name,
                    FormatAcceptedLight(light));
            }
        }

        private static string FormatAcceptedLight(HoShadowCastRuntimeDiagnosticLight light)
        {
            string summary = light.Stage + " " + light.Type + " 片 " + light.FirstSlice + "+" + light.SliceCount + " @ " + light.Resolution + "px";
            return light.BlockOffsetX >= 0 && light.BlockOffsetY >= 0 && light.BlockWidth > 0 && light.BlockHeight > 0
                ? summary + ", 块 (" + light.BlockOffsetX + ", " + light.BlockOffsetY + ") " + light.BlockWidth + "x" + light.BlockHeight
                : summary;
        }

        private static void DrawSkippedLights(HoShadowCastRuntimeDiagnosticSkip[] skippedLights, int skippedCandidateCount)
        {
            if (skippedLights == null || skippedLights.Length == 0)
            {
                return;
            }

            EditorGUILayout.Space(3.0f);
            EditorGUILayout.LabelField("已跳过灯光", EditorStyles.boldLabel);
            for (int i = 0; i < skippedLights.Length; i++)
            {
                HoShadowCastRuntimeDiagnosticSkip skipped = skippedLights[i];
                EditorGUILayout.LabelField(skipped.Name, skipped.Stage + " " + skipped.Type + ": " + skipped.Reason);
            }

            int remaining = skippedCandidateCount - skippedLights.Length;
            if (remaining > 0)
            {
                EditorGUILayout.LabelField("更多跳过", remaining.ToString());
            }
        }

        private string GetIntSummary(string propertyName, string suffix)
        {
            SerializedProperty property = settingsProperty.FindPropertyRelative(propertyName);
            return property != null ? property.intValue + suffix : string.Empty;
        }

        private int GetIntSummaryValue(string propertyName, int fallback)
        {
            SerializedProperty property = settingsProperty.FindPropertyRelative(propertyName);
            return property != null ? property.intValue : fallback;
        }

        private static void DrawProperty(SerializedProperty property, string label)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
            }
        }

    }
}
