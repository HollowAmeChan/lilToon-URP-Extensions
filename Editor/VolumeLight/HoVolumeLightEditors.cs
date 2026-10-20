using lilToon.URP.Extensions.VolumeLight;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.VolumeLight
{
    [CustomEditor(typeof(HoVolumeLightRendererFeature))]
    internal sealed class HoVolumeLightFeatureEditor : UnityEditor.Editor
    {
        private static readonly Color RuntimeColor = new(.46f, .64f, .92f), AdvancedColor = new(.62f, .58f, .78f);
        private static bool showRuntime = true, showAdvanced;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var settings = serializedObject.FindProperty("settings");
            EditorGUILayout.HelpBox("读取固定 HoAtmosphere 介质输入与当前主方向光；v0.1 使用 RenderGraph。", MessageType.Info);
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行（兜底默认值）", "太阳 HD", RuntimeColor))
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    foreach (string field in new[] { "enabled", "intensity", "maxDistance", "stepCount", "antialiasing", "overrideAnisotropy" }) EditorGUILayout.PropertyField(settings.FindPropertyRelative(field));
                    if (settings.FindPropertyRelative("overrideAnisotropy").boolValue) EditorGUILayout.PropertyField(settings.FindPropertyRelative("anisotropy"));
                }
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showAdvanced, "高级", "Shader", AdvancedColor))
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) EditorGUILayout.PropertyField(settings.FindPropertyRelative("shader"));
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("逐相机覆盖与调试位于 Ho-VolumeLight Volume。", MessageType.Info);
            EditorGUILayout.LabelField("运行状态", ((HoVolumeLightRendererFeature)target).Status);
        }
    }
    [CustomEditor(typeof(HoVolumeLightVolume))]
    internal sealed class HoVolumeLightVolumeEditor : VolumeComponentEditor
    {
        private static readonly Color RuntimeColor = new(.46f, .64f, .92f), DebugColor = new(.86f, .62f, .38f);
        private static bool showRuntime = true, showDebug;
        private SerializedDataParameter enable, intensity, distance, steps, antialiasing, anisotropy, debug, scene, game;
        public override void OnEnable()
        {
            var f = new PropertyFetcher<HoVolumeLightVolume>(serializedObject);
            enable = Unpack(f.Find(x => x.enable)); intensity = Unpack(f.Find(x => x.intensity));
            distance = Unpack(f.Find(x => x.maxDistance)); steps = Unpack(f.Find(x => x.stepCount)); anisotropy = Unpack(f.Find(x => x.anisotropy));
            antialiasing = Unpack(f.Find(x => x.antialiasing));
            debug = Unpack(f.Find(x => x.debugMode)); scene = Unpack(f.Find(x => x.debugInSceneView)); game = Unpack(f.Find(x => x.debugInGameView));
        }
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("未覆盖的运行参数使用 Feature 兜底值。", MessageType.Info);
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", "太阳 HD", RuntimeColor))
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    PropertyField(enable, new GUIContent("启用")); PropertyField(intensity, new GUIContent("体积亮度倍率"));
                    PropertyField(distance, new GUIContent("最大照亮距离")); PropertyField(steps, new GUIContent("采样步数")); PropertyField(anisotropy, new GUIContent("散射方向性覆盖"));
                    PropertyField(antialiasing, new GUIContent("抗锯齿"));
                }
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showDebug, "调试", LilUrpEditorSectionGui.EnumSummary(debug), DebugColor))
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    PropertyField(debug, new GUIContent("调试模式"));
                    string description = debug.value.intValue == 1 ? "仅显示实际体积 RGB 贡献，包含 SDF 激活与光源遮挡。" :
                        debug.value.intValue == 2 ? "仅显示忽略阴影的体积 RGB 贡献，用于比较遮挡。" : "";
                    if (description.Length > 0) EditorGUILayout.HelpBox(description, MessageType.None);
                    PropertyField(scene, new GUIContent("Debug In Scene View")); PropertyField(game, new GUIContent("Debug In Game View"));
                }
        }
    }
}
