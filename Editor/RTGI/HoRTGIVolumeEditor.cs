using lilToon.URP.Extensions.RTGI;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
namespace lilToon.URP.Extensions.Editor.RTGI
{
    [CustomEditor(typeof(HoRTGIVolume))]
    internal sealed class HoRTGIVolumeEditor:HoVolumeComponentEditor
    {

        private static bool runtime=true,debug;
        private SerializedDataParameter enable,denoise,mode,scene,game,exposure,tonemap;
        public override void OnEnable()
        {var f=new PropertyFetcher<HoRTGIVolume>(serializedObject);enable=Unpack(f.Find(x=>x.enable));denoise=Unpack(f.Find(x=>x.denoise));mode=Unpack(f.Find(x=>x.debugMode));scene=Unpack(f.Find(x=>x.debugInSceneView));game=Unpack(f.Find(x=>x.debugInGameView));exposure=Unpack(f.Find(x=>x.debugExposure));tonemap=Unpack(f.Find(x=>x.debugTonemap));}
        public override void OnInspectorGUI()
        {
            if(HoVolumeEditorGUI.Section(ref runtime,"运行",LilUrpEditorSectionGui.BoolSummary(enable)))
            using(new EditorGUILayout.VerticalScope())
            {
                PropertyField(enable,new GUIContent("启用"));PropertyField(denoise,new GUIContent("降噪"));
            }
            if(HoVolumeEditorGUI.Section(ref debug,"调试",LilUrpEditorSectionGui.EnumSummary(mode)))
            using(new EditorGUILayout.VerticalScope())
            {
                PropertyField(mode,new GUIContent("调试模式"));PropertyField(scene,new GUIContent("场景视图"));PropertyField(game,new GUIContent("游戏视图"));
                PropertyField(exposure,new GUIContent("调试曝光（EV）"));PropertyField(tonemap,new GUIContent("调试 Reinhard 映射"));
            }
        }
    }
}
