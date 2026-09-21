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
                "逐相机覆盖；未勾选覆盖的字段用 RendererFeature 兜底值。接收对象由场景组件声明。",
                MessageType.Info);

            DrawRuntime();
            DrawSoftShadow();
            DrawDebug();
        }

        private void DrawRuntime()
        {
            string summary = LilUrpEditorSectionGui.BoolSummary(enable)
                + " / " + LilUrpEditorSectionGui.EnumSummary(resolution)
                + " / " + LilUrpEditorSectionGui.FloatSummary(softnessRadius);
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawParameter(enable, "启用");
                DrawParameter(resolution, "单角色分辨率");
                DrawParameter(softnessRadius, "阴影软边（米）");
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
                DrawParameter(pcssQuality, "质量档");
                DrawParameter(pcssSoftness, "半影放大");
                DrawParameter(pcssBlockerSearchRadius, "Blocker 搜索半径（米）");
                DrawParameter(pcssMaxPenumbraRadius, "半影半径上限（米）");
                DrawParameter(pcssDepthBias, "Blocker 深度偏移");

                EditorGUILayout.HelpBox(
                    "Add Override 会把该组件所有字段都设成覆盖态，所以 feature 上改不动是正常的（要么在这里改，要么把覆盖勾掉）。",
                    MessageType.None);
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
                        "整张图集：所有接收域的 tile 排一张图，画光空间线性深度（越亮越远）。",
                        MessageType.None);
                }
                else if (mode == (int)HoCharacterShadowDebugMode.Character)
                {
                    EditorGUILayout.HelpBox(
                        "只放大「单角色 tile」那个 tile（编号见组件 Inspector 的「图集 Tile」；超范围显示紫色）。",
                        MessageType.None);
                }

                DrawParameter(debugInSceneView, "Debug In Scene View");
                DrawParameter(debugInGameView, "Debug In Game View");
                DrawParameter(debugCharacter, "单角色 tile");
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
