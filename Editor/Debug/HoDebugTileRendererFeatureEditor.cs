using System.Collections.Generic;
using lilToon.URP.Extensions.Debugging;
using lilToon.URP.Extensions.Editor;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.Debugging
{
    [CustomEditor(typeof(HoDebugTileRendererFeature))]
    internal sealed class HoDebugTileRendererFeatureEditor : UnityEditor.Editor
    {
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color RegistryColor = new Color(0.62f, 0.58f, 0.78f);
        private static bool showRuntime = true;
        private static bool showRegistry = true;
        private SerializedProperty enabled;
        private SerializedProperty enabledForGameView;
        private SerializedProperty enabledForSceneView;
        private SerializedProperty performanceMode;

        private void OnEnable()
        {
            enabled = serializedObject.FindProperty("enabled");
            enabledForGameView = serializedObject.FindProperty("enabledForGameView");
            enabledForSceneView = serializedObject.FindProperty("enabledForSceneView");
            performanceMode = serializedObject.FindProperty("performanceMode");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawRuntime();
            DrawRegistry();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRuntime()
        {
            string game = enabledForGameView != null && enabledForGameView.boolValue ? "G开" : "G关";
            string scene = enabledForSceneView != null && enabledForSceneView.boolValue ? "S开" : "S关";
            string summary = game + " / " + scene;
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "Ho-DebugPerformance", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(enabled, new GUIContent("启用"));
                EditorGUILayout.PropertyField(enabledForGameView, new GUIContent("游戏视图"));
                EditorGUILayout.PropertyField(enabledForSceneView, new GUIContent("场景视图"));
                EditorGUILayout.PropertyField(performanceMode, new GUIContent("性能信息"));
                EditorGUILayout.HelpBox(
                    "这是 Ho 渲染体系专用的轻量性能 HUD。它只在最终颜色之后叠加文字，不替代各 Feature 自己的调试视图。",
                    MessageType.None);
            }
        }

        private void DrawRegistry()
        {
            HoDebugPerformanceRegistry.EnsureInitialized();
            IReadOnlyList<HoDebugPerformanceInfo> entries = HoDebugPerformanceRegistry.All;
            string summary = entries.Count + " features";
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRegistry, "Performance Registry", summary, RegistryColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    HoDebugPerformanceInfo entry = entries[i];
                    EditorGUILayout.LabelField(entry.FeatureName, entry.DisplayName, EditorStyles.miniLabel);
                }

                EditorGUILayout.HelpBox(
                    "新 Feature 需要声明 HoDebugPerformanceInfo 和共享计时 marker，并通过 EnqueuePass 登记本相机的执行顺序；HUD 仅显示本相机参与渲染的 Feature。",
                    MessageType.None);
            }
        }
    }
}
