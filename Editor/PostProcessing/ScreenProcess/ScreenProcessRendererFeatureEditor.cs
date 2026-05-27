using lilToon.URP.Extensions.Editor;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    [CustomEditor(typeof(ScreenProcessRendererFeature))]
    internal sealed class ScreenProcessRendererFeatureEditor : UnityEditor.Editor
    {
        private static readonly Color SettingsColor = new Color(0.45f, 0.64f, 0.96f);
        private static readonly Color SubjectColor = new Color(0.58f, 0.72f, 0.55f);
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);

        private static bool showSettings;
        private static bool showSubjectMask;
        private static bool showRuntime;

        private SerializedProperty settingsProperty;
        private SerializedProperty useVolumesProperty;

        private void OnEnable()
        {
            settingsProperty = serializedObject.FindProperty("settings");
            useVolumesProperty = serializedObject.FindProperty("UseVolumes");
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

            DrawSettings();
            DrawSubjectMask();
            DrawRuntimeStatus();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSettings()
        {
            SerializedProperty enabled = Find("enabled");
            string summary = FormatEnabled(enabled) + " / " + FormatUseVolumes();
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSettings, "RendererFeature", summary, SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(useVolumesProperty, "使用 Volume");
                DrawProperty(enabled, "启用");
                DrawProperty(Find("defaultLayerShader"), "默认 Layer Shader");
            }
        }

        private void DrawSubjectMask()
        {
            string summary = GetQueueSummary();
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSubjectMask, "Subject Mask", summary, SubjectColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(Find("subjectLayerMask"), "对象图层");
                DrawProperty(Find("subjectMinRenderQueue"), "最小 Render Queue");
                DrawProperty(Find("subjectMaxRenderQueue"), "最大 Render Queue");
                DrawProperty(Find("subjectMaskShader"), "Mask Shader");
            }
        }

        private static void DrawRuntimeStatus()
        {
            ScreenProcessRuntimeDiagnosticSnapshot snapshot = ScreenProcessRuntimeDiagnostics.CurrentSnapshot;
            string summary = snapshot.IsValid
                ? snapshot.WrittenLayerCount + "/" + snapshot.ActiveLayerCount + " 层"
                : "暂无帧";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行状态", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!snapshot.IsValid)
                {
                    EditorGUILayout.HelpBox("尚未记录 Ho-ScreenProcess 运行帧。进入 Play Mode，或让使用该 RendererFeature 的 Scene/Game camera 渲染一帧。", MessageType.Info);
                    return;
                }

                EditorGUILayout.LabelField("帧", snapshot.FrameCount.ToString());
                EditorGUILayout.LabelField("相机", snapshot.CameraName);
                EditorGUILayout.LabelField("阶段", snapshot.Stage);
                EditorGUILayout.LabelField("Active Layers", snapshot.ActiveLayerCount.ToString());
                EditorGUILayout.LabelField("Written Layers", snapshot.WrittenLayerCount.ToString());
                EditorGUILayout.LabelField("Active Back Buffer", snapshot.BackBufferActive ? "是" : "否");
                EditorGUILayout.LabelField("Camera Color", LilUrpEditorSectionGui.FormatAvailable(snapshot.CameraColorAvailable));
                DrawRequiredStatus("MetadataBuffer", snapshot.RequiresMetadataBuffer, snapshot.MetadataBufferAvailable);
                DrawRequiredStatus("GeometryBuffer", snapshot.RequiresGeometryBuffer, snapshot.GeometryBufferAvailable);
                DrawRequiredStatus("MaskId", snapshot.RequiresMaskId, snapshot.MaskIdAvailable);
                DrawRequiredStatus("SurfaceData", snapshot.RequiresSurfaceData, snapshot.SurfaceDataAvailable);
                DrawRequiredStatus("Custom0", snapshot.RequiresCustom0, snapshot.Custom0Available);
                DrawRequiredStatus("ObjectCustom0", snapshot.RequiresObjectCustom0, snapshot.ObjectCustom0Available);
                DrawRequiredStatus("ObjectCustom1", snapshot.RequiresObjectCustom1, snapshot.ObjectCustom1Available);
                DrawRequiredStatus("NormalDepth", snapshot.RequiresNormalDepth, snapshot.NormalDepthAvailable);

                EditorGUILayout.HelpBox(
                    snapshot.Ready
                        ? "ScreenProcess 输入有效：当前 layer 需要的 MetadataBuffer / GeometryBuffer 项均可用。"
                        : "ScreenProcess 已降级：" + snapshot.Reason,
                    snapshot.Ready ? MessageType.Info : MessageType.Warning);
            }
        }

        private static void DrawRequiredStatus(string label, bool required, bool available)
        {
            EditorGUILayout.LabelField(label, required ? LilUrpEditorSectionGui.FormatAvailable(available) : "未使用");
        }

        private SerializedProperty Find(string relativeName)
        {
            return settingsProperty?.FindPropertyRelative(relativeName);
        }

        private static void DrawProperty(SerializedProperty property, string label)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
            }
        }

        private static string FormatEnabled(SerializedProperty enabled)
        {
            return enabled != null && enabled.boolValue ? "开" : "关";
        }

        private string FormatUseVolumes()
        {
            return useVolumesProperty != null && useVolumesProperty.boolValue ? "Volume" : "手动";
        }

        private string GetQueueSummary()
        {
            SerializedProperty minQueue = Find("subjectMinRenderQueue");
            SerializedProperty maxQueue = Find("subjectMaxRenderQueue");
            return minQueue != null && maxQueue != null
                ? minQueue.intValue + "-" + maxQueue.intValue
                : "-";
        }
    }
}
