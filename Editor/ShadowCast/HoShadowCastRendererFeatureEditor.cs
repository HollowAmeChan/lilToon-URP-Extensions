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
        private static bool showAtlas = true;
        private static bool showPcss = true;
        private static bool showSecondDirectional = true;
        private static bool showRuntime = true;
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
                "ShadowCast now collects eligible URP visible lights from this RendererFeature by default. HoShadowCastController is kept as an optional legacy override for manual light lists.",
                MessageType.Info);

            if (settingsProperty == null)
            {
                DrawDefaultInspector();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawSettings();
            DrawAtlas();
            DrawPcss();
            DrawSecondDirectional();
            DrawRuntimeStatus();
            DrawControllerStatus();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSettings()
        {
            SerializedProperty enabled = settingsProperty.FindPropertyRelative("enabled");
            SerializedProperty collectVisibleLights = settingsProperty.FindPropertyRelative("collectVisibleLights");
            string summary = enabled != null && enabled.boolValue
                ? collectVisibleLights != null && collectVisibleLights.boolValue ? "Auto visible lights" : "Manual override only"
                : "Disabled";

            if (!DrawSectionHeader(ref showSettings, "RendererFeature", summary, SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(enabled, "Enabled");
                SerializedProperty passEvent = settingsProperty.FindPropertyRelative("passEvent");
                DrawProperty(passEvent, "Pass Event");
                DrawProperty(collectVisibleLights, "Collect Visible Lights");
                DrawProperty(settingsProperty.FindPropertyRelative("useActiveControllerOverride"), "Use Active Controller Override");
                DrawProperty(settingsProperty.FindPropertyRelative("casterLayerMask"), "Caster Layer Mask");
                DrawProperty(settingsProperty.FindPropertyRelative("shadowStrength"), "Shadow Strength");
                DrawProperty(settingsProperty.FindPropertyRelative("punctualShadowStrength"), "Punctual Shadow Strength");
                DrawProperty(settingsProperty.FindPropertyRelative("punctualShadowFadeSpeed"), "Punctual Fade Speed");
                DrawProperty(settingsProperty.FindPropertyRelative("debugMode"), "Debug Mode");

                if (passEvent != null && passEvent.intValue < BeforeRenderingPrePassesValue)
                {
                    EditorGUILayout.HelpBox("ShadowCast should not run before URP's built-in shadow stage. Runtime clamps this to BeforeRenderingPrePasses.", MessageType.Info);
                }
            }
        }

        private void DrawAtlas()
        {
            if (!DrawSectionHeader(ref showAtlas, "Atlas", GetIntSummary("atlasSize", "px"), SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(settingsProperty.FindPropertyRelative("atlasSize"), "Atlas Size");
                DrawProperty(settingsProperty.FindPropertyRelative("spotResolution"), "Spot Resolution");
                DrawProperty(settingsProperty.FindPropertyRelative("pointFaceResolution"), "Point Face Resolution");
                DrawProperty(settingsProperty.FindPropertyRelative("directionalResolution"), "Directional Resolution");
                DrawProperty(settingsProperty.FindPropertyRelative("directionalNearPlane"), "Directional Near Plane");
                DrawProperty(settingsProperty.FindPropertyRelative("directionalShadowSize"), "Directional Shadow Size");
                DrawProperty(settingsProperty.FindPropertyRelative("directionalShadowDepth"), "Directional Shadow Depth");
            }
        }

        private void DrawPcss()
        {
            SerializedProperty enabled = settingsProperty.FindPropertyRelative("pcssEnabled");
            string summary = enabled != null && enabled.boolValue ? "Enabled" : "Disabled";
            if (!DrawSectionHeader(ref showPcss, "PCSS", summary, SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(enabled, "Enable PCSS");
                DrawProperty(settingsProperty.FindPropertyRelative("pcssQuality"), "Quality");
                DrawProperty(settingsProperty.FindPropertyRelative("punctualPcssSoftness"), "Punctual Softness");
                DrawProperty(settingsProperty.FindPropertyRelative("secondDirectionalPcssSoftness"), "Second Directional Softness");
                DrawProperty(settingsProperty.FindPropertyRelative("pcssBlockerSearchRadius"), "Blocker Search Radius");
                DrawProperty(settingsProperty.FindPropertyRelative("pcssMaxPenumbraRadius"), "Max Penumbra Radius");
                DrawProperty(settingsProperty.FindPropertyRelative("pcssDepthBias"), "Depth Bias");
            }
        }

        private void DrawSecondDirectional()
        {
            if (!DrawSectionHeader(ref showSecondDirectional, "Second Directional", GetIntSummary("secondDirectionalAtlasSize", "px"), SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(settingsProperty.FindPropertyRelative("secondDirectionalShadowStrength"), "Strength");
                DrawProperty(settingsProperty.FindPropertyRelative("secondDirectionalAtlasSize"), "Atlas Size");
                DrawProperty(settingsProperty.FindPropertyRelative("secondDirectionalCascadeCount"), "Cascade Count");
                DrawProperty(settingsProperty.FindPropertyRelative("secondDirectionalMaxDistance"), "Max Distance");
                DrawProperty(settingsProperty.FindPropertyRelative("secondDirectionalShadowDepth"), "Shadow Depth");
                DrawProperty(settingsProperty.FindPropertyRelative("secondDirectionalCascadeSplits"), "Cascade Splits");
            }
        }

        private void DrawRuntimeStatus()
        {
            HoShadowCastRuntimeDiagnosticSnapshot snapshot = HoShadowCastRuntimeDiagnostics.CurrentSnapshot;
            string summary = snapshot.IsValid
                ? snapshot.LightCount + " lights / " + snapshot.SliceCount + " slices"
                : "No frame yet";

            if (!DrawSectionHeader(ref showRuntime, "Runtime", summary, SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!snapshot.IsValid)
                {
                    EditorGUILayout.HelpBox("No ShadowCast frame has been recorded yet. Enter Play Mode or render a Scene/Game camera that uses this RendererFeature.", MessageType.Info);
                    return;
                }

                EditorGUILayout.LabelField("Frame", snapshot.FrameCount.ToString());
                EditorGUILayout.LabelField("Path", snapshot.Path);
                EditorGUILayout.LabelField("Camera", snapshot.CameraName);
                EditorGUILayout.LabelField("Source", snapshot.Source);
                EditorGUILayout.LabelField("Visible Lights", snapshot.VisibleLightCount.ToString());
                EditorGUILayout.LabelField("Candidates", snapshot.CandidateCount + " checked, " + snapshot.SkippedCandidateCount + " skipped");
                EditorGUILayout.LabelField("Punctual Atlas", FormatAtlas(snapshot.HasFrame, snapshot.LightCount, snapshot.SliceCount, snapshot.AtlasSize));
                EditorGUILayout.LabelField("Second Directional", FormatSecondDirectional(snapshot));

                DrawAcceptedLights(snapshot.AcceptedLights);
                DrawSkippedLights(snapshot.SkippedLights, snapshot.SkippedCandidateCount);
            }
        }

        private static string FormatAtlas(bool active, int lightCount, int sliceCount, int atlasSize)
        {
            return active
                ? lightCount + " lights, " + sliceCount + " slices, " + atlasSize + "px"
                : "Inactive";
        }

        private static string FormatSecondDirectional(HoShadowCastRuntimeDiagnosticSnapshot snapshot)
        {
            return snapshot.HasSecondDirectionalFrame
                ? snapshot.SecondDirectionalLightCount + " lights, " + snapshot.SecondDirectionalSliceCount + " slices, " + snapshot.SecondDirectionalCascadeCount + " cascades, " + snapshot.SecondDirectionalAtlasSize + "px"
                : "Inactive";
        }

        private static void DrawAcceptedLights(HoShadowCastRuntimeDiagnosticLight[] lights)
        {
            if (lights == null || lights.Length == 0)
            {
                return;
            }

            EditorGUILayout.Space(3.0f);
            EditorGUILayout.LabelField("Accepted Lights", EditorStyles.boldLabel);
            for (int i = 0; i < lights.Length; i++)
            {
                HoShadowCastRuntimeDiagnosticLight light = lights[i];
                EditorGUILayout.LabelField(
                    light.Name,
                    light.Stage + " " + light.Type + " slices " + light.FirstSlice + "+" + light.SliceCount + " @ " + light.Resolution + "px");
            }
        }

        private static void DrawSkippedLights(HoShadowCastRuntimeDiagnosticSkip[] skippedLights, int skippedCandidateCount)
        {
            if (skippedLights == null || skippedLights.Length == 0)
            {
                return;
            }

            EditorGUILayout.Space(3.0f);
            EditorGUILayout.LabelField("Skipped Lights", EditorStyles.boldLabel);
            for (int i = 0; i < skippedLights.Length; i++)
            {
                HoShadowCastRuntimeDiagnosticSkip skipped = skippedLights[i];
                EditorGUILayout.LabelField(skipped.Name, skipped.Stage + " " + skipped.Type + ": " + skipped.Reason);
            }

            int remaining = skippedCandidateCount - skippedLights.Length;
            if (remaining > 0)
            {
                EditorGUILayout.LabelField("More skipped", remaining.ToString());
            }
        }

        private void DrawControllerStatus()
        {
            HoShadowCastController controller = HoShadowCastController.ActiveController;
            string summary = controller != null ? controller.name : "No active override";

            if (!DrawSectionHeader(ref showController, "Legacy Controller", summary, ControllerColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (controller == null)
                {
                    EditorGUILayout.HelpBox("No active HoShadowCastController found. The RendererFeature will use automatic visible-light collection unless controller override is required for a legacy manual light list.", MessageType.Info);

                    if (GUILayout.Button("Create HoShadowCastController"))
                    {
                        CreateController();
                    }
                }
                else
                {
                    EditorGUILayout.ObjectField("Active Controller", controller, typeof(HoShadowCastController), true);
                    EditorGUILayout.LabelField("Atlas Size", controller.atlasSize.ToString());
                    EditorGUILayout.HelpBox("When Use Active Controller Override is enabled, this controller supplies the manual light list and legacy tuning values.", MessageType.Info);
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

        private string GetIntSummary(string propertyName, string suffix)
        {
            SerializedProperty property = settingsProperty.FindPropertyRelative(propertyName);
            return property != null ? property.intValue + suffix : string.Empty;
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
