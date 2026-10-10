using lilToon.URP.Extensions.Atmosphere;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.Atmosphere
{
    [CustomEditor(typeof(HoAtmosphereVolume))]
    internal sealed class HoAtmosphereVolumeEditor : VolumeComponentEditor
    {
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color DebugColor = new Color(0.86f, 0.62f, 0.38f);
        private static bool showRuntime = true, showDebug;
        private SerializedDataParameter enable, intensity, tint, rotation, mode, scene, game;
        public override void OnEnable()
        {
            var fetch = new PropertyFetcher<HoAtmosphereVolume>(serializedObject);
            enable = Unpack(fetch.Find(x => x.enable)); intensity = Unpack(fetch.Find(x => x.intensity));
            tint = Unpack(fetch.Find(x => x.tint)); rotation = Unpack(fetch.Find(x => x.rotationY));
            mode = Unpack(fetch.Find(x => x.debugMode)); scene = Unpack(fetch.Find(x => x.debugInSceneView));
            game = Unpack(fetch.Find(x => x.debugInGameView));
        }
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("未覆盖的运行参数使用 HoAtmosphere feature 的兜底值。", MessageType.Info);
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", "环境采样", RuntimeColor))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    PropertyField(enable, new GUIContent("环境输入启用")); PropertyField(intensity, new GUIContent("强度"));
                    PropertyField(tint, new GUIContent("染色")); PropertyField(rotation, new GUIContent("水平旋转（度）"));
                }
            }
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showDebug, "调试", LilUrpEditorSectionGui.EnumSummary(mode), DebugColor))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    PropertyField(mode, new GUIContent("调试模式"));
                    string description = mode.value.intValue == 1 ? "全方向 HDR 辐射的经纬图，中心 +Z、右侧 +X、上方 +Y。" :
                        mode.value.intValue == 2 ? "世界方向映射到 RGB；与天空颜色无关。" :
                        mode.value.intValue == 3 ? "绿色为有效来源，洋红色为来源缺失。有效的黑色天空仍为绿色。" :
                        mode.value.intValue == 4 ? "区域局部 XY 中央切片：灰度为密度乘体积光激活权重。" :
                        mode.value.intValue == 5 ? "区域局部 XY 中央切片：灰度为体积光激活权重。" : "";
                    if (description.Length > 0) EditorGUILayout.HelpBox(description, MessageType.None);
                    PropertyField(scene, new GUIContent("Debug In Scene View"));
                    PropertyField(game, new GUIContent("Debug In Game View"));
                }
            }
        }
    }
}
