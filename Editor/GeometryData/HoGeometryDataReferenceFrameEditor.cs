using lilToon.URP.Extensions.GeometryData;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GeometryData
{
    [CustomEditor(typeof(HoGeometryDataReferenceFrame)), CanEditMultipleObjects]
    internal sealed class HoGeometryDataReferenceFrameEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("方向来源是骨骼 Transform；作用对象是 Renderer 列表。眼透从 GD 输出关联屏幕身份，OB 不需要引用此组件。空列表不扩展到整个角色。", MessageType.None);
            if (GUILayout.Button("添加子级 Renderer"))
            {
                foreach (HoGeometryDataReferenceFrame producer in targets)
                {
                    Undo.RecordObject(producer, "添加 GD 参考系作用对象");
                    foreach (var renderer in producer.GetComponentsInChildren<Renderer>(true))
                        if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer) && !producer.targetRenderers.Contains(renderer))
                            producer.targetRenderers.Add(renderer);
                    EditorUtility.SetDirty(producer);
                }
                serializedObject.Update();
            }
            if (targets.Length == 1 && ((HoGeometryDataReferenceFrame)target).TryGetFrame(out var frame))
            {
                int count = 0;
                foreach (var renderer in ((HoGeometryDataReferenceFrame)target).GetTargetRenderers()) count++;
                if (count == 0) EditorGUILayout.HelpBox("尚未指定作用 Renderer；目前只有参考系数据，眼透没有可关联的作用对象。", MessageType.Info);
                EditorGUILayout.Space();
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Vector3Field("世界原点", frame.originValid);
                    EditorGUILayout.Vector3Field("世界前轴", frame.forward);
                    EditorGUILayout.Vector3Field("世界右轴", frame.right);
                    EditorGUILayout.Vector3Field("世界上轴", frame.up);
                }
            }
        }
        private void OnSceneGUI()
        {
            var producer = (HoGeometryDataReferenceFrame)target;
            if (producer.TryGetFrame(out var frame)) DrawFrame(frame, "GD 参考系");
        }
        private static void DrawFrame(HoGeometryFrameData frame, string label)
        {
            Vector3 origin = frame.originValid;
            float size = HandleUtility.GetHandleSize(origin) * 0.6f;
            Color previous = Handles.color;
            DrawAxis(origin, frame.forward, size, Color.blue, "前");
            DrawAxis(origin, frame.right, size, Color.red, "右");
            DrawAxis(origin, frame.up, size, Color.green, "上");
            Handles.color = Color.white; Handles.Label(origin, label); Handles.color = previous;
        }
        private static void DrawAxis(Vector3 origin, Vector3 direction, float size, Color color, string label)
        {
            if (direction.sqrMagnitude < 0.0001f) return;
            Handles.color = color;
            Handles.ArrowHandleCap(0, origin, Quaternion.LookRotation(direction), size, EventType.Repaint);
            Handles.Label(origin + direction * size, label);
        }
    }

}
