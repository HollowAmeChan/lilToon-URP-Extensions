using lilToon.URP.Extensions.Editor;
using lilToon.URP.Extensions.PlanarReflection;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PlanarReflection
{
    [CustomEditor(typeof(HoPlanarReflectionRendererFeature))]
    internal sealed class HoPlanarReflectionRendererFeatureEditor : UnityEditor.Editor
    {
        private static readonly Color SettingsColor = new Color(0.45f, 0.64f, 0.96f);
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);

        private static bool showSettings;
        private static bool showRuntime;

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

            DrawSettings();
            DrawRuntimeStatus();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSettings()
        {
            SerializedProperty enabled = Find("enabled");
            string summary = LilUrpEditorSectionGui.BoolSummary(enabled);
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSettings, "RendererFeature", summary, SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(enabled, "启用");
                DrawProperty(Find("renderGameView"), "渲染 Game View");
                DrawProperty(Find("renderSceneView"), "渲染 Scene View");
                DrawProperty(Find("maxSurfacesPerCamera"), "每相机最大表面数");
                EditorGUILayout.HelpBox("当前版本仍使用镜像 Camera 渲染反射纹理；RendererFeature 负责统一开关、调度与运行状态。", MessageType.Info);
            }
        }

        private static void DrawRuntimeStatus()
        {
            HoPlanarReflectionRuntimeDiagnosticSnapshot snapshot = HoPlanarReflectionRuntimeDiagnostics.CurrentSnapshot;
            string summary = snapshot.IsValid
                ? snapshot.ActiveSurfaceCount + "/" + snapshot.SurfaceCount + " 面"
                : "暂无帧";
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行状态", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!snapshot.IsValid)
                {
                    EditorGUILayout.HelpBox("尚未记录 Ho-PlanarReflection 运行帧。添加 RendererFeature 后让 Scene/Game camera 渲染一帧。", MessageType.Info);
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
    }
}
