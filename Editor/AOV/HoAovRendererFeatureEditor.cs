using lilToon.URP.Extensions.AOV;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.AOV
{
    [CustomEditor(typeof(HoAovRendererFeature))]
    internal sealed class HoAovRendererFeatureEditor : UnityEditor.Editor
    {
        private static bool showAdvancedSettings;
        private static bool showCustomChannels;
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
                "HoAOV 是数据层 RendererFeature。第一次确认是否跑通时，请把“调试模式”设为“遮罩”，并确保“场景视图显示调试”已开启。ID 和 Custom 通道需要 HoAovSubject 写入数据后才明显。",
                MessageType.Info);

            DrawSettings();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSettings()
        {
            DrawProperty("enabled");
            DrawProperty("layerMask");
            DrawProperty("minRenderQueue");
            DrawProperty("maxRenderQueue");
            DrawProperty("renderScale");
            DrawProperty("systemChannels");
            DrawProperty("customChannelCount");

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("调试预览", EditorStyles.boldLabel);
            DrawProperty("debugMode");
            DrawProperty("debugInSceneView");
            DrawProperty("debugInGameView");
            DrawProperty("debugDepthNear");
            DrawProperty("debugDepthFar");
            DrawDebugInteractionNotice();

            EditorGUILayout.Space(6);
            showCustomChannels = EditorGUILayout.Foldout(showCustomChannels, "自定义通道名称/颜色", true);
            if (showCustomChannels)
            {
                EditorGUI.indentLevel++;
                DrawProperty("customChannelNames", true);
                DrawProperty("customChannelColors", true);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(6);
            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "高级设置", true);
            if (showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                DrawProperty("aovPassEvent");
                DrawProperty("debugPassEvent");
                DrawProperty("useFallbackMaterial");
                DrawProperty("fallbackShader");
                DrawProperty("debugShader");
                EditorGUI.indentLevel--;
            }
        }

        private void DrawProperty(string relativeName, bool includeChildren = false)
        {
            SerializedProperty property = settingsProperty.FindPropertyRelative(relativeName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, includeChildren);
            }
        }

        private void DrawDebugInteractionNotice()
        {
            SerializedProperty debugMode = settingsProperty.FindPropertyRelative("debugMode");
            if (debugMode == null || debugMode.enumValueIndex == (int)HoAovDebugMode.Off)
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "AOV 调试预览会写到当前视图颜色上。如果 HoPost 或 ShoostStack 也在 Scene View 生效，它们会继续处理这个调试画面，颜色可能被滤镜影响。检查原始 AOV 时，建议临时关闭 Shoost/HoPost 的 Scene View 显示或把 HoAOV 调试放在最后。",
                MessageType.Info);
        }
    }
}
