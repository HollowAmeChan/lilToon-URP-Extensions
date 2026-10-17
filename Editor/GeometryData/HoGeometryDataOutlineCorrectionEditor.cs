using lilToon.URP.Extensions.GeometryData;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GeometryData
{
    [CustomEditor(typeof(HoGeometryDataOutlineCorrection)),CanEditMultipleObjects]
    internal sealed class HoGeometryDataOutlineCorrectionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if(GUILayout.Button("生成 / 更新描边修正"))
            {
                foreach(Object item in targets)
                {
                    var correction=(HoGeometryDataOutlineCorrection)item;
                    Undo.RecordObject(correction,"Prepare Ho outline correction");
                    if (!correction.Prepare()) Debug.LogError(correction.Status, correction);
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
