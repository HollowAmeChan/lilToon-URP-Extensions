using lilToon.URP.Extensions.RT;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RT
{
    [CustomEditor(typeof(HoRTVolume))]
    internal sealed class HoRTVolumeEditor : HoVolumeComponentEditor
    {
        private SerializedDataParameter enable;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<HoRTVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.enable));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(enable);
        }
    }
}
