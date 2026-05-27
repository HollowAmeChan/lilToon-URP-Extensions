using lilToon.URP.Extensions.Editor;
using lilToon.URP.Extensions.PlanarReflection;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PlanarReflection
{
    [CustomEditor(typeof(HoPlanarReflectionRendererFeature))]
    internal sealed class HoPlanarReflectionRendererFeatureEditor : UnityEditor.Editor
    {
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color CompositeColor = new Color(0.42f, 0.72f, 0.58f);
        private static readonly Color DebugColor = new Color(0.86f, 0.62f, 0.38f);
        private static readonly Color AdvancedColor = new Color(0.62f, 0.58f, 0.78f);

        private static bool showRuntime;
        private static bool showComposite;
        private static bool showDebug;
        private static bool showAdvanced;

        private SerializedProperty settingsProperty;

        private void OnEnable()
        {
            settingsProperty = serializedObject.FindProperty("settings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (settingsProperty == null)
            {
                DrawDefaultInspector();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUILayout.HelpBox(
                "Ho-PlanarReflection 先渲染镜像相机，再由可选合成 pass 消费 MetadataBuffer 与 GeometryBuffer，对水面反射做扰动和混合。",
                MessageType.Info);

            DrawRuntime();
            DrawComposite();
            DrawDebug();
            DrawAdvanced();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRuntime()
        {
            SerializedProperty enabled = Find("enabled");
            SerializedProperty renderGameView = Find("renderGameView");
            SerializedProperty renderSceneView = Find("renderSceneView");
            string summary = LilUrpEditorSectionGui.BoolSummary(enabled)
                + " / Game " + LilUrpEditorSectionGui.BoolSummary(renderGameView)
                + " / Scene " + LilUrpEditorSectionGui.BoolSummary(renderSceneView);

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(enabled, "启用");
                DrawProperty(renderGameView, "渲染 Game View");
                DrawProperty(renderSceneView, "渲染 Scene View");
                DrawProperty(Find("maxSurfacesPerCamera"), "每相机最大表面数");
                DrawRuntimeStatus();
            }
        }

        private void DrawComposite()
        {
            SerializedProperty compositeEnabled = Find("compositeEnabled");
            SerializedProperty compositeStrength = Find("compositeStrength");
            SerializedProperty distortion = Find("distortion");
            string summary = compositeEnabled != null && compositeEnabled.boolValue
                ? "开 " + FormatFloat(compositeStrength) + " / 扰动 " + FormatFloat(distortion)
                : "关";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showComposite, "反射合成", summary, CompositeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(compositeEnabled, "启用后处理合成");
                DrawProperty(compositeStrength, "合成强度");
                DrawProperty(distortion, "法线扰动");
                DrawProperty(Find("edgeExtendDistance"), "屏幕边缘像素外扩");
                DrawProperty(Find("minSmoothness"), "最小 Smoothness");
                DrawProperty(Find("tint"), "反射 Tint");
                DrawProperty(Find("compositeFlipY"), "反射纹理 Flip Y");
                DrawProperty(Find("enableDepthGate"), "启用深度门控");

                SerializedProperty enableDepthGate = Find("enableDepthGate");
                using (new EditorGUI.DisabledScope(enableDepthGate != null && !enableDepthGate.boolValue))
                {
                    DrawProperty(Find("depthTolerance"), "深度容差");
                }
            }
        }

        private void DrawDebug()
        {
            SerializedProperty debugMode = Find("debugMode");
            string summary = LilUrpEditorSectionGui.EnumName(debugMode);

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showDebug, "调试", summary, DebugColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(debugMode, "调试模式");
                DrawProperty(Find("debugDepthFar"), "深度显示 Far");
                DrawProperty(Find("debugDistortionScale"), "扰动显示倍率");
                EditorGUILayout.HelpBox(
                    "这里的调试模式会直接替换当前相机颜色。小窗调试请添加 Ho-DebugTile，并选择 PlanarReflection 条目。",
                    MessageType.Info);
            }
        }

        private void DrawAdvanced()
        {
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showAdvanced, "高级", "Pass / Shader", AdvancedColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(Find("compositePassEvent"), "合成 Pass Event");
                DrawProperty(Find("compositeShader"), "合成 Shader");
            }
        }

        private static void DrawRuntimeStatus()
        {
            HoPlanarReflectionRuntimeDiagnosticSnapshot snapshot = HoPlanarReflectionRuntimeDiagnostics.CurrentSnapshot;
            EditorGUILayout.Space(4.0f);
            EditorGUILayout.LabelField("运行状态", EditorStyles.boldLabel);

            if (!snapshot.IsValid)
            {
                EditorGUILayout.HelpBox("尚未记录 Ho-PlanarReflection 运行帧。启用 RendererFeature 后让 Scene/Game camera 渲染一帧。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("帧", snapshot.FrameCount.ToString());
            EditorGUILayout.LabelField("相机", snapshot.CameraName);
            EditorGUILayout.LabelField("表面总数", snapshot.SurfaceCount.ToString());
            EditorGUILayout.LabelField("本帧有效", snapshot.ActiveSurfaceCount.ToString());
            EditorGUILayout.HelpBox(
                snapshot.Ready ? "Ho-PlanarReflection 输入有效。" : "Ho-PlanarReflection 已跳过：" + snapshot.Reason,
                snapshot.Ready ? MessageType.Info : MessageType.Warning);
        }

        private SerializedProperty Find(string relativeName)
        {
            return settingsProperty?.FindPropertyRelative(relativeName);
        }

        private static void DrawProperty(SerializedProperty property, string label, bool includeChildren = false)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label), includeChildren);
            }
        }

        private static string FormatFloat(SerializedProperty property)
        {
            return property != null ? property.floatValue.ToString("0.###") : "-";
        }
    }
}
