using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
namespace lilToon.URP.Extensions.Editor.RTDI
{
    [CustomEditor(typeof(HoRTDIVolume))]
    internal sealed class HoRTDIVolumeEditor:VolumeComponentEditor
    {
        private static readonly Color RuntimeColor=new Color(.46f,.64f,.92f),DebugColor=new Color(.86f,.62f,.38f);
        private static bool runtime=true,appearance=true,sky=true,debug;
        private SerializedDataParameter enable,denoise,composite,blend,weight,additive,environment,environmentIntensity,mode,scene,game;
        public override void OnEnable()
        {
            var f=new PropertyFetcher<HoRTDIVolume>(serializedObject);
            enable=Unpack(f.Find(x=>x.enable));denoise=Unpack(f.Find(x=>x.denoise));composite=Unpack(f.Find(x=>x.composite));blend=Unpack(f.Find(x=>x.compositeMode));
            weight=Unpack(f.Find(x=>x.compositeWeight));additive=Unpack(f.Find(x=>x.additiveIntensity));environment=Unpack(f.Find(x=>x.useEnvironment));environmentIntensity=Unpack(f.Find(x=>x.environmentIntensity));
            mode=Unpack(f.Find(x=>x.debugMode));scene=Unpack(f.Find(x=>x.debugInSceneView));game=Unpack(f.Find(x=>x.debugInGameView));
        }
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("常用画面参数在此调节，勾选左侧覆盖后生效；未覆盖时沿用旧 Renderer 配置。采样质量、资源与筛选保留在 Feature。",MessageType.Info);
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref runtime,"运行",LilUrpEditorSectionGui.BoolSummary(enable),RuntimeColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {PropertyField(enable,new GUIContent("启用"));PropertyField(denoise,new GUIContent("NRD RELAX 降噪"));}
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref appearance,"画面合成",LilUrpEditorSectionGui.EnumSummary(blend),RuntimeColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                PropertyField(composite,new GUIContent("合成到画面"));PropertyField(blend,new GUIContent("混合模式"));
                PropertyField(additive,new GUIContent("叠加强度（加法）"));PropertyField(weight,new GUIContent("过渡权重（替换）"));
                EditorGUILayout.HelpBox("加法保留原画面，不重复添加自发光；替换使用参考 PBR 照明。两种模式各自使用对应强度。",MessageType.None);
            }
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref sky,"天空照明",LilUrpEditorSectionGui.BoolSummary(environment),RuntimeColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {PropertyField(environment,new GUIContent("天空环境光"));PropertyField(environmentIntensity,new GUIContent("天空照明强度"));}
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref debug,"调试",LilUrpEditorSectionGui.EnumSummary(mode),DebugColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                PropertyField(mode,new GUIContent("调试模式"));PropertyField(scene,new GUIContent("Scene View 中调试"));PropertyField(game,new GUIContent("Game View 中调试"));
                EditorGUILayout.HelpBox("这里显示独立的参考 BRDF 直接光；调试关闭不影响正常画面合成。",MessageType.None);
            }
        }
    }
}
