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
        private static readonly Color DebugColor = new Color(0.86f, 0.62f, 0.38f);

        private static bool showRuntime = true;
        private static bool showDebug;

        private SerializedDataParameter enable;
        private SerializedDataParameter resolution;
        private SerializedDataParameter debugMode;
        private SerializedDataParameter debugInSceneView;
        private SerializedDataParameter debugInGameView;
        private SerializedDataParameter debugCharacter;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<HoCharacterShadowVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.enable));
            resolution = Unpack(fetcher.Find(x => x.resolution));
            debugMode = Unpack(fetcher.Find(x => x.debugMode));
            debugInSceneView = Unpack(fetcher.Find(x => x.debugInSceneView));
            debugInGameView = Unpack(fetcher.Find(x => x.debugInGameView));
            debugCharacter = Unpack(fetcher.Find(x => x.debugCharacter));
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "逐相机覆盖 Ho-CharacterShadow：启用、单角色分辨率与调试画面。接收对象（OB 组、接收部件、包围盒）"
                + "仍然由场景里 Ho-CharacterShadow 组件声明，不在这里。未勾选覆盖的字段用 RendererFeature 上的兜底值。",
                MessageType.Info);

            DrawRuntime();
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
