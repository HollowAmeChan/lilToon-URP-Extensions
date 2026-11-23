using lilToon.URP.Extensions.RTShadow;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTShadow
{
    [CustomEditor(typeof(HoRTShadowVolume))]
    internal sealed class HoRTShadowVolumeEditor : HoVolumeComponentEditor
    {
        private static bool quality = true, debug;
        public override void OnInspectorGUI()
        {
            Draw("enable", "启用");
            if (HoVolumeEditorGUI.Section(ref quality, "重建质量", "无历史 / 稳定预览"))
            using (new EditorGUILayout.VerticalScope())
            {
                Draw("reconstruction", "阴影重建"); Draw("angularDiameter", "默认角直径（度）");
                Draw("samplesPerPixel", "原始参考样本数"); Draw("singleFrameEstimates", "无历史同帧估计数"); Draw("samplingSeed", "采样种子");
            }
            if (HoVolumeEditorGUI.Section(ref debug, "调试", "生产 / 实际 lilToon 接收"))
            using (new EditorGUILayout.VerticalScope())
            {
                Draw("debugMode", "调试模式"); Draw("debugLight", "调试灯槽"); Draw("debugDistance", "距离显示上限（米）");
                Draw("debugInSceneView", "场景视图"); Draw("debugInGameView", "游戏视图");
            }
        }
        private void Draw(string name, string label)
        {
            var property = serializedObject.FindProperty(name);
            PropertyField(Unpack(property), new GUIContent(label, property.tooltip));
        }
    }
}
