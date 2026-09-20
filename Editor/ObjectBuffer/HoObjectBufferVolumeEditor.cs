using lilToon.URP.Extensions.ObjectBuffer;
using UnityEditor;
using UnityEditor.Rendering;

namespace lilToon.URP.Extensions.Editor.ObjectBuffer
{
    [CustomEditor(typeof(HoObjectBufferVolume))]
    internal sealed class HoObjectBufferVolumeEditor : VolumeComponentEditor
    {
        private SerializedDataParameter enable;
        private SerializedDataParameter debugMode;
        private SerializedDataParameter debugInSceneView;
        private SerializedDataParameter debugInGameView;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<HoObjectBufferVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.enable));
            debugMode = Unpack(fetcher.Find(x => x.debugMode));
            debugInSceneView = Unpack(fetcher.Find(x => x.debugInSceneView));
            debugInGameView = Unpack(fetcher.Find(x => x.debugInGameView));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(enable);
            PropertyField(debugMode);
            PropertyField(debugInSceneView);
            PropertyField(debugInGameView);
        }
    }
}
