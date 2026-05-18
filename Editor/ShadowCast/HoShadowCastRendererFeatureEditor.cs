using lilToon.URP.Extensions.ShadowCast;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.ShadowCast
{
    [CustomEditor(typeof(HoShadowCastRendererFeature))]
    internal sealed class HoShadowCastRendererFeatureEditor : UnityEditor.Editor
    {
        private const int BeforeRenderingPrePassesValue = 150;
        private const float SectionHeaderHeight = 30.0f;

        private static readonly Color SettingsColor = new Color(0.45f, 0.64f, 0.96f);
        private static readonly Color ControllerColor = new Color(0.38f, 0.76f, 0.55f);

        private static bool showSettings = true;
        private static bool showController = true;
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

            EditorGUILayout.HelpBox(
                "这个 RendererFeature 只负责把 HoShadowCast shadow atlas pass 装进 URP Renderer。光源选择请放在场景里的 HoShadowCastController 上。",
                MessageType.Info);

            if (settingsProperty == null)
            {
                DrawDefaultInspector();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawSettings();
            DrawControllerStatus();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSettings()
        {
            SerializedProperty enabled = settingsProperty.FindPropertyRelative("enabled");
            SerializedProperty passEvent = settingsProperty.FindPropertyRelative("passEvent");
            string summary = enabled != null && enabled.boolValue ? "已启用" : "已关闭";

            if (!DrawSectionHeader(ref showSettings, "RendererFeature", summary, SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(enabled, "启用");
                DrawProperty(passEvent, "渲染时机");
                if (passEvent != null && passEvent.intValue < BeforeRenderingPrePassesValue)
                {
                    EditorGUILayout.HelpBox("HoShadowCast 不应插入到 URP 内置阴影阶段之前。运行时会钳制到 BeforeRenderingPrePasses。", MessageType.Info);
                }
            }
        }

        private void DrawControllerStatus()
        {
            HoShadowCastController controller = HoShadowCastController.ActiveController;
            string summary = controller != null ? controller.name : "未找到";

            if (!DrawSectionHeader(ref showController, "场景控制器", summary, ControllerColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (controller == null)
                {
                    EditorGUILayout.HelpBox("当前场景没有启用的 HoShadowCastController。需要在空物体上添加一个控制器后，本 pass 才会输出 atlas。", MessageType.Warning);

                    if (GUILayout.Button("创建 HoShadowCastController"))
                    {
                        CreateController();
                    }
                }
                else
                {
                    EditorGUILayout.ObjectField("活动控制器", controller, typeof(HoShadowCastController), true);
                    EditorGUILayout.LabelField("Atlas 尺寸", controller.atlasSize.ToString());
                }
            }
        }

        private static void CreateController()
        {
            GameObject go = new GameObject("HoShadowCast Controller");
            Undo.RegisterCreatedObjectUndo(go, "Create HoShadowCast Controller");
            go.AddComponent<HoShadowCastController>();
            Selection.activeGameObject = go;
        }

        private static void DrawProperty(SerializedProperty property, string label)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
            }
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

            Rect titleRect = new Rect(rect.x + 26.0f, rect.y + 6.0f, Mathf.Max(80.0f, rect.width * 0.45f), 20.0f);
            GUI.Label(titleRect, title, SectionTitleStyle);

            Rect summaryRect = new Rect(rect.x + rect.width * 0.45f, rect.y + 7.0f, rect.width * 0.55f - 10.0f, 18.0f);
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
                ? new Color(0.16f, 0.17f, 0.18f)
                : new Color(0.93f, 0.93f, 0.93f);
            float strength = hover ? 0.42f : 0.34f;
            Color result = Color.Lerp(neutral, baseColor, strength);
            result.a = 1.0f;
            return result;
        }
    }
}
