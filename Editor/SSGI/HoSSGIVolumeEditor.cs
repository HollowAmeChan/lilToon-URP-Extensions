using lilToon.URP.Extensions.SSGI;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.SSGI
{
    [CustomEditor(typeof(HoSSGIVolume))]
    internal sealed class HoSSGIVolumeEditor : VolumeComponentEditor
    {
        private SerializedDataParameter enable;
        private SerializedDataParameter rayCount;
        private SerializedDataParameter stepCount;
        private SerializedDataParameter rayLength;
        private SerializedDataParameter thickness;
        private SerializedDataParameter intensity;
        private SerializedDataParameter sourceSaturation;
        private SerializedDataParameter debugMode;

        public override void OnEnable()
        {
            PropertyFetcher<HoSSGIVolume> fetcher = new PropertyFetcher<HoSSGIVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.enable));
            rayCount = Unpack(fetcher.Find(x => x.rayCount));
            stepCount = Unpack(fetcher.Find(x => x.stepCount));
            rayLength = Unpack(fetcher.Find(x => x.rayLength));
            thickness = Unpack(fetcher.Find(x => x.thickness));
            intensity = Unpack(fetcher.Find(x => x.intensity));
            sourceSaturation = Unpack(fetcher.Find(x => x.sourceSaturation));
            debugMode = Unpack(fetcher.Find(x => x.debugMode));
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Ho-SSGI runs after GeometryBuffer and MetadataBuffer at BeforeRenderingOpaques. The Volume is the main tuning surface; the RendererFeature only owns resource and shader fallback settings.",
                MessageType.Info);

            PropertyField(enable);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("追踪", EditorStyles.boldLabel);
            PropertyField(rayCount);
            PropertyField(stepCount);
            PropertyField(rayLength);
            PropertyField(thickness);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("外观", EditorStyles.boldLabel);
            PropertyField(intensity);
            PropertyField(sourceSaturation);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("调试", EditorStyles.boldLabel);
            PropertyField(debugMode);
        }
    }
}
