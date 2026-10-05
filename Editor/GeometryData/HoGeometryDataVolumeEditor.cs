using lilToon.URP.Extensions.GeometryData;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GeometryData
{
    [CustomEditor(typeof(HoGeometryDataVolume))]
    internal sealed class HoGeometryDataVolumeEditor : VolumeComponentEditor
    {
        private bool showRuntime = true, showDebug;
        private SerializedDataParameter enable, mode, scene, game, layers, range, timing;
        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<HoGeometryDataVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.enable)); mode = Unpack(fetcher.Find(x => x.debugMode));
            scene = Unpack(fetcher.Find(x => x.debugInSceneView)); game = Unpack(fetcher.Find(x => x.debugInGameView));
            layers = Unpack(fetcher.Find(x => x.debugLayerMask)); range = Unpack(fetcher.Find(x => x.debugMaxValue));
            timing = Unpack(fetcher.Find(x => x.debugPassEvent));
        }
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("GD 的逐相机调试入口。RendererFeature 保留运行兜底与数据源状态；选择预览模式并勾选覆盖，即可独立查看几何数据，不需要开启材质 Tesion。", MessageType.Info);
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", LilUrpEditorSectionGui.BoolSummary(enable), new Color(0.46f,0.64f,0.92f)))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    PropertyField(enable, new GUIContent("启用"));
                    EditorGUILayout.HelpBox("未勾选覆盖时使用 Feature 的运行兜底值。", MessageType.None);
                }
            }
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showDebug, "调试", LilUrpEditorSectionGui.EnumSummary(mode), new Color(0.86f,0.62f,0.38f))) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                PropertyField(mode, new GUIContent("调试模式"));
                var selected = (HoGeometryDataDebugMode)mode.value.enumValueIndex;
                if (selected != HoGeometryDataDebugMode.Off) EditorGUILayout.HelpBox(Describe(selected), MessageType.Info);
                PropertyField(scene, new GUIContent("Scene 视图")); PropertyField(game, new GUIContent("Game 视图"));
                PropertyField(layers, new GUIContent("预览 Layer Mask"));
                if (selected >= HoGeometryDataDebugMode.TensionStretch && selected <= HoGeometryDataDebugMode.TensionAngular)
                    PropertyField(range, new GUIContent("热图满量程"));
                PropertyField(timing, new GUIContent("预览绘制时机"));
                if (selected != HoGeometryDataDebugMode.Off)
                    EditorGUILayout.HelpBox("预览替换所选视图画面，直接画几何，跳过材质裁剪与描边位移。后续屏幕处理仍可能处理预览；数据源的准备状态和失败原因在组件与 Feature 中查看。", MessageType.None);
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
