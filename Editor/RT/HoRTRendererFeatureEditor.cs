using lilToon.URP.Extensions.RT;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RT
{
    [CustomEditor(typeof(HoRTRendererFeature))]
    internal sealed class HoRTRendererFeatureEditor : UnityEditor.Editor
    {
        private static bool status = true;
        public override void OnInspectorGUI()
        {
            var feature = (HoRTRendererFeature)target;
            EditorGUILayout.HelpBox("RT 公共资源宿主，放在 RTDI / RTGI / RTShadow 前。消费者声明所需场景，本模块共享姿态并管理 AS / 材质资源寿命；质量、输出和调试在对应消费者配置。没有消费者时不主动追踪。", MessageType.Info);
            if (LilUrpEditorSectionGui.DrawSectionHeader(ref status, "运行状态", feature.Status, new Color(.46f, .64f, .92f)))
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("状态", feature.Status, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("活动 Renderer 域", feature.ActiveDomainCount.ToString());
                EditorGUILayout.LabelField("场景 View", feature.ViewCount.ToString());
                EditorGUILayout.LabelField("累计共享姿态 Bake", feature.BakeCount.ToString());
                EditorGUILayout.LabelField("上次准备等待（ms）", feature.LastPrepareWaitMilliseconds.ToString("0.###"));
                EditorGUILayout.LabelField("待退休域 / 消费资源", $"{HoRTRendererFeature.RetiredDomainCount} / {HoRTRendererFeature.RetiredResourceCount}");
                EditorGUILayout.HelpBox("等待值是宿主准备阶段的 CPU fence 等待，不是 GPU 整帧耗时；Bake 计数也不代表性能提升。", MessageType.None);
            }
        }
    }
}
