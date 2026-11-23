using lilToon.URP.Extensions.Debugging;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.Debugging
{
    [CustomEditor(typeof(HoDebugTileVolume))]
    internal sealed class HoDebugTileVolumeEditor : HoVolumeComponentEditor
    {
        private SerializedDataParameter enable;
        private SerializedDataParameter performance;
        private SerializedDataParameter debugInSceneView;
        private SerializedDataParameter debugInGameView;

        public override void OnEnable()
        {
            PropertyFetcher<HoDebugTileVolume> fetcher = new PropertyFetcher<HoDebugTileVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.enable));
            performance = Unpack(fetcher.Find(x => x.performance));
            debugInSceneView = Unpack(fetcher.Find(x => x.debugInSceneView));
            debugInGameView = Unpack(fetcher.Find(x => x.debugInGameView));
        }

        public override void OnInspectorGUI()
        {
            DrawParameter(enable, "启用");
            DrawParameter(performance, "性能信息");
            DrawParameter(debugInSceneView, "场景视图");
            DrawParameter(debugInGameView, "游戏视图");
        }

        private void DrawParameter(SerializedDataParameter parameter, string label)
        {
            if (parameter != null) PropertyField(parameter, new GUIContent(label));
        }
    }
}
