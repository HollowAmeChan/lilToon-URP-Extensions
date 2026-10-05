using lilToon.URP.Extensions.AttributeComposite;
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
        private static bool showDebug;

        private SerializedDataParameter enable;
        private SerializedDataParameter debugMode;
        private SerializedDataParameter debugSemanticName;
        private SerializedDataParameter debugInSceneView;
        private SerializedDataParameter debugInGameView;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<HoAttributeCompositeVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.enable));
            debugMode = Unpack(fetcher.Find(x => x.debugMode));
            debugSemanticName = Unpack(fetcher.Find(x => x.debugSemanticName));
            debugInSceneView = Unpack(fetcher.Find(x => x.debugInSceneView));
            debugInGameView = Unpack(fetcher.Find(x => x.debugInGameView));
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "逐相机覆盖 AC：启用与调试画面。语义声明和消费者登记汇总在 RendererFeature 上。",
                MessageType.Info);

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
                DrawParameter(debugMode, "调试模式");
                HoAttributeCompositeDebugMode mode = (HoAttributeCompositeDebugMode)debugMode.value.enumValueIndex;
                bool semanticDebug = mode >= HoAttributeCompositeDebugMode.ObjectCoverage && mode <= HoAttributeCompositeDebugMode.SemanticCompare;
                if (semanticDebug) DrawSemanticSelector();

                // 只画当前模式的说明：整张表铺出来就等于把说明又挪回下拉里。
                string description = DescribeDebugMode(mode);
                if (semanticDebug && !HoSemanticSchema.TryGetByName(debugSemanticName.value.stringValue, out _))
                    description = "未声明的语义名称：" + debugSemanticName.value.stringValue + "。";
                if (!string.IsNullOrEmpty(description))
                {
                    EditorGUILayout.HelpBox(description, MessageType.None);
                }

                DrawParameter(debugInSceneView, "Debug In Scene View");
                DrawParameter(debugInGameView, "Debug In Game View");
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
        private static string DescribeDebugMode(HoAttributeCompositeDebugMode mode)
        {
            switch (mode)
            {
                case HoAttributeCompositeDebugMode.LaneCoverage:
                    return "上下半屏各一组：RGBA = 连续四条 lane 的覆盖率。";
                case HoAttributeCompositeDebugMode.LaneSemanticId:
                    return "上下半屏各一组：RGBA = 连续四条 lane 的 SemanticId（÷255 显示）。";
                case HoAttributeCompositeDebugMode.LaneObjectMask:
                    return "左右半屏各一条 lane：R = object 位（÷8），G = sourceMode（÷4），B = 是否在产出范围内。";
                case HoAttributeCompositeDebugMode.GeometryCoverage:
                    return "场景几何总覆盖率；单采样时由有效几何深度得到二值覆盖率。";
                case HoAttributeCompositeDebugMode.OutlineCoverage:
                    return "描边视觉壳覆盖率，与场景物理几何分开查询。";
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
                default:
                    // Off：没有要解释的东西就不画那一行。
                    return string.Empty;
            }
        }
    }
}
