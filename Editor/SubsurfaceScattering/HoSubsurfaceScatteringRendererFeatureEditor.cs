using lilToon.URP.Extensions.SubsurfaceScattering;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.SubsurfaceScattering
{
    [CustomEditor(typeof(HoSubsurfaceScatteringRendererFeature))]
    internal sealed class HoSubsurfaceScatteringRendererFeatureEditor : UnityEditor.Editor
    {
        private const float SectionHeaderHeight = 30.0f;

        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color ProfileColor = new Color(0.42f, 0.72f, 0.58f);
        private static readonly Color DebugColor = new Color(0.86f, 0.62f, 0.38f);
        private static readonly Color AdvancedColor = new Color(0.62f, 0.58f, 0.78f);

        private static bool showRuntime = true;
        private static bool showProfiles = true;
        private static bool showDebug;
        private static bool showAdvanced;
        private static GUIStyle sectionTitleStyle;
        private static GUIStyle sectionSummaryStyle;

        private SerializedProperty settingsProperty;

        private static GUIStyle SectionTitleStyle
        {
            get
            {
                if (sectionTitleStyle == null)
                {
                    sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        clipping = TextClipping.Clip
                    };
                }

                sectionTitleStyle.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : new Color(0.12f, 0.12f, 0.12f);
                return sectionTitleStyle;
            }
        }

        private static GUIStyle SectionSummaryStyle
        {
            get
            {
                if (sectionSummaryStyle == null)
                {
                    sectionSummaryStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        clipping = TextClipping.Clip
                    };
                }

                sectionSummaryStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.86f, 0.88f, 0.90f) : new Color(0.22f, 0.22f, 0.22f);
                return sectionSummaryStyle;
            }
        }

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
                "HoSSS consumes MetadataBuffer Base Color as diffuse input. Material/profile settings control diffusion tint, radius, thickness, and transmission.",
                MessageType.Info);

            DrawRuntime();
            DrawProfiles();
            DrawDebug();
            DrawAdvanced();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRuntime()
        {
            SerializedProperty enabled = Find("enabled");
            SerializedProperty renderScale = Find("renderScale");
            SerializedProperty quality = Find("quality");
            SerializedProperty strength = Find("strength");
            string summary = enabled != null && enabled.boolValue
                ? $"强度 {FormatFloat(strength)} / {EnumName(renderScale)} / {EnumName(quality)}"
                : "已关闭";

            if (!DrawSectionHeader(ref showRuntime, "运行", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(enabled);
                DrawProperty(renderScale);
                DrawProperty(quality);
                DrawProperty(strength);
                DrawRuntimeDiagnostics();
            }
        }

        private static void DrawRuntimeDiagnostics()
        {
            HoSubsurfaceScatteringRuntimeDiagnosticSnapshot snapshot = HoSubsurfaceScatteringRuntimeDiagnostics.CurrentSnapshot;
            EditorGUILayout.Space(4.0f);
            EditorGUILayout.LabelField("运行状态", EditorStyles.boldLabel);

            if (!snapshot.IsValid)
            {
                EditorGUILayout.HelpBox("尚未记录 HoSSS 运行帧。进入 Play Mode，或让使用该 RendererFeature 的 Scene/Game camera 渲染一帧。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("帧", snapshot.FrameCount.ToString());
            EditorGUILayout.LabelField("相机", snapshot.CameraName);
            EditorGUILayout.LabelField("阶段", snapshot.Stage);
            EditorGUILayout.LabelField("Camera Color", FormatAvailable(snapshot.CameraColorAvailable));
            EditorGUILayout.LabelField("MetadataBuffer", FormatAvailable(snapshot.MetadataBufferAvailable));
            EditorGUILayout.LabelField("GeometryBuffer", FormatAvailable(snapshot.GeometryBufferAvailable));

            EditorGUILayout.HelpBox(
                snapshot.Ready
                    ? "HoSSS 输入有效：MetadataBuffer 与 GeometryBuffer 均可用。"
                    : "HoSSS 已跳过：" + snapshot.Reason,
                snapshot.Ready ? MessageType.Info : MessageType.Warning);
        }

        private void DrawProfiles()
        {
            SerializedProperty profiles = Find("profiles");
            string summary = $"{CountEnabledProfiles(profiles)} 个启用";

            if (!DrawSectionHeader(ref showProfiles, "Diffusion Profiles", summary, ProfileColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(profiles, includeChildren: true);
                EditorGUILayout.HelpBox("皮肤外观优先调 profile：扩散颜色、扩散半径、源色保留、厚度倍率。RendererFeature 的全局颜色/半径只作为未命中 profile 的兼容回退。", MessageType.None);
            }
        }

        private void DrawDebug()
        {
            SerializedProperty debugMode = Find("debugMode");
            string summary = EnumName(debugMode);

            if (!DrawSectionHeader(ref showDebug, "调试", summary, DebugColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(debugMode);
                DrawProperty("renderInSceneView");
                EditorGUILayout.HelpBox("Debug shader 只在调试模式非关闭时按需查找。若要把它纳入构建，请显式生成 Debug Shader Collection。", MessageType.None);
            }
        }

        private void DrawAdvanced()
        {
            if (!DrawSectionHeader(ref showAdvanced, "高级/兼容", "时机、fallback、透射补偿", AdvancedColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("调度", EditorStyles.boldLabel);
                DrawProperty("sourcePassEvent");
                DrawProperty("compositePassEvent");
                DrawProperty("shader");

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Profile 未命中回退", EditorStyles.boldLabel);
                DrawProperty("radius");
                DrawProperty("color");
                DrawProperty("sourcePreserve");
                DrawProperty("depthTolerance");
                DrawProperty("normalTolerance");

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("透射补偿", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("透射只用于耳缘、鼻翼、指尖等薄处暖边。主要皮肤柔和感应来自 Diffusion Profiles。", MessageType.None);
                DrawProperty("transmissionStrength");
                DrawProperty("transmissionRadius");
                DrawProperty("transmissionSamples");
                DrawProperty("transmissionMainLightDirection");
                DrawProperty("transmissionDepthWeight");
                DrawProperty("transmissionEdgeBoost");
                DrawProperty("transmissionRimWeight");
                DrawProperty("transmissionBlendMode");
                DrawProperty("transmissionTintInjection");
                DrawProperty("transmissionSmoothing");
                DrawProperty("transmissionColor");
            }
        }

        private void DrawProperty(string relativeName, bool includeChildren = false)
        {
            DrawProperty(Find(relativeName), includeChildren);
        }

        private SerializedProperty Find(string relativeName)
        {
            return settingsProperty?.FindPropertyRelative(relativeName);
        }

        private static void DrawProperty(SerializedProperty property, bool includeChildren = false)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, includeChildren);
            }
        }

        private static string FormatFloat(SerializedProperty property)
        {
            return property != null ? property.floatValue.ToString("0.##") : "-";
        }

        private static string EnumName(SerializedProperty property)
        {
            if (property == null || property.propertyType != SerializedPropertyType.Enum)
            {
                return "-";
            }

            int index = Mathf.Clamp(property.enumValueIndex, 0, property.enumDisplayNames.Length - 1);
            return property.enumDisplayNames[index];
        }

        private static int CountEnabledProfiles(SerializedProperty profiles)
        {
            if (profiles == null || !profiles.isArray)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < profiles.arraySize; i++)
            {
                SerializedProperty profile = profiles.GetArrayElementAtIndex(i);
                SerializedProperty enabled = profile?.FindPropertyRelative("enabled");
                if (enabled != null && enabled.boolValue)
                {
                    count++;
                }
            }

            return count;
        }

        private static string FormatAvailable(bool available)
        {
            return available ? "可用" : "缺失";
        }

        private static bool DrawSectionHeader(ref bool expanded, string title, string summary, Color color)
        {
            EditorGUILayout.Space(5.0f);
            Rect rect = EditorGUILayout.GetControlRect(false, SectionHeaderHeight);
            Event evt = Event.current;
            bool hover = rect.Contains(evt.mousePosition);

            EditorGUI.DrawRect(rect, GetSectionColor(color, hover));

            Rect foldoutRect = new Rect(rect.x + 6.0f, rect.y + 7.0f, 16.0f, EditorGUIUtility.singleLineHeight);
            expanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);

            Rect titleRect = new Rect(rect.x + 26.0f, rect.y + 6.0f, Mathf.Max(90.0f, rect.width * 0.48f), 20.0f);
            GUI.Label(titleRect, title, SectionTitleStyle);

            Rect summaryRect = new Rect(rect.x + rect.width * 0.48f, rect.y + 7.0f, rect.width * 0.52f - 10.0f, 18.0f);
            GUI.Label(summaryRect, summary, SectionSummaryStyle);

            if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition) && !foldoutRect.Contains(evt.mousePosition))
            {
                expanded = !expanded;
                evt.Use();
            }

            return expanded;
        }

        private static Color GetSectionColor(Color baseColor, bool hover)
        {
            Color neutral = EditorGUIUtility.isProSkin
                ? new Color(0.20f, 0.21f, 0.23f)
                : new Color(0.78f, 0.80f, 0.83f);
            Color color = Color.Lerp(neutral, baseColor, EditorGUIUtility.isProSkin ? 0.42f : 0.35f);
            if (hover)
            {
                color = Color.Lerp(color, Color.white, EditorGUIUtility.isProSkin ? 0.10f : 0.18f);
            }

            return color;
        }

    }
}
