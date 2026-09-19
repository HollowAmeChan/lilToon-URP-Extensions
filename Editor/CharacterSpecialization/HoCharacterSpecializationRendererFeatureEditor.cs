using lilToon.URP.Extensions.Editor;
using lilToon.URP.Extensions.CharacterSpecialization;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    [CustomEditor(typeof(HoCharacterSpecializationRendererFeature))]
    internal sealed class HoCharacterSpecializationRendererFeatureEditor : UnityEditor.Editor
    {
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color SettingsColor = new Color(0.45f, 0.64f, 0.96f);
        private static readonly Color CaptureColor = new Color(0.42f, 0.72f, 0.58f);
        private static readonly Color DebugColor = new Color(0.86f, 0.62f, 0.38f);

        private static bool showRuntime;
        private static bool showRendererFeature;
        private static bool showCapture;
        private static bool showDebug;

        private SerializedProperty settingsProperty;

        private void OnEnable()
        {
            settingsProperty = serializedObject.FindProperty("settings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "这个 RendererFeature 负责把 HoCharacter 捕获/合成 pass 安装进当前 Renderer。效果参数只从场景或全局 Volume 的“Ho-CharacterSpecialization/角色特化”读取；这里仅保留捕获、高级资源和调试设置。",
                MessageType.Info);

            if (settingsProperty == null)
            {
                DrawDefaultInspector();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawRendererFeature();
            DrawCapture();
            DrawDebug();
            DrawRuntime();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRendererFeature()
        {
            SerializedProperty enabled = Find("enabled");
            string summary = LilUrpEditorSectionGui.BoolSummary(enabled);

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRendererFeature, "渲染器功能", summary, SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(enabled, "启用");
                DrawProperty("passEvent", "渲染时机");
                DrawProperty("renderScale", "渲染缩放");
            }
        }

        private void DrawCapture()
        {
            string summary = GetIntSummary("minRenderQueue") + "-" + GetIntSummary("maxRenderQueue");
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showCapture, "捕获范围", summary, CaptureColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("layerMask", "图层遮罩");
                DrawProperty("minRenderQueue", "最小渲染队列");
                DrawProperty("maxRenderQueue", "最大渲染队列");
            }
        }

        private void DrawDebug()
        {
            SerializedProperty debugMode = Find("debugMode");
            string summary = LilUrpEditorSectionGui.EnumName(debugMode);
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showDebug, "调试/预留", summary, DebugColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(debugMode, "调试模式");
                DrawProperty("compositeShader", "合成 Shader");
                DrawProperty("faceHairDiffuseShader", "脸色扩散 Shader");
                DrawProperty("subjectOutlineShader", "轮廓场 Shader");
                DrawProperty("farPlaneShadowReserved", "远平面阴影预留");
                DrawProperty("reflectionSpaceReserved", "反射空间预留");
            }
        }

        private static void DrawRuntime()
        {
            HoCharacterSpecializationRuntimeDiagnosticSnapshot snapshot = HoCharacterSpecializationRuntimeDiagnostics.CurrentSnapshot;
            string summary = snapshot.IsValid
                ? snapshot.CameraName + " / " + snapshot.Stage
                : "尚无帧";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行状态", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!snapshot.IsValid)
                {
                    EditorGUILayout.HelpBox("尚未记录 Ho-CharacterSpecialization 运行帧。进入 Play Mode，或让使用该 RendererFeature 的 Scene/Game camera 渲染一帧。", MessageType.Info);
                    return;
                }

                EditorGUILayout.LabelField("帧", snapshot.FrameCount.ToString());
                EditorGUILayout.LabelField("相机", snapshot.CameraName);
                EditorGUILayout.LabelField("阶段", snapshot.Stage);
                EditorGUILayout.LabelField("Active Back Buffer", snapshot.BackBufferActive ? "是" : "否");
                EditorGUILayout.LabelField("Camera Color", LilUrpEditorSectionGui.FormatAvailable(snapshot.CameraColorAvailable));
                EditorGUILayout.LabelField("MetadataBuffer", LilUrpEditorSectionGui.FormatAvailable(snapshot.MetadataBufferAvailable));
                EditorGUILayout.LabelField("GeometryBuffer", LilUrpEditorSectionGui.FormatAvailable(snapshot.GeometryBufferAvailable));
                EditorGUILayout.LabelField("MaskId", LilUrpEditorSectionGui.FormatAvailable(snapshot.MetadataMaskIdAvailable));
                EditorGUILayout.LabelField("ObjectCustom0-3", LilUrpEditorSectionGui.FormatAvailable(snapshot.MetadataObjectCustom0Available));
                EditorGUILayout.LabelField("ObjectCustom4-7", LilUrpEditorSectionGui.FormatAvailable(snapshot.MetadataObjectCustom1Available));
                EditorGUILayout.LabelField("SurfaceColor", LilUrpEditorSectionGui.FormatAvailable(snapshot.MetadataSurfaceColorAvailable));
                EditorGUILayout.LabelField("SurfaceColor Required", snapshot.MetadataSurfaceColorRequired ? "Yes" : "No");
                EditorGUILayout.LabelField("NormalDepth", LilUrpEditorSectionGui.FormatAvailable(snapshot.GeometryNormalDepthAvailable));
                EditorGUILayout.LabelField("Depth", LilUrpEditorSectionGui.FormatAvailable(snapshot.GeometryDepthAvailable));
                EditorGUILayout.LabelField("Depth Required", snapshot.GeometryDepthRequired ? "Yes" : "No");

                EditorGUILayout.HelpBox(
                    snapshot.Ready
                        ? "角色特化输入有效：MetadataBuffer 与 GeometryBuffer 均可用。"
                        : "角色特化已跳过：" + snapshot.Reason,
                    snapshot.Ready ? MessageType.Info : MessageType.Warning);
            }
        }

        private void DrawProperty(string relativeName, string label)
        {
            DrawProperty(Find(relativeName), label);
        }

        private static void DrawProperty(SerializedProperty property, string label)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
            }
        }

        private SerializedProperty Find(string relativeName)
        {
            return settingsProperty?.FindPropertyRelative(relativeName);
        }

        private string GetIntSummary(string relativeName)
        {
            SerializedProperty property = Find(relativeName);
            return property != null ? property.intValue.ToString() : "-";
        }
    }
}
