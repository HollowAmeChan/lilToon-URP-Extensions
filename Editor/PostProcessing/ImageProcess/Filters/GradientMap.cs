using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        /// <summary>Ramp inputs. Rec.709 luma is index 0 and the default.</summary>
        private static readonly string[] GradientMapInputNames =
        {
            "亮度", "红", "绿", "蓝", "最大值", "平均值", "饱和度"
        };

        private static readonly string[] GradientMapInterpolationSpaceNames =
        {
            "显示空间", "线性光", "Oklab"
        };

        private const int GradientMapStopCount = 4;

        /// <summary>
        /// Lines <see cref="DrawGradientMapElement"/> draws after the foldout row, with every
        /// optional core field disabled: input, black point, white point, preview strip,
        /// 4 stop colours, stop-position heading, 4 stop positions, interpolation space,
        /// posterise bands, reverse, dither, blend mode.
        /// </summary>
        private const int GradientMapBodyLineCount = 18;

        private static bool HasGradientMapParameters(SerializedProperty element)
        {
            if (element == null)
            {
                return false;
            }

            for (int i = 0; i < 7; i++)
            {
                SerializedProperty parameter = element.FindPropertyRelative("parameters" + i);
                if (parameter == null || parameter.propertyType != SerializedPropertyType.Vector4)
                {
                    return false;
                }
            }

            return element.FindPropertyRelative("blendMode") != null;
        }

        private static int GetGradientMapLineCount(SerializedProperty element)
        {
            // Malformed layers fall back to the generic single-line row, which reserves one line
            // (the foldout) and nothing else; GetElementLineCount already counts that one.
            return HasGradientMapParameters(element) ? GradientMapBodyLineCount : 0;
        }

        private void DrawGradientMapElement(Rect rect, SerializedProperty element)
        {
            if (!HasGradientMapParameters(element))
            {
                DrawSimpleLayerElement(rect, element);
                return;
            }

            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty parameters4 = element.FindPropertyRelative("parameters4");
            SerializedProperty parameters5 = element.FindPropertyRelative("parameters5");
            SerializedProperty parameters6 = element.FindPropertyRelative("parameters6");
            SerializedProperty blendMode = element.FindPropertyRelative("blendMode");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureGradientMapDefaults(parameters0, parameters1, parameters2, parameters3, parameters4, parameters5, parameters6);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            Vector4 p3 = parameters3.vector4Value;
            Vector4 p4 = parameters4.vector4Value;
            Vector4 p5 = parameters5.vector4Value;
            Vector4 p6 = parameters6.vector4Value;

            // Repair non-monotonic stops (hand-edited profile, or a layer written by an older
            // build) so the sliders below can use their neighbours as bounds without inverting.
            p1.x = Mathf.Clamp01(p1.x);
            p1.y = Mathf.Clamp(p1.y, p1.x, 1.0f);
            p1.z = Mathf.Clamp(p1.z, p1.y, 1.0f);
            p1.w = Mathf.Clamp(p1.w, p1.z, 1.0f);

            int input = Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, GradientMapInputNames.Length - 1);
            input = EditorGUI.Popup(
                EditorGUI.PrefixLabel(
                    new Rect(rect.x, y, rect.width, LineHeight),
                    new GUIContent("输入", "用画面中的哪个量去索引色标。亮度使用 Rec.709 加权（0.2126/0.7152/0.0722）。")),
                input,
                GradientMapInputNames);
            p0.x = input;
            y += LineHeight + LineSpacing;

            p0.y = EditorGUI.Slider(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("黑场", "输入窗口的下端，映射到色标 1。与白场相等时退化为二值阈值。"),
                p0.y,
                0.0f,
                Mathf.Max(p0.z, 0.0f));
            y += LineHeight + LineSpacing;
            p0.z = EditorGUI.Slider(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("白场", "输入窗口的上端，映射到色标 4。"),
                p0.z,
                Mathf.Min(p0.y, 1.0f),
                1.0f);
            y += LineHeight + LineSpacing;

            int bands = Mathf.Clamp(Mathf.RoundToInt(p6.z), 0, 16);
            DrawGradientMapPreview(
                new Rect(rect.x, y, rect.width, LineHeight),
                p1,
                p2,
                p3,
                p4,
                p5,
                bands);
            y += LineHeight + LineSpacing;

            p2 = DrawVectorColorLineWithAlpha(rect.x, y, rect.width, "颜色 1（黑场端）", p2);
            y += LineHeight + LineSpacing;
            p3 = DrawVectorColorLineWithAlpha(rect.x, y, rect.width, "颜色 2", p3);
            y += LineHeight + LineSpacing;
            p4 = DrawVectorColorLineWithAlpha(rect.x, y, rect.width, "颜色 3", p4);
            y += LineHeight + LineSpacing;
            p5 = DrawVectorColorLineWithAlpha(rect.x, y, rect.width, "颜色 4（白场端）", p5);
            y += LineHeight + LineSpacing;

            EditorGUI.LabelField(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("色标位置", "0 = 输入黑场，1 = 输入白场。拖动时不会越过相邻色标。"),
                EditorStyles.miniBoldLabel);
            y += LineHeight + LineSpacing;

            p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "位置 1", p1.x, 0.0f, p1.y);
            y += LineHeight + LineSpacing;
            p1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "位置 2", p1.y, p1.x, p1.z);
            y += LineHeight + LineSpacing;
            p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "位置 3", p1.z, p1.y, p1.w);
            y += LineHeight + LineSpacing;
            p1.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "位置 4", p1.w, p1.z, 1.0f);
            y += LineHeight + LineSpacing;

            int space = Mathf.Clamp(Mathf.RoundToInt(p6.x), 0, GradientMapInterpolationSpaceNames.Length - 1);
            space = EditorGUI.Popup(
                EditorGUI.PrefixLabel(
                    new Rect(rect.x, y, rect.width, LineHeight),
                    new GUIContent("插值空间", "色标之间的插值空间。显示空间与旧行为一致；线性光的中间调更亮；Oklab 让宽色标的中间不容易发灰。")),
                space,
                GradientMapInterpolationSpaceNames);
            p6.x = space;
            y += LineHeight + LineSpacing;

            bands = EditorGUI.IntSlider(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("色阶数", "0 或 1 = 关闭。N ≥ 2 时把输入量化成 N 级再查表，得到平涂色块。"),
                bands,
                0,
                16);
            p6.z = bands;
            y += LineHeight + LineSpacing;

            p6.y = EditorGUI.Toggle(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("反转", "反转色标方向：输入黑场对应色标 4。"),
                p6.y > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;

            EditorGUI.LabelField(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("抖动", "0.5 = ±0.5/255（1 个 8-bit 码）；作用在输出上，用来压掉大面积渐变的色带。"));
            p0.w = EditorGUI.Slider(new Rect(rect.x + 76.0f, y, rect.width - 76.0f, LineHeight), p0.w, 0.0f, 4.0f);
            y += LineHeight + LineSpacing;

            DrawBlendModeLine(rect.x, y, rect.width, blendMode);

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = p3;
            parameters4.vector4Value = p4;
            parameters5.vector4Value = p5;
            parameters6.vector4Value = p6;
            EditorGUI.indentLevel--;
        }

        private static void EnsureGradientMapDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty parameters2,
            SerializedProperty parameters3,
            SerializedProperty parameters4,
            SerializedProperty parameters5,
            SerializedProperty parameters6)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            // An uninitialised layer has an all-zero parameters0. Anything else is treated as
            // authored data, so a stop that the user deliberately set to transparent black is not
            // overwritten on the next repaint.
            if (parameters0.vector4Value.sqrMagnitude > 0.000001f)
            {
                return;
            }

            // Default = the linear black-to-white ramp, which is what makes a freshly added
            // gradient map behave like a luminance-to-greyscale conversion.
            parameters0.vector4Value = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
            parameters1.vector4Value = new Vector4(0.0f, 1.0f / 3.0f, 2.0f / 3.0f, 1.0f);
            parameters2.vector4Value = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
            parameters3.vector4Value = new Vector4(1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 3.0f, 1.0f);
            parameters4.vector4Value = new Vector4(2.0f / 3.0f, 2.0f / 3.0f, 2.0f / 3.0f, 1.0f);
            parameters5.vector4Value = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);

            if (parameters6 != null && parameters6.propertyType == SerializedPropertyType.Vector4)
            {
                parameters6.vector4Value = Vector4.zero;
            }
        }

        /// <summary>
        /// Draws the ramp strip: the authoring preview for the four stops, their alpha and the
        /// posterise bands.
        /// </summary>
        /// <remarks>
        /// The strip is evaluated in display space, which is exactly what the shader does in its
        /// default interpolation space; linear-light and Oklab interpolation only exist in the
        /// render, so the strip is not a preview of those two modes.
        /// </remarks>
        private static void DrawGradientMapPreview(
            Rect rect,
            Vector4 positions,
            Vector4 color1,
            Vector4 color2,
            Vector4 color3,
            Vector4 color4,
            int bands)
        {
            const int sampleCount = 64;
            float sampleWidth = rect.width / sampleCount;

            // Flat backing colour so the per-stop alpha is readable.
            EditorGUI.DrawRect(rect, new Color(0.25f, 0.25f, 0.25f, 1.0f));

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (i + 0.5f) / sampleCount;
                if (bands >= 2)
                {
                    t = Mathf.Clamp01(Mathf.Floor(t * bands) / Mathf.Max(bands - 1, 1));
                }

                Color color = EvaluateGradientMapRamp(t, positions, color1, color2, color3, color4);
                EditorGUI.DrawRect(
                    new Rect(rect.x + i * sampleWidth, rect.y, sampleWidth + 0.5f, rect.height),
                    color);
            }

            for (int i = 0; i < GradientMapStopCount; i++)
            {
                float position = Mathf.Clamp01(GetGradientMapStopPosition(positions, i));
                Color stopColor = GetGradientMapStopColor(color1, color2, color3, color4, i);
                Color marker = (stopColor.r * 0.3f + stopColor.g * 0.59f + stopColor.b * 0.11f) > 0.55f
                    ? new Color(0.0f, 0.0f, 0.0f, 0.8f)
                    : new Color(1.0f, 1.0f, 1.0f, 0.8f);
                EditorGUI.DrawRect(
                    new Rect(rect.x + position * rect.width - 0.5f, rect.y, 1.0f, rect.height),
                    marker);
            }

            Color border = new Color(0.0f, 0.0f, 0.0f, 0.6f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1.0f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1.0f, rect.width, 1.0f), border);

            GUI.Label(
                rect,
                new GUIContent(string.Empty, "渐变条预览（显示空间）。α 与灰底混合；竖线是色标位置。"),
                GUIStyle.none);
        }

        private static Color EvaluateGradientMapRamp(
            float t,
            Vector4 positions,
            Vector4 color1,
            Vector4 color2,
            Vector4 color3,
            Vector4 color4)
        {
            // Mirrors SampleGradientMapRamp in GradientMap.shader, display-space branch.
            Vector4 colorA = color1;
            Vector4 colorB = color1;
            float local = 0.0f;

            if (t >= positions.w)
            {
                colorA = color4;
                colorB = color4;
            }
            else if (t >= positions.z)
            {
                colorA = color3;
                colorB = color4;
                local = (t - positions.z) / Mathf.Max(positions.w - positions.z, 0.000001f);
            }
            else if (t >= positions.y)
            {
                colorA = color2;
                colorB = color3;
                local = (t - positions.y) / Mathf.Max(positions.z - positions.y, 0.000001f);
            }
            else if (t >= positions.x)
            {
                colorA = color1;
                colorB = color2;
                local = (t - positions.x) / Mathf.Max(positions.y - positions.x, 0.000001f);
            }

            local = Mathf.Clamp01(local);
            return new Color(
                Mathf.Clamp01(Mathf.Lerp(colorA.x, colorB.x, local)),
                Mathf.Clamp01(Mathf.Lerp(colorA.y, colorB.y, local)),
                Mathf.Clamp01(Mathf.Lerp(colorA.z, colorB.z, local)),
                Mathf.Lerp(colorA.w, colorB.w, local));
        }

        private static float GetGradientMapStopPosition(Vector4 positions, int index)
        {
            if (index == 0) return positions.x;
            if (index == 1) return positions.y;
            if (index == 2) return positions.z;
            return positions.w;
        }

        private static Color GetGradientMapStopColor(Vector4 color1, Vector4 color2, Vector4 color3, Vector4 color4, int index)
        {
            Vector4 value = color1;
            if (index == 1) value = color2;
            if (index == 2) value = color3;
            if (index == 3) value = color4;
            return new Color(value.x, value.y, value.z, value.w);
        }
    }
}
