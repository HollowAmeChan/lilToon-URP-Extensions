using System.Collections.Generic;
using lilToon.URP.Extensions.SubsurfaceScattering;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.SubsurfaceScattering
{
    [VolumeParameterDrawer(typeof(HoSubsurfaceScatteringProfilesParameter))]
    internal sealed class HoSubsurfaceScatteringProfilesDrawer : VolumeParameterDrawer
    {
        public override bool OnGUI(SerializedDataParameter parameter, GUIContent title)
        {
            EditorGUILayout.PropertyField(parameter.value, title, true);
            return true;
        }

        public override float GetElementHeight(SerializedDataParameter parameter)
            => EditorGUI.GetPropertyHeight(parameter.value, true);
    }

    [CustomEditor(typeof(HoSubsurfaceScatteringVolume))]
    internal sealed class HoSubsurfaceScatteringVolumeEditor : HoVolumeComponentEditor
    {
        private static readonly string[] Fields = {
            "enable", "renderInSceneView", "renderScale", "quality", "strength", "profiles",
            "depthTolerance", "normalTolerance", "radius", "color", "sourcePreserve",
            "transmissionStrength", "transmissionRadius", "transmissionColor", "transmissionSamples",
            "transmissionMainLightDirection", "transmissionDepthWeight", "transmissionEdgeBoost", "transmissionRimWeight",
            "transmissionBlendMode", "transmissionTintInjection", "transmissionSmoothing", "debugMode"
        };
        private readonly Dictionary<string, SerializedDataParameter> parameters = new Dictionary<string, SerializedDataParameter>();
        private bool showProfiles, showDiffusion = true, showFallback, showTransmission, showDebug;

        public override void OnEnable()
        {
            parameters.Clear();
            foreach (string name in Fields) parameters.Add(name, Unpack(serializedObject.FindProperty(name)));
        }

        public override void OnInspectorGUI()
        {
            Draw("enable", "strength", "renderScale", "quality");
            if (HoVolumeEditorGUI.Section(ref showProfiles, "材质配置", "按 Profile ID 选择，整张表统一覆盖；最多使用前 8 项。"))
                Draw("profiles");
            if (HoVolumeEditorGUI.Section(ref showDiffusion, "扩散"))
                Draw("depthTolerance", "normalTolerance");
            if (HoVolumeEditorGUI.Section(ref showFallback, "配置未命中回退"))
                Draw("radius", "color", "sourcePreserve", "transmissionStrength", "transmissionRadius", "transmissionColor");
            if (HoVolumeEditorGUI.Section(ref showTransmission, "透射"))
                Draw("transmissionSamples", "transmissionMainLightDirection", "transmissionDepthWeight", "transmissionEdgeBoost",
                    "transmissionRimWeight", "transmissionBlendMode", "transmissionTintInjection", "transmissionSmoothing");
            if (HoVolumeEditorGUI.Section(ref showDebug, "调试")) Draw("debugMode", "renderInSceneView");
        }

        private void Draw(params string[] names)
        {
            foreach (string name in names) PropertyField(parameters[name]);
        }
    }
}
