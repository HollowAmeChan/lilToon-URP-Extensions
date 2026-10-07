using System.Collections.Generic;
using lilToon.URP.Extensions.Editor;
using lilToon.URP.Extensions.Debugging;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.Debugging
{
    [CustomEditor(typeof(HoDebugTileRendererFeature))]
    internal sealed class HoDebugTileRendererFeatureEditor : UnityEditor.Editor
    {
        private static readonly string[] EmptyLabels = { "None" };
        private static readonly string[] EmptyIds = { HoDebugTileRendererFeature.NoneViewId };
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color ViewColor = new Color(0.42f, 0.72f, 0.58f);
        private static readonly Color RegistryColor = new Color(0.62f, 0.58f, 0.78f);
        private static readonly Color CheckColor = new Color(0.86f, 0.62f, 0.38f);

        private static bool showRuntime;
        private static bool showView;
        private static bool showRegistry;
        private static bool showCheck;

        private SerializedProperty enabledForGameView;
        private SerializedProperty enabledForSceneView;
        private SerializedProperty selectedDebugViewId;
        private string[] labels = EmptyLabels;
        private string[] ids = EmptyIds;
        private string lastDebugShaderMessage;
        private MessageType lastDebugShaderMessageType = MessageType.None;

        private void OnEnable()
        {
            enabledForGameView = serializedObject.FindProperty("enabledForGameView");
            enabledForSceneView = serializedObject.FindProperty("enabledForSceneView");
            selectedDebugViewId = serializedObject.FindProperty("selectedDebugViewId");
            RebuildDebugViewOptions();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawRuntime();
            DrawView();
            DrawRegistry();
            DrawDebugShaderTools();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRuntime()
        {
            string game = enabledForGameView != null && enabledForGameView.boolValue ? "G开" : "G关";
            string scene = enabledForSceneView != null && enabledForSceneView.boolValue ? "S开" : "S关";
            string summary = game + " / " + scene;

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(enabledForGameView);
                EditorGUILayout.PropertyField(enabledForSceneView);
            }
        }

        private void DrawView()
        {
            string summary = CurrentDebugViewLabel();
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showView, "Debug View", summary, ViewColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawDebugViewPopup();
                EditorGUILayout.HelpBox("Only external-system diagnostics (currently APV). Buffer and effect views use their own Volume debug modes and All grids. Presentation always runs after final post-processing.", MessageType.Info);
            }
        }

        private void DrawRegistry()
        {
            IReadOnlyList<HoDebugViewInfo> views = HoDebugViewRegistry.SystemViews;
            string summary = views.Count + " views";
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRegistry, "Registry", summary, RegistryColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("External-system views only. Feature-owned diagnostics are not dispatched by this renderer feature.", MessageType.None);
                string currentFeature = null;
                for (int i = 0; i < views.Count; i++)
                {
                    HoDebugViewInfo view = views[i];
                    if (currentFeature != view.FeatureName)
                    {
                        currentFeature = view.FeatureName;
                        EditorGUILayout.Space(3.0f);
                        EditorGUILayout.LabelField(currentFeature, EditorStyles.boldLabel);
                    }

                    EditorGUILayout.LabelField(view.ShortName + " / " + view.ViewId, BuildViewStatusText(view), EditorStyles.miniLabel);
                }
            }
        }

        private void DrawDebugShaderTools()
        {
            string summary = string.IsNullOrEmpty(lastDebugShaderMessage) ? "未检查" : lastDebugShaderMessageType.ToString();
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showCheck, "Shader Check", summary, CheckColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (GUILayout.Button("Validate Debug Shaders"))
                {
                    ValidateDebugShaders();
                }

                if (!string.IsNullOrEmpty(lastDebugShaderMessage))
                {
                    EditorGUILayout.HelpBox(lastDebugShaderMessage, lastDebugShaderMessageType);
                }
            }
        }

        private void ValidateDebugShaders()
        {
            try
            {
                LilUrpDebugShaderValidator.Result result = LilUrpDebugShaderValidator.Validate();
                lastDebugShaderMessage = result.ToSummary();
                lastDebugShaderMessageType = result.HasWarnings ? MessageType.Warning : MessageType.Info;
            }
            catch (System.Exception exception)
            {
                lastDebugShaderMessage = "Validate failed: " + exception.Message;
                lastDebugShaderMessageType = MessageType.Error;
                Debug.LogException(exception);
            }
        }

        private void DrawDebugViewPopup()
        {
            int selectedIndex = FindSelectedIndex(selectedDebugViewId.stringValue);
            bool migrated = !string.IsNullOrEmpty(selectedDebugViewId.stringValue)
                && selectedDebugViewId.stringValue != HoDebugTileRendererFeature.AllRegisteredViewId;
            foreach (var view in HoDebugViewRegistry.SystemViews)
                if (view.ViewId == selectedDebugViewId.stringValue) migrated = false;
            if (migrated)
                EditorGUILayout.HelpBox("该视图已迁回所属 Feature 的调试入口：" + selectedDebugViewId.stringValue, MessageType.Info);
            EditorGUI.BeginChangeCheck();
            int nextIndex = EditorGUILayout.Popup("Debug View", selectedIndex, labels);
            if (EditorGUI.EndChangeCheck()) selectedDebugViewId.stringValue = ids[nextIndex];
        }

        private int FindSelectedIndex(string value)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == value)
                {
                    return i;
                }
            }

            return 0;
        }

        private void RebuildDebugViewOptions()
        {
            IReadOnlyList<HoDebugViewInfo> views = HoDebugViewRegistry.SystemViews;
            List<string> labelList = new List<string>(views.Count + 2)
            {
                "None",
                "AllRegistered"
            };
            List<string> idList = new List<string>(views.Count + 2)
            {
                HoDebugTileRendererFeature.NoneViewId,
                HoDebugTileRendererFeature.AllRegisteredViewId
            };

            for (int i = 0; i < views.Count; i++)
            {
                HoDebugViewInfo view = views[i];
                labelList.Add(view.FeatureName + " / " + view.ShortName + " (" + view.ViewId + ")");
                idList.Add(view.ViewId);
            }

            labels = labelList.ToArray();
            ids = idList.ToArray();
        }

        private string CurrentDebugViewLabel()
        {
            int selectedIndex = FindSelectedIndex(selectedDebugViewId.stringValue);
            return labels != null && selectedIndex >= 0 && selectedIndex < labels.Length ? labels[selectedIndex] : "None";
        }

        private static string BuildViewStatusText(HoDebugViewInfo view)
        {
            string tileStatus = "external system";
            string shaderStatus = view.HasShader ? GetShaderAssetStatus(view.ShaderAssetPath) : "no shader";
            return "Mode " + view.ModeValue + " / " + tileStatus + " / " + shaderStatus;
        }

        private static string GetShaderAssetStatus(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return "none";
            }

            System.Type assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            if (assetType == null)
            {
                return "missing";
            }

            return assetType == typeof(Shader) ? "shader" : assetType.Name;
        }
    }
}
