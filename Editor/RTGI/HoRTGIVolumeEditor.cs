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
        private static bool runtime=true,appearance=true,debug;
        private SerializedDataParameter enable,composite,intensity,denoise,mode,scene,game;
        public override void OnEnable()
        {var f=new PropertyFetcher<HoRTGIVolume>(serializedObject);enable=Unpack(f.Find(x=>x.enable));composite=Unpack(f.Find(x=>x.composite));intensity=Unpack(f.Find(x=>x.intensity));denoise=Unpack(f.Find(x=>x.denoise));mode=Unpack(f.Find(x=>x.debugMode));scene=Unpack(f.Find(x=>x.debugInSceneView));game=Unpack(f.Find(x=>x.debugInGameView));}
        public override void OnInspectorGUI()
        {
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref runtime,"运行",LilUrpEditorSectionGui.BoolSummary(enable),RuntimeColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                PropertyField(enable,new GUIContent("启用"));PropertyField(denoise,new GUIContent("降噪"));
                EditorGUILayout.HelpBox("未覆盖时沿用 Feature 默认值；采样质量和天空照明在 Ho-RTGI Feature 调节。",MessageType.None);
            }
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref appearance,"画面合成","间接光加法",RuntimeColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                PropertyField(composite,new GUIContent("合成到场景"));PropertyField(intensity,new GUIContent("间接光强度"));
                EditorGUILayout.HelpBox("向现有画面增加间接光，不重复添加材质自发光。与RTDI配合时，GI的主表面直接天空保持关闭。",MessageType.None);
            }
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref debug,"调试",LilUrpEditorSectionGui.EnumSummary(mode),DebugColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                PropertyField(mode,new GUIContent("调试模式"));PropertyField(scene,new GUIContent("Scene View 中调试"));PropertyField(game,new GUIContent("Game View 中调试"));
                EditorGUILayout.HelpBox("查看独立的间接光贡献和接收域；正常画面合成由上方开关控制。",MessageType.None);
            }
        }
    }
}
