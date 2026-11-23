using lilToon.URP.Extensions.SSGI;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.SSGI
{
    [CustomEditor(typeof(HoSSGIVolume))]
    internal sealed class HoSSGIVolumeEditor : HoVolumeComponentEditor
    {
        private bool showTracing = true, showDenoise = true, showReservoir, showAppearance = true, showDebug;
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

            PropertyField(enable);
            if (HoVolumeEditorGUI.Section(ref showTracing, "追踪"))
            {
                PropertyField(rayCount);
                PropertyField(stepCount);
                PropertyField(rayLength);
                PropertyField(thickness);
            }
            if (HoVolumeEditorGUI.Section(ref showDenoise, "去噪"))
            {
                PropertyField(temporalBlend);
                PropertyField(spatialRadius);
            }
            if (HoVolumeEditorGUI.Section(ref showReservoir, "高级 · ReSTIR"))
            {
                PropertyField(temporalReservoirReuse);
                PropertyField(spatialReservoirReuse);
                PropertyField(temporalReservoirValidation);
                PropertyField(spatialReservoirValidation);
                PropertyField(fireflySuppression);
            }
            if (HoVolumeEditorGUI.Section(ref showAppearance, "外观"))
            {
                PropertyField(intensity);
                PropertyField(sourceSaturation);
            }
            if (HoVolumeEditorGUI.Section(ref showDebug, "调试")) PropertyField(debugMode);
        }
    }
}
