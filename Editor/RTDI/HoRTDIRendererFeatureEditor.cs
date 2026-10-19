using lilToon.URP.Extensions.RTDI;
using lilToon.URP.Extensions.RTDI.NRD;
using UnityEditor;
using UnityEngine;
namespace lilToon.URP.Extensions.Editor.RTDI
{
    [CustomEditor(typeof(HoRTDIRendererFeature))]
    internal sealed class HoRTDIRendererFeatureEditor:UnityEditor.Editor
    {
        private static readonly Color RuntimeColor=new Color(.46f,.64f,.92f),AdvancedColor=new Color(.62f,.58f,.78f);
        private static bool runtime=true,quality,advanced;
        private void OnEnable()=>((HoRTDIRendererFeature)target).EnsureEditorShaderReferences();
        public override void OnInspectorGUI()
        {
            serializedObject.Update();var settings=serializedObject.FindProperty("settings");
            EditorGUILayout.HelpBox("混合模式、画面/天空强度和降噪开关在 Ho-RTDI Volume 调节。此处保留低频的采样、资源与筛选配置。需要同分辨率单采样 GB/SB 与 D3D12。",MessageType.Info);
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref runtime,"基础","RTXDI DI",RuntimeColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Draw(settings,"enabled","生产总开关");Draw(settings,"inSceneView","Scene View 中生产");
            }
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref quality,"采样质量","高级 / 低频",AdvancedColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Draw(settings,"localSampling","局部灯采样");Draw(settings,"initialSamples","局部灯候选数");Draw(settings,"temporal","时间重采样");Draw(settings,"maxHistory","ReSTIR 历史上限");
                Draw(settings,"spatial","空间重采样");Draw(settings,"spatialSamples","邻域样本数");Draw(settings,"spatialRadius","邻域半径");
                Draw(settings,"finalSampling","最终可见性样本");
                Draw(settings,"environmentSamples","环境光候选数");Draw(settings,"environmentBrdfSamples","BRDF 环境候选");
            }
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref advanced,"高级","筛选 / Shader",AdvancedColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Draw(settings,"rayBias","射线偏移");Draw(settings,"geometryLayers","几何层");Draw(settings,"lightLayers","灯所在物体层");
                Draw(settings,"excludedRayMaterials","不参与求交的材质");
                EditorGUILayout.PropertyField(serializedObject.FindProperty("shader"),new GUIContent("Compute Shader"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("nrdShaders"),new GUIContent("NRD Shader Library"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("debugShader"),new GUIContent("Debug Shader"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("compositeShader"),new GUIContent("Composite Shader"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("debugOverlayShader"),new GUIContent("Debug Grid Shader"));
            }
            serializedObject.ApplyModifiedProperties();EditorGUILayout.LabelField("运行状态",((HoRTDIRendererFeature)target).Status);
            var feature=(HoRTDIRendererFeature)target;
            var context=feature.ContextForCamera(SceneView.lastActiveSceneView?.camera)??feature.LastContext;
            if(context?.nrd!=null)
            {
                EditorGUILayout.LabelField(new GUIContent("降噪历史（主/快速）","快速历史也是裁剪主历史的参考；两路都必须保留有效累积。"),$"{(context.nrd.StableDiffuseHistory?"静止累积":"变化响应")} / 漫反射 {context.nrd.AppliedSettings.diffFrames}/{context.nrd.AppliedSettings.diffFast} / 镜面 {context.nrd.AppliedSettings.specFrames}/{context.nrd.AppliedSettings.specFast}");
                if(context.nrd.SceneViewPreview)EditorGUILayout.LabelField("预览采样",$"{context.nrd.SceneViewAccumulatedFrames}/{HoRTDINrdContext.SceneViewSettledSamples}（静止后自动补帧）");
            }
        }
        private static void Draw(SerializedProperty settings,string name,string label)
        {
            var property=settings.FindPropertyRelative(name);
            EditorGUILayout.PropertyField(property,new GUIContent(label,property.tooltip),true);
        }
    }
}
