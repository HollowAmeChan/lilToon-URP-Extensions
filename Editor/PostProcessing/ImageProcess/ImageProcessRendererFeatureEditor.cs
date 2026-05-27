using lilToon.URP.Extensions.Editor;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    [CustomEditor(typeof(ImageProcessRendererFeature))]
    internal sealed class ImageProcessRendererFeatureEditor : UnityEditor.Editor
    {
        private static readonly Color SettingsColor = new Color(0.45f, 0.64f, 0.96f);
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);

        private static bool showSettings;
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

        private static void DrawRuntimeStatus()
        {
            ImageProcessRuntimeDiagnosticSnapshot snapshot = ImageProcessRuntimeDiagnostics.CurrentSnapshot;
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
                    EditorGUILayout.HelpBox("尚未记录 Ho-ImageProcess 运行帧。进入 Play Mode，或让使用该 RendererFeature 的 Scene/Game camera 渲染一帧。", MessageType.Info);
                    return;
                }

                EditorGUILayout.LabelField("帧", snapshot.FrameCount.ToString());
                EditorGUILayout.LabelField("相机", snapshot.CameraName);
                EditorGUILayout.LabelField("阶段", snapshot.Stage);
                EditorGUILayout.LabelField("Active Layers", snapshot.ActiveLayerCount.ToString());
                EditorGUILayout.LabelField("Written Layers", snapshot.WrittenLayerCount.ToString());
                EditorGUILayout.LabelField("Active Back Buffer", snapshot.BackBufferActive ? "是" : "否");
                EditorGUILayout.LabelField("Camera Color", LilUrpEditorSectionGui.FormatAvailable(snapshot.CameraColorAvailable));

                EditorGUILayout.HelpBox(
                    snapshot.Ready
                        ? "ImageProcess 输入有效：当前 layer 已写入 camera color。"
                        : "ImageProcess 已跳过：" + snapshot.Reason,
                    snapshot.Ready ? MessageType.Info : MessageType.Warning);
            }
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
    }
}
