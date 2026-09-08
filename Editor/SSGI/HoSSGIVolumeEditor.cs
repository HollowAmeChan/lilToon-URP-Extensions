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
        private SerializedDataParameter temporalBlend;
        private SerializedDataParameter spatialRadius;
        private SerializedDataParameter temporalReservoirReuse;
        private SerializedDataParameter spatialReservoirReuse;
        private SerializedDataParameter temporalReservoirValidation;
        private SerializedDataParameter spatialReservoirValidation;
        private SerializedDataParameter fireflySuppression;
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
            temporalBlend = Unpack(fetcher.Find(x => x.temporalBlend));
            spatialRadius = Unpack(fetcher.Find(x => x.spatialRadius));
            temporalReservoirReuse = Unpack(fetcher.Find(x => x.temporalReservoirReuse));
            spatialReservoirReuse = Unpack(fetcher.Find(x => x.spatialReservoirReuse));
            temporalReservoirValidation = Unpack(fetcher.Find(x => x.temporalReservoirValidation));
            spatialReservoirValidation = Unpack(fetcher.Find(x => x.spatialReservoirValidation));
            fireflySuppression = Unpack(fetcher.Find(x => x.fireflySuppression));
            intensity = Unpack(fetcher.Find(x => x.intensity));
            sourceSaturation = Unpack(fetcher.Find(x => x.sourceSaturation));
            debugMode = Unpack(fetcher.Find(x => x.debugMode));
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Ho-SSGI reads the lit opaque camera color after GeometryBuffer and composites the result before post-processing. MetadataBuffer is not required. This Volume is the only tuning surface for runtime parameters.",
                MessageType.Info);

            PropertyField(enable);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("追踪", EditorStyles.boldLabel);
            PropertyField(rayCount);
            PropertyField(stepCount);
            PropertyField(rayLength);
            PropertyField(thickness);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("去噪", EditorStyles.boldLabel);
            PropertyField(temporalBlend);
            PropertyField(spatialRadius);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("ReSTIR", EditorStyles.boldLabel);
            PropertyField(temporalReservoirReuse);
            PropertyField(spatialReservoirReuse);
            PropertyField(temporalReservoirValidation);
            PropertyField(spatialReservoirValidation);
            PropertyField(fireflySuppression);
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
