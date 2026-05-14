using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    [CustomEditor(typeof(ShoostPostProcessStackVolume))]
    internal sealed class ShoostPostProcessStackVolumeEditor : VolumeComponentEditor
    {
        private SerializedDataParameter enable;
        private SerializedDataParameter showInSceneView;
        private SerializedProperty layers;
        private SerializedProperty layerValues;
        private ReorderableList layerList;

        public override void OnEnable()
        {
            PropertyFetcher<ShoostPostProcessStackVolume> fetcher = new PropertyFetcher<ShoostPostProcessStackVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.Enable));
            showInSceneView = Unpack(fetcher.Find(x => x.ShowInSceneView));
            layers = serializedObject.FindProperty("layers");
            layerValues = layers != null ? layers.FindPropertyRelative("m_Value") : null;
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            layerList = new ReorderableList(serializedObject, layerValues, true, true, true, true);
            layerList.drawHeaderCallback = DrawHeader;
            layerList.elementHeightCallback = GetElementHeight;
            layerList.drawElementCallback = DrawElement;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            PropertyField(enable);
            PropertyField(showInSceneView);

            if (!ShoostPostProcessRendererFeature.IsUseVolumes)
            {
                EditorGUILayout.HelpBox("Enable Use Volumes on the lilToon-Shoost renderer feature to drive this stack from Volume profiles.", MessageType.Warning);
            }

            EditorGUILayout.Space(4.0f);
            DrawLayerList();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawLayerList()
        {
            if (layers == null)
            {
                return;
            }

            SerializedProperty overrideState = layers.FindPropertyRelative("m_OverrideState");
            if (overrideState != null)
            {
                EditorGUILayout.PropertyField(overrideState, new GUIContent("Override Layers"));
            }

            if (layerList != null)
            {
                layerList.DoLayoutList();
            }
            else
            {
                EditorGUILayout.PropertyField(layers, true);
            }
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
            EditorGUI.PropertyField(rect, element, GetLayerLabel(element), true);
        }

        private SerializedProperty GetLayerProperty(int index)
        {
            if (layerList == null || layerList.serializedProperty == null || index < 0 || index >= layerList.serializedProperty.arraySize)
            {
                return null;
            }

            return layerList.serializedProperty.GetArrayElementAtIndex(index);
        }

        private static GUIContent GetLayerLabel(SerializedProperty layer)
        {
            string effectName = "Layer";
            SerializedProperty effectProperty = layer.FindPropertyRelative("effect");
            if (effectProperty != null && effectProperty.propertyType == SerializedPropertyType.Enum)
            {
                int effectIndex = effectProperty.enumValueIndex;
                if (effectIndex >= 0 && effectIndex < effectProperty.enumDisplayNames.Length)
                {
                    effectName = effectProperty.enumDisplayNames[effectIndex];
                }
            }

            SerializedProperty nameProperty = layer.FindPropertyRelative("name");
            string layerName = nameProperty != null ? nameProperty.stringValue : string.Empty;
            string tooltip = string.IsNullOrWhiteSpace(layerName) ? "Layer name is empty." : $"Layer name: {layerName}";
            return new GUIContent(effectName, tooltip);
        }
    }
}
