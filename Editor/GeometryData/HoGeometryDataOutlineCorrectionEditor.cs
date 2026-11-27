using lilToon.URP.Extensions.GeometryData;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GeometryData
{
    [CustomEditor(typeof(HoGeometryDataOutlineCorrection)),CanEditMultipleObjects]
    internal sealed class HoGeometryDataOutlineCorrectionEditor : UnityEditor.Editor
    {
        private SerializedProperty script;
        private SerializedProperty targetScope;
        private SerializedProperty targetRenderers;
        private SerializedProperty prepareOnInitialize;
        private SerializedProperty prepareBeforePlay;
        private SerializedProperty weldCoincidentVertices;

        private void OnEnable()
        {
            script = serializedObject.FindProperty("m_Script");
            targetScope = serializedObject.FindProperty(nameof(HoGeometryDataOutlineCorrection.targetScope));
            targetRenderers = serializedObject.FindProperty(nameof(HoGeometryDataOutlineCorrection.targetRenderers));
            prepareOnInitialize = serializedObject.FindProperty(nameof(HoGeometryDataOutlineCorrection.prepareOnInitialize));
            prepareBeforePlay = serializedObject.FindProperty(nameof(HoGeometryDataOutlineCorrection.prepareBeforePlay));
            weldCoincidentVertices = serializedObject.FindProperty(nameof(HoGeometryDataOutlineCorrection.weldCoincidentVertices));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(script);
            EditorGUILayout.PropertyField(targetScope);
            if (!targetScope.hasMultipleDifferentValues &&
                targetScope.enumValueIndex == (int)HoGeometryDataOutlineCorrection.TargetScope.Manual)
                EditorGUILayout.PropertyField(targetRenderers, true);
            EditorGUILayout.PropertyField(prepareOnInitialize);
            EditorGUILayout.PropertyField(prepareBeforePlay);
            EditorGUILayout.PropertyField(weldCoincidentVertices);
            serializedObject.ApplyModifiedProperties();

            if (targets.Length == 1 && target is HoGeometryDataOutlineCorrection selected)
                EditorGUILayout.LabelField("已准备 / 目标", $"{selected.PreparedRendererCount} / {selected.TargetCount}");

            if (GUILayout.Button("生成 / 更新描边修正"))
            {
                foreach (HoGeometryDataOutlineCorrection correction in targets)
                {
                    Undo.RecordObject(correction, "Prepare Ho outline correction");
                    if (!correction.Prepare()) Debug.LogError(correction.Status, correction);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(correction);
                    EditorUtility.SetDirty(correction);
                }
            }
        }
    }

    [InitializeOnLoad]
    internal static class HoOutlineBeforePlay
    {
        static HoOutlineBeforePlay()
        {
            EditorApplication.playModeStateChanged+=state=>
            {
                if(state!=PlayModeStateChange.ExitingEditMode)return;
                foreach(var producer in Resources.FindObjectsOfTypeAll<HoGeometryDataOutlineCorrection>())
                    if(producer!=null && producer.gameObject.scene.IsValid() && producer.isActiveAndEnabled && producer.prepareBeforePlay)
                        producer.Prepare();
            };
        }
    }
}
