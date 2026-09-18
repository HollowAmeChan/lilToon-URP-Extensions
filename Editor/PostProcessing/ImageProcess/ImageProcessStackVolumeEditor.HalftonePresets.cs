using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        /// <summary>
        /// One 网点 look for <see cref="ImageProcessEffect.Halftone"/>.
        /// </summary>
        /// <remarks>
        /// The numbers follow <c>Halftone.shader</c> and <c>ImageProcessHalftoneMath</c>:
        /// <list type="bullet">
        /// <item><description><c>Mode</c>: 0 Bayer, 1 round, 2 square, 3 diamond, 4 line.</description></item>
        /// <item><description><c>Input</c>: 0 luma (Rec.709), 1 red, 2 green, 3 blue, 4 maximum, 5 average, 6 saturation.</description></item>
        /// <item><description><c>Window</c> is the black/white point: black = no ink, white = full ink.</description></item>
        /// <item><description><c>Invert</c> 1 inks the dark areas (normal print/comic), 0 the bright ones.</description></item>
        /// <item><description><c>Cell</c> is the dot pitch in screen pixels, <c>Softness</c> the edge width in cell fractions, <c>Jitter</c> the per-cell dot offset (0 = regular screen).</description></item>
        /// <item><description><c>MaxCoverage</c> caps the ink so the darkest areas keep some paper.</description></item>
        /// <item><description><c>BayerSize</c> 0..3 = 2x2 / 3x3 / 4x4 / 8x8; <c>Ratio</c> stretches the diamond dot.</description></item>
        /// <item><description><c>Composite</c>: 0 OverInk (print on the image), 1 TwoTone (ink+paper only), 2 Multiply, 3 Mask (dots keep the image colour).</description></item>
        /// </list>
        /// Halftone.md describes the intended pairing with 渐变映射: the ramp decides the palette,
        /// the screen decides how much ink each cell gets.
        /// </remarks>
        private readonly struct HalftoneLook
        {
            public readonly string Group;
            public readonly string Label;
            public readonly float Intensity;
            public readonly int Mode;
            public readonly int Input;
            public readonly Vector2 Window;
            public readonly float Cell;
            public readonly float Angle;
            public readonly float Invert;
            public readonly float Softness;
            public readonly float Jitter;
            public readonly float MaxCoverage;
            public readonly int BayerSize;
            public readonly float Ratio;
            public readonly float Dither;
            public readonly int Composite;
            public readonly Color Ink;
            public readonly Color Paper;
            public readonly ImageProcessBlendMode BlendMode;

            public HalftoneLook(
                string group,
                string label,
                float intensity,
                int mode,
                int input,
                Vector2 window,
                float cell,
                float angle,
                float invert,
                float softness,
                float jitter,
                float maxCoverage,
                int bayerSize,
                float ratio,
                float dither,
                int composite,
                Color ink,
                Color paper,
                ImageProcessBlendMode blendMode)
            {
                Group = group;
                Label = label;
                Intensity = intensity;
                Mode = mode;
                Input = input;
                Window = window;
                Cell = cell;
                Angle = angle;
                Invert = invert;
                Softness = softness;
                Jitter = jitter;
                MaxCoverage = maxCoverage;
                BayerSize = bayerSize;
                Ratio = ratio;
                Dither = dither;
                Composite = composite;
                Ink = ink;
                Paper = paper;
                BlendMode = blendMode;
            }
        }

        // Hand-tuned starting points, one per use case the effect is meant to cover:
        // comic screens, pop-art two-colour prints, print/paper stocks, engraving lines and the
        // 渐变映射 pairing. Values are screen-space (cell = pixels at the render resolution).
        private static readonly HalftoneLook[] HalftoneLooks =
        {
            // ---- 漫画 ----
            new HalftoneLook(
                "漫画", "黑白漫画", 1.0f,
                (int)ImageProcessHalftoneMode.Round, (int)ImageProcessHalftoneInput.Luminance, new Vector2(0.0f, 1.0f),
                6.0f, 45.0f, 1.0f, 0.10f, 0.0f, 1.00f, 3, 1.0f, 0.0f,
                (int)ImageProcessHalftoneComposite.TwoTone, Color.black, Color.white, ImageProcessBlendMode.Normal),
            new HalftoneLook(
                "漫画", "少年漫粗网", 1.0f,
                (int)ImageProcessHalftoneMode.Round, (int)ImageProcessHalftoneInput.Luminance, new Vector2(0.04f, 0.96f),
                11.0f, 45.0f, 1.0f, 0.08f, 0.0f, 0.95f, 3, 1.0f, 0.0f,
                (int)ImageProcessHalftoneComposite.TwoTone, new Color(0.04f, 0.04f, 0.05f, 1.0f), new Color(0.98f, 0.97f, 0.94f, 1.0f), ImageProcessBlendMode.Normal),
            new HalftoneLook(
                "漫画", "少女漫细网", 1.0f,
                (int)ImageProcessHalftoneMode.Round, (int)ImageProcessHalftoneInput.Luminance, new Vector2(0.0f, 0.92f),
                3.5f, 45.0f, 1.0f, 0.22f, 0.0f, 0.85f, 3, 1.0f, 0.0f,
                (int)ImageProcessHalftoneComposite.TwoTone, new Color(0.16f, 0.14f, 0.18f, 1.0f), Color.white, ImageProcessBlendMode.Normal),

            // ---- 波普 ----
            new HalftoneLook(
                "波普", "波普圆点", 1.0f,
                (int)ImageProcessHalftoneMode.Round, (int)ImageProcessHalftoneInput.Luminance, new Vector2(0.0f, 1.0f),
                9.0f, 15.0f, 1.0f, 0.06f, 0.0f, 1.00f, 3, 1.0f, 0.0f,
                (int)ImageProcessHalftoneComposite.OverInk, new Color(1.0f, 0.10f, 0.45f, 1.0f), Color.white, ImageProcessBlendMode.Normal),
            new HalftoneLook(
                "波普", "丝网双色", 1.0f,
                (int)ImageProcessHalftoneMode.Round, (int)ImageProcessHalftoneInput.Luminance, new Vector2(0.05f, 0.95f),
                8.0f, 30.0f, 1.0f, 0.12f, 0.15f, 1.00f, 3, 1.0f, 0.0f,
                (int)ImageProcessHalftoneComposite.TwoTone, new Color(0.05f, 0.25f, 0.35f, 1.0f), new Color(0.97f, 0.94f, 0.86f, 1.0f), ImageProcessBlendMode.Normal),
            new HalftoneLook(
                "波普", "海报方点", 1.0f,
                (int)ImageProcessHalftoneMode.Square, (int)ImageProcessHalftoneInput.Luminance, new Vector2(0.06f, 0.94f),
                7.0f, 45.0f, 1.0f, 0.05f, 0.0f, 1.00f, 3, 1.0f, 0.0f,
                (int)ImageProcessHalftoneComposite.TwoTone, Color.black, new Color(1.0f, 0.85f, 0.15f, 1.0f), ImageProcessBlendMode.Normal),

            // ---- 印刷 ----
            new HalftoneLook(
                "印刷", "报纸", 1.0f,
                (int)ImageProcessHalftoneMode.Round, (int)ImageProcessHalftoneInput.Luminance, new Vector2(0.08f, 0.92f),
                4.5f, 45.0f, 1.0f, 0.25f, 0.25f, 0.82f, 3, 1.0f, 0.3f,
                (int)ImageProcessHalftoneComposite.TwoTone, new Color(0.09f, 0.09f, 0.11f, 1.0f), new Color(0.93f, 0.90f, 0.82f, 1.0f), ImageProcessBlendMode.Normal),
            new HalftoneLook(
                "印刷", "杂志细网", 1.0f,
                (int)ImageProcessHalftoneMode.Round, (int)ImageProcessHalftoneInput.Luminance, new Vector2(0.0f, 1.0f),
                3.0f, 45.0f, 1.0f, 0.22f, 0.0f, 0.70f, 3, 1.0f, 0.0f,
                (int)ImageProcessHalftoneComposite.OverInk, Color.black, Color.white, ImageProcessBlendMode.Normal),
            new HalftoneLook(
                "印刷", "菱形链网", 1.0f,
                (int)ImageProcessHalftoneMode.Diamond, (int)ImageProcessHalftoneInput.Luminance, new Vector2(0.02f, 0.98f),
                8.0f, 15.0f, 1.0f, 0.08f, 0.0f, 0.95f, 3, 1.0f, 0.0f,
                (int)ImageProcessHalftoneComposite.TwoTone, new Color(0.12f, 0.12f, 0.14f, 1.0f), new Color(0.96f, 0.95f, 0.92f, 1.0f), ImageProcessBlendMode.Normal),

            // ---- 线条 ----
            new HalftoneLook(
                "线条", "铜版画线条", 1.0f,
                (int)ImageProcessHalftoneMode.Line, (int)ImageProcessHalftoneInput.Luminance, new Vector2(0.02f, 0.98f),
                6.0f, 30.0f, 1.0f, 0.05f, 0.0f, 0.95f, 3, 1.0f, 0.0f,
                (int)ImageProcessHalftoneComposite.TwoTone, new Color(0.12f, 0.10f, 0.09f, 1.0f), new Color(0.95f, 0.93f, 0.87f, 1.0f), ImageProcessBlendMode.Normal),
            new HalftoneLook(
                "线条", "木刻排线", 1.0f,
                (int)ImageProcessHalftoneMode.Line, (int)ImageProcessHalftoneInput.Luminance, new Vector2(0.0f, 1.0f),
                9.0f, 75.0f, 1.0f, 0.04f, 0.30f, 0.90f, 3, 1.0f, 0.0f,
                (int)ImageProcessHalftoneComposite.Multiply, Color.black, Color.white, ImageProcessBlendMode.Normal),
            new HalftoneLook(
                "线条", "斜线阴影", 1.0f,
                (int)ImageProcessHalftoneMode.Line, (int)ImageProcessHalftoneInput.Luminance, new Vector2(0.10f, 1.0f),
                4.0f, 135.0f, 1.0f, 0.10f, 0.0f, 0.85f, 3, 1.0f, 0.0f,
                (int)ImageProcessHalftoneComposite.OverInk, Color.black, Color.white, ImageProcessBlendMode.Normal),

            // ---- 风格 ----
            new HalftoneLook(
                "风格", "复古拜耳", 1.0f,
                (int)ImageProcessHalftoneMode.Bayer, (int)ImageProcessHalftoneInput.Luminance, new Vector2(0.05f, 0.95f),
                3.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.00f, 3, 1.0f, 0.0f,
                (int)ImageProcessHalftoneComposite.OverInk, Color.black, Color.white, ImageProcessBlendMode.Normal),
            new HalftoneLook(
                "风格", "拜耳 4 阶", 1.0f,
                (int)ImageProcessHalftoneMode.Bayer, (int)ImageProcessHalftoneInput.Luminance, new Vector2(0.0f, 1.0f),
                5.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.00f, 2, 1.0f, 0.0f,
                (int)ImageProcessHalftoneComposite.TwoTone, new Color(0.07f, 0.07f, 0.09f, 1.0f), new Color(0.90f, 0.92f, 0.86f, 1.0f), ImageProcessBlendMode.Normal),
            new HalftoneLook(
                "风格", "配合渐变映射", 0.85f,
                (int)ImageProcessHalftoneMode.Round, (int)ImageProcessHalftoneInput.Luminance, new Vector2(0.02f, 0.98f),
                5.0f, 45.0f, 1.0f, 0.20f, 0.0f, 1.00f, 3, 1.0f, 0.0f,
                (int)ImageProcessHalftoneComposite.Mask, new Color(0.2f, 0.18f, 0.2f, 1.0f), new Color(0.86f, 0.86f, 0.89f, 1.0f), ImageProcessBlendMode.Normal),
        };

        private void AddImageProcessHalftoneLookMenuItems(GenericMenu menu, string propertyPath, ImageProcessEffect effect)
        {
            for (int i = 0; i < HalftoneLooks.Length; i++)
            {
                HalftoneLook look = HalftoneLooks[i];
                string path = look.Group + "/" + look.Label;
                menu.AddItem(new GUIContent(path), false, () => ApplyImageProcessHalftoneLook(propertyPath, effect, look));
            }
        }

        private void ApplyImageProcessHalftoneLook(string propertyPath, ImageProcessEffect effect, HalftoneLook look)
        {
            ApplyImageProcessPreset(propertyPath, effect, (element, targetEffect) => ApplyImageProcessHalftoneLookPreset(element, targetEffect, look));
        }

        private static void ApplyImageProcessHalftoneLookPreset(SerializedProperty element, ImageProcessEffect effect, HalftoneLook look)
        {
            // Resets the layer and writes the Halftone defaults first, then the look.
            ApplyImageProcessDefaultPreset(element, effect);
            SetFloat(element, "intensity", look.Intensity);
            SetEnum(element, "blendMode", (int)look.BlendMode);
            SetColor(element, "color", look.Ink);
            SetVector4(
                element,
                "parameters0",
                new Vector4(look.Mode, look.Input, look.Window.x, look.Window.y));
            SetVector4(
                element,
                "parameters1",
                new Vector4(look.Cell, look.Angle, look.Invert, look.Softness));
            SetVector4(
                element,
                "parameters2",
                new Vector4(look.Paper.r, look.Paper.g, look.Paper.b, look.Composite));
            SetVector4(
                element,
                "parameters3",
                new Vector4(look.MaxCoverage, look.Jitter, look.BayerSize, 0.0f));
            SetVector4(
                element,
                "parameters4",
                new Vector4(look.Ratio, look.Dither, 0.0f, 0.0f));
        }
    }
}
