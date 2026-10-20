using lilToon.URP.Extensions.Atmosphere;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.Atmosphere
{
    [CustomEditor(typeof(HoAtmosphereRegion)), CanEditMultipleObjects]
    internal sealed class HoAtmosphereRegionEditor : UnityEditor.Editor
    {
        private static readonly Color ContentColor = new(.42f, .72f, .58f);
        private static readonly Color RuntimeColor = new(.46f, .64f, .92f);
        private static bool showRegion = true, showMedium = true, showOptics = true, showDust;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showRegion, "区域", "盒形 / 世界软边", ContentColor))
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    foreach (string field in new[] { "size", "blendDistance", "volumeLightWeight" }) EditorGUILayout.PropertyField(serializedObject.FindProperty(field));
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showMedium, "介质", "密度 / 颜色 / 动态纹理", RuntimeColor))
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    HoAtmosphereDensityPresets.Draw(targets);
                    foreach (string field in new[] { "density", "tint", "densityTexture" }) EditorGUILayout.PropertyField(serializedObject.FindProperty(field));
                    bool textured = serializedObject.FindProperty("densityTexture").objectReferenceValue != null || serializedObject.FindProperty("densityTexture").hasMultipleDifferentValues;
                    if (textured)
                        foreach (string field in new[] { "noiseStrength", "noiseWorldSize" }) EditorGUILayout.PropertyField(serializedObject.FindProperty(field));
                    var dustEnabled = serializedObject.FindProperty("dust").FindPropertyRelative("enabled");
                    if (textured || dustEnabled.boolValue || dustEnabled.hasMultipleDifferentValues) EditorGUILayout.PropertyField(serializedObject.FindProperty("wind"));
                }
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showOptics, "散射光学", "反照率 / 方向性", RuntimeColor))
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    foreach (string field in new[] { "scatteringAlbedo", "anisotropy", "pathExtinction" }) EditorGUILayout.PropertyField(serializedObject.FindProperty(field));
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showDust, "可见灰尘", "共享世界尘粒", ContentColor))
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    HoAtmosphereDustPresets.Draw(serializedObject);
                    var dust = serializedObject.FindProperty("dust"); EditorGUILayout.PropertyField(dust.FindPropertyRelative("enabled"));
                    if (dust.FindPropertyRelative("enabled").boolValue || dust.FindPropertyRelative("enabled").hasMultipleDifferentValues)
                    {
                        foreach (string field in new[] { "texture", "grainDensity", "clusterSize", "textureWorldSize", "brightness", "flutter", "maxDistance", "maxClusters", "seed" }) EditorGUILayout.PropertyField(dust.FindPropertyRelative(field));
                        if (targets.Length == 1)
                        {
                            var source = (HoAtmosphereRegion)target;
                            EditorGUILayout.LabelField("已准备簇片", source.PreparedDustCount + (source.DustCapacityLimited ? "（已裁至片数预算）" : ""));
                        }
                    }
                }
            serializedObject.ApplyModifiedProperties();
            if (HoAtmosphereRegionRegistry.ActiveCount > 1) EditorGUILayout.HelpBox("v0.1 仅支持一个有效区域；多个区域会发布空介质。", MessageType.Warning);
            EditorGUILayout.HelpBox("范围与密度调试位于 HoAtmosphere Volume。", MessageType.Info);
        }
        private void OnSceneGUI()
        {
            var region = (HoAtmosphereRegion)target;
            using (new Handles.DrawingScope(region.transform.localToWorldMatrix))
            {
                var handle = new UnityEditor.IMGUI.Controls.BoxBoundsHandle { center = Vector3.zero, size = region.size };
                EditorGUI.BeginChangeCheck(); handle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(region, "调整大气区域"); region.size = handle.size;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(region); EditorUtility.SetDirty(region);
                }
            }
        }
        [MenuItem("GameObject/Rendering/Ho-Atmosphere 大气区域", false, 11)]
        private static void CreateRegion(MenuCommand command)
        {
            var go = new GameObject("Ho 大气区域", typeof(HoAtmosphereRegion));
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "创建大气区域"); Selection.activeGameObject = go;
        }
    }
}
