using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditorInternal;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    [CustomEditor(typeof(ShoostPostProcessStackVolume))]
    internal sealed class ShoostPostProcessStackVolumeEditor : VolumeComponentEditor
    {
        private SerializedDataParameter enable;
        private SerializedDataParameter showInSceneView;
        private SerializedDataParameter layers;
        private ReorderableList layerList;

        public override void OnEnable()
        {
            PropertyFetcher<ShoostPostProcessStackVolume> fetcher = new PropertyFetcher<ShoostPostProcessStackVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.Enable));
            showInSceneView = Unpack(fetcher.Find(x => x.ShowInSceneView));
            layers = Unpack(fetcher.Find(x => x.layers));

            layerList = new ReorderableList(serializedObject, layers.value, true, true, true, true);
            layerList.drawHeaderCallback = DrawHeader;
            layerList.elementHeightCallback = GetElementHeight;
            layerList.drawElementCallback = DrawElement;
        }

        public override void OnInspectorGUI()
        {
            PropertyField(enable);
            PropertyField(showInSceneView);

            if (!ShoostPostProcessRendererFeature.IsUseVolumes)
            {
                EditorGUILayout.HelpBox("Enable Use Volumes on the Shoost Post Process renderer feature to drive this stack from Volume profiles.", MessageType.Warning);
            }

            EditorGUILayout.Space(4.0f);
            serializedObject.Update();
            layerList.DoLayoutList();
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, "Layers");
        }

        private float GetElementHeight(int index)
        {
            SerializedProperty element = GetLayerProperty(index);

            return element != null
                ? EditorGUI.GetPropertyHeight(element, true) + 6.0f
                : EditorGUIUtility.singleLineHeight + 6.0f;
        }

        private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = GetLayerProperty(index);
            if (element == null)
            {
                return;
            }

            rect.y += 2.0f;
            rect.height = EditorGUI.GetPropertyHeight(element, true);
            EditorGUI.PropertyField(rect, element, new GUIContent($"Layer {index}"), true);
        }

        private SerializedProperty GetLayerProperty(int index)
        {
            if (layerList == null || layerList.serializedProperty == null || index < 0 || index >= layerList.serializedProperty.arraySize)
            {
                return null;
            }

            return layerList.serializedProperty.GetArrayElementAtIndex(index);
        }
    }
}
