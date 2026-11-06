using lilToon.URP.Extensions.RTGI;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
namespace lilToon.URP.Extensions.Editor.RTGI
{
    [CustomEditor(typeof(HoRTGIVolume))]
    internal sealed class HoRTGIVolumeEditor:VolumeComponentEditor
    {
        private static readonly Color RuntimeColor=new Color(.46f,.64f,.92f),DebugColor=new Color(.86f,.62f,.38f);
        private static bool runtime=true,debug;
        private SerializedDataParameter enable,denoise,mode,scene,game,exposure,tonemap;
        public override void OnEnable()
        {var f=new PropertyFetcher<HoRTGIVolume>(serializedObject);enable=Unpack(f.Find(x=>x.enable));denoise=Unpack(f.Find(x=>x.denoise));mode=Unpack(f.Find(x=>x.debugMode));scene=Unpack(f.Find(x=>x.debugInSceneView));game=Unpack(f.Find(x=>x.debugInGameView));exposure=Unpack(f.Find(x=>x.debugExposure));tonemap=Unpack(f.Find(x=>x.debugTonemap));}
        public override void OnInspectorGUI()
        {
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref runtime,"运行",LilUrpEditorSectionGui.BoolSummary(enable),RuntimeColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                PropertyField(enable,new GUIContent("启用"));PropertyField(denoise,new GUIContent("降噪"));
                EditorGUILayout.HelpBox("未覆盖时沿用 Feature 默认值；camera color 消费由 Ho-ScreenProcess 的 RTGI layer 控制。",MessageType.None);
            }
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref debug,"调试",LilUrpEditorSectionGui.EnumSummary(mode),DebugColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                PropertyField(mode,new GUIContent("调试模式"));PropertyField(scene,new GUIContent("Scene View 中调试"));PropertyField(game,new GUIContent("Game View 中调试"));
                PropertyField(exposure,new GUIContent("调试曝光（EV）"));PropertyField(tonemap,new GUIContent("调试 Reinhard 映射"));
                EditorGUILayout.HelpBox("查看独立的间接光 product 和接收域；调试不改变 RTGI product 生产。",MessageType.None);
            }
        }
    }
}
