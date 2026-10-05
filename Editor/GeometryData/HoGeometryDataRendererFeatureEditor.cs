using System.Collections.Generic;
using lilToon.URP.Extensions.GeometryData;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GeometryData
{
    [CustomEditor(typeof(HoGeometryDataRendererFeature))]
    internal sealed class HoGeometryDataRendererFeatureEditor : UnityEditor.Editor
    {
        private SerializedProperty settings;
        private bool showRuntime, showDebug, showSources, showReferences, showOutlines, showTensions;
        private double refreshAt;
        private HoGeometryDataReferenceFrame[] references = new HoGeometryDataReferenceFrame[0];
        private HoGeometryDataOutlineCorrection[] outlines = new HoGeometryDataOutlineCorrection[0];
        private HoGeometryDataTension[] tensions = new HoGeometryDataTension[0];
        private void OnEnable() => settings = serializedObject.FindProperty("settings");
        public override bool RequiresConstantRepaint() => true;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            if (settings == null) { DrawDefaultInspector(); return; }
            RefreshSources();
            var feature = (HoGeometryDataRendererFeature)target;
            EditorGUILayout.HelpBox("GD 生产专用几何数据。描边与 Tesion 需要此 Feature 发布；参考朝向由组件提供，眼透自己消费。展开“调试”查看数据，展开“数据源”定位对象与无效原因。", MessageType.Info);
            EditorGUILayout.LabelField("已载入数据源", $"参考系 {references.Length} / 描边 {outlines.Length} / Tesion {tensions.Length}");
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", LilUrpEditorSectionGui.BoolSummary(Find("enabled")), new Color(0.46f, 0.64f, 0.92f)))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    Draw("enabled");
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("最近执行相机", feature.LastCamera, typeof(Camera), true);
                    EditorGUILayout.LabelField("最近安排帧", feature.LastScheduledFrame < 0 ? "尚未执行" : feature.LastScheduledFrame.ToString());
                    EditorGUILayout.LabelField("描边已发布", $"{HoOutlineDataRegistry.PublishedSourceCount} 来源 / {HoOutlineDataRegistry.PublishedVertexCount} 顶点");
                    EditorGUILayout.LabelField("Tesion 最近有效样本", $"{HoTensionDataRegistry.ValidSourceCount} / {HoTensionDataRegistry.BoundSourceCount} 来源，{HoTensionDataRegistry.PublishedVertexCount} 顶点");
                    EditorGUILayout.HelpBox("生产固定在绘制前；数据统计属于已载入场景的共享资源。选择 Feature 资产不会触发准备、GPU 读取或改材质。", MessageType.None);
                }
            }
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showDebug, "调试", LilUrpEditorSectionGui.EnumName(Find("debugMode")), new Color(0.86f, 0.62f, 0.38f)))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    Draw("debugMode"); Draw("debugInSceneView"); Draw("debugInGameView"); Draw("debugLayerMask");
                    int mode = Find("debugMode").enumValueIndex;
                    if (mode >= (int)HoGeometryDataDebugMode.TensionStretch && mode <= (int)HoGeometryDataDebugMode.TensionAngular) Draw("debugMaxValue");
                    if (mode != 0)
                    {
                        EditorGUILayout.HelpBox(Legend((HoGeometryDataDebugMode)mode), MessageType.Info);
                        EditorGUILayout.HelpBox("数据预览替换所选视图的画面，直接绘制几何，跳过材质透明裁剪和描边位移。后续屏幕处理仍可能处理预览；用绘制时机调整显示顺序。", MessageType.None);
                        EditorGUILayout.LabelField("最近相机预览", feature.DebugStatus);
                        if (!Find("debugInSceneView").boolValue && !Find("debugInGameView").boolValue)
                            EditorGUILayout.HelpBox("尚未选择显示视图，请开启 Scene 或 Game。", MessageType.Warning);
                    }
                    Draw("debugPassEvent");
                }
            }
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref showSources, "数据源", "只读状态 / 定位组件", new Color(0.62f, 0.58f, 0.78f)))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (references.Length + outlines.Length + tensions.Length == 0)
                        EditorGUILayout.HelpBox("当前没有场景组件。先在对象上添加 HoGeometryDataReferenceFrame、HoGeometryDataOutlineCorrection 或 HoGeometryDataTension。", MessageType.Info);
                    showReferences = EditorGUILayout.Foldout(showReferences, $"参考朝向 ({references.Length})", true);
                    if (showReferences) foreach (var p in references)
                    {
                        if (p == null) continue;
                        SourceLink(p);
                        EditorGUILayout.LabelField(p.TryGetFrame(out _) ? "有效；选择组件后在 Scene 显示前/右/上轴。" : "未启用或未指定参考朝向。");
                    }
                    showOutlines = EditorGUILayout.Foldout(showOutlines, $"描边修正 ({outlines.Length})", true);
                    if (showOutlines) foreach (var p in outlines)
                    {
                        if (p == null) continue;
                        SourceLink(p);
                        string state = !p.isActiveAndEnabled ? "停用" : HoOutlineDataRegistry.IsPublished(p) ? "已发布" : p.HasData ? "已准备 / 待发布" : "未准备或缓存失效";
                        EditorGUILayout.LabelField($"{state} / GD 槽 {Slot(p.targetRenderer)}");
                        EditorGUILayout.HelpBox(p.Status, MessageType.None);
                    }
                    showTensions = EditorGUILayout.Foldout(showTensions, $"Tesion ({tensions.Length})", true);
                    if (showTensions) foreach (var p in tensions)
                    {
                        if (p == null) continue;
                        SourceLink(p);
                        string state = !p.isActiveAndEnabled ? "停用" : HoTensionDataRegistry.IsValid(p) ? "有有效样本" : p.HasData ? "参考已准备 / 当前来源未有效" : "未准备或缓存失效";
                        EditorGUILayout.LabelField($"{state} / GD 槽 {Slot(p.targetRenderer)}");
                        EditorGUILayout.LabelField($"顶点 {p.VertexCount} / 面 {p.TriangleCount} / 参考版本 {p.ReferenceVersion}");
                        EditorGUILayout.LabelField($"最近生产帧 {p.LastProducedFrame} / 累计生产 {p.ProductionCount}");
                        EditorGUILayout.HelpBox(p.Status, MessageType.None);
                    }
                }
            }
            if (serializedObject.ApplyModifiedProperties()) SceneView.RepaintAll();
        }
        private static uint Slot(Renderer renderer) => HoGeometryRendererBinding.GetManagedValue(renderer) >> 16;
        private static void SourceLink(Component source)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(source, source.GetType(), true);
                if (GUILayout.Button("定位", GUILayout.Width(44))) { Selection.activeObject = source; EditorGUIUtility.PingObject(source.gameObject); }
            }
        }
        private void RefreshSources()
        {
            if (EditorApplication.timeSinceStartup < refreshAt) return;
            refreshAt = EditorApplication.timeSinceStartup + 0.5;
            references = SceneSources<HoGeometryDataReferenceFrame>(); outlines = SceneSources<HoGeometryDataOutlineCorrection>(); tensions = SceneSources<HoGeometryDataTension>();
        }
        private static T[] SceneSources<T>() where T : Component
        {
            var scene = new List<T>();
            foreach (var item in Resources.FindObjectsOfTypeAll<T>())
                if (item != null && item.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(item)) scene.Add(item);
            return scene.ToArray();
        }
        private SerializedProperty Find(string name) => settings.FindPropertyRelative(name);
        private void Draw(string name) => EditorGUILayout.PropertyField(Find(name));
        private static string Legend(HoGeometryDataDebugMode mode)
        {
            switch (mode)
            {
                case HoGeometryDataDebugMode.RendererSlot: return "彩色：有 GD Renderer 绑定；深灰：没有绑定。相同对象的描边和 Tesion 共用同一槽。";
                case HoGeometryDataDebugMode.OutlineDirection: return "RGB = 世界方向 × 0.5 + 0.5；品红表示该对象没有有效描边数据。";
                case HoGeometryDataDebugMode.OutlineThickness: return "黑到白显示描边 RGBA 的原始 A；0.5 是算法中性值。品红表示没有有效描边数据。";
                case HoGeometryDataDebugMode.TensionValidity: return "绿色：有效张力样本；品红：无来源、未生产或无效顶点。此模式也显示未绑定的模型。";
                default: return "黑色：中性 0；蓝→青→黄→红：信号增大；品红：无有效张力数据。满量程只控制热图，不改变测量或材质。";
            }
        }
    }
}
