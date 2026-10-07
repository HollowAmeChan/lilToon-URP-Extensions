using lilToon.URP.Extensions.Atmosphere;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.Atmosphere
{
    [CustomEditor(typeof(HoAtmosphereRendererFeature))]
    internal sealed class HoAtmosphereRendererFeatureEditor : UnityEditor.Editor
    {
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color AdvancedColor = new Color(0.62f, 0.58f, 0.78f);
        private static bool showRuntime = true, showAdvanced;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var settings = serializedObject.FindProperty("settings");
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行（兜底默认值）", "全方向 HDR 环境", RuntimeColor))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    Draw(settings, "enabled", "启用");
                    Draw(settings, "source", "天空来源");
                    var source = (HoAtmosphereSource)settings.FindPropertyRelative("source").enumValueIndex;
                    if (source == HoAtmosphereSource.SkyboxMaterial) Draw(settings, "skyboxMaterial", "天空盒材质");
                    if (source == HoAtmosphereSource.RadianceCubemap) Draw(settings, "radianceCubemap", "原始 HDR Cubemap");
                    if (source == HoAtmosphereSource.Constant) Draw(settings, "constantColor", "环境颜色");
                    Draw(settings, "intensity", "强度");
                    Draw(settings, "tint", "染色");
                    Draw(settings, "rotationY", "水平旋转（度）");
                    Draw(settings, "inSceneView", "Scene View 中生产");
                }
            }
            EditorGUILayout.HelpBox("调试模式与视图开关在 HoAtmosphere Volume 的「调试」分组。", MessageType.Info);
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showAdvanced, "高级", "天空捕获 / 更新", AdvancedColor))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    Draw(settings, "resolution", "每面分辨率");
                    Draw(settings, "update", "更新方式");
                    if (GUILayout.Button("刷新环境")) ((HoAtmosphereRendererFeature)target).RefreshEnvironment();
                }
            }
            serializedObject.ApplyModifiedProperties();
            var feature = (HoAtmosphereRendererFeature)target;
            EditorGUILayout.LabelField("运行状态", feature.Status);
            EditorGUILayout.LabelField("累计捕获", feature.CaptureCount.ToString());
        }
        private static void Draw(SerializedProperty settings, string name, string label) =>
            EditorGUILayout.PropertyField(settings.FindPropertyRelative(name), new GUIContent(label), true);
    }
}
