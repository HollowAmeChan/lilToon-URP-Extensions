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
        private static bool showRegion = true, showMedium = true;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showRegion, "区域", "盒形 / 世界软边", ContentColor))
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    foreach (string field in new[] { "size", "blendDistance", "volumeLightWeight" }) EditorGUILayout.PropertyField(serializedObject.FindProperty(field));
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showMedium, "介质", "密度 / 颜色 / 动态纹理", RuntimeColor))
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    foreach (string field in new[] { "density", "tint", "densityTexture" }) EditorGUILayout.PropertyField(serializedObject.FindProperty(field));
                    if (serializedObject.FindProperty("densityTexture").objectReferenceValue != null)
                        foreach (string field in new[] { "noiseStrength", "noiseWorldSize", "wind" }) EditorGUILayout.PropertyField(serializedObject.FindProperty(field));
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
