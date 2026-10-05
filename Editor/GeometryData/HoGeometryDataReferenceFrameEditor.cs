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
            if (targets.Length == 1 && ((HoGeometryDataReferenceFrame)target).TryGetFrame(out var frame))
            {
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
            if (producer.TryGetFrame(out var frame)) DrawFrame(frame, "默认参考系");
            foreach (var part in producer.parts)
                if (part != null && part.reference != null && producer.TryGetFrame(part.partName, out var partFrame)) DrawFrame(partFrame, part.partName);
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
