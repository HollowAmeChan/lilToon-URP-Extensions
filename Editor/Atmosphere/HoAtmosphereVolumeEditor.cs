using lilToon.URP.Extensions.Atmosphere;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.Atmosphere
{
    [CustomEditor(typeof(HoAtmosphereVolume))]
    internal sealed class HoAtmosphereVolumeEditor : HoVolumeComponentEditor
    {


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
            if (HoVolumeEditorGUI.Section(ref showRuntime, "运行", "环境采样"))
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    PropertyField(enable, new GUIContent("环境输入启用")); PropertyField(intensity, new GUIContent("强度"));
                    PropertyField(tint, new GUIContent("染色")); PropertyField(rotation, new GUIContent("水平旋转（度）"));
                }
            }
            if (HoVolumeEditorGUI.Section(ref showDebug, "调试", LilUrpEditorSectionGui.EnumSummary(mode)))
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    string description = mode.value.intValue == 1 ? "全方向 HDR 辐射的经纬图，中心 +Z、右侧 +X、上方 +Y。" :
                        mode.value.intValue == 2 ? "世界方向映射到 RGB；与天空颜色无关。" :
                        mode.value.intValue == 3 ? "绿色为有效来源，洋红色为来源缺失。有效的黑色天空仍为绿色。" :
                        mode.value.intValue == 4 ? "区域局部 XY 中央切片：灰度为密度乘体积光激活权重。" :
                        mode.value.intValue == 5 ? "区域局部 XY 中央切片：灰度为体积光激活权重。" : "";
                    PropertyField(mode, new GUIContent("调试模式", description));
                    PropertyField(scene, new GUIContent("场景视图"));
                    PropertyField(game, new GUIContent("游戏视图"));
                }
            }
        }
    }
}
