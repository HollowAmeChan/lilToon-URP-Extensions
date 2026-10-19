using lilToon.URP.Extensions.RTGI;
using UnityEditor;
using UnityEngine;
namespace lilToon.URP.Extensions.Editor.RTGI
{
    [CustomEditor(typeof(HoRTGIRendererFeature))]
    internal sealed class HoRTGIRendererFeatureEditor:UnityEditor.Editor
    {
        private static readonly Color RuntimeColor=new Color(.46f,.64f,.92f),AdvancedColor=new Color(.62f,.58f,.78f);
        private static bool runtime=true,advanced;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();var settings=serializedObject.FindProperty("settings");
            EditorGUILayout.HelpBox("需要 GB/SB 同分辨率单采样输入与 D3D12；在 Ho-RTGI Volume 中开启场景合成并调整强度。",MessageType.Info);
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref runtime,"运行","ReSTIR GI",RuntimeColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Draw(settings,"enabled","启用");Draw(settings,"denoise","独立 GI 降噪");Draw(settings,"secondaryLightSamples","次级灯光采样数");Draw(settings,"temporal","时间重采样");Draw(settings,"maxHistory","历史长度");
                Draw(settings,"spatial","空间重采样");Draw(settings,"spatialSamples","邻域样本数");Draw(settings,"spatialRadius","邻域半径");
            }
            EditorGUILayout.HelpBox("调试在 Ho-RTGI Volume 的「调试」分组。",MessageType.Info);
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref advanced,"高级","筛选 / Shader",AdvancedColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Draw(settings,"rayBias","射线偏移");Draw(settings,"maxDistance","几何追踪距离");Draw(settings,"useEnvironment","使用大气环境");
                Draw(settings,"secondaryEnvironmentSamples","次级天空候选数");Draw(settings,"includeDirectEnvironment","额外包含主表面直接天空");
                EditorGUILayout.HelpBox("与 RTDI 配合时，主表面直接天空由 RTDI 提供；GI 默认只增加表面反弹。GI 单独使用时可显式包含直接天空。",MessageType.Info);
                Draw(settings,"geometryLayers","几何层");Draw(settings,"lightLayers","灯所在物体层");Draw(settings,"inSceneView","Scene View 中生产");
                Draw(settings,"excludedRayMaterials","不参与求交的材质");
                Draw(settings,"composite","未覆盖时合成到场景");Draw(settings,"intensity","未覆盖时间接光强度");
                EditorGUILayout.PropertyField(serializedObject.FindProperty("shader"),new GUIContent("Compute Shader"));EditorGUILayout.PropertyField(serializedObject.FindProperty("rayShader"),new GUIContent("Ray Tracing Shader"));EditorGUILayout.PropertyField(serializedObject.FindProperty("secondaryMaterialShader"),new GUIContent("命中材质 Shader"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("compositeShader"),new GUIContent("合成 Shader"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("nrdShaders"),new GUIContent("GI NRD Shaders"));
            }
            serializedObject.ApplyModifiedProperties();EditorGUILayout.LabelField("运行状态",((HoRTGIRendererFeature)target).Status);
        }
        private static void Draw(SerializedProperty settings,string name,string label)=>EditorGUILayout.PropertyField(settings.FindPropertyRelative(name),new GUIContent(label));
    }
}
