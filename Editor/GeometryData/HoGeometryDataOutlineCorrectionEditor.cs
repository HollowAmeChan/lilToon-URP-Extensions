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
            EditorGUILayout.HelpBox("Renderer 添加 HoGeometryData；lilToon 描边修正来源选择 GD。结果不写入 Mesh 顶点色。",MessageType.None);
            if(GUILayout.Button("生成 / 更新描边修正"))
            {
                foreach(Object item in targets)
                {
                    var correction=(HoGeometryDataOutlineCorrection)item;
                    Undo.RecordObject(correction,"Prepare Ho outline correction");
                    correction.Prepare();EditorUtility.SetDirty(correction);
                }
            }
            if(targets.Length==1)
            {
                var producer=(HoGeometryDataOutlineCorrection)target;
                EditorGUILayout.LabelField("GD 槽",(HoGeometryRendererBinding.GetManagedValue(producer.targetRenderer)>>16).ToString());
                EditorGUILayout.LabelField("发布状态",HoOutlineDataRegistry.IsPublished(producer)?"已发布":"尚未发布");
                EditorGUILayout.HelpBox(producer.Status,MessageType.Info);
                EditorGUILayout.HelpBox("在 Renderer 的 Ho-GeometryData → 调试选择描边方向 / 厚度，直接检查生产结果。",MessageType.None);
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
