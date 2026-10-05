using lilToon.URP.Extensions.GeometryData;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GeometryData
{
    [CustomEditor(typeof(HoGeometryDataTension)), CanEditMultipleObjects]
    internal sealed class HoGeometryDataTensionEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => true;
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("生成 / 更新张力参考状态"))
                foreach (Object item in targets)
                {
                    var p = (HoGeometryDataTension)item; Undo.RecordObject(p, "Prepare GeometryData tension");
                    p.Prepare(); EditorUtility.SetDirty(p);
                }
            if (targets.Length != 1) return;
            var producer = (HoGeometryDataTension)target;
            EditorGUILayout.LabelField("GD 槽", (HoGeometryRendererBinding.GetManagedValue(producer.targetRenderer) >> 16).ToString());
            EditorGUILayout.LabelField("顶点 / 面", $"{producer.VertexCount} / {producer.TriangleCount}");
            EditorGUILayout.LabelField("参考版本", producer.ReferenceVersion.ToString());
            EditorGUILayout.LabelField("最近生产帧 / 次数", $"{producer.LastProducedFrame} / {producer.ProductionCount}");
            EditorGUILayout.HelpBox("在 Renderer 的 Ho-GeometryData → 调试选择 Tesion 拉伸 / 挤压 / 角变化 / 有效性，不需要先开启材质 Tesion。", MessageType.None);
            EditorGUILayout.HelpBox(producer.Status, MessageType.Info);
            if (producer.targetRenderer != null && !producer.targetRenderer.updateWhenOffscreen)
                EditorGUILayout.HelpBox("离屏来源需要 Renderer 的 Update When Offscreen，才能保证 native skinning 持续提供新姿势。", MessageType.Info);
        }
    }
    [InitializeOnLoad]
    internal static class HoTensionBeforePlay
    {
        static HoTensionBeforePlay()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingEditMode) return;
                foreach (var p in Resources.FindObjectsOfTypeAll<HoGeometryDataTension>())
                    if (p != null && p.gameObject.scene.IsValid() && p.isActiveAndEnabled && p.prepareBeforePlay) p.Prepare();
            };
        }
    }
}
