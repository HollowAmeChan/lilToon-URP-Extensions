using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEngine;
namespace lilToon.URP.Extensions.Editor.RTDI
{
    [CustomEditor(typeof(HoRTDIRendererFeature))]
    internal sealed class HoRTDIRendererFeatureEditor:UnityEditor.Editor
    {
        private static readonly Color RuntimeColor=new Color(.46f,.64f,.92f),AdvancedColor=new Color(.62f,.58f,.78f);
        private static bool runtime=true,advanced;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();var settings=serializedObject.FindProperty("settings");
            EditorGUILayout.HelpBox("需要 GB/SB 同分辨率单采样输入与 D3D12；当前提供独立光照，不自动合成。",MessageType.Info);
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref runtime,"运行","RTXDI DI",RuntimeColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Draw(settings,"enabled","启用");Draw(settings,"localSampling","局部灯采样");Draw(settings,"initialSamples","局部灯候选数");Draw(settings,"temporal","时间重采样");Draw(settings,"maxHistory","历史长度");
                Draw(settings,"spatial","空间重采样");Draw(settings,"spatialSamples","邻域样本数");Draw(settings,"spatialRadius","邻域半径");
            }
            EditorGUILayout.HelpBox("调试在 Ho-RTDI Volume 的「调试」分组。",MessageType.Info);
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref advanced,"高级","筛选 / Shader",AdvancedColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Draw(settings,"rayBias","射线偏移");Draw(settings,"geometryLayers","几何层");Draw(settings,"lightLayers","灯所在物体层");Draw(settings,"inSceneView","Scene View 中生产");
                EditorGUILayout.PropertyField(serializedObject.FindProperty("shader"),new GUIContent("Compute Shader"));
            }
            serializedObject.ApplyModifiedProperties();EditorGUILayout.LabelField("运行状态",((HoRTDIRendererFeature)target).Status);
        }
        private static void Draw(SerializedProperty settings,string name,string label)
        {
            var property=settings.FindPropertyRelative(name);
            EditorGUILayout.PropertyField(property,new GUIContent(label,property.tooltip));
        }
    }
}
