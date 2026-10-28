using lilToon.URP.Extensions.RTShadow;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTShadow
{
    [CustomEditor(typeof(HoRTShadowVolume))]
    internal sealed class HoRTShadowVolumeEditor : VolumeComponentEditor
    {
        private static bool runtime = true, quality = true, debug = true;
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("勾选左侧覆盖后作用于当前相机；未覆盖字段使用 Feature 默认值。Feature 生产总开关仍须开启。投射物、请求灯与接收声明保持在 Feature / 场景组件。", MessageType.Info);
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref runtime, "运行", "逐相机覆盖", new Color(.46f, .64f, .92f)))
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) Draw("enable", "启用");
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref quality, "重建质量", "无历史 / 稳定预览", new Color(.42f, .72f, .58f)))
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Draw("reconstruction", "阴影重建"); Draw("angularDiameter", "默认角直径（度）");
                Draw("samplesPerPixel", "原始参考样本数"); Draw("singleFrameEstimates", "无历史同帧估计数"); Draw("samplingSeed", "采样种子");
                EditorGUILayout.HelpBox("逐灯声明角度优先。Raw 只使用原始样本数；SIGMA 无历史只使用同帧估计数，次数增加会增加完整降噪成本；稳定预览每请求一次。点 / 聚光当前始终输出 delta 硬影。", MessageType.None);
            }
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref debug, "调试", "指定灯 / 六种视图", new Color(.86f, .62f, .38f)))
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Draw("debugMode", "调试模式"); Draw("debugLight", "调试灯槽"); Draw("debugDistance", "距离显示上限（米）");
                Draw("debugInSceneView", "Scene View 调试"); Draw("debugInGameView", "Game View 调试");
                EditorGUILayout.HelpBox("灯槽从 0 开始，映射见 Feature 运行状态；无当前产品显示紫色。状态图：黑=未请求、绿=有效、红=无效表面、蓝=灯域外、橙=距离不足、紫=查询错误。Blocker 无命中为蓝色。多估计模式 Raw 是最后一个真实单样本，差值只用于观察重建改动。", MessageType.None);
            }
        }
        private void Draw(string name, string label)
        {
            var property = serializedObject.FindProperty(name);
            PropertyField(Unpack(property), new GUIContent(label, property.tooltip));
        }
    }
}
