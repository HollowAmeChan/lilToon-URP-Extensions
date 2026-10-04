using lilToon.URP.Extensions.GeometryData;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GeometryData
{
    [CustomEditor(typeof(HoOutlineCorrection)),CanEditMultipleObjects]
    internal sealed class HoOutlineCorrectionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Renderer 添加 HoGeometryData；lilToon 描边修正来源选择 GD。结果不写入 Mesh 顶点色。",MessageType.None);
            if(GUILayout.Button("生成 / 更新描边修正"))
            {
                foreach(Object item in targets)
                {
                    var correction=(HoOutlineCorrection)item;
                    Undo.RecordObject(correction,"Prepare Ho outline correction");
                    correction.Prepare();EditorUtility.SetDirty(correction);
                }
            }
            if(targets.Length==1)EditorGUILayout.HelpBox(((HoOutlineCorrection)target).Status,MessageType.Info);
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
                foreach(var producer in Resources.FindObjectsOfTypeAll<HoOutlineCorrection>())
                    if(producer!=null && producer.gameObject.scene.IsValid() && producer.isActiveAndEnabled && producer.prepareBeforePlay)
                        producer.Prepare();
            };
        }
    }
}
