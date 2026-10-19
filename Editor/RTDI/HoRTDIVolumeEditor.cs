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
        private static bool runtime=true,appearance=true,sky=true,quality,debug;
        private SerializedDataParameter enable,denoise,composite,blend,weight,additive,environment,environmentIntensity,mode,scene,game;
        private SerializedDataParameter localSampling,initialSamples,temporal,maxHistory,spatial,spatialSamples,spatialRadius,finalSampling,environmentSamples,environmentBrdfSamples;
        public override void OnEnable()
        {
            var f=new PropertyFetcher<HoRTDIVolume>(serializedObject);
            enable=Unpack(f.Find(x=>x.enable));denoise=Unpack(f.Find(x=>x.denoise));composite=Unpack(f.Find(x=>x.composite));blend=Unpack(f.Find(x=>x.compositeMode));
            weight=Unpack(f.Find(x=>x.compositeWeight));additive=Unpack(f.Find(x=>x.additiveIntensity));environment=Unpack(f.Find(x=>x.useEnvironment));environmentIntensity=Unpack(f.Find(x=>x.environmentIntensity));
            mode=Unpack(f.Find(x=>x.debugMode));scene=Unpack(f.Find(x=>x.debugInSceneView));game=Unpack(f.Find(x=>x.debugInGameView));
            localSampling=Unpack(f.Find(x=>x.localSampling));initialSamples=Unpack(f.Find(x=>x.initialSamples));
            temporal=Unpack(f.Find(x=>x.temporal));maxHistory=Unpack(f.Find(x=>x.maxHistory));spatial=Unpack(f.Find(x=>x.spatial));
            spatialSamples=Unpack(f.Find(x=>x.spatialSamples));spatialRadius=Unpack(f.Find(x=>x.spatialRadius));finalSampling=Unpack(f.Find(x=>x.finalSampling));
            environmentSamples=Unpack(f.Find(x=>x.environmentSamples));environmentBrdfSamples=Unpack(f.Find(x=>x.environmentBrdfSamples));
        }
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("必须勾选“启用”左侧覆盖并打开才运行 RTDI。画面和采样质量在此调节；其它参数未覆盖时沿用旧 Renderer 配置。资源、筛选与射线偏移保留在 Feature。",MessageType.Info);
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
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref quality,"采样质量","候选 / 重采样",RuntimeColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                PropertyField(localSampling,new GUIContent("局部灯采样"));PropertyField(initialSamples,new GUIContent("局部灯候选数"));
                PropertyField(temporal,new GUIContent("时间重采样"));PropertyField(maxHistory,new GUIContent("ReSTIR 历史上限"));
                PropertyField(spatial,new GUIContent("空间重采样"));PropertyField(spatialSamples,new GUIContent("邻域样本数"));PropertyField(spatialRadius,new GUIContent("邻域半径（像素）"));
                PropertyField(finalSampling,new GUIContent("最终可见性样本"));PropertyField(environmentSamples,new GUIContent("天空候选数"));PropertyField(environmentBrdfSamples,new GUIContent("BRDF 天空候选"));
                EditorGUILayout.HelpBox("候选数、重采样和天空预算影响噪声与开销。ReSTIR 历史上限不是 NRD 历史帧数，通常从4开始。",MessageType.None);
            }
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref debug,"调试",LilUrpEditorSectionGui.EnumSummary(mode),DebugColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                PropertyField(mode,new GUIContent("调试模式"));PropertyField(scene,new GUIContent("Scene View 中调试"));PropertyField(game,new GUIContent("Game View 中调试"));
                EditorGUILayout.HelpBox("这里显示独立的参考 BRDF 直接光；调试关闭不影响正常画面合成。",MessageType.None);
            }
        }
    }
}
