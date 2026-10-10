using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.AttributeComposite
{
    /// <summary>
    /// AC 的调试入口面板（AC 架构 §9.13：调试在 Volume，feature 只放高级设置 + 消费者登记表）。
    /// 调试模式的下拉里**只写视图名**，每个模式的说明按当前选择在下面**单独画一行** ——
    /// 说明塞进枚举显示名会被 Unity 的下拉当成分组。
    /// </summary>
    [CustomEditor(typeof(HoAttributeCompositeVolume))]
    internal sealed class HoAttributeCompositeVolumeEditor : VolumeComponentEditor
    {
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color DebugColor = new Color(0.86f, 0.62f, 0.38f);

        private static bool showRuntime = true;
        private static bool showDebug = true;
        private static bool showTechnical;
        private static int diagnosticFamily;

        private SerializedDataParameter enable;
        private SerializedDataParameter debugMode;
        private SerializedDataParameter debugSemanticName;
        private SerializedDataParameter debugSelectionName, debugGroupId;
        private SerializedDataParameter debugCategory, debugTarget;
        private SerializedDataParameter debugInSceneView;
        private SerializedDataParameter debugInGameView;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<HoAttributeCompositeVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.enable));
            debugMode = Unpack(fetcher.Find(x => x.debugMode));
            debugSemanticName = Unpack(fetcher.Find(x => x.debugSemanticName));
            debugSelectionName = Unpack(fetcher.Find(x => x.debugSelectionName));
            debugGroupId = Unpack(fetcher.Find(x => x.debugGroupId));
            debugCategory = Unpack(fetcher.Find(x => x.debugCategory));
            debugTarget = Unpack(fetcher.Find(x => x.debugTarget));
            debugInSceneView = Unpack(fetcher.Find(x => x.debugInSceneView));
            debugInGameView = Unpack(fetcher.Find(x => x.debugInGameView));
        }

        public override void OnInspectorGUI()
        {
            DrawRuntime();
            DrawDebug();
        }

        private void DrawRuntime()
        {
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", LilUrpEditorSectionGui.BoolSummary(enable), RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawParameter(enable, "启用");
                EditorGUILayout.HelpBox(
                    "不勾选覆盖时用 Ho-AttributeComposite RendererFeature 的「运行」兜底值。",
                    MessageType.None);
            }
        }

        private void DrawDebug()
        {
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showDebug, "调试", LilUrpEditorSectionGui.EnumSummary(debugMode), DebugColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawViewSelector();
                HoAttributeCompositeDebugMode mode = (HoAttributeCompositeDebugMode)debugMode.value.enumValueIndex;
                if (mode == HoAttributeCompositeDebugMode.ColorOverview || mode == HoAttributeCompositeDebugMode.MaskPreview)
                    DrawPresentation(mode);
                EditorGUILayout.Space(4);
                bool technicalActive = mode != HoAttributeCompositeDebugMode.Off && mode < HoAttributeCompositeDebugMode.ColorOverview;
                showTechnical = EditorGUILayout.Foldout(showTechnical, technicalActive ? "技术诊断（正在显示）" : "技术诊断", true);
                if (showTechnical) DrawTechnical(mode);
                DrawParameter(debugInSceneView, "场景视图");
                DrawParameter(debugInGameView, "游戏视图");
            }
        }

        private void DrawViewSelector()
        {
            int mode = debugMode.value.intValue;
            int selected = mode == 0 ? 0 : mode == (int)HoAttributeCompositeDebugMode.ColorOverview ? 1 :
                mode == (int)HoAttributeCompositeDebugMode.MaskPreview ? 2 : -1;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(debugMode.overrideState, GUIContent.none, GUILayout.Width(15));
                using (new EditorGUI.DisabledScope(!debugMode.overrideState.boolValue))
                {
                    EditorGUI.BeginChangeCheck();
                    int next = GUILayout.Toolbar(selected, new[] { "关闭", "彩色总览", "单项遮罩" });
                    if (EditorGUI.EndChangeCheck()) debugMode.value.intValue = next == 0 ? 0 : next == 1 ?
                        (int)HoAttributeCompositeDebugMode.ColorOverview : (int)HoAttributeCompositeDebugMode.MaskPreview;
                }
            }
        }

        private void DrawPresentation(HoAttributeCompositeDebugMode mode)
        {
            EditorGUI.BeginChangeCheck();
            DrawParameter(debugCategory, "分类");
            if (EditorGUI.EndChangeCheck()) { debugTarget.value.stringValue = string.Empty; debugTarget.overrideState.boolValue = true; }
            var category = (HoACDebugCategory)debugCategory.value.intValue;
            var entries = HoACDebugCatalog.GetEntries(category);
            var labels = new string[entries.Count + 1];
            labels[0] = mode == HoAttributeCompositeDebugMode.ColorOverview ? "全部" : "请选择一项";
            int selected = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                labels[i + 1] = entries[i].Label;
                if (entries[i].Key == debugTarget.value.stringValue) selected = i + 1;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(debugTarget.overrideState, GUIContent.none, GUILayout.Width(15));
                using (new EditorGUI.DisabledScope(!debugTarget.overrideState.boolValue))
                {
                    EditorGUI.BeginChangeCheck();
                    int next = EditorGUILayout.Popup(mode == HoAttributeCompositeDebugMode.ColorOverview ? "突出显示" : "查看遮罩", selected, labels);
                    if (EditorGUI.EndChangeCheck()) debugTarget.value.stringValue = next == 0 ? string.Empty : entries[next - 1].Key;
                }
            }
            if (selected == 0 && !string.IsNullOrEmpty(debugTarget.value.stringValue))
                EditorGUILayout.HelpBox("目标已不存在，请重新选择。", MessageType.Warning);
            if (mode == HoAttributeCompositeDebugMode.ColorOverview && !string.IsNullOrEmpty(debugTarget.value.stringValue) && GUILayout.Button("显示全部"))
            { debugTarget.value.stringValue = string.Empty; debugTarget.overrideState.boolValue = true; }
            if (entries.Count == 0)
                EditorGUILayout.HelpBox(category == HoACDebugCategory.SelectionGroups ? "先在 OB 的「选区」中添加选择组和对象。" : "当前分类还没有登记项。", MessageType.None);
            else
            {
                // A searchable color legend replaces numeric IDs in the normal workflow.
                legendSearch = EditorGUILayout.TextField("查找名称", legendSearch);
                legendScroll = EditorGUILayout.BeginScrollView(legendScroll, GUILayout.MaxHeight(180));
                foreach (var entry in entries)
                {
                    if (!string.IsNullOrEmpty(legendSearch) && entry.Label.IndexOf(legendSearch, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Rect rect = EditorGUILayout.GetControlRect(false, 20);
                    EditorGUI.DrawRect(new Rect(rect.x + 2, rect.y + 3, 14, 14), entry.Color);
                    bool current = debugTarget.value.stringValue == entry.Key;
                    if (GUI.Button(new Rect(rect.x + 22, rect.y, rect.width - 22, rect.height), entry.Label,
                        current ? EditorStyles.miniButton : EditorStyles.label))
                    {
                        debugTarget.value.stringValue = current && mode == HoAttributeCompositeDebugMode.ColorOverview ? string.Empty : entry.Key;
                        debugTarget.overrideState.boolValue = true;
                        SceneView.RepaintAll();
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            if (mode == HoAttributeCompositeDebugMode.ColorOverview)
                EditorGUILayout.LabelField("点击色标旁的名称突出显示；重叠的选择组或语义会混色。", EditorStyles.wordWrappedMiniLabel);
        }

        private string legendSearch = string.Empty;
        private Vector2 legendScroll;

        private static readonly HoAttributeCompositeDebugMode[][] DiagnosticModes = {
            new[] { HoAttributeCompositeDebugMode.GeometryCoverage, HoAttributeCompositeDebugMode.OutlineCoverage, HoAttributeCompositeDebugMode.OutlineOwner, HoAttributeCompositeDebugMode.InputAvailability },
            new[] { HoAttributeCompositeDebugMode.ObjectCoverage, HoAttributeCompositeDebugMode.SurfaceWritten, HoAttributeCompositeDebugMode.SurfaceValue, HoAttributeCompositeDebugMode.FinalCoverage, HoAttributeCompositeDebugMode.SemanticCompare },
            new[] { HoAttributeCompositeDebugMode.SemanticOwnerMatch, HoAttributeCompositeDebugMode.WrittenCoverage, HoAttributeCompositeDebugMode.WeightedCoverage, HoAttributeCompositeDebugMode.SemanticPrecision },
            new[] { HoAttributeCompositeDebugMode.LaneCoverage, HoAttributeCompositeDebugMode.LaneSemanticId, HoAttributeCompositeDebugMode.LaneObjectMask, HoAttributeCompositeDebugMode.IdentityGroupCoverage, HoAttributeCompositeDebugMode.SelectionGroupCoverage }
        };

        private void DrawTechnical(HoAttributeCompositeDebugMode currentMode)
        {
            for (int i = 0; i < DiagnosticModes.Length; i++)
                if (System.Array.IndexOf(DiagnosticModes[i], currentMode) >= 0) diagnosticFamily = i;
            EditorGUI.BeginChangeCheck();
            int family = EditorGUILayout.Popup("检查内容", diagnosticFamily, new[] { "几何与输入", "语义合成", "关联与精度", "原始通道与兼容视图" });
            bool changedFamily = EditorGUI.EndChangeCheck();
            diagnosticFamily = family;
            var modes = DiagnosticModes[family];
            var labels = new string[modes.Length];
            for (int i = 0; i < modes.Length; i++) labels[i] = DiagnosticLabel(modes[i]);
            int selected = Mathf.Max(0, System.Array.IndexOf(modes, currentMode));
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup("诊断视图", selected, labels);
            if (EditorGUI.EndChangeCheck() || changedFamily)
            {
                debugMode.value.intValue = (int)modes[changedFamily ? 0 : next];
                debugMode.overrideState.boolValue = true;
                currentMode = (HoAttributeCompositeDebugMode)debugMode.value.intValue;
            }
            if (GUILayout.Button("显示此诊断")) { debugMode.value.intValue = (int)modes[selected]; debugMode.overrideState.boolValue = true; currentMode = modes[selected]; }
            bool semantic = currentMode >= HoAttributeCompositeDebugMode.ObjectCoverage && currentMode <= HoAttributeCompositeDebugMode.SemanticPrecision;
            if (semantic) DrawSemanticSelector();
            if (currentMode == HoAttributeCompositeDebugMode.SelectionGroupCoverage) DrawSelectionSelector();
            if (currentMode == HoAttributeCompositeDebugMode.IdentityGroupCoverage) DrawParameter(debugGroupId, "身份组 ID");
            string description = DescribeDebugMode(currentMode);
            if (!string.IsNullOrEmpty(description)) EditorGUILayout.HelpBox(description, MessageType.None);
        }

        private static string DiagnosticLabel(HoAttributeCompositeDebugMode mode)
        {
            switch (mode)
            {
                case HoAttributeCompositeDebugMode.GeometryCoverage: return "几何覆盖率";
                case HoAttributeCompositeDebugMode.OutlineCoverage: return "描边覆盖率";
                case HoAttributeCompositeDebugMode.OutlineOwner: return "描边归属";
                case HoAttributeCompositeDebugMode.InputAvailability: return "输入可用性";
                case HoAttributeCompositeDebugMode.ObjectCoverage: return "物体来源";
                case HoAttributeCompositeDebugMode.SurfaceWritten: return "表面是否写入";
                case HoAttributeCompositeDebugMode.SurfaceValue: return "表面权重";
                case HoAttributeCompositeDebugMode.FinalCoverage: return "合成结果";
                case HoAttributeCompositeDebugMode.SemanticCompare: return "合成过程对照";
                case HoAttributeCompositeDebugMode.SemanticOwnerMatch: return "表面归属匹配";
                case HoAttributeCompositeDebugMode.WrittenCoverage: return "关联写入覆盖率";
                case HoAttributeCompositeDebugMode.WeightedCoverage: return "关联加权覆盖率";
                case HoAttributeCompositeDebugMode.SemanticPrecision: return "样本关联精度";
                case HoAttributeCompositeDebugMode.LaneCoverage: return "语义通道覆盖率";
                case HoAttributeCompositeDebugMode.LaneSemanticId: return "语义通道编号";
                case HoAttributeCompositeDebugMode.LaneObjectMask: return "语义通道声明";
                case HoAttributeCompositeDebugMode.IdentityGroupCoverage: return "旧身份组遮罩";
                case HoAttributeCompositeDebugMode.SelectionGroupCoverage: return "旧选择组遮罩";
                default: return mode.ToString();
            }
        }

        private void DrawParameter(SerializedDataParameter parameter, string label)
        {
            if (parameter != null)
            {
                PropertyField(parameter, new GUIContent(label));
            }
        }

        private void DrawSemanticSelector()
        {
            var entries = HoSemanticSchema.Declarations;
            var labels = new string[entries.Count + 1];
            labels[0] = "未声明：" + debugSemanticName.value.stringValue;
            int current = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                labels[i + 1] = entries[i].displayName + "（" + entries[i].name + "）";
                if (entries[i].name == debugSemanticName.value.stringValue) current = i + 1;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(debugSemanticName.overrideState, GUIContent.none, GUILayout.Width(15));
                using (new EditorGUI.DisabledScope(!debugSemanticName.overrideState.boolValue))
                {
                    EditorGUI.BeginChangeCheck();
                    int selected = EditorGUILayout.Popup("调试语义", current, labels);
                    if (EditorGUI.EndChangeCheck() && selected > 0)
                        debugSemanticName.value.stringValue = entries[selected - 1].name;
                }
            }
        }

        /// <summary>通道含义与取景范围照 `Shaders/Debug/HoAttributeCompositeDebug.shader` 写，改 shader 就改这里。</summary>
        private void DrawSelectionSelector()
        {
            var names = HoObjectBufferRegistry.GetSelectionNames();
            var labels = new string[names.Count + 1];
            labels[0] = "未声明：" + debugSelectionName.value.stringValue;
            int current = 0;
            for (int i = 0; i < names.Count; i++)
            {
                labels[i + 1] = names[i];
                if (names[i] == debugSelectionName.value.stringValue) current = i + 1;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(debugSelectionName.overrideState, GUIContent.none, GUILayout.Width(15));
                using (new EditorGUI.DisabledScope(!debugSelectionName.overrideState.boolValue))
                {
                    EditorGUI.BeginChangeCheck();
                    int next = EditorGUILayout.Popup("调试选择组", current, labels);
                    if (EditorGUI.EndChangeCheck() && next > 0) debugSelectionName.value.stringValue = names[next - 1];
                }
            }
            if (names.Count == 0) EditorGUILayout.HelpBox("尚无选择组。先在 OB 组件的「选区」中创建一组并添加对象。", MessageType.Info);
        }

        private static string DescribeDebugMode(HoAttributeCompositeDebugMode mode)
        {
            switch (mode)
            {
                case HoAttributeCompositeDebugMode.IdentityGroupCoverage:
                    return "所选身份组的可见覆盖率，包含归属于该组的部件与描边。白 = 命中，黑 = 未命中。";
                case HoAttributeCompositeDebugMode.SelectionGroupCoverage:
                    return "OB 自定义选择组的可见覆盖率，包含成员对象及其描边。与内置语义 Lane 分开；同一对象可属于多个选择组。白 = 命中，黑 = 未命中，暗红 = 查询或输入缺失。";
                case HoAttributeCompositeDebugMode.LaneCoverage:
                    return "内置语义，非自定义选择组。上下半屏各一组：RGBA = 连续四条语义 lane 的覆盖率。";
                case HoAttributeCompositeDebugMode.LaneSemanticId:
                    return "上下半屏各一组：RGBA = 连续四条 lane 的 SemanticId（÷255 显示）。";
                case HoAttributeCompositeDebugMode.LaneObjectMask:
                    return "左右半屏各一条 lane：R = object 位（÷8），G = sourceMode（÷4），B = 是否在产出范围内。";
                case HoAttributeCompositeDebugMode.GeometryCoverage:
                    return "场景几何总覆盖率；单采样时由有效几何深度得到二值覆盖率。";
                case HoAttributeCompositeDebugMode.OutlineCoverage:
                    return "描边视觉壳覆盖率，与场景物理几何分开查询。";
                case HoAttributeCompositeDebugMode.OutlineOwner:
                    return "颜色区分原始 owner；黄=未登记，洋红=缺少描边 owner writer。";
                case HoAttributeCompositeDebugMode.InputAvailability:
                    return "R = 身份池可用，G = Selection 可用，B = 场景几何可用。";
                case HoAttributeCompositeDebugMode.ObjectCoverage:
                    return "所选语义的物体侧覆盖率；不包含表面权重。";
                case HoAttributeCompositeDebugMode.SurfaceWritten:
                    return "白 = 声明 ID 匹配并写入，黑 = 未写或 ID 不匹配；写零仍显示白。";
                case HoAttributeCompositeDebugMode.SurfaceValue:
                    return "所选语义的有效表面值；需与 Surface Written 区分未写和写零。";
                case HoAttributeCompositeDebugMode.SemanticOwnerMatch:
                    return "R = owner 在身份池内，G = owner 为主导身份，B = owner 非零；这是诊断，不是合成 gate。";
                case HoAttributeCompositeDebugMode.FinalCoverage:
                    return "所选语义的实际 Selection 覆盖率。";
                case HoAttributeCompositeDebugMode.SemanticCompare:
                    return "上排：物体 / 写入 / 表面值；下排：owner / 最终值 / 重算差（扣除 1 LSB 后×64）。每格显示完整画面。";
                case HoAttributeCompositeDebugMode.WrittenCoverage:
                    return "按 owner 关联的有效写入覆盖率 W；写零仍计入覆盖。";
                case HoAttributeCompositeDebugMode.WeightedCoverage:
                    return "按 owner 关联的加权语义贡献 V。";
                case HoAttributeCompositeDebugMode.SemanticPrecision:
                    return "绿 = 关联统计可用；橙 = 近似路径或样本位置不匹配。";
                default:
                    // Off：没有要解释的东西就不画那一行。
                    return string.Empty;
            }
        }
    }
}
