using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    [CustomEditor(typeof(HoPostProcessStackVolume))]
    internal sealed class HoPostProcessStackVolumeEditor : VolumeComponentEditor
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
            PropertyFetcher<HoPostProcessStackVolume> fetcher = new PropertyFetcher<HoPostProcessStackVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.Enable));
            showInSceneView = Unpack(fetcher.Find(x => x.ShowInSceneView));
            layers = serializedObject.FindProperty("layers");
            layerValues = layers != null ? layers.FindPropertyRelative("m_Value") : null;
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            layerList = new ReorderableList(serializedObject, layerValues, true, true, false, false);
            layerList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "HoPost Stack - After URP / Before Shoost");
            layerList.elementHeightCallback = GetElementHeight;
            layerList.drawElementCallback = DrawElement;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            PropertyField(enable, new GUIContent("启用"));
            PropertyField(showInSceneView, new GUIContent("场景视图"));
            EditorGUILayout.Space(4.0f);

            DrawAddButtons();
            EditorGUILayout.Space(4.0f);

            if (layerList != null)
            {
                layerList.DoLayoutList();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawAddButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("边缘光"))
                {
                    AddLayer(HoPostProcessEffect.EdgeLight);
                }

                if (GUILayout.Button("轮廓"))
                {
                    AddLayer(HoPostProcessEffect.Outline);
                }

                if (GUILayout.Button("投影"))
                {
                    AddLayer(HoPostProcessEffect.DropShadow);
                }

                if (GUILayout.Button("自定义"))
                {
                    AddLayer(HoPostProcessEffect.CustomMaterial);
                }
            }
        }

        private float GetElementHeight(int index)
        {
            SerializedProperty element = GetLayerProperty(index);
            if (element == null)
            {
                return LineHeight + 6.0f;
            }

            int lines = element.isExpanded ? 16 : 1;
            return lines * (LineHeight + LineSpacing) + 6.0f;
        }

        private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = GetLayerProperty(index);
            if (element == null)
            {
                return;
            }

            rect.y += 2.0f;
            rect.height = LineHeight;

            SerializedProperty enabledProperty = element.FindPropertyRelative("enabled");
            SerializedProperty effectProperty = element.FindPropertyRelative("effect");
            SerializedProperty nameProperty = element.FindPropertyRelative("name");

            Rect foldoutRect = new Rect(rect.x, rect.y, 18.0f, LineHeight);
            Rect toggleRect = new Rect(rect.x + 18.0f, rect.y, 18.0f, LineHeight);
            Rect titleRect = new Rect(rect.x + 40.0f, rect.y, rect.width - 66.0f, LineHeight);
            Rect removeRect = new Rect(rect.xMax - 22.0f, rect.y, 22.0f, LineHeight);

            element.isExpanded = EditorGUI.Foldout(foldoutRect, element.isExpanded, GUIContent.none, true);
            enabledProperty.boolValue = EditorGUI.Toggle(toggleRect, enabledProperty.boolValue);
            EditorGUI.LabelField(titleRect, GetLayerTitle(element));
            if (GUI.Button(removeRect, "x", EditorStyles.miniButton))
            {
                RemoveLayer(index);
                return;
            }

            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            float y = rect.y + LineHeight + LineSpacing;

            EditorGUI.BeginChangeCheck();
            HoPostProcessEffect effect = (HoPostProcessEffect)effectProperty.enumValueIndex;
            effect = (HoPostProcessEffect)EditorGUI.EnumPopup(new Rect(rect.x, y, rect.width, LineHeight), "效果", effect);
            if (EditorGUI.EndChangeCheck())
            {
                effectProperty.enumValueIndex = (int)effect;
                ResetLayerDefaults(element, effect);
            }
            y += LineHeight + LineSpacing;

            nameProperty.stringValue = EditorGUI.TextField(new Rect(rect.x, y, rect.width, LineHeight), "名称", nameProperty.stringValue);
            y += LineHeight + LineSpacing;

            SerializedProperty intensity = element.FindPropertyRelative("intensity");
            intensity.floatValue = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "强度", intensity.floatValue, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            SerializedProperty color = element.FindPropertyRelative("color");
            color.colorValue = EditorGUI.ColorField(new Rect(rect.x, y, rect.width, LineHeight), new GUIContent("颜色"), color.colorValue, true, true, true);
            y += LineHeight + LineSpacing;

            DrawPropertyLine(rect, ref y, element, "blendMode", "混合模式");
            DrawPropertyLine(rect, ref y, element, "materialOverride", "材质覆盖");
            DrawPropertyLine(rect, ref y, element, "shaderOverride", "Shader 覆盖");
            DrawPropertyLine(rect, ref y, element, "passIndex", "Pass");
            DrawPropertyLine(rect, ref y, element, "texture", "纹理");
            DrawPropertyLine(rect, ref y, element, "parameters0", "参数 0");
            DrawPropertyLine(rect, ref y, element, "parameters1", "参数 1");
            DrawPropertyLine(rect, ref y, element, "parameters2", "参数 2");
            DrawPropertyLine(rect, ref y, element, "parameters3", "参数 3");
            DrawPropertyLine(rect, ref y, element, "parameters4", "参数 4");
            DrawPropertyLine(rect, ref y, element, "parameters5", "参数 5");

            EditorGUI.indentLevel--;
        }

        private static void DrawPropertyLine(Rect rect, ref float y, SerializedProperty element, string propertyName, string label)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property == null)
            {
                return;
            }

            EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, LineHeight), property, new GUIContent(label));
            y += LineHeight + LineSpacing;
        }

        private void AddLayer(HoPostProcessEffect effect)
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            int index = layerValues.arraySize;
            layerValues.InsertArrayElementAtIndex(index);
            SerializedProperty element = layerValues.GetArrayElementAtIndex(index);
            ResetLayerDefaults(element, effect);
            element.isExpanded = true;
        }

        private void RemoveLayer(int index)
        {
            if (layerValues == null || index < 0 || index >= layerValues.arraySize)
            {
                return;
            }

            layerValues.DeleteArrayElementAtIndex(index);
        }

        private SerializedProperty GetLayerProperty(int index)
        {
            if (layerValues == null || index < 0 || index >= layerValues.arraySize)
            {
                return null;
            }

            return layerValues.GetArrayElementAtIndex(index);
        }

        private static string GetLayerTitle(SerializedProperty element)
        {
            HoPostProcessEffect effect = GetEffect(element);
            SerializedProperty name = element.FindPropertyRelative("name");
            string displayName = !string.IsNullOrEmpty(name.stringValue) ? name.stringValue : GetEffectDisplayName(effect);
            return $"{displayName} ({GetEffectDisplayName(effect)})";
        }

        private static HoPostProcessEffect GetEffect(SerializedProperty element)
        {
            SerializedProperty effect = element.FindPropertyRelative("effect");
            int value = effect != null ? effect.enumValueIndex : 0;
            return (HoPostProcessEffect)Mathf.Clamp(value, 0, 3);
        }

        private static string GetEffectDisplayName(HoPostProcessEffect effect)
        {
            switch (effect)
            {
                case HoPostProcessEffect.EdgeLight:
                    return "边缘光";
                case HoPostProcessEffect.Outline:
                    return "轮廓";
                case HoPostProcessEffect.DropShadow:
                    return "投影";
                case HoPostProcessEffect.CustomMaterial:
                default:
                    return "自定义";
            }
        }

        private static void ResetLayerDefaults(SerializedProperty element, HoPostProcessEffect effect)
        {
            SetBool(element, "enabled", true);
            SetEnum(element, "effect", (int)effect);
            SetString(element, "name", GetEffectDisplayName(effect));
            SetObjectReference(element, "materialOverride", null);
            SetObjectReference(element, "shaderOverride", null);
            SetObjectReference(element, "texture", null);
            SetInt(element, "passIndex", 0);
            SetFloat(element, "intensity", 1.0f);
            SetColor(element, "color", Color.white);
            SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Add);
            SetVector4(element, "parameters0", Vector4.zero);
            SetVector4(element, "parameters1", Vector4.zero);
            SetVector4(element, "parameters2", Vector4.zero);
            SetVector4(element, "parameters3", Vector4.zero);
            SetVector4(element, "parameters4", Vector4.zero);
            SetVector4(element, "parameters5", Vector4.zero);

            switch (effect)
            {
                case HoPostProcessEffect.Outline:
                    SetColor(element, "color", Color.black);
                    SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Normal);
                    break;
                case HoPostProcessEffect.DropShadow:
                    SetColor(element, "color", new Color(0.0f, 0.0f, 0.0f, 0.5f));
                    SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Multiply);
                    break;
                case HoPostProcessEffect.CustomMaterial:
                    SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Normal);
                    break;
            }
        }

        private static void SetBool(SerializedProperty element, string propertyName, bool value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetEnum(SerializedProperty element, string propertyName, int value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.enumValueIndex = value;
            }
        }

        private static void SetString(SerializedProperty element, string propertyName, string value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.stringValue = value;
            }
        }

        private static void SetInt(SerializedProperty element, string propertyName, int value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.intValue = value;
            }
        }

        private static void SetFloat(SerializedProperty element, string propertyName, float value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetColor(SerializedProperty element, string propertyName, Color value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.colorValue = value;
            }
        }

        private static void SetVector4(SerializedProperty element, string propertyName, Vector4 value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.vector4Value = value;
            }
        }

        private static void SetObjectReference(SerializedProperty element, string propertyName, Object value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }
    }
}
