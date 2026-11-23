using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
namespace lilToon.URP.Extensions.Editor.RTDI
{
    [CustomEditor(typeof(HoRTDIVolume))]
    internal sealed class HoRTDIVolumeEditor:HoVolumeComponentEditor
    {

        private static bool runtime=true,sky=true,quality,debug;
        private SerializedDataParameter enable,denoise,environment,environmentIntensity,mode,scene,game;
        private SerializedDataParameter localSampling,initialSamples,temporal,maxHistory,spatial,spatialSamples,spatialRadius,finalSampling,environmentSamples,environmentBrdfSamples;
        public override void OnEnable()
        {
            var f=new PropertyFetcher<HoRTDIVolume>(serializedObject);
            enable=Unpack(f.Find(x=>x.enable));denoise=Unpack(f.Find(x=>x.denoise));environment=Unpack(f.Find(x=>x.useEnvironment));environmentIntensity=Unpack(f.Find(x=>x.environmentIntensity));
            mode=Unpack(f.Find(x=>x.debugMode));scene=Unpack(f.Find(x=>x.debugInSceneView));game=Unpack(f.Find(x=>x.debugInGameView));
            localSampling=Unpack(f.Find(x=>x.localSampling));initialSamples=Unpack(f.Find(x=>x.initialSamples));
            temporal=Unpack(f.Find(x=>x.temporal));maxHistory=Unpack(f.Find(x=>x.maxHistory));spatial=Unpack(f.Find(x=>x.spatial));
            spatialSamples=Unpack(f.Find(x=>x.spatialSamples));spatialRadius=Unpack(f.Find(x=>x.spatialRadius));finalSampling=Unpack(f.Find(x=>x.finalSampling));
            environmentSamples=Unpack(f.Find(x=>x.environmentSamples));environmentBrdfSamples=Unpack(f.Find(x=>x.environmentBrdfSamples));
        }
        public override void OnInspectorGUI()
        {
            if(HoVolumeEditorGUI.Section(ref runtime,"运行",LilUrpEditorSectionGui.BoolSummary(enable)))
            using(new EditorGUILayout.VerticalScope())
            {PropertyField(enable,new GUIContent("启用"));PropertyField(denoise,new GUIContent("NRD RELAX 降噪"));}
            if(HoVolumeEditorGUI.Section(ref sky,"天空照明",LilUrpEditorSectionGui.BoolSummary(environment)))
            using(new EditorGUILayout.VerticalScope())
            {PropertyField(environment,new GUIContent("天空环境光"));PropertyField(environmentIntensity,new GUIContent("天空照明强度"));}
            if(HoVolumeEditorGUI.Section(ref quality,"采样质量","候选 / 重采样"))
            using(new EditorGUILayout.VerticalScope())
            {
                PropertyField(localSampling,new GUIContent("局部灯采样"));PropertyField(initialSamples,new GUIContent("局部灯候选数"));
                PropertyField(temporal,new GUIContent("时间重采样"));PropertyField(maxHistory,new GUIContent("ReSTIR 历史上限"));
                PropertyField(spatial,new GUIContent("空间重采样"));PropertyField(spatialSamples,new GUIContent("邻域样本数"));PropertyField(spatialRadius,new GUIContent("邻域半径（像素）"));
                PropertyField(finalSampling,new GUIContent("最终可见性样本"));PropertyField(environmentSamples,new GUIContent("天空候选数"));PropertyField(environmentBrdfSamples,new GUIContent("BRDF 天空候选"));
            }
            if(HoVolumeEditorGUI.Section(ref debug,"调试",LilUrpEditorSectionGui.EnumSummary(mode)))
            using(new EditorGUILayout.VerticalScope())
            {
                PropertyField(mode,new GUIContent("调试模式"));PropertyField(scene,new GUIContent("场景视图"));PropertyField(game,new GUIContent("游戏视图"));
            }
        }
    }
}
