using lilToon.URP.Extensions.CharacterShadow;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterShadow
{
    [CustomEditor(typeof(HoCharacterShadowVolume))]
    internal sealed class HoCharacterShadowVolumeEditor : VolumeComponentEditor
    {
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color SoftShadowColor = new Color(0.42f, 0.72f, 0.58f);
        private static readonly Color DebugColor = new Color(0.86f, 0.62f, 0.38f);

        private static bool showRuntime = true;
        private static bool showSoftShadow = true;
        private static bool showDebug;

        private SerializedDataParameter enable;
        private SerializedDataParameter resolution;
        private SerializedDataParameter pcssEnabled;
        private SerializedDataParameter softnessRadius;
        private SerializedDataParameter pcssQuality;
        private SerializedDataParameter pcssSoftness;
        private SerializedDataParameter pcssBlockerSearchRadius;
        private SerializedDataParameter pcssMaxPenumbraRadius;
        private SerializedDataParameter pcssDepthBias;
        private SerializedDataParameter debugMode;
        private SerializedDataParameter debugInSceneView;
        private SerializedDataParameter debugInGameView;
        private SerializedDataParameter debugCharacter;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<HoCharacterShadowVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.enable));
            resolution = Unpack(fetcher.Find(x => x.resolution));
            pcssEnabled = Unpack(fetcher.Find(x => x.pcssEnabled));
            softnessRadius = Unpack(fetcher.Find(x => x.softnessRadius));
            pcssQuality = Unpack(fetcher.Find(x => x.pcssQuality));
            pcssSoftness = Unpack(fetcher.Find(x => x.pcssSoftness));
            pcssBlockerSearchRadius = Unpack(fetcher.Find(x => x.pcssBlockerSearchRadius));
            pcssMaxPenumbraRadius = Unpack(fetcher.Find(x => x.pcssMaxPenumbraRadius));
            pcssDepthBias = Unpack(fetcher.Find(x => x.pcssDepthBias));
            debugMode = Unpack(fetcher.Find(x => x.debugMode));
            debugInSceneView = Unpack(fetcher.Find(x => x.debugInSceneView));
            debugInGameView = Unpack(fetcher.Find(x => x.debugInGameView));
            debugCharacter = Unpack(fetcher.Find(x => x.debugCharacter));
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "逐相机覆盖 Ho-CharacterShadow：启用、单角色分辨率、软阴影与调试画面。接收对象（OB 组、接收部件、包围盒）"
                + "仍然由场景里 Ho-CharacterShadow 组件声明，不在这里。未勾选覆盖的字段用 RendererFeature 上的兜底值。",
                MessageType.Info);

            DrawRuntime();
            DrawSoftShadow();
            DrawDebug();
        }

        private void DrawRuntime()
        {
            string summary = LilUrpEditorSectionGui.BoolSummary(enable)
                + " / " + LilUrpEditorSectionGui.EnumSummary(resolution);
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawParameter(enable, "启用");
                DrawParameter(resolution, "单角色分辨率");
                EditorGUILayout.HelpBox(
                    "不勾选覆盖时用 Ho-CharacterShadow RendererFeature 的「运行」兜底值。分辨率提高会同时提高图集占用："
                    + "图集可容纳的 tile 数按「图集边长上限 / 单角色分辨率」换算。",
                    MessageType.None);
            }
        }

        private void DrawSoftShadow()
        {
            string summary = LilUrpEditorSectionGui.BoolSummary(pcssEnabled)
                + " / " + LilUrpEditorSectionGui.EnumSummary(pcssQuality)
                + " / " + LilUrpEditorSectionGui.FloatSummary(pcssSoftness);
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSoftShadow, "软阴影（PCSS）", summary, SoftShadowColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawParameter(pcssEnabled, "启用 PCSS");
                DrawParameter(softnessRadius, "最低软度（米）");
                DrawParameter(pcssQuality, "质量档");
                DrawParameter(pcssSoftness, "半影放大");
                DrawParameter(pcssBlockerSearchRadius, "Blocker 搜索半径（米）");
                DrawParameter(pcssMaxPenumbraRadius, "半影半径上限（米）");
                DrawParameter(pcssDepthBias, "Blocker 深度偏移");

                EditorGUILayout.HelpBox(
                    "PCSS 先在 blocker 搜索盘里找遮挡物，用平均遮挡深度估半影宽度，再按该宽度做可变半径滤波 —— 离遮挡物越远边缘越软。"
                    + "关掉、或半影放大为 0 时回退「最低软度」那个固定半径的旋转盘 PCF（降级即回退）。质量档只决定采样数。",
                    MessageType.None);

                EditorGUILayout.HelpBox(
                    "**软阴影半径一律是米（世界单位）**：tile 越细（单角色分辨率越高），同样世界半径吃掉的 texel 越多、"
                    + "同样采样数铺开也越稀；超出采样预算时半径会被收窄，以免出现颗粒噪点。想要更软的边又不想出噪点，"
                    + "优先把「单角色分辨率」降到 1024/2048，或提高质量档（Ultra = 32/64 采样）。",
                    MessageType.None);

                EditorGUILayout.HelpBox(
                    "这一组在 RendererFeature 上也有一份兜底值。**本面板勾了覆盖就以这里为准**（Add Override 会把该组件所有"
                    + "字段都设成覆盖态，包括「启用 PCSS」），所以 feature 上改不动是正常的 —— 要么在这里改，要么把对应字段的"
                    + "覆盖勾掉。关闭 PCSS 后仍会保留「最低软度」那一档抗锯齿滤波；要完全硬边就把它设为 0。",
                    MessageType.None);

                if (pcssEnabled != null && pcssEnabled.value != null && !pcssEnabled.value.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "PCSS 关闭：边缘走固定半径的旋转盘 PCF（半影不会随遮挡距离变化），半径就是上面的「最低软度」。",
                        MessageType.None);
                }
            }
        }

        private void DrawDebug()
        {
            string summary = LilUrpEditorSectionGui.EnumSummary(debugMode);
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showDebug, "调试", summary, DebugColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawParameter(debugMode, "调试模式");

                int mode = debugMode != null && debugMode.value != null ? debugMode.value.enumValueIndex : 0;
                if (mode == (int)HoCharacterShadowDebugMode.Atlas)
                {
                    EditorGUILayout.HelpBox(
                        "整张图集：同一时刻所有接收域的 tile 排在一张图里，画的是光空间线性深度（越亮越远）。"
                        + "总图集边长 = ceil(sqrt(域数)) × 单角色分辨率。",
                        MessageType.None);
                }
                else if (mode == (int)HoCharacterShadowDebugMode.Character)
                {
                    EditorGUILayout.HelpBox(
                        "单个接收域：只放大「单角色 tile」那一个 tile，编号见 Ho-CharacterShadow 组件 Inspector 的「图集 Tile」。"
                        + "编号超出已分配数量时显示紫色。",
                        MessageType.None);
                }

                DrawParameter(debugInSceneView, "Debug In Scene View");
                DrawParameter(debugInGameView, "Debug In Game View");
                DrawParameter(debugCharacter, "单角色 tile");

                if (debugInGameView != null && debugInGameView.value != null && debugInGameView.value.boolValue)
                {
                    EditorGUILayout.HelpBox("Game View 调试是直接替换最终画面；做完检查记得把调试模式改回 Off。", MessageType.Warning);
                }

                EditorGUILayout.HelpBox(
                    "调试画面只有这一份真值：RendererFeature 上不再放调试开关。调试视图没有独立强度曲线——"
                    + "它直出光空间深度，加曲线会让人把显示亮度误读成深度。",
                    MessageType.None);
            }
        }

        private void DrawParameter(SerializedDataParameter parameter, string label)
        {
            if (parameter != null)
            {
                PropertyField(parameter, new GUIContent(label));
            }
        }
    }
}
