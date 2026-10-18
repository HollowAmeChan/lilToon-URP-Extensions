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
        private static bool runtime=true,debug;
        private SerializedDataParameter enable,mode,scene,game;
        public override void OnEnable()
        {var f=new PropertyFetcher<HoRTDIVolume>(serializedObject);enable=Unpack(f.Find(x=>x.enable));mode=Unpack(f.Find(x=>x.debugMode));scene=Unpack(f.Find(x=>x.debugInSceneView));game=Unpack(f.Find(x=>x.debugInGameView));}
        public override void OnInspectorGUI()
        {
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref runtime,"运行",LilUrpEditorSectionGui.BoolSummary(enable),RuntimeColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))PropertyField(enable,new GUIContent("启用"));
            if(LilUrpEditorSectionGui.DrawSectionHeader(ref debug,"调试",LilUrpEditorSectionGui.EnumSummary(mode),DebugColor))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                PropertyField(mode,new GUIContent("调试模式"));PropertyField(scene,new GUIContent("Debug In Scene View"));PropertyField(game,new GUIContent("Debug In Game View"));
                EditorGUILayout.HelpBox("这里显示独立的参考 BRDF 直接光。正常画面的参考 PBR 合成与 NRD 开关位于 Renderer Feature；调试关闭不影响合成。",MessageType.None);
            }
        }
    }
}
