using lilToon.URP.Extensions.GeometryData;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GeometryData
{
    [CustomEditor(typeof(HoGeometryReferenceFrame)), CanEditMultipleObjects]
    internal sealed class HoGeometryReferenceFrameEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (targets.Length == 1 && ((HoGeometryReferenceFrame)target).TryGetFrame(out var frame))
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
    }

}
