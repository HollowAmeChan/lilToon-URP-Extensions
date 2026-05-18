using lilToon.URP.Extensions.ShadowCast;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.ShadowCast
{
    [CustomEditor(typeof(HoShadowCastController))]
    internal sealed class HoShadowCastControllerEditor : UnityEditor.Editor
    {
        private const float SectionHeaderHeight = 30.0f;

        private static readonly Color DirectionalColor = new Color(0.45f, 0.64f, 0.96f);
        private static readonly Color SpotColor = new Color(0.55f, 0.48f, 0.90f);
        private static readonly Color PointColor = new Color(0.38f, 0.76f, 0.55f);
        private static readonly Color AtlasColor = new Color(0.30f, 0.72f, 0.78f);
        private static readonly Color StatusColor = new Color(0.70f, 0.70f, 0.70f);

        private static bool showDirectionalLights = true;
        private static bool showSpotLights = true;
        private static bool showPointLights = true;
        private static bool showAtlasSettings = true;
        private static bool showStatus = true;

        private static GUIStyle sectionTitleStyle;
        private static GUIStyle sectionSummaryStyle;

        private SerializedProperty priorityProperty;
        private SerializedProperty directionalLightsProperty;
        private SerializedProperty spotLightsProperty;
        private SerializedProperty pointLightsProperty;
        private SerializedProperty casterLayerMaskProperty;
        private SerializedProperty shadowStrengthProperty;
        private SerializedProperty atlasSizeProperty;
        private SerializedProperty directionalResolutionProperty;
        private SerializedProperty spotResolutionProperty;
        private SerializedProperty pointFaceResolutionProperty;
        private SerializedProperty directionalNearPlaneProperty;
        private SerializedProperty debugModeProperty;

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
            priorityProperty = serializedObject.FindProperty("priority");
            directionalLightsProperty = serializedObject.FindProperty("directionalLights");
            spotLightsProperty = serializedObject.FindProperty("spotLights");
            pointLightsProperty = serializedObject.FindProperty("pointLights");
            casterLayerMaskProperty = serializedObject.FindProperty("casterLayerMask");
            shadowStrengthProperty = serializedObject.FindProperty("shadowStrength");
            atlasSizeProperty = serializedObject.FindProperty("atlasSize");
            directionalResolutionProperty = serializedObject.FindProperty("directionalResolution");
            spotResolutionProperty = serializedObject.FindProperty("spotResolution");
            pointFaceResolutionProperty = serializedObject.FindProperty("pointFaceResolution");
            directionalNearPlaneProperty = serializedObject.FindProperty("directionalNearPlane");
            debugModeProperty = serializedObject.FindProperty("debugMode");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            HoShadowCastController controller = (HoShadowCastController)target;
            DrawHeader(controller);
            DrawLightSection(
                directionalLightsProperty,
                directionalResolutionProperty,
                "额外方向光",
                "每个方向光 1 个 shadow slice。不要把 URP 主光放进来；运行时如果发现某个灯就是当前 URP mainLightIndex，会自动跳过。",
                LightType.Directional,
                DirectionalColor,
                ref showDirectionalLights);
            DrawLightSection(
                spotLightsProperty,
                spotResolutionProperty,
                "额外聚光",
                "每个聚光 1 个 shadow slice",
                LightType.Spot,
                SpotColor,
                ref showSpotLights);
            DrawLightSection(
                pointLightsProperty,
                pointFaceResolutionProperty,
                "额外点光",
                "每个点光 6 个 cube face slice",
                LightType.Point,
                PointColor,
                ref showPointLights);
            DrawAtlasSection();
            DrawStatus();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHeader(HoShadowCastController controller)
        {
            EditorGUILayout.HelpBox(
                "HoShadowCast 是项目级额外投影光源列表。把它挂在空物体上，集中指定主光之外仍需写入 Ho shadow atlas 的普通 Unity Light。",
                MessageType.Info);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(priorityProperty, new GUIContent("优先级"));

                bool isActive = HoShadowCastController.ActiveController == controller;
                EditorGUILayout.LabelField("当前状态", isActive ? "正在作为活动控制器" : "未被选为活动控制器");
            }
        }

        private void DrawLightSection(
            SerializedProperty arrayProperty,
            SerializedProperty resolutionProperty,
            string label,
            string hint,
            LightType expectedType,
            Color color,
            ref bool expanded)
        {
            int assignedCount = CountAssigned(arrayProperty);
            string summary = string.Format("{0}/{1}  |  分辨率 {2}", assignedCount, arrayProperty.arraySize, resolutionProperty.intValue);
            if (!DrawSectionHeader(ref expanded, label, summary, color))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(resolutionProperty, new GUIContent(expectedType == LightType.Point ? "单面分辨率" : "分辨率"));
                EditorGUILayout.HelpBox(hint, MessageType.None);

                EditorGUILayout.Space(2.0f);
                EditorGUI.indentLevel++;
                for (int i = 0; i < arrayProperty.arraySize; i++)
                {
                    SerializedProperty element = arrayProperty.GetArrayElementAtIndex(i);
                    EditorGUILayout.PropertyField(element, new GUIContent(string.Format("{0} {1}", label, i + 1)));

                    Light light = element.objectReferenceValue as Light;
                    if (light != null && light.type != expectedType)
                    {
                        EditorGUILayout.HelpBox(string.Format("{0} 需要 {1}，当前是 {2}。", label, expectedType, light.type), MessageType.Warning);
                    }
                    else if (light != null && expectedType == LightType.Directional && RenderSettings.sun == light)
                    {
                        EditorGUILayout.HelpBox("这个方向光当前是 RenderSettings.sun，通常会被 URP 当作主光。HoShadowCast 运行时会跳过 URP 主光，建议不要放在额外投影列表里。", MessageType.Warning);
                    }
                }
                EditorGUI.indentLevel--;
            }
        }

        private void DrawAtlasSection()
        {
            string summary = string.Format("{0}px  |  强度 {1:0.##}", atlasSizeProperty.intValue, shadowStrengthProperty.floatValue);
            if (!DrawSectionHeader(ref showAtlasSettings, "Atlas 与调试", summary, AtlasColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(shadowStrengthProperty, new GUIContent("投影强度"));
                EditorGUILayout.PropertyField(atlasSizeProperty, new GUIContent("Atlas 尺寸"));
                EditorGUILayout.PropertyField(directionalNearPlaneProperty, new GUIContent("方向光近裁剪"));

                EditorGUILayout.Space(2.0f);
                EditorGUILayout.PropertyField(casterLayerMaskProperty, new GUIContent("Caster 图层遮罩（预留）"));
                EditorGUILayout.HelpBox("第一版尚未用这个遮罩重做 shadow caster culling；它是后续 caster scope / 区域约束的入口。", MessageType.None);

                EditorGUILayout.Space(2.0f);
                EditorGUILayout.PropertyField(debugModeProperty, new GUIContent("调试模式"));
            }
        }

        private void DrawStatus()
        {
            int lightCount =
                CountAssigned(directionalLightsProperty) +
                CountAssigned(spotLightsProperty) +
                CountAssigned(pointLightsProperty);

            if (!DrawSectionHeader(ref showStatus, "输出概览", string.Format("已指定 {0} 个光源", lightCount), StatusColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("最大容量", "4 额外方向光 / 4 额外聚光 / 4 额外点光，最多 32 个 shadow slice");
                EditorGUILayout.LabelField("主光处理", "运行时跳过 URP 当前 mainLightIndex");
                EditorGUILayout.LabelField("材质接入", "使用材质自己的 ShadowCaster pass");
                EditorGUILayout.LabelField("后续消费", "_HoShadowCastAtlas 与固定数组全局 light data");
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

        private static int CountAssigned(SerializedProperty arrayProperty)
        {
            if (arrayProperty == null || !arrayProperty.isArray)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < arrayProperty.arraySize; i++)
            {
                if (arrayProperty.GetArrayElementAtIndex(i).objectReferenceValue != null)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
