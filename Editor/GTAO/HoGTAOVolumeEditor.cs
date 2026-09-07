using lilToon.URP.Extensions.GTAO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GTAO
{
    [CustomEditor(typeof(HoGTAOVolume))]
    internal sealed class HoGTAOVolumeEditor : VolumeComponentEditor
    {
        public override void OnInspectorGUI()
        {
            // 质量档预设（一键写入 Volume 字段；逐项参数仍可再调）
            EditorGUILayout.BeginHorizontal();
            bool presetLow = GUILayout.Button("预设：Low");
            bool presetMedium = GUILayout.Button("预设：Medium");
            bool presetHigh = GUILayout.Button("预设：High");
            EditorGUILayout.EndHorizontal();
            if (presetLow) ApplyPreset(HoGTAOQuality.Low);
            if (presetMedium) ApplyPreset(HoGTAOQuality.Medium);
            if (presetHigh) ApplyPreset(HoGTAOQuality.High);

            base.OnInspectorGUI();
        }

        private void ApplyPreset(HoGTAOQuality qualityValue)
        {
            var volume = (HoGTAOVolume)target;
            volume.quality.Override(qualityValue);

            HoGTAOSettings temp = new HoGTAOSettings();
            HoGTAOQualityPresets.Apply(qualityValue, temp);

            volume.resolution.Override(temp.resolution);
            volume.sliceCount.Override(temp.sliceCount);
            volume.stepCount.Override(temp.stepCount);
            volume.temporalFrameCount.Override(temp.temporalFrameCount);
            volume.spatialFilter.Override(temp.spatialFilter);
            volume.boxPassCount.Override(temp.boxPassCount);

            EditorUtility.SetDirty(volume);
        }
    }
}
