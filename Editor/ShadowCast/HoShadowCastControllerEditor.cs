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
        private static readonly Color DebugColor = new Color(0.86f, 0.68f, 0.34f);
        private static readonly Color StatusColor = new Color(0.70f, 0.70f, 0.70f);

        private static bool showDirectionalLights = true;
        private static bool showSpotLights = true;
        private static bool showPointLights = true;
        private static bool showAtlasSettings = true;
        private static bool showSecondDirectionalSettings = true;
        private static bool showStatus = true;
        private static bool showDebugSettings = true;

        private static GUIStyle sectionTitleStyle;
        private static GUIStyle sectionSummaryStyle;

        private SerializedProperty priorityProperty;
        private SerializedProperty directionalLightsProperty;
        private SerializedProperty spotLightsProperty;
        private SerializedProperty pointLightsProperty;
        private SerializedProperty casterLayerMaskProperty;
        private SerializedProperty punctualShadowStrengthProperty;
        private SerializedProperty punctualShadowFadeSpeedProperty;
        private SerializedProperty secondDirectionalShadowStrengthProperty;
        private SerializedProperty secondDirectionalAtlasSizeProperty;
        private SerializedProperty secondDirectionalCascadeCountProperty;
        private SerializedProperty secondDirectionalMaxDistanceProperty;
        private SerializedProperty secondDirectionalShadowDepthProperty;
        private SerializedProperty secondDirectionalCascadeSplitsProperty;
        private SerializedProperty atlasSizeProperty;
        private SerializedProperty directionalResolutionProperty;
        private SerializedProperty spotResolutionProperty;
        private SerializedProperty pointFaceResolutionProperty;
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
            punctualShadowStrengthProperty = serializedObject.FindProperty("punctualShadowStrength");
            punctualShadowFadeSpeedProperty = serializedObject.FindProperty("punctualShadowFadeSpeed");
            secondDirectionalShadowStrengthProperty = serializedObject.FindProperty("secondDirectionalShadowStrength");
            secondDirectionalAtlasSizeProperty = serializedObject.FindProperty("secondDirectionalAtlasSize");
            secondDirectionalCascadeCountProperty = serializedObject.FindProperty("secondDirectionalCascadeCount");
            secondDirectionalMaxDistanceProperty = serializedObject.FindProperty("secondDirectionalMaxDistance");
            secondDirectionalShadowDepthProperty = serializedObject.FindProperty("secondDirectionalShadowDepth");
            secondDirectionalCascadeSplitsProperty = serializedObject.FindProperty("secondDirectionalCascadeSplits");
            atlasSizeProperty = serializedObject.FindProperty("atlasSize");
            directionalResolutionProperty = serializedObject.FindProperty("directionalResolution");
            spotResolutionProperty = serializedObject.FindProperty("spotResolution");
            pointFaceResolutionProperty = serializedObject.FindProperty("pointFaceResolution");
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
                LightType.Directional,
                DirectionalColor,
                ref showDirectionalLights);
            DrawLightSection(
                spotLightsProperty,
                spotResolutionProperty,
                "额外聚光",
                LightType.Spot,
                SpotColor,
                ref showSpotLights);
            DrawLightSection(
                pointLightsProperty,
                pointFaceResolutionProperty,
                "额外点光",
                LightType.Point,
                PointColor,
                ref showPointLights);
            DrawAtlasSection();
            DrawSecondDirectionalSection();
            DrawStatus();
            DrawDebugSection();

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
            LightType expectedType,
            Color color,
            ref bool expanded)
        {
            int assignedCount = CountAssigned(arrayProperty);
            string summary = expectedType == LightType.Directional
                ? string.Format("{0}/{1}  |  级联 atlas", assignedCount, arrayProperty.arraySize)
                : string.Format("{0}/{1}  |  分辨率 {2}", assignedCount, arrayProperty.arraySize, resolutionProperty.intValue);
            if (!DrawSectionHeader(ref expanded, label, summary, color))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (expectedType != LightType.Directional)
                {
                    EditorGUILayout.PropertyField(resolutionProperty, new GUIContent(expectedType == LightType.Point ? "单面分辨率" : "分辨率"));
                    DrawResolutionCapacityWarning(resolutionProperty, expectedType);
                }
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
            string summary = string.Format("{0}px  |  点聚 {1:0.##}", atlasSizeProperty.intValue, punctualShadowStrengthProperty.floatValue);
            if (!DrawSectionHeader(ref showAtlasSettings, "普通 Atlas", summary, AtlasColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(punctualShadowStrengthProperty, new GUIContent("点光/聚光投影强度"));
                EditorGUILayout.PropertyField(punctualShadowFadeSpeedProperty, new GUIContent("点光/聚光范围衰减速度", "1 保持旧曲线；更大更快淡出，更小保留更久。"));
                EditorGUILayout.PropertyField(atlasSizeProperty, new GUIContent("Atlas 尺寸"));
                DrawAtlasCapacitySummary();

                EditorGUILayout.Space(2.0f);
                EditorGUILayout.PropertyField(casterLayerMaskProperty, new GUIContent("Caster 图层遮罩"));
                EditorGUILayout.HelpBox("生成 HoShadowCast atlas 时只绘制这个图层范围内、拥有 ShadowCaster pass 的投射物；接收阴影的材质不受这个遮罩影响。", MessageType.None);
            }
        }

        private void DrawSecondDirectionalSection()
        {
            string summary = string.Format(
                "{0}/{1} lights | {2} cascades | {3}px",
                CountAssigned(directionalLightsProperty),
                directionalLightsProperty.arraySize,
                secondDirectionalCascadeCountProperty.intValue,
                secondDirectionalAtlasSizeProperty.intValue);
            if (!DrawSectionHeader(ref showSecondDirectionalSettings, "第二天光级联", summary, DirectionalColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(secondDirectionalShadowStrengthProperty, new GUIContent("投影强度"));
                EditorGUILayout.PropertyField(secondDirectionalAtlasSizeProperty, new GUIContent("Atlas 尺寸"));
                EditorGUILayout.PropertyField(secondDirectionalCascadeCountProperty, new GUIContent("级联数"));
                EditorGUILayout.PropertyField(secondDirectionalMaxDistanceProperty, new GUIContent("最大距离"));
                EditorGUILayout.PropertyField(secondDirectionalShadowDepthProperty, new GUIContent("投影深度"));
                EditorGUILayout.PropertyField(secondDirectionalCascadeSplitsProperty, new GUIContent("级联分割"));
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
                EditorGUILayout.LabelField("最大容量", "4 级联方向光 / 4 聚光 / 4 点光");
                EditorGUILayout.LabelField("主光处理", "运行时跳过 URP 当前 mainLightIndex");
                EditorGUILayout.LabelField("Atlas", "普通点聚 atlas + 独立第二天光 atlas");
            }
        }

        private void DrawDebugSection()
        {
            string summary = ((HoShadowCastDebugMode)debugModeProperty.enumValueIndex).ToString();
            if (!DrawSectionHeader(ref showDebugSettings, "调试", summary, DebugColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(debugModeProperty, new GUIContent("调试模式"));
                if (debugModeProperty.enumValueIndex == (int)HoShadowCastDebugMode.Atlas)
                {
                    EditorGUILayout.HelpBox("显示普通 _HoShadowCastAtlas。若仍显示正常画面，通常说明本帧没有生成 point/spot slice。", MessageType.Info);
                }
                else if (debugModeProperty.enumValueIndex == (int)HoShadowCastDebugMode.SecondDirectionalAtlas)
                {
                    EditorGUILayout.HelpBox("显示 _HoShadowCastSecondDirectionalAtlas。若仍显示正常画面，通常说明额外方向光槽为空、方向光被识别为 URP 主光，或本帧没有生成 cascade。", MessageType.Info);
                }
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

        private void DrawResolutionCapacityWarning(SerializedProperty resolutionProperty, LightType lightType)
        {
            int requestedSliceCount = CountRequestedSlices();
            int maxResolution = GetMaxResolutionForSliceCount(atlasSizeProperty.intValue, requestedSliceCount);
            if (resolutionProperty.intValue <= maxResolution)
            {
                return;
            }

            string lightLabel = lightType == LightType.Point ? "点光单面" : lightType == LightType.Spot ? "聚光" : "方向光";
            EditorGUILayout.HelpBox(
                string.Format(
                    "{0}分辨率 {1}px 超过当前 Atlas 容量建议上限 {2}px。本帧需要 {3} 个 shadow slice，运行时会自动降级到可容纳的尺寸；如果想保留这个分辨率，请增大 Atlas 或减少灯数。",
                    lightLabel,
                    resolutionProperty.intValue,
                    maxResolution,
                    requestedSliceCount),
                MessageType.Warning);
        }

        private void DrawAtlasCapacitySummary()
        {
            int requestedSliceCount = CountRequestedSlices();
            if (requestedSliceCount <= 0)
            {
                return;
            }

            int maxResolution = GetMaxResolutionForSliceCount(atlasSizeProperty.intValue, requestedSliceCount);
            EditorGUILayout.HelpBox(
                string.Format("当前有效灯光预计需要 {0} 个 shadow slice；Atlas {1}px 下，单 slice 自动上限约为 {2}px。点光每盏占 6 个 slice。", requestedSliceCount, atlasSizeProperty.intValue, maxResolution),
                MessageType.None);
        }

        private int CountRequestedSlices()
        {
            int count = 0;
            count += CountRequestedSlices(spotLightsProperty, LightType.Spot, 1);
            count += CountRequestedSlices(pointLightsProperty, LightType.Point, 6);
            return count;
        }

        private static int CountRequestedSlices(SerializedProperty arrayProperty, LightType expectedType, int slicesPerLight)
        {
            if (arrayProperty == null || !arrayProperty.isArray)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < arrayProperty.arraySize; i++)
            {
                Light light = arrayProperty.GetArrayElementAtIndex(i).objectReferenceValue as Light;
                if (light != null && light.type == expectedType && light.isActiveAndEnabled)
                {
                    count += slicesPerLight;
                }
            }

            return count;
        }

        private static int GetMaxResolutionForSliceCount(int atlasSize, int requestedSliceCount)
        {
            atlasSize = Mathf.Max(1, atlasSize);
            if (requestedSliceCount <= 1)
            {
                return atlasSize;
            }

            int gridSize = Mathf.CeilToInt(Mathf.Sqrt(requestedSliceCount));
            return Mathf.Max(64, atlasSize / Mathf.Max(1, gridSize));
        }
    }
}
