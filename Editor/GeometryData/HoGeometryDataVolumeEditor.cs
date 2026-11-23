using lilToon.URP.Extensions.GeometryData;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GeometryData
{
    [CustomEditor(typeof(HoGeometryDataVolume))]
    internal sealed class HoGeometryDataVolumeEditor : HoVolumeComponentEditor
    {
        private bool showDebug;
        private SerializedDataParameter enable, mode, scene, game, layers, range;
        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<HoGeometryDataVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.enable)); mode = Unpack(fetcher.Find(x => x.debugMode));
            scene = Unpack(fetcher.Find(x => x.debugInSceneView)); game = Unpack(fetcher.Find(x => x.debugInGameView));
            layers = Unpack(fetcher.Find(x => x.debugLayerMask)); range = Unpack(fetcher.Find(x => x.debugMaxValue));
        }
        public override void OnInspectorGUI()
        {
            PropertyField(enable, new GUIContent("启用"));
            if (!HoVolumeEditorGUI.Section(ref showDebug, "调试", LilUrpEditorSectionGui.EnumSummary(mode))) return;
            using (new EditorGUILayout.VerticalScope())
            {
                var selected = (HoGeometryDataDebugMode)mode.value.intValue;
                PropertyField(mode, new GUIContent("调试模式", selected == HoGeometryDataDebugMode.Off ? null : Describe(selected)));
                PropertyField(scene, new GUIContent("场景视图")); PropertyField(game, new GUIContent("游戏视图"));
                PropertyField(layers, new GUIContent("预览 Layer Mask"));
                if (selected == HoGeometryDataDebugMode.All || (selected >= HoGeometryDataDebugMode.TensionStretch && selected <= HoGeometryDataDebugMode.TensionAngular))
                    PropertyField(range, new GUIContent("热图满量程"));
            }
        }
        private static string Describe(HoGeometryDataDebugMode mode)
        {
            switch (mode)
            {
                case HoGeometryDataDebugMode.RendererSlot: return "彩色：有 GD 绑定；深灰：未绑定。描边与 Tesion 使用同一 Renderer 槽。";
                case HoGeometryDataDebugMode.OutlineDirection: return "RGB = 世界方向 × 0.5 + 0.5；品红：缺少有效描边数据。";
                case HoGeometryDataDebugMode.OutlineThickness: return "灰度为原始描边 A，0.5 中性；品红：缺少有效描边数据。";
                case HoGeometryDataDebugMode.TensionValidity: return "绿色：有效顶点；品红：无来源、未准备、未生产或无效顶点。";
                default: return "黑色中性；蓝→青→黄→红随信号增大；品红无有效数据。满量程只影响显示，不改变测量与材质。";
            }
        }
    }
}
