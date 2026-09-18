using UnityEngine;
using UnityEditor;
using lilToon.URP.Extensions.PostProcessing;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        /// <summary>Screen families of the Halftone effect; the index is <c>parameters0.x</c>.</summary>
        private static readonly string[] HalftoneModeNames =
        {
            "拜耳渐变", "圆点", "方点", "菱形", "线条"
        };

        /// <summary>Which quantity indexes the screen; the index is <c>parameters0.y</c>.</summary>
        private static readonly string[] HalftoneInputNames =
        {
            "亮度", "红", "绿", "蓝", "最大值", "平均值", "饱和度"
        };

        /// <summary>How the ink/paper pair meets the image; the index is <c>parameters2.w</c>.</summary>
        private static readonly string[] HalftoneCompositeNames =
        {
            "叠墨", "双色", "乘算", "遮罩原色"
        };

        /// <summary>Bayer matrix sizes; the index is <c>parameters3.z</c>.</summary>
        private static readonly string[] HalftoneBayerSizeNames =
        {
            "2 × 2", "3 × 3", "4 × 4", "8 × 8"
        };

        private const int HalftoneBayerMode = 0;
        private const int HalftoneRoundMode = 1;
        private const int HalftoneDiamondMode = 3;

        /// <summary>Rows drawn for every Halftone layer, not counting the conditional ones.</summary>
        private const int HalftoneFixedBodyLineCount = 15;

        private static bool HasHalftoneParameters(SerializedProperty element)
        {
            if (element == null)
            {
                return false;
            }

            for (int i = 0; i <= 4; i++)
            {
                SerializedProperty parameters = element.FindPropertyRelative("parameters" + i);
                if (parameters == null || parameters.propertyType != SerializedPropertyType.Vector4)
                {
                    return false;
                }
            }

            return element.FindPropertyRelative("color") != null
                && element.FindPropertyRelative("blendMode") != null;
        }

        private static int GetHalftoneMode(SerializedProperty element)
        {
            SerializedProperty parameters0 = element?.FindPropertyRelative("parameters0");
            if (parameters0 == null)
            {
                return HalftoneRoundMode;
            }

            return Mathf.Clamp(Mathf.RoundToInt(parameters0.vector4Value.x), 0, HalftoneModeNames.Length - 1);
        }

        /// <summary>Rows the mode-specific controls occupy: Bayer adds the matrix size, diamond the ratio.</summary>
        private static int GetHalftoneConditionalLineCount(SerializedProperty element)
        {
            if (!HasHalftoneParameters(element))
            {
                return 0;
            }

            int mode = GetHalftoneMode(element);
            return mode == HalftoneBayerMode || mode == HalftoneDiamondMode ? 1 : 0;
        }

        private static int GetHalftoneLineCount(SerializedProperty element)
        {
            // Malformed layers fall back to the generic single-line row, which reserves one line (the
            // foldout) and nothing else; GetElementLineCount already counts that one.
            if (!HasHalftoneParameters(element))
            {
                return 0;
            }

            return HalftoneFixedBodyLineCount + GetHalftoneConditionalLineCount(element);
        }

        private void DrawHalftoneElement(Rect rect, SerializedProperty element)
        {
            if (!HasHalftoneParameters(element))
            {
                DrawSimpleLayerElement(rect, element);
                return;
            }

            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty parameters4 = element.FindPropertyRelative("parameters4");
            SerializedProperty color = element.FindPropertyRelative("color");
            SerializedProperty blendMode = element.FindPropertyRelative("blendMode");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureHalftoneDefaults(parameters0, parameters1, parameters2, parameters3, parameters4);

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

            int mode = Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, HalftoneModeNames.Length - 1);
            mode = EditorGUI.Popup(
                EditorGUI.PrefixLabel(
                    new Rect(rect.x, y, rect.width, LineHeight),
                    new GUIContent("模式", "网屏种类：拜耳渐变是有序抖动（规则的阈值矩阵），圆点/方点/菱形是面积调制的印刷网点，线条是排线。")),
                mode,
                HalftoneModeNames);
            p0.x = mode;
            y += LineHeight + LineSpacing;

            int input = Mathf.Clamp(Mathf.RoundToInt(p0.y), 0, HalftoneInputNames.Length - 1);
            input = EditorGUI.Popup(
                EditorGUI.PrefixLabel(
                    new Rect(rect.x, y, rect.width, LineHeight),
                    new GUIContent("输入", "用画面中的哪个量决定墨量。亮度使用 Rec.709 加权（与渐变映射一致）。")),
                input,
                HalftoneInputNames);
            p0.y = input;
            y += LineHeight + LineSpacing;

            p0.z = EditorGUI.Slider(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("黑场", "输入窗口下端：它对应“没有墨”。与白场相等时退化为二值网点。"),
                p0.z,
                0.0f,
                Mathf.Max(p0.w, 0.0f));
            y += LineHeight + LineSpacing;

            p0.w = EditorGUI.Slider(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("白场", "输入窗口上端：它对应“满墨”。"),
                p0.w,
                Mathf.Min(p0.z, 1.0f),
                1.0f);
            y += LineHeight + LineSpacing;

            p1.z = EditorGUI.Toggle(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("暗部上墨", "开：暗的地方网点更大（正常的印刷/漫画方向）。关：亮的地方网点更大（反白/负片）。"),
                p1.z > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;

            p1.x = EditorGUI.Slider(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("单元大小", "一个网点（或拜耳矩阵的一格）占多少屏幕像素。像素越小网点越细，也越容易在运动时闪烁，4~12 px 比较稳。"),
                p1.x,
                1.0f,
                48.0f);
            y += LineHeight + LineSpacing;

            p1.y = EditorGUI.Slider(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("角度", "网屏旋转角度。45° 的单色网屏最不显眼；多种网屏套印时用 15°/75° 之类互质角度。"),
                p1.y,
                0.0f,
                180.0f);
            y += LineHeight + LineSpacing;

            p1.w = EditorGUI.Slider(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("柔化", "网点边缘的过渡宽度（按单元比例）。给 0.05~0.2 可以压掉高频网点的锯齿和闪烁。"),
                p1.w,
                0.0f,
                0.5f);
            y += LineHeight + LineSpacing;

            p3.y = EditorGUI.Slider(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("网格抖动", "每个单元内的网点随机偏移，打散规则网格，手感更接近手绘/丝网；0 = 规整网屏。"),
                p3.y,
                0.0f,
                1.0f);
            y += LineHeight + LineSpacing;

            p3.x = EditorGUI.Slider(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("浓度上限", "墨量的上限：小于 1 时最暗处也留一点纸色。"),
                p3.x,
                0.0f,
                1.0f);
            y += LineHeight + LineSpacing;

            int composite = Mathf.Clamp(Mathf.RoundToInt(p2.w), 0, HalftoneCompositeNames.Length - 1);
            composite = EditorGUI.Popup(
                EditorGUI.PrefixLabel(
                    new Rect(rect.x, y, rect.width, LineHeight),
                    new GUIContent("合成", "叠墨：只把墨色印上去，其余保持原图。双色：整个画面只剩墨色与纸色。乘算：保留原图明暗并染色。遮罩原色：网点里保留原图颜色，网点外压成纸色。")),
                composite,
                HalftoneCompositeNames);
            p2.w = composite;
            y += LineHeight + LineSpacing;

            // DrawVectorColorLine returns alpha 1, so keep the composite mode that lives in p2.w.
            Vector4 paperColor = DrawVectorColorLine(rect.x, y, rect.width, "纸色", p2);
            p2 = new Vector4(paperColor.x, paperColor.y, paperColor.z, p2.w);
            y += LineHeight + LineSpacing;

            mode = Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, HalftoneModeNames.Length - 1);
            if (mode == HalftoneBayerMode)
            {
                int bayerSize = Mathf.Clamp(Mathf.RoundToInt(p3.z), 0, HalftoneBayerSizeNames.Length - 1);
                bayerSize = EditorGUI.Popup(
                    EditorGUI.PrefixLabel(
                        new Rect(rect.x, y, rect.width, LineHeight),
                        new GUIContent("拜耳矩阵", "阈值矩阵大小：越大过渡层次越多、图案越细。3 × 3 是传统非二次幂矩阵。")),
                    bayerSize,
                    HalftoneBayerSizeNames);
                p3.z = bayerSize;
                y += LineHeight + LineSpacing;
            }
            else if (mode == HalftoneDiamondMode)
            {
                p4.x = EditorGUI.Slider(
                    new Rect(rect.x, y, rect.width, LineHeight),
                    new GUIContent("菱形比例", "横竖比：1 是正菱形（50% 时连成链），偏离 1 得到椭圆网点。"),
                    p4.x,
                    0.2f,
                    3.0f);
                y += LineHeight + LineSpacing;
            }

            EditorGUI.LabelField(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("抖动", "0.5 = ±0.5/255（1 个 8-bit 码）；作用在输出上，用来压掉大面积平涂的色带。"));
            p4.y = EditorGUI.Slider(new Rect(rect.x + 76.0f, y, rect.width - 76.0f, LineHeight), p4.y, 0.0f, 4.0f);
            y += LineHeight + LineSpacing;

            y = DrawPropertyLine(rect.x, y, rect.width, color, "墨色");
            y = DrawBlendModeLine(rect.x, y, rect.width, blendMode);

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = p3;
            parameters4.vector4Value = p4;
            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// An uninitialised layer has all-zero parameter vectors. Anything else is treated as authored
        /// data, so a deliberately black screen is never overwritten on the next repaint.
        /// </summary>
        private static void EnsureHalftoneDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty parameters2,
            SerializedProperty parameters3,
            SerializedProperty parameters4)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            if (parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                // Mode 1 (round), luma input, full 0..1 window.
                parameters0.vector4Value = new Vector4(HalftoneRoundMode, 0.0f, 0.0f, 1.0f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4
                && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                // 6 px cells at 45 degrees, dark areas inked, gentle anti-aliasing.
                parameters1.vector4Value = new Vector4(6.0f, 45.0f, 1.0f, 0.15f);
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4
                && parameters2.vector4Value.sqrMagnitude <= 0.000001f)
            {
                // White paper, OverInk.
                parameters2.vector4Value = new Vector4(1.0f, 1.0f, 1.0f, 0.0f);
            }

            if (parameters3 != null && parameters3.propertyType == SerializedPropertyType.Vector4
                && parameters3.vector4Value.sqrMagnitude <= 0.000001f)
            {
                // Full ink, no jitter, 8x8 Bayer matrix.
                parameters3.vector4Value = new Vector4(1.0f, 0.0f, 3.0f, 0.0f);
            }

            if (parameters4 != null && parameters4.propertyType == SerializedPropertyType.Vector4
                && parameters4.vector4Value.sqrMagnitude <= 0.000001f)
            {
                // Round diamond, no output dither.
                parameters4.vector4Value = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
            }
        }
    }
}
