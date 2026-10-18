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
        private static bool runtime=true,advanced;
        private void OnEnable()=>((HoRTDIRendererFeature)target).EnsureEditorShaderReferences();
        public override void OnInspectorGUI()
        {
            serializedObject.Update();var settings=serializedObject.FindProperty("settings");
            EditorGUILayout.HelpBox("需要 GB/SB 同分辨率单采样输入与 D3D12。参考 PBR 合成会替换有效接收面的旧照明，保留自发光；原 NPR ramp、间接光与反射探针不参与。",MessageType.Info);
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref runtime,"运行","RTXDI DI",RuntimeColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Draw(settings,"enabled","启用");Draw(settings,"localSampling","局部灯采样");Draw(settings,"initialSamples","局部灯候选数");Draw(settings,"temporal","时间重采样");Draw(settings,"maxHistory","ReSTIR 历史上限");
                Draw(settings,"composite","合成到画面（参考 PBR）");
                if(settings.FindPropertyRelative("composite").boolValue)Draw(settings,"compositeWeight","合成过渡权重");
                Draw(settings,"spatial","空间重采样");Draw(settings,"spatialSamples","邻域样本数");Draw(settings,"spatialRadius","邻域半径");
                Draw(settings,"finalSampling","最终可见性样本");
                Draw(settings,"denoise","NRD RELAX 降噪");
                Draw(settings,"useEnvironment","天空环境光");Draw(settings,"environmentSamples","环境光候选数");
                Draw(settings,"environmentBrdfSamples","BRDF 环境候选");
            }
            EditorGUILayout.HelpBox("调试在 Ho-RTDI Volume 的「调试」分组。",MessageType.Info);
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref advanced,"高级","筛选 / Shader",AdvancedColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Draw(settings,"rayBias","射线偏移");Draw(settings,"geometryLayers","几何层");Draw(settings,"lightLayers","灯所在物体层");Draw(settings,"inSceneView","Scene View 中生产");
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
