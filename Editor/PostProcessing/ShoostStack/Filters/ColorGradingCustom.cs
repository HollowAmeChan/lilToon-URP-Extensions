using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static bool GetColorGradingUsesLogWheels(SerializedProperty element)
        {
            SerializedProperty parameters6 = element?.FindPropertyRelative("parameters6");
            if (parameters6 == null || parameters6.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            return Mathf.RoundToInt(parameters6.vector4Value.z) == 1;
        }

        private void DrawColorGradingCustomElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty parameters4 = element.FindPropertyRelative("parameters4");
            SerializedProperty parameters5 = element.FindPropertyRelative("parameters5");
            SerializedProperty parameters6 = element.FindPropertyRelative("parameters6");
            SerializedProperty parameters7 = element.FindPropertyRelative("parameters7");
            SerializedProperty parameters8 = element.FindPropertyRelative("parameters8");
            SerializedProperty parameters9 = element.FindPropertyRelative("parameters9");
            SerializedProperty parameters10 = element.FindPropertyRelative("parameters10");
            SerializedProperty parameters11 = element.FindPropertyRelative("parameters11");
            SerializedProperty parameters12 = element.FindPropertyRelative("parameters12");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureColorGradingCustomDefaults(parameters0, parameters1, parameters2, parameters3, parameters4, parameters5, parameters6);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 lift = parameters0.vector4Value;
            Vector4 gamma = parameters1.vector4Value;
            Vector4 gain = parameters2.vector4Value;
            Vector4 shadows = parameters3.vector4Value;
            Vector4 midtones = parameters4.vector4Value;
            Vector4 highlights = parameters5.vector4Value;
            Vector4 modeParams = parameters6.vector4Value;
            Vector4 hueVsHueA = parameters7.vector4Value;
            Vector4 hueVsHueB = parameters8.vector4Value;
            Vector4 hueVsSatA = parameters9.vector4Value;
            Vector4 hueVsSatB = parameters10.vector4Value;
            Vector4 hueVsLumA = parameters11.vector4Value;
            Vector4 hueVsLumB = parameters12.vector4Value;

            int wheelMode = Mathf.Clamp(Mathf.RoundToInt(modeParams.z), 0, ColorGradingWheelModeNames.Length - 1);
            wheelMode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "类型", wheelMode, ColorGradingWheelModeNames);
            modeParams.z = wheelMode;
            y += LineHeight + LineSpacing;

            Rect wheelArea = new Rect(rect.x, y, rect.width, GetColorWheelTripletHeight(rect.width));
            if (wheelMode == 0)
            {
                DrawColorWheelTriplet(wheelArea, "提升", ref lift, "伽马", ref gamma, "增益", ref gain, new Vector4(1.0f, 1.0f, 1.0f, 0.0f));
            }
            else
            {
                DrawColorWheelTriplet(wheelArea, "阴影", ref shadows, "中间调", ref midtones, "高光", ref highlights, Vector4.zero);
            }
            y += wheelArea.height + LineSpacing;

            if (wheelMode == 1)
            {
                modeParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "阴影范围", Mathf.Clamp01(modeParams.x), 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                modeParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "高光范围", Mathf.Clamp01(modeParams.y), 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
            }

            EditorGUI.LabelField(new Rect(rect.x, y, rect.width, LineHeight), "色偏", EditorStyles.boldLabel);
            y += LineHeight + LineSpacing;

            int shiftMode = Mathf.Clamp(Mathf.RoundToInt(modeParams.w), 0, ColorGradingShiftModeNames.Length - 1);
            shiftMode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "模式", shiftMode, ColorGradingShiftModeNames);
            modeParams.w = shiftMode;
            y += LineHeight + LineSpacing;

            DrawSixColorAdjustmentSliders(rect.x, ref y, rect.width, shiftMode, ref hueVsHueA, ref hueVsHueB, ref hueVsSatA, ref hueVsSatB, ref hueVsLumA, ref hueVsLumB);

            parameters0.vector4Value = lift;
            parameters1.vector4Value = gamma;
            parameters2.vector4Value = gain;
            parameters3.vector4Value = shadows;
            parameters4.vector4Value = midtones;
            parameters5.vector4Value = highlights;
            parameters6.vector4Value = modeParams;
            parameters7.vector4Value = hueVsHueA;
            parameters8.vector4Value = hueVsHueB;
            parameters9.vector4Value = hueVsSatA;
            parameters10.vector4Value = hueVsSatB;
            parameters11.vector4Value = hueVsLumA;
            parameters12.vector4Value = hueVsLumB;
            EditorGUI.indentLevel--;
        }

        private static void DrawColorWheelTriplet(Rect rect, string firstLabel, ref Vector4 first, string secondLabel, ref Vector4 second, string thirdLabel, ref Vector4 third, Vector4 resetValue)
        {
            float cellWidth = (rect.width - ColorWheelGap * 2.0f) / 3.0f;
            DrawColorWheelCell(new Rect(rect.x, rect.y, cellWidth, rect.height), firstLabel, ref first, resetValue);
            DrawColorWheelCell(new Rect(rect.x + cellWidth + ColorWheelGap, rect.y, cellWidth, rect.height), secondLabel, ref second, resetValue);
            DrawColorWheelCell(new Rect(rect.x + (cellWidth + ColorWheelGap) * 2.0f, rect.y, cellWidth, rect.height), thirdLabel, ref third, resetValue);
        }

        private static void DrawColorWheelCell(Rect rect, string label, ref Vector4 value, Vector4 resetValue)
        {
            float wheelSize = GetColorWheelSize(rect.width);
            Rect wheelRect = new Rect(rect.x + (rect.width - wheelSize) * 0.5f, rect.y, wheelSize, wheelSize);
            value = DrawColorWheel(wheelRect, value, resetValue);

            Rect sliderRect = new Rect(rect.x + rect.width * 0.05f, wheelRect.yMax + 4.0f, rect.width * 0.9f, 17.0f);
            value.w = GUI.HorizontalSlider(sliderRect, value.w, -1.0f, 1.0f);

            Vector3 displayValue = GetLiftGammaGainDisplayValue(value);
            Rect valueRect = new Rect(rect.x, sliderRect.yMax + 1.0f, rect.width / 3.0f, 17.0f);
            EditorGUI.LabelField(valueRect, displayValue.x.ToString("F2"), EditorStyles.centeredGreyMiniLabel);
            valueRect.x += valueRect.width;
            EditorGUI.LabelField(valueRect, displayValue.y.ToString("F2"), EditorStyles.centeredGreyMiniLabel);
            valueRect.x += valueRect.width;
            EditorGUI.LabelField(valueRect, displayValue.z.ToString("F2"), EditorStyles.centeredGreyMiniLabel);

            Rect labelRect = new Rect(rect.x, valueRect.yMax + 1.0f, rect.width, 17.0f);
            EditorGUI.LabelField(labelRect, label, EditorStyles.centeredGreyMiniLabel);
        }

        private static float GetColorWheelTripletHeight(float width)
        {
            float cellWidth = (width - ColorWheelGap * 2.0f) / 3.0f;
            return GetColorWheelSize(cellWidth) + 58.0f;
        }

        private static float GetColorWheelSize(float width)
        {
            return Mathf.Clamp(width, ColorWheelMinSize, ColorWheelMaxSize);
        }

        private static float DrawSixColorAdjustmentLine(float x, float y, float width, string label, Color swatch, float value, float min, float max)
        {
            Rect swatchRect = new Rect(x, y + 3.0f, 14.0f, 14.0f);
            EditorGUI.DrawRect(swatchRect, swatch);

            Rect labelRect = new Rect(x + 22.0f, y, 68.0f, LineHeight);
            EditorGUI.LabelField(labelRect, label);

            float fieldWidth = 58.0f;
            Rect valueRect = new Rect(x + width - fieldWidth, y, fieldWidth, LineHeight);
            Rect sliderRect = new Rect(labelRect.xMax + 8.0f, y, Mathf.Max(40.0f, valueRect.x - labelRect.xMax - 14.0f), LineHeight);
            value = GUI.HorizontalSlider(new Rect(sliderRect.x, sliderRect.y + 2.0f, sliderRect.width, sliderRect.height - 4.0f), value, min, max);
            value = EditorGUI.FloatField(valueRect, value);
            return Mathf.Clamp(value, min, max);
        }

        private static Vector4 DrawColorWheel(Rect rect, Vector4 value, Vector4 resetValue)
        {
            DrawTrackballTexture(rect, value);

            Event current = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive, rect);
            if ((current.type == EventType.MouseDown || current.type == EventType.MouseDrag) && current.button == 0 && rect.Contains(current.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                value = PickColorWheelValue(rect, current.mousePosition, value);
                GUI.changed = true;
                current.Use();
            }

            if (current.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
            {
                GUIUtility.hotControl = 0;
                current.Use();
            }
            else if (current.type == EventType.MouseDown && current.button == 1 && rect.Contains(current.mousePosition))
            {
                value = resetValue;
                GUI.changed = true;
                current.Use();
            }

            Color.RGBToHSV(VectorToWheelColor(value), out float hue, out float saturation, out _);
            float angle = hue * Mathf.PI * 2.0f;
            float radius = Mathf.Clamp01(saturation) * rect.width * 0.38f;
            Vector2 center = rect.center;
            Vector2 marker = center + new Vector2(Mathf.Cos(angle + (Mathf.PI * 0.5f)), Mathf.Sin(angle - (Mathf.PI * 0.5f))) * radius;
            DrawTrackballThumb(marker);

            return value;
        }

        private static Vector4 PickColorWheelValue(Rect rect, Vector2 mousePosition, Vector4 currentValue)
        {
            Vector2 delta = mousePosition - rect.center;
            float radius = rect.width * 0.38f;
            float saturation = Mathf.Clamp01(delta.magnitude / Mathf.Max(1.0f, radius));
            float hueRadians = Mathf.Atan2(delta.x, -delta.y);
            float hue = 1.0f - ((hueRadians > 0.0f) ? hueRadians : (Mathf.PI * 2.0f) + hueRadians) / (Mathf.PI * 2.0f);
            if (hue >= 1.0f)
            {
                hue -= 1.0f;
            }

            Color color = Color.HSVToRGB(hue, saturation, 1.0f);
            currentValue.x = color.r;
            currentValue.y = color.g;
            currentValue.z = color.b;
            return currentValue;
        }

        private static void DrawTrackballTexture(Rect rect, Vector4 value)
        {
            Material material = GetTrackballMaterial();
            if (material == null)
            {
                GUI.DrawTexture(rect, GetColorWheelTexture(), ScaleMode.ScaleToFit, true);
                return;
            }

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            float scale = EditorGUIUtility.pixelsPerPoint;
            int width = Mathf.Max(1, Mathf.RoundToInt(rect.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(rect.height * scale));
            RenderTexture oldTarget = RenderTexture.active;
            RenderTexture temp = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            material.SetFloat("_Offset", value.w);
            material.SetFloat("_DisabledState", GUI.enabled ? 1.0f : 0.5f);
            material.SetVector("_Resolution", new Vector2(width, height * 0.5f));
            Graphics.Blit(null, temp, material, EditorGUIUtility.isProSkin ? 0 : 1);
            RenderTexture.active = oldTarget;
            GUI.DrawTexture(rect, temp);
            RenderTexture.ReleaseTemporary(temp);
        }

        private static void DrawTrackballThumb(Vector2 center)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (colorWheelThumbStyle == null)
            {
                colorWheelThumbStyle = new GUIStyle("ColorPicker2DThumb");
                colorWheelThumbSize = new Vector2(
                    !Mathf.Approximately(colorWheelThumbStyle.fixedWidth, 0.0f) ? colorWheelThumbStyle.fixedWidth : colorWheelThumbStyle.padding.horizontal,
                    !Mathf.Approximately(colorWheelThumbStyle.fixedHeight, 0.0f) ? colorWheelThumbStyle.fixedHeight : colorWheelThumbStyle.padding.vertical);
            }

            if (colorWheelThumbSize.x > 0.0f && colorWheelThumbSize.y > 0.0f)
            {
                Rect rect = new Rect(center.x - colorWheelThumbSize.x * 0.5f, center.y - colorWheelThumbSize.y * 0.5f, colorWheelThumbSize.x, colorWheelThumbSize.y);
                colorWheelThumbStyle.Draw(rect, false, false, false, false);
                return;
            }

            Rect markerRect = new Rect(center.x - 3.0f, center.y - 3.0f, 6.0f, 6.0f);
            EditorGUI.DrawRect(new Rect(markerRect.x - 1.0f, markerRect.y - 1.0f, markerRect.width + 2.0f, markerRect.height + 2.0f), Color.black);
            EditorGUI.DrawRect(markerRect, Color.white);
        }

        private static Vector3 GetLiftGammaGainDisplayValue(Vector4 value)
        {
            return new Vector3(value.x + value.w, value.y + value.w, value.z + value.w);
        }

        private static Color VectorToWheelColor(Vector4 value)
        {
            if (Mathf.Abs(value.x) <= 0.0001f && Mathf.Abs(value.y) <= 0.0001f && Mathf.Abs(value.z) <= 0.0001f)
            {
                return Color.white;
            }

            return new Color(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y), Mathf.Clamp01(value.z), 1.0f);
        }

        private static Material GetTrackballMaterial()
        {
            if (trackballMaterial != null)
            {
                return trackballMaterial;
            }

            Shader shader = Shader.Find(TrackballShaderName);
            if (shader == null)
            {
                return null;
            }

            trackballMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            return trackballMaterial;
        }

        private static Texture2D GetColorWheelTexture()
        {
            if (colorWheelTexture != null)
            {
                return colorWheelTexture;
            }

            const int size = 128;
            colorWheelTexture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            float center = (size - 1) * 0.5f;
            float radius = center;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / radius;
                    float dy = (y - center) / radius;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    if (distance > 1.0f)
                    {
                        colorWheelTexture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float hue = Mathf.Atan2(dy, dx) / (Mathf.PI * 2.0f);
                    if (hue < 0.0f)
                    {
                        hue += 1.0f;
                    }

                    Color color = Color.HSVToRGB(hue, distance, 1.0f);
                    color.a = 1.0f;
                    colorWheelTexture.SetPixel(x, y, color);
                }
            }

            colorWheelTexture.Apply(false, true);
            return colorWheelTexture;
        }

        private static void EnsureColorWheelVector(SerializedProperty property)
        {
            if (property == null || property.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = property.vector4Value;
            if (Mathf.Abs(value.x) <= 0.0001f && Mathf.Abs(value.y) <= 0.0001f && Mathf.Abs(value.z) <= 0.0001f)
            {
                value.x = 1.0f;
                value.y = 1.0f;
                value.z = 1.0f;
                property.vector4Value = value;
            }
        }

        private static void EnsureLogColorWheelVector(SerializedProperty property)
        {
            if (property == null || property.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = property.vector4Value;
            if (Mathf.Abs(value.x - 1.0f) <= 0.0001f && Mathf.Abs(value.y - 1.0f) <= 0.0001f && Mathf.Abs(value.z - 1.0f) <= 0.0001f && Mathf.Abs(value.w) <= 0.0001f)
            {
                property.vector4Value = Vector4.zero;
            }
        }

        private static void EnsureColorGradingModeDefaults(SerializedProperty property)
        {
            if (property == null || property.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = property.vector4Value;
            if (value.sqrMagnitude <= 0.000001f)
            {
                property.vector4Value = new Vector4(0.3f, 0.55f, 0.0f, 0.0f);
            }
        }

        private static void EnsureColorGradingCustomDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty parameters2,
            SerializedProperty parameters3,
            SerializedProperty parameters4,
            SerializedProperty parameters5,
            SerializedProperty parameters6)
        {
            EnsureColorWheelVector(parameters0);
            EnsureColorWheelVector(parameters1);
            EnsureColorWheelVector(parameters2);
            EnsureLogColorWheelVector(parameters3);
            EnsureLogColorWheelVector(parameters4);
            EnsureLogColorWheelVector(parameters5);
            EnsureColorGradingModeDefaults(parameters6);
        }
    }
}
