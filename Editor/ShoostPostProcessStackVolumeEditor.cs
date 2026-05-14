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
        private const float LineHeight = 18.0f;
        private const float LineSpacing = 2.0f;

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
                EditorGUILayout.PropertyField(overrideState, new GUIContent("覆盖图层"));
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
            EditorGUI.LabelField(rect, "图层");
        }

        private float GetElementHeight(int index)
        {
            SerializedProperty element = GetLayerProperty(index);
            if (element == null)
            {
                return LineHeight + 6.0f;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.VignetteCustom && element.isExpanded)
            {
                return (LineHeight + LineSpacing) * 18.0f + 12.0f;
            }

            return EditorGUI.GetPropertyHeight(element, true) + 6.0f;
        }

        private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = GetLayerProperty(index);
            if (element == null)
            {
                return;
            }

            rect.y += 2.0f;
            if (GetEffect(element) == ShoostPostProcessEffect.VignetteCustom)
            {
                DrawVignetteCustomElement(rect, element);
            }
            else
            {
                rect.height = EditorGUI.GetPropertyHeight(element, true);
                EditorGUI.PropertyField(rect, element, GetLayerLabel(element), true);
            }
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
            string tooltip = string.IsNullOrWhiteSpace(layerName) ? "图层名称为空。" : $"图层名称：{layerName}";
            return new GUIContent(effectName, tooltip);
        }

        private static ShoostPostProcessEffect GetEffect(SerializedProperty layer)
        {
            SerializedProperty effectProperty = layer.FindPropertyRelative("effect");
            if (effectProperty == null || effectProperty.propertyType != SerializedPropertyType.Enum)
            {
                return ShoostPostProcessEffect.CustomMaterial;
            }

            int effectIndex = effectProperty.enumValueIndex;
            if (effectIndex < 0 || effectIndex >= System.Enum.GetValues(typeof(ShoostPostProcessEffect)).Length)
            {
                return ShoostPostProcessEffect.CustomMaterial;
            }

            return (ShoostPostProcessEffect)effectIndex;
        }

        private void DrawVignetteCustomElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty layerName = element.FindPropertyRelative("name");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");
            SerializedProperty effect = element.FindPropertyRelative("effect");
            SerializedProperty showInSceneView = element.FindPropertyRelative("showInSceneView");
            SerializedProperty materialOverride = element.FindPropertyRelative("materialOverride");
            SerializedProperty shaderOverride = element.FindPropertyRelative("shaderOverride");
            SerializedProperty passIndex = element.FindPropertyRelative("passIndex");
            SerializedProperty intensity = element.FindPropertyRelative("intensity");
            SerializedProperty color = element.FindPropertyRelative("color");
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");

            EnsureVignetteCustomDefaults(parameters0);

            float y = rect.y;
            Rect lineRect = new Rect(rect.x, y, rect.width, LineHeight);
            element.isExpanded = EditorGUI.Foldout(lineRect, element.isExpanded, GetLayerLabel(element), true);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y += LineHeight + LineSpacing;

            y = DrawPropertyLine(rect.x, y, rect.width, layerName, "图层名称");
            y = DrawPropertyLine(rect.x, y, rect.width, enabled, "启用");
            y = DrawPropertyLine(rect.x, y, rect.width, effect, "效果类型");
            y = DrawPropertyLine(rect.x, y, rect.width, showInSceneView, "场景视图");
            y = DrawPropertyLine(rect.x, y, rect.width, intensity, "强度");

            Rect modeRect = new Rect(rect.x, y, rect.width, LineHeight);
            int mode = Mathf.Clamp(passIndex.intValue, 0, 1);
            mode = EditorGUI.Popup(modeRect, "模式", mode, new[] { "压暗", "染色" });
            passIndex.intValue = mode;
            y += LineHeight + LineSpacing;

            Vector4 vignetteParams = parameters0.vector4Value;
            Rect centerXRect = new Rect(rect.x, y, rect.width, LineHeight);
            vignetteParams.x = EditorGUI.Slider(centerXRect, "中心 X", vignetteParams.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            Rect centerYRect = new Rect(rect.x, y, rect.width, LineHeight);
            vignetteParams.y = EditorGUI.Slider(centerYRect, "中心 Y", vignetteParams.y, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            Rect radiusRect = new Rect(rect.x, y, rect.width, LineHeight);
            vignetteParams.z = EditorGUI.Slider(radiusRect, "半径", vignetteParams.z, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            Rect softnessRect = new Rect(rect.x, y, rect.width, LineHeight);
            vignetteParams.w = EditorGUI.Slider(softnessRect, "柔和度", vignetteParams.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            parameters0.vector4Value = vignetteParams;

            y = DrawPropertyLine(rect.x, y, rect.width, color, "染色颜色");
            y = DrawPropertyLine(rect.x, y, rect.width, materialOverride, "材质覆盖");
            y = DrawPropertyLine(rect.x, y, rect.width, shaderOverride, "Shader 覆盖");
            EditorGUI.indentLevel--;
        }

        private static void EnsureVignetteCustomDefaults(SerializedProperty parameters0)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = parameters0.vector4Value;
            if (value.sqrMagnitude > 0.000001f)
            {
                return;
            }

            parameters0.vector4Value = new Vector4(0.5f, 0.5f, 0.35f, 0.25f);
        }

        private static float DrawPropertyLine(float x, float y, float width, SerializedProperty property, string label)
        {
            if (property == null)
            {
                return y;
            }

            Rect lineRect = new Rect(x, y, width, LineHeight);
            EditorGUI.PropertyField(lineRect, property, new GUIContent(label));
            return y + LineHeight + LineSpacing;
        }
    }
}
