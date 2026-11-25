using lilToon.URP.Extensions.Editor;
using lilToon.URP.Extensions.GTAO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GTAO
{
    [CustomEditor(typeof(HoGTAOVolume))]
    internal sealed class HoGTAOVolumeEditor : HoVolumeComponentEditor
    {





        private static bool showRuntime = true;
        private static bool showTracing = true;
        private static bool showDenoise = true;
        private static bool showDebug;

        private SerializedDataParameter enable;
        private SerializedDataParameter publishAOTextureOnly;
        private SerializedDataParameter debugInSceneView;
        private SerializedDataParameter debugInGameView;
        private SerializedDataParameter resolution;
        private SerializedDataParameter worldSpaceRadius;
        private SerializedDataParameter screenSpaceRadius;
        private SerializedDataParameter thickness;
        private SerializedDataParameter sliceCount;
        private SerializedDataParameter stepCount;
        private SerializedDataParameter useAttenuation;
        private SerializedDataParameter useLinearThickness;
        private SerializedDataParameter temporalFrameCount;
        private SerializedDataParameter temporalRejection;
        private SerializedDataParameter spatialFilter;
        private SerializedDataParameter filterRadius;
        private SerializedDataParameter filterAdaptivity;
        private SerializedDataParameter boxPassCount;
        private SerializedDataParameter debugMode;
        private SerializedDataParameter debugIntensity;

        public override void OnEnable()
        {
            PropertyFetcher<HoGTAOVolume> fetcher = new PropertyFetcher<HoGTAOVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.enable));
            publishAOTextureOnly = Unpack(fetcher.Find(x => x.publishAOTextureOnly));
            debugInSceneView = Unpack(fetcher.Find(x => x.debugInSceneView));
            debugInGameView = Unpack(fetcher.Find(x => x.debugInGameView));
            resolution = Unpack(fetcher.Find(x => x.resolution));
            worldSpaceRadius = Unpack(fetcher.Find(x => x.worldSpaceRadius));
            screenSpaceRadius = Unpack(fetcher.Find(x => x.screenSpaceRadius));
            thickness = Unpack(fetcher.Find(x => x.thickness));
            sliceCount = Unpack(fetcher.Find(x => x.sliceCount));
            stepCount = Unpack(fetcher.Find(x => x.stepCount));
            useAttenuation = Unpack(fetcher.Find(x => x.useAttenuation));
            useLinearThickness = Unpack(fetcher.Find(x => x.useLinearThickness));
            temporalFrameCount = Unpack(fetcher.Find(x => x.temporalFrameCount));
            temporalRejection = Unpack(fetcher.Find(x => x.temporalRejection));
            spatialFilter = Unpack(fetcher.Find(x => x.spatialFilter));
            filterRadius = Unpack(fetcher.Find(x => x.filterRadius));
            filterAdaptivity = Unpack(fetcher.Find(x => x.filterAdaptivity));
            boxPassCount = Unpack(fetcher.Find(x => x.boxPassCount));
            debugMode = Unpack(fetcher.Find(x => x.debugMode));
            debugIntensity = Unpack(fetcher.Find(x => x.debugIntensity));
        }

        public override void OnInspectorGUI()
        {

            DrawRuntime();
            if (!PublishOnly)
            {
                DrawTracing();
                DrawDenoise();
            }
            DrawDebug();
        }

        private bool PublishOnly => publishAOTextureOnly != null
            && publishAOTextureOnly.value.boolValue;

        private void DrawRuntime()
        {
            string summary = LilUrpEditorSectionGui.BoolSummary(enable)
                + " / " + LilUrpEditorSectionGui.EnumSummary(resolution);
            if (!HoVolumeEditorGUI.Section(ref showRuntime, "运行", summary))
                return;

            using (new EditorGUILayout.VerticalScope())
            {
                DrawParameter(enable, "启用");
                DrawParameter(publishAOTextureOnly, "仅发布 AO 纹理");
                if (!PublishOnly)
                {
                    DrawPresets();
                    DrawParameter(resolution, "计算分辨率");
                }
                else EditorGUILayout.HelpBox("发布全白 HoAO，跳过追踪、运动输入与去噪；调试仅支持 AO 视图。独立接收面胶囊 AO 不需要此模式。", MessageType.Info);
            }
        }

        private void DrawPresets()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                const string hint = "填写采样密度和去噪参数，不改变 Override 勾选、半径或厚度；填入后仍可逐项调整。";
                EditorGUILayout.PrefixLabel(new GUIContent("质量预设", hint));
                if (GUILayout.Button(new GUIContent("Low", hint))) ApplyPreset(HoGTAOQuality.Low);
                if (GUILayout.Button(new GUIContent("Medium", hint))) ApplyPreset(HoGTAOQuality.Medium);
                if (GUILayout.Button(new GUIContent("High", hint))) ApplyPreset(HoGTAOQuality.High);
            }
        }

        private void ApplyPreset(HoGTAOQuality preset)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObjects(serializedObject.targetObjects, "Fill GTAO Volume Preset");
            foreach (Object targetObject in serializedObject.targetObjects)
            {
                HoGTAOQualityPresets.Apply(preset, (HoGTAOVolume)targetObject);
                EditorUtility.SetDirty(targetObject);
            }
            serializedObject.Update();
        }

        private void DrawTracing()
        {
            string summary = LilUrpEditorSectionGui.IntSummary(sliceCount, " slices")
                + " / " + LilUrpEditorSectionGui.IntSummary(stepCount, " steps");
            if (!HoVolumeEditorGUI.Section(ref showTracing, "追踪", summary))
                return;

            using (new EditorGUILayout.VerticalScope())
            {
                DrawParameter(worldSpaceRadius, "世界空间半径");
                DrawParameter(screenSpaceRadius, "最小屏幕半径");
                DrawParameter(thickness, "厚度");
                DrawParameter(useAttenuation, "距离衰减");
                DrawParameter(useLinearThickness, "线性厚度");
                DrawParameter(sliceCount, "切片数");
                DrawParameter(stepCount, "每切片步数");
            }
        }

        private void DrawDenoise()
        {
            string summary = LilUrpEditorSectionGui.IntSummary(temporalFrameCount, " frames")
                + " / " + LilUrpEditorSectionGui.EnumSummary(spatialFilter);
            if (!HoVolumeEditorGUI.Section(ref showDenoise, "去噪", summary))
                return;

            using (new EditorGUILayout.VerticalScope())
            {
                DrawParameter(temporalFrameCount, "时间累积帧数");
                DrawParameter(temporalRejection, "时间拒绝强度");
                DrawParameter(spatialFilter, "空间滤波类型");
                DrawParameter(filterRadius, "滤波半径");
                DrawParameter(filterAdaptivity, "边缘自适应");
                DrawParameter(boxPassCount, "Box 趟数");
            }
        }

        private void DrawDebug()
        {
            string summary = LilUrpEditorSectionGui.EnumSummary(debugMode);
            if (!HoVolumeEditorGUI.Section(ref showDebug, "调试", summary))
                return;

            using (new EditorGUILayout.VerticalScope())
            {
                DrawParameter(debugMode, "调试模式");
                DrawParameter(debugInSceneView, "场景视图");
                DrawParameter(debugInGameView, "游戏视图");
                DrawParameter(debugIntensity, "AO Debug Pow");
            }
        }

        private void DrawParameter(SerializedDataParameter parameter, string label)
        {
            if (parameter != null)
                PropertyField(parameter, new GUIContent(label));
        }
    }
}
