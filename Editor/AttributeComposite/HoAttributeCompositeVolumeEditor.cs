using lilToon.URP.Extensions.AttributeComposite;
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
        private SerializedDataParameter enable;
        private SerializedDataParameter debugMode;
        private SerializedDataParameter debugInSceneView;
        private SerializedDataParameter debugInGameView;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<HoAttributeCompositeVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.enable));
            debugMode = Unpack(fetcher.Find(x => x.debugMode));
            debugInSceneView = Unpack(fetcher.Find(x => x.debugInSceneView));
            debugInGameView = Unpack(fetcher.Find(x => x.debugInGameView));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(enable);
            PropertyField(debugMode);

            // 只画当前模式的说明：整张表铺出来就等于把说明又挪回下拉里。
            string description = DescribeDebugMode((HoAttributeCompositeDebugMode)debugMode.value.enumValueIndex);
            if (!string.IsNullOrEmpty(description))
            {
                EditorGUILayout.HelpBox(description, MessageType.None);
            }

            PropertyField(debugInSceneView);
            PropertyField(debugInGameView);
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
                default:
                    // Off：没有要解释的东西就不画那一行。
                    return string.Empty;
            }
        }
    }
}
