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

        /// <summary>Interpolation space of the ramp bake (see ImageProcessGradientRampBaker).</summary>
        private static readonly string[] GradientMapInterpolationSpaceNames =
        {
            "显示空间", "线性光", "Oklab"
        };

        /// <summary>
        /// Body lines after the foldout row, excluding the rows the gradient editor itself takes:
        /// input, black point, white point, interpolation space, posterise bands, reverse, dither,
        /// blend mode. (The gradient editor draws its own ramp bar, so there is no preview row.)
        /// </summary>
        private const int GradientMapFixedBodyLineCount = 8;

        private static bool HasGradientMapParameters(SerializedProperty element)
        {
            if (element == null)
            {
                return false;
            }

            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters6 = element.FindPropertyRelative("parameters6");
            SerializedProperty ramp = element.FindPropertyRelative("ramp");

            return parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4
                && parameters6 != null && parameters6.propertyType == SerializedPropertyType.Vector4
                && ramp != null && ramp.propertyType == SerializedPropertyType.Gradient
                && element.FindPropertyRelative("blendMode") != null;
        }

        /// <summary>Rows the gradient editor occupies, measured from the property itself.</summary>
        private static int GetGradientMapRampLineCount(SerializedProperty element)
        {
            SerializedProperty ramp = element.FindPropertyRelative("ramp");
            if (ramp == null)
            {
                return 2;
            }

            float height = GetGradientMapRampHeight(ramp);
            float rowHeight = LineHeight + LineSpacing;
            return Mathf.Max(1, Mathf.CeilToInt((height + LineSpacing) / rowHeight));
        }

        private static float GetGradientMapRampHeight(SerializedProperty ramp)
        {
            float height = EditorGUI.GetPropertyHeight(ramp, GUIContent.none, true);
            return Mathf.Max(LineHeight * 2.0f, height);
        }

        private static int GetGradientMapLineCount(SerializedProperty element)
        {
            // Malformed layers fall back to the generic single-line row, which reserves one line
            // (the foldout) and nothing else; GetElementLineCount already counts that one.
            if (!HasGradientMapParameters(element))
            {
                return 0;
            }

            return GradientMapFixedBodyLineCount + GetGradientMapRampLineCount(element);
        }

        private void DrawGradientMapElement(Rect rect, SerializedProperty element)
        {
            if (!HasGradientMapParameters(element))
            {
                DrawSimpleLayerElement(rect, element);
                return;
            }

            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters6 = element.FindPropertyRelative("parameters6");
            SerializedProperty ramp = element.FindPropertyRelative("ramp");
            SerializedProperty blendMode = element.FindPropertyRelative("blendMode");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureGradientMapDefaults(parameters0, parameters6, ramp);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p6 = parameters6.vector4Value;
            Gradient gradient = ramp.gradientValue ?? CreateDefaultGradientMapRamp();

            // Unity's own gradient editor: up to 8 colour keys + 8 alpha keys, Blend or Fixed. It
            // already draws the ramp bar and the key handles, so this effect adds no second preview
            // row, and the field needs no label of its own.
            float rampHeight = GetGradientMapRampHeight(ramp);
            Rect rampRect = new Rect(rect.x, y, rect.width, rampHeight);
            gradient = EditorGUI.GradientField(rampRect, gradient);
            ramp.gradientValue = gradient;
            GUI.Label(
                rampRect,
                new GUIContent(string.Empty, "Unity 渐变：最多 8 个颜色键 + 8 个透明度键；Fixed 模式下每个键的颜色保持到下一个键。"),
                GUIStyle.none);
            y += rampHeight + LineSpacing;

            int space = Mathf.Clamp(Mathf.RoundToInt(p6.x), 0, GradientMapInterpolationSpaceNames.Length - 1);
            int bands = Mathf.Clamp(Mathf.RoundToInt(p6.z), 0, 16);

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
                new GUIContent("黑场", "输入窗口的下端，映射到色标起点。与白场相等时退化为二值阈值。"),
                p0.y,
                0.0f,
                Mathf.Max(p0.z, 0.0f));
            y += LineHeight + LineSpacing;
            p0.z = EditorGUI.Slider(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("白场", "输入窗口的上端，映射到色标终点。"),
                p0.z,
                Mathf.Min(p0.y, 1.0f),
                1.0f);
            y += LineHeight + LineSpacing;

            space = EditorGUI.Popup(
                EditorGUI.PrefixLabel(
                    new Rect(rect.x, y, rect.width, LineHeight),
                    new GUIContent("插值空间", "烘焙色标时用的插值空间。显示空间与 Unity 渐变条一致；线性光的中间调更亮；Oklab 让宽色标的中间不容易发灰。")),
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
                new GUIContent("反转", "反转色标方向：输入黑场对应色标末端。"),
                p6.y > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;

            EditorGUI.LabelField(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("抖动", "0.5 = ±0.5/255（1 个 8-bit 码）；作用在输出上，用来压掉大面积渐变的色带。"));
            p0.w = EditorGUI.Slider(new Rect(rect.x + 76.0f, y, rect.width - 76.0f, LineHeight), p0.w, 0.0f, 4.0f);
            y += LineHeight + LineSpacing;

            DrawBlendModeLine(rect.x, y, rect.width, blendMode);

            parameters0.vector4Value = p0;
            parameters6.vector4Value = p6;
            EditorGUI.indentLevel--;
        }

        private static void EnsureGradientMapDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters6,
            SerializedProperty ramp)
        {
            if (ramp != null &&
                ramp.propertyType == SerializedPropertyType.Gradient &&
                ramp.gradientValue == null)
            {
                ramp.gradientValue = CreateDefaultGradientMapRamp();
            }

            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            // An uninitialised layer has an all-zero parameters0. Anything else is treated as
            // authored data, so a deliberately black window is not overwritten on the next repaint.
            if (parameters0.vector4Value.sqrMagnitude > 0.000001f)
            {
                return;
            }

            parameters0.vector4Value = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
            if (parameters6 != null && parameters6.propertyType == SerializedPropertyType.Vector4)
            {
                parameters6.vector4Value = Vector4.zero;
            }
        }

        /// <summary>
        /// Default ramp for new layers and for the reset preset: black to white, which makes a
        /// freshly added gradient map behave like a luminance-to-greyscale conversion.
        /// </summary>
        internal static Gradient CreateDefaultGradientMapRamp()
        {
            var gradient = new Gradient { mode = GradientMode.Blend };
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.black, 0.0f),
                    new GradientColorKey(Color.white, 1.0f)
                },
                new[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(1.0f, 1.0f)
                });
            return gradient;
        }

        /// <summary>Sets a layer's ramp from a preset.</summary>
        private static void SetGradientMapRamp(SerializedProperty element, Gradient gradient)
        {
            SerializedProperty ramp = element?.FindPropertyRelative("ramp");
            if (ramp != null && ramp.propertyType == SerializedPropertyType.Gradient)
            {
                ramp.gradientValue = gradient;
            }
        }
    }
}
