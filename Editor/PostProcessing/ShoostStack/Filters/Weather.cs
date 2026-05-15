using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static readonly string[] WeatherParticleNames = { "雨", "雪", "烟雾", "灰尘" };
        private static readonly string[] WeatherBlendModeNames = { "正常", "加亮", "滤色", "柔光" };
        private static bool weatherBasicFoldout = true;
        private static bool weatherDepthFoldout = true;
        private static bool weatherParticleFoldout = true;

        private static int GetWeatherLineCount(SerializedProperty element)
        {
            int count = 3;
            if (weatherBasicFoldout)
            {
                count += 5;
            }

            if (weatherDepthFoldout)
            {
                count += 4;
            }

            if (weatherParticleFoldout)
            {
                count += 8;
            }

            return count;
        }

        private void DrawWeatherElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty color = element.FindPropertyRelative("color");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureWeatherDefaults(parameters0, parameters1, parameters2, parameters3);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 weatherParams = parameters0.vector4Value;
            Vector4 depthParams = parameters1.vector4Value;
            Vector4 particleParams = parameters2.vector4Value;
            Vector4 variationParams = parameters3.vector4Value;

            y = DrawWeatherSectionFoldout(rect, y, "基础", ref weatherBasicFoldout);
            if (weatherBasicFoldout)
            {
                EditorGUI.indentLevel++;
                int particle = Mathf.Clamp(Mathf.RoundToInt(weatherParams.x), 0, WeatherParticleNames.Length - 1);
                particle = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "粒子", particle, WeatherParticleNames);
                weatherParams.x = particle;
                y += LineHeight + LineSpacing;

                color.colorValue = EditorGUI.ColorField(new Rect(rect.x, y, rect.width, LineHeight), new GUIContent("颜色"), color.colorValue, true, true, true);
                y += LineHeight + LineSpacing;

                weatherParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "发生率", Mathf.Clamp01(weatherParams.y), 0.0f, 1.0f);
                y += LineHeight + LineSpacing;

                weatherParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", Mathf.Clamp01(weatherParams.z), 0.0f, 1.0f);
                y += LineHeight + LineSpacing;

                int blendMode = Mathf.Clamp(Mathf.RoundToInt(depthParams.w), 0, WeatherBlendModeNames.Length - 1);
                blendMode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "叠加模式", blendMode, WeatherBlendModeNames);
                depthParams.w = blendMode;
                y += LineHeight + LineSpacing;
                EditorGUI.indentLevel--;
            }

            y = DrawWeatherSectionFoldout(rect, y, "假景深", ref weatherDepthFoldout);
            if (weatherDepthFoldout)
            {
                EditorGUI.indentLevel++;
                weatherParams.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "焦距", Mathf.Clamp(weatherParams.w, 0.05f, 1.0f), 0.05f, 1.0f);
                y += LineHeight + LineSpacing;

                depthParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "虚化强度", Mathf.Clamp(depthParams.x, 0.0f, 2.0f), 0.0f, 2.0f);
                y += LineHeight + LineSpacing;

                depthParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "虚化柔化", Mathf.Clamp01(depthParams.y), 0.0f, 1.0f);
                y += LineHeight + LineSpacing;

                depthParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "虚化曲线", Mathf.Clamp(depthParams.z, 0.25f, 4.0f), 0.25f, 4.0f);
                y += LineHeight + LineSpacing;
                EditorGUI.indentLevel--;
            }

            y = DrawWeatherSectionFoldout(rect, y, "粒子变化", ref weatherParticleFoldout);
            if (weatherParticleFoldout)
            {
                EditorGUI.indentLevel++;
                particleParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "速度", Mathf.Clamp(particleParams.x, 0.0f, 3.0f), 0.0f, 3.0f);
                y += LineHeight + LineSpacing;

                particleParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "数量", Mathf.Clamp(particleParams.y, 0.0f, 3.0f), 0.0f, 3.0f);
                y += LineHeight + LineSpacing;

                particleParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "大小", Mathf.Clamp(particleParams.z, 0.25f, 3.0f), 0.25f, 3.0f);
                y += LineHeight + LineSpacing;

                particleParams.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "随机", Mathf.Clamp(particleParams.w, 0.0f, 2.0f), 0.0f, 2.0f);
                y += LineHeight + LineSpacing;

                variationParams.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "漂移", Mathf.Clamp(variationParams.w, 0.0f, 3.0f), 0.0f, 3.0f);
                y += LineHeight + LineSpacing;

                variationParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "层次", Mathf.Clamp(variationParams.x, 0.0f, 2.0f), 0.0f, 2.0f);
                y += LineHeight + LineSpacing;

                variationParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "上下不均", Mathf.Clamp(variationParams.y, 0.0f, 2.0f), 0.0f, 2.0f);
                y += LineHeight + LineSpacing;

                variationParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "明暗变化", Mathf.Clamp(variationParams.z, 0.0f, 2.0f), 0.0f, 2.0f);
                y += LineHeight + LineSpacing;
                EditorGUI.indentLevel--;
            }

            parameters0.vector4Value = weatherParams;
            parameters1.vector4Value = depthParams;
            parameters2.vector4Value = particleParams;
            parameters3.vector4Value = variationParams;
            EditorGUI.indentLevel--;
        }

        private static float DrawWeatherSectionFoldout(Rect rect, float y, string label, ref bool foldout)
        {
            foldout = EditorGUI.Foldout(new Rect(rect.x, y, rect.width, LineHeight), foldout, label, true);
            return y + LineHeight + LineSpacing;
        }

        private static void EnsureWeatherDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty parameters2,
            SerializedProperty parameters3)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.0f, 1.0f, 1.0f, 1.0f);
            }
            else if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.w <= 0.0001f)
            {
                Vector4 value = parameters0.vector4Value;
                value.w = 1.0f;
                parameters0.vector4Value = value;
            }
            else if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters0.vector4Value;
                bool oldSilentDefault = Mathf.Abs(value.x) <= 0.0001f
                    && Mathf.Abs(value.y) <= 0.0001f
                    && Mathf.Abs(value.z - 1.0f) <= 0.0001f
                    && Mathf.Abs(value.w - 1.0f) <= 0.0001f;
                if (oldSilentDefault)
                {
                    value.y = 1.0f;
                    parameters0.vector4Value = value;
                }
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(1.0f, 0.35f, 1.0f, 1.0f);
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4 && parameters2.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters2.vector4Value = new Vector4(1.0f, 1.0f, 1.0f, 0.35f);
            }
            else if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters2.vector4Value;
                bool oldRandomDefault = Mathf.Abs(value.x - 1.0f) <= 0.0001f
                    && Mathf.Abs(value.y - 1.0f) <= 0.0001f
                    && Mathf.Abs(value.z - 1.0f) <= 0.0001f
                    && Mathf.Abs(value.w - 1.0f) <= 0.0001f;
                if (oldRandomDefault)
                {
                    value.w = 0.35f;
                    parameters2.vector4Value = value;
                }
            }

            if (parameters3 != null && parameters3.propertyType == SerializedPropertyType.Vector4 && parameters3.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters3.vector4Value = Vector4.one;
            }
        }
    }
}
