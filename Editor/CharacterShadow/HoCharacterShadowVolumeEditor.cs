using lilToon.URP.Extensions.CharacterShadow;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterShadow
{
    [CustomEditor(typeof(HoCharacterShadowVolume))]
    internal sealed class HoCharacterShadowVolumeEditor : HoVolumeComponentEditor
    {




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

            DrawRuntime();
            DrawSoftShadow();
            DrawDebug();
        }

        private void DrawRuntime()
        {
            string summary = LilUrpEditorSectionGui.BoolSummary(enable)
                + " / " + LilUrpEditorSectionGui.EnumSummary(resolution)
                + " / " + LilUrpEditorSectionGui.FloatSummary(softnessRadius);
            if (!HoVolumeEditorGUI.Section(ref showRuntime, "运行", summary))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope())
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
            if (!HoVolumeEditorGUI.Section(ref showSoftShadow, "软阴影（PCSS）", summary))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope())
            {
                DrawParameter(pcssEnabled, "启用 PCSS");
                DrawParameter(pcssQuality, "质量档");
                DrawParameter(pcssSoftness, "光源角直径（度）");
                DrawParameter(pcssBlockerSearchRadius, "Blocker 搜索半径（米）");
                DrawParameter(pcssMaxPenumbraRadius, "半影半径上限（米）");
                DrawParameter(pcssDepthBias, "Blocker 深度偏移");

            }
        }

        private void DrawDebug()
        {
            string summary = LilUrpEditorSectionGui.EnumSummary(debugMode);
            if (!HoVolumeEditorGUI.Section(ref showDebug, "调试", summary))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope())
            {
                int mode = debugMode != null && debugMode.value != null ? debugMode.value.intValue : 0;
                string desc = null;
                if (mode == (int)HoCharacterShadowDebugMode.Atlas)
                {
                    desc = "整张图集：所有接收域的 tile 排一张图，画光空间线性深度（越亮越远）。";
                }
                else if (mode == (int)HoCharacterShadowDebugMode.All)
                {
                    desc = "平铺整张图集与所选角色 tile，在最终后处理之后显示。";
                }
                else if (mode == (int)HoCharacterShadowDebugMode.Character)
                {
                    desc = "只放大「单角色 tile」那个 tile（编号见组件 Inspector 的「图集 Tile」；超范围显示紫色）。";
                }

                DrawParameter(debugMode, "调试模式", desc);

                DrawParameter(debugInSceneView, "场景视图");
                DrawParameter(debugInGameView, "游戏视图");
                DrawParameter(debugCharacter, "单角色 tile");
            }
        }

        private void DrawParameter(SerializedDataParameter parameter, string label, string tooltip = null)
        {
            if (parameter != null)
            {
                PropertyField(parameter, new GUIContent(label, tooltip));
            }
        }
    }
}
