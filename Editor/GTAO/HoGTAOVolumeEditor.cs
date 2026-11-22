using lilToon.URP.Extensions.Editor;
using lilToon.URP.Extensions.GTAO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GTAO
{
    [CustomEditor(typeof(HoGTAOVolume))]
    internal sealed class HoGTAOVolumeEditor : VolumeComponentEditor
    {
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color TracingColor = new Color(0.38f, 0.72f, 0.62f);
        private static readonly Color DenoiseColor = new Color(0.62f, 0.58f, 0.78f);
        private static readonly Color DebugColor = new Color(0.86f, 0.62f, 0.38f);

        private static bool showRuntime = true;
        private static bool showTracing = true;
        private static bool showDenoise = true;
        private static bool showDebug;

        private SerializedDataParameter enable;
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
            EditorGUILayout.HelpBox(
                "Ho-GTAO consumes Ho-GeometryBuffer raw depth and normal data. Keep Ho-GeometryBuffer above Ho-GTAO in the Renderer Feature list. Debug output is controlled here by the active Volume.",
                MessageType.Info);

            DrawRuntime();
            DrawTracing();
            DrawDenoise();
            DrawDebug();
        }

        private void DrawRuntime()
        {
            string summary = LilUrpEditorSectionGui.BoolSummary(enable)
                + " / " + LilUrpEditorSectionGui.EnumSummary(resolution);
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", summary, RuntimeColor))
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawParameter(enable, "启用");
                DrawPresets();
                DrawParameter(resolution, "计算分辨率");
                EditorGUILayout.Space(3.0f);
                EditorGUILayout.HelpBox("预设只填写密度和去噪参数，不改 Override 勾选、半径或厚度。请手动勾选需要覆盖的参数，填入后可继续调整。", MessageType.None);
                EditorGUILayout.HelpBox("Half/Quarter 会降低 GTAO 追踪和去噪的计算分辨率，最终 AO 仍输出到相机分辨率。", MessageType.None);
            }
        }

        private void DrawPresets()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("填入质量预设");
                if (GUILayout.Button("Low")) ApplyPreset(HoGTAOQuality.Low);
                if (GUILayout.Button("Medium")) ApplyPreset(HoGTAOQuality.Medium);
                if (GUILayout.Button("High")) ApplyPreset(HoGTAOQuality.High);
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
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showTracing, "追踪", summary, TracingColor))
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
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
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showDenoise, "去噪", summary, DenoiseColor))
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
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
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showDebug, "调试", summary, DebugColor))
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("调试输出由 Ho-GTAO 自己持有，不经过 Ho-DebugTile。Motion 静止时为黑色符合 HTrace 语义；Temporal Disocclusion 用红色表示被拒绝的历史。", MessageType.None);
                DrawParameter(debugMode, "调试模式");
                DrawParameter(debugInSceneView, "Debug In Scene View");
                DrawParameter(debugInGameView, "Debug In Game View");
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
