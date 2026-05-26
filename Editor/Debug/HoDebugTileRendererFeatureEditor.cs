using System.Collections.Generic;
using lilToon.URP.Extensions.Debugging;
using UnityEditor;

namespace lilToon.URP.Extensions.Editor.Debugging
{
    [CustomEditor(typeof(HoDebugTileRendererFeature))]
    internal sealed class HoDebugTileRendererFeatureEditor : UnityEditor.Editor
    {
        private static readonly string[] EmptyLabels = { "None" };
        private static readonly string[] EmptyIds = { HoDebugTileRendererFeature.NoneViewId };

        private SerializedProperty enabledForGameView;
        private SerializedProperty enabledForSceneView;
        private SerializedProperty passEvent;
        private SerializedProperty geometryDepthNear;
        private SerializedProperty geometryDepthFar;
        private SerializedProperty selectedDebugViewId;
        private string[] labels = EmptyLabels;
        private string[] ids = EmptyIds;

        private void OnEnable()
        {
            enabledForGameView = serializedObject.FindProperty("enabledForGameView");
            enabledForSceneView = serializedObject.FindProperty("enabledForSceneView");
            passEvent = serializedObject.FindProperty("passEvent");
            geometryDepthNear = serializedObject.FindProperty("geometryDepthNear");
            geometryDepthFar = serializedObject.FindProperty("geometryDepthFar");
            selectedDebugViewId = serializedObject.FindProperty("selectedDebugViewId");
            RebuildDebugViewOptions();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(enabledForGameView);
            EditorGUILayout.PropertyField(enabledForSceneView);
            EditorGUILayout.PropertyField(passEvent);
            EditorGUILayout.PropertyField(geometryDepthNear);
            EditorGUILayout.PropertyField(geometryDepthFar);
            DrawDebugViewPopup();
            EditorGUILayout.HelpBox("Automatic tiles currently include registry views with render-kind metadata. MetadataBuffer and GeometryBuffer are wired in this step; ScreenProcess rule-mask remains layer-local.", MessageType.Info);
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawDebugViewPopup()
        {
            int selectedIndex = FindSelectedIndex(selectedDebugViewId.stringValue);
            int nextIndex = EditorGUILayout.Popup("Debug View", selectedIndex, labels);
            selectedDebugViewId.stringValue = ids[nextIndex];
        }

        private int FindSelectedIndex(string value)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == value)
                {
                    return i;
                }
            }

            return 0;
        }

        private void RebuildDebugViewOptions()
        {
            IReadOnlyList<HoDebugViewInfo> views = HoDebugViewRegistry.AllViews;
            List<string> labelList = new List<string>(views.Count + 2)
            {
                "None",
                "AllRegistered"
            };
            List<string> idList = new List<string>(views.Count + 2)
            {
                HoDebugTileRendererFeature.NoneViewId,
                HoDebugTileRendererFeature.AllRegisteredViewId
            };

            for (int i = 0; i < views.Count; i++)
            {
                HoDebugViewInfo view = views[i];
                if (!view.SupportsAutomaticTile)
                {
                    continue;
                }

                labelList.Add(view.FeatureName + " / " + view.ShortName + " (" + view.ViewId + ")");
                idList.Add(view.ViewId);
            }

            labels = labelList.ToArray();
            ids = idList.ToArray();
        }
    }
}
