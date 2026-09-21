using System;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        /// <summary>
        /// One colour-grading look for <see cref="ImageProcessEffect.ColorGradingCustom"/>.
        /// </summary>
        /// <remarks>
        /// The numbers follow the conventions of <c>ColorGradingCustom.shader</c>:
        /// <list type="bullet">
        /// <item><description><c>lift</c>/<c>gamma</c>/<c>gain</c> are the three colour wheels, neutral = (1,1,1,0).</description></item>
        /// <item><description><c>hue</c>/<c>saturation</c>/<c>value</c> are six-colour (R Y G C B M) offsets; <c>luminanceSaturation</c> is indexed by luminance bands (0.0/0.2/0.4/0.6/0.8/1.0).</description></item>
        /// </list>
        /// Every look below is authored in wheel mode 0 (色轮) and leaves the log wheels
        /// (<c>parameters3</c>-<c>parameters5</c>) at zero, so all of its parameters stay visible in the
        /// default editor UI. The table was tuned numerically against a port of the shader; see
        /// Documentation~/后处理/ColorGradingPresets.md.
        /// </remarks>
        private readonly struct ColorGradingLook
        {
            public readonly string Group;
            public readonly string Label;
            public readonly float Intensity;
            public readonly Vector4 Lift;
            public readonly Vector4 Gamma;
            public readonly Vector4 Gain;
            public readonly ColorGradingSixColor Hue;
            public readonly ColorGradingSixColor Saturation;
            public readonly ColorGradingSixColor Value;
            public readonly ColorGradingSixColor LuminanceSaturation;

            public ColorGradingLook(
                string group,
                string label,
                float intensity,
                Vector4 lift,
                Vector4 gamma,
                Vector4 gain,
                ColorGradingSixColor hue,
                ColorGradingSixColor saturation,
                ColorGradingSixColor value,
                ColorGradingSixColor luminanceSaturation)
            {
                Group = group;
                Label = label;
                Intensity = intensity;
                Lift = lift;
                Gamma = gamma;
                Gain = gain;
                Hue = hue;
                Saturation = saturation;
                Value = value;
                LuminanceSaturation = luminanceSaturation;
            }
        }

        /// <summary>Six-colour offsets in R, Y, G, C, B, M order.</summary>
        private readonly struct ColorGradingSixColor
        {
            public readonly float Red;
            public readonly float Yellow;
            public readonly float Green;
            public readonly float Cyan;
            public readonly float Blue;
            public readonly float Magenta;

            public ColorGradingSixColor(float red, float yellow, float green, float cyan, float blue, float magenta)
            {
                Red = red;
                Yellow = yellow;
                Green = green;
                Cyan = cyan;
                Blue = blue;
                Magenta = magenta;
            }
        }

        private static ColorGradingSixColor Six(float red, float yellow, float green, float cyan, float blue, float magenta)
        {
            return new ColorGradingSixColor(red, yellow, green, cyan, blue, magenta);
        }

        private static readonly ColorGradingLook[] ColorGradingLooks =
        {
            // 暖调 — 原有预设（参数原样保留）：gamma 偏红、gain 偏红压蓝；纯白约 +5% 溢出
            new ColorGradingLook("基础", "暖调", 1.0f,
                new Vector4(1.0f, 1.0f, 1.0f, 0.0f),
                new Vector4(1.0f, 0.95f, 0.88f, 0.03f),
                new Vector4(1.08f, 1.02f, 0.92f, 0.04f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 冷调 — 原有预设（参数原样保留）：gamma 偏蓝、gain 偏蓝压红；纯白约 +6% 溢出
            new ColorGradingLook("基础", "冷调", 1.0f,
                new Vector4(1.0f, 1.0f, 1.0f, 0.0f),
                new Vector4(0.9f, 0.96f, 1.08f, 0.02f),
                new Vector4(0.92f, 1.0f, 1.12f, 0.03f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 青橙大片 — Teal & Orange（变形金刚 / 漫威）：青影 ~190°、橙在高光/中间调、不压黑
            new ColorGradingLook("电影感", "青橙大片", 1.0f,
                new Vector4(0.92f, 0.98f, 1.0f, 0.0f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.04f),
                new Vector4(1.05f, 1.0f, 0.95f, 0.005f),
                Six(0.0f, 0.0f, 6.0f, 0.0f, 0.0f, 0.0f),
                Six(0.06f, 0.05f, -0.22f, -0.06f, 0.16f, -0.12f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 漂白旁路 — Bleach Bypass：银盐保留，去饱和 ~40%、压中间调、略偏棕、深黑高白
            new ColorGradingLook("电影感", "漂白旁路", 1.0f,
                new Vector4(1.0f, 0.99f, 0.96f, -0.035f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.16f),
                new Vector4(1.0f, 0.985f, 0.96f, 0.03f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(-0.22f, -0.2f, -0.42f, -0.35f, -0.3f, -0.35f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 暗绿罪案 — Se7en / 芬奇（七宗罪）：橄榄黄绿 ~75–100°、浓黑、去饱和
            new ColorGradingLook("电影感", "暗绿罪案", 1.0f,
                new Vector4(0.95f, 0.97f, 0.88f, -0.02f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.1f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.02f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(-0.05f, -0.12f, -0.25f, -0.2f, -0.22f, -0.15f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 银翼琥珀 — Blade Runner 2049：冷青蓝洛杉矶 + 琥珀拉斯维加斯、黑不死压
            new ColorGradingLook("电影感", "银翼琥珀", 1.0f,
                new Vector4(0.97f, 0.98f, 1.0f, 0.02f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.05f),
                new Vector4(1.02f, 1.0f, 0.95f, -0.02f),
                Six(0.0f, 0.0f, 6.0f, 3.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, -0.12f, -0.06f, 0.08f, -0.05f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 末世废土 — 末世废土（Dune / The Walking Dead 方向）：脏黄绿、低对比抬黑、蓝压得最狠、黄也去饱和
            new ColorGradingLook("电影感", "末世废土", 1.0f,
                new Vector4(1.0f, 0.985f, 0.94f, 0.02f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.03f),
                new Vector4(1.0f, 0.99f, 0.95f, -0.03f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.02f, -0.1f, -0.2f, -0.1f, -0.3f, -0.1f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 黑客绿 — The Matrix：阴影去饱和绿 ~120°、中间调偏黄、整体压暗
            new ColorGradingLook("电影感", "黑客绿", 1.0f,
                new Vector4(0.92f, 0.99f, 0.92f, -0.015f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.03f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.01f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(-0.12f, -0.12f, -0.05f, -0.12f, -0.25f, -0.18f),
                Six(0.0f, 0.015f, 0.005f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 教父琥珀 — The Godfather（教父）：琥珀暖调 + 染料转印的浓黑 + 欠曝
            new ColorGradingLook("电影感", "教父琥珀", 1.0f,
                new Vector4(1.0f, 0.98f, 0.92f, -0.01f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.0f),
                new Vector4(1.0f, 0.98f, 0.92f, -0.025f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, -0.1f, -0.06f, -0.06f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 月光夜戏 — Day for Night（日拍夜）：淡紫蓝、越深越偏海军蓝、低对比低饱和、抬黑保留高光
            new ColorGradingLook("电影感", "月光夜戏", 1.0f,
                new Vector4(0.95f, 0.97f, 1.0f, 0.02f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.05f),
                new Vector4(1.0f, 0.98f, 1.04f, -0.42f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(-0.2f, -0.2f, -0.35f, -0.25f, -0.05f, -0.25f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 北欧冷调 — Nordic Noir（谋杀 / 桥）：强去饱和 + 弱冷调（来源对偏蓝还是偏卡其有分歧）
            new ColorGradingLook("电影感", "北欧冷调", 1.0f,
                new Vector4(0.92f, 0.97f, 1.0f, -0.02f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.05f),
                new Vector4(0.99f, 1.0f, 1.02f, -0.02f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(-0.1f, -0.15f, -0.35f, -0.25f, -0.2f, -0.3f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 赛博霓虹 — 赛博霓虹（霓虹恶魔 / 碳变）：青影 + 洋红高光；实际影片靠自发光光源，属类型化处理
            new ColorGradingLook("电影感", "赛博霓虹", 1.0f,
                new Vector4(0.94f, 0.99f, 1.0f, 0.0f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.05f),
                new Vector4(1.02f, 0.97f, 1.0f, -0.03f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.05f, -0.05f, -0.18f, 0.15f, 0.1f, 0.3f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 交叉冲洗 — Cross Process（E-6 冲 C-41）：青绿阴影 + 洋红中性 + 金黄高光；蓝通道最陡（gamma.rgb 非中性）
            new ColorGradingLook("电影感", "交叉冲洗", 1.0f,
                new Vector4(0.9f, 0.96f, 0.98f, 0.0f),
                new Vector4(1.02f, 1.0f, 0.96f, -0.08f),
                new Vector4(1.02f, 1.01f, 0.94f, 0.005f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.05f, 0.1f, -0.15f, 0.1f, 0.15f, 0.1f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 柯达 Portra 400 — Kodak Portra 400：低对比、白点 ~0.98、饱和 −2%；暖抬脚来自第三方实现而非柯达数据
            new ColorGradingLook("胶片", "柯达 Portra 400", 1.0f,
                new Vector4(1.0f, 0.99f, 0.97f, 0.015f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.055f),
                new Vector4(1.02f, 1.0f, 0.98f, -0.03f),
                Six(0.0f, 0.0f, -8.0f, 0.0f, 0.0f, 0.0f),
                Six(0.06f, 0.04f, -0.16f, -0.08f, 0.0f, -0.08f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 富士 Eterna — Fujifilm Eterna 400/500：柔灰低对比、阴影偏青蓝（不是绿）
            new ColorGradingLook("胶片", "富士 Eterna", 1.0f,
                new Vector4(0.94f, 0.97f, 0.99f, 0.0f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.03f),
                new Vector4(0.99f, 1.0f, 1.0f, -0.02f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(-0.08f, -0.12f, -0.2f, -0.15f, -0.12f, -0.18f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // CineStill 800T — CineStill 800T：额定 3200K 下中性（日光/蓝调才偏蓝）、绿转青、红光晕高光
            new ColorGradingLook("胶片", "CineStill 800T", 1.0f,
                new Vector4(0.96f, 0.98f, 1.0f, 0.0f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.02f),
                new Vector4(1.03f, 0.99f, 0.99f, -0.005f),
                Six(0.0f, 4.0f, 14.0f, 0.0f, 0.0f, 0.0f),
                Six(0.1f, -0.04f, -0.1f, 0.04f, 0.12f, 0.04f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 柯达 2383 印片 — Kodak 2383 印片：陡中间调 + 略高于纯黑的弧形脚 + 中性高光 + 青橙分离
            new ColorGradingLook("胶片", "柯达 2383 印片", 1.0f,
                new Vector4(0.99f, 0.995f, 1.0f, 0.003f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.165f),
                new Vector4(1.0f, 0.995f, 0.99f, 0.01f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.05f, 0.06f, -0.05f, 0.05f, 0.12f, 0.04f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 特艺三色 — Technicolor 3-strip：染料层浓黑 + 极高对比 + 原色饱和
            new ColorGradingLook("胶片", "特艺三色", 1.0f,
                new Vector4(1.0f, 1.0f, 1.0f, -0.03f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.08f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.02f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.06f, 0.12f, 0.18f, 0.2f, 0.22f, 0.14f),
                Six(0.0f, 0.0f, 0.0f, 0.02f, 0.02f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 爱克发 Ultra 100 — Agfa Ultra 100：饱和 +15%、黑压到 0、白点不压（对赛璐璐最友好）
            new ColorGradingLook("胶片", "爱克发 Ultra 100", 1.0f,
                new Vector4(1.0f, 0.99f, 0.98f, 0.0f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.03f),
                new Vector4(1.0f, 0.99f, 0.98f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.08f, 0.12f, 0.15f, 0.15f, 0.12f, 0.15f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 富士 Velvia 50 — Fujifilm Velvia 50：饱和 +25%、黑压到 0、风光反转片
            new ColorGradingLook("胶片", "富士 Velvia 50", 1.0f,
                new Vector4(1.0f, 1.0f, 1.0f, 0.0f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.05f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.0f),
                Six(0.0f, -4.0f, -6.0f, 0.0f, 0.0f, 0.0f),
                Six(0.12f, 0.15f, 0.2f, 0.15f, 0.12f, 0.16f),
                Six(0.0f, 0.0f, 0.0f, 0.01f, 0.01f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 柯达 Kodachrome — Kodachrome 25/64：阴影偏蓝、相对高对比、绿蓝红极饱和、黑不抬
            new ColorGradingLook("胶片", "柯达 Kodachrome", 1.0f,
                new Vector4(0.94f, 0.97f, 1.0f, -0.01f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.09f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.005f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.12f, 0.1f, 0.2f, 0.16f, 0.14f, 0.08f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 黑白胶片 — 中性黑白（负片扫描）；HSV 去饱和会用 V 而非亮度，见文档
            new ColorGradingLook("胶片", "黑白胶片", 1.0f,
                new Vector4(1.0f, 1.0f, 1.0f, -0.02f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.03f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.06f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(-1.0f, -1.0f, -1.0f, -1.0f, -1.0f, -1.0f),
                Six(0.0f, -0.02f, -0.06f, -0.1f, -0.16f, -0.1f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 黑色电影 — Film Noir（高反差黑白）：单光源、无中间调补光
            new ColorGradingLook("胶片", "黑色电影", 1.0f,
                new Vector4(1.0f, 1.0f, 1.0f, -0.05f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.2f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.02f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(-1.0f, -1.0f, -1.0f, -1.0f, -1.0f, -1.0f),
                Six(0.0f, -0.02f, -0.06f, -0.1f, -0.16f, -0.1f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 复古棕褐 — Sepia / 老照片
            new ColorGradingLook("胶片", "复古棕褐", 1.0f,
                new Vector4(1.0f, 0.95f, 0.86f, 0.0f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.0f),
                new Vector4(1.0f, 0.96f, 0.82f, -0.03f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(-0.85f, -0.85f, -0.85f, -0.85f, -0.85f, -0.85f),
                Six(-0.18f, -0.08f, -0.1f, -0.08f, -0.12f, -0.1f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 新海诚青空 — 新海誠：天空色相带 194–209°、低亮度对比 + 高彩度对比、白柔和滚降
            new ColorGradingLook("动画", "新海诚青空", 1.0f,
                new Vector4(0.99f, 1.0f, 1.01f, 0.0f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.05f),
                new Vector4(1.0f, 1.03f, 1.06f, 0.005f),
                Six(0.0f, 0.0f, 0.0f, 6.0f, -8.0f, 0.0f),
                Six(0.08f, 0.0f, 0.02f, 0.14f, 0.2f, 0.02f),
                Six(0.0f, 0.0f, 0.0f, 0.02f, 0.02f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 京阿尼通透 — 京都アニメーション的合成方向（无公开数值，属本表插值）
            new ColorGradingLook("动画", "京阿尼通透", 1.0f,
                new Vector4(0.99f, 1.0f, 1.0f, 0.01f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.09f),
                new Vector4(1.0f, 1.0f, 1.02f, 0.015f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.04f, 0.0f, -0.05f, 0.0f, 0.04f, -0.05f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, -0.08f, -0.05f, 0.0f, 0.0f)),
            // 日系空气感 — 日系清新 / ハイキー：抬黑 + 低对比 + 高光偏青 188°、绿转青、蓝去饱和
            new ColorGradingLook("动画", "日系空气感", 1.0f,
                new Vector4(0.98f, 1.0f, 1.0f, 0.035f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.06f),
                new Vector4(0.98f, 0.995f, 1.01f, -0.035f),
                Six(0.0f, 0.0f, 11.0f, 0.0f, 0.0f, 0.0f),
                Six(-0.03f, -0.06f, -0.15f, 0.02f, -0.1f, -0.18f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, -0.08f, -0.08f, 0.0f, 0.0f, 0.0f)),
            // 赛璐璐高饱和 — TV 赛璐璐：黑不是纯黑（#3A3A3A 底 / #151313 线）、阴影色偏蓝紫、肤色阴影偏红
            new ColorGradingLook("动画", "赛璐璐高饱和", 1.0f,
                new Vector4(1.0f, 0.995f, 1.0f, 0.008f),
                new Vector4(1.0f, 1.0f, 1.0f, -0.02f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.005f),
                Six(0.0f, -3.0f, 0.0f, 4.0f, 8.0f, 6.0f),
                Six(0.08f, 0.16f, 0.1f, 0.12f, 0.14f, 0.14f),
                Six(0.0f, 0.0f, 0.0f, 0.02f, 0.02f, 0.02f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 怀旧动画胶片 — 90 年代赛璐璐 + 印片：抬黑、温和对比、相对数字动画略去饱和
            new ColorGradingLook("动画", "怀旧动画胶片", 1.0f,
                new Vector4(1.0f, 0.98f, 0.97f, 0.025f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.04f),
                new Vector4(1.0f, 0.99f, 0.98f, -0.035f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(-0.03f, -0.06f, -0.15f, -0.08f, -0.03f, 0.04f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 韦斯粉彩 — Wes Anderson：粉彩偏低彩度（来源只给「去饱和」，抬黑为本表插值）
            new ColorGradingLook("风格", "韦斯粉彩", 1.0f,
                new Vector4(1.0f, 0.97f, 0.99f, 0.025f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.05f),
                new Vector4(0.99f, 1.0f, 1.0f, -0.03f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.08f, 0.04f, 0.04f, 0.04f, -0.04f, 0.08f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 艾米丽绿金 — Amélie：金绿 + 大量红、蓝色被极度去饱和、绿偏黄绿
            new ColorGradingLook("风格", "艾米丽绿金", 1.0f,
                new Vector4(0.93f, 0.98f, 0.93f, 0.005f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.01f),
                new Vector4(1.05f, 1.0f, 0.92f, 0.0f),
                Six(0.0f, 0.0f, -8.0f, 0.0f, 0.0f, 0.0f),
                Six(0.12f, 0.08f, 0.08f, -0.12f, -0.3f, -0.04f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 浪漫暖阳 — Golden Hour（魔幻时刻）
            new ColorGradingLook("风格", "浪漫暖阳", 1.0f,
                new Vector4(1.0f, 0.97f, 0.9f, 0.0f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.05f),
                new Vector4(1.0f, 0.97f, 0.9f, -0.045f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.02f, 0.04f, -0.1f, -0.04f, -0.04f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 寒冬冷冽 — Winter / 冷调雪景
            new ColorGradingLook("风格", "寒冬冷冽", 1.0f,
                new Vector4(0.95f, 0.98f, 1.01f, 0.015f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.045f),
                new Vector4(0.99f, 1.0f, 1.02f, -0.025f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(-0.06f, -0.12f, -0.25f, -0.12f, 0.02f, -0.18f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
            // 肤色保护 — Skin Tone：橙去饱和 8–12% 并提亮（Qualifier 保护），清掉绿/洋红污染
            new ColorGradingLook("风格", "肤色保护", 1.0f,
                new Vector4(1.0f, 0.995f, 0.99f, 0.0f),
                new Vector4(1.0f, 1.0f, 1.0f, 0.0f),
                new Vector4(1.0f, 0.995f, 0.99f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(-0.06f, -0.1f, -0.15f, -0.08f, -0.08f, -0.18f),
                Six(0.005f, 0.01f, 0.0f, 0.0f, 0.0f, 0.0f),
                Six(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)),
        };

        /// <summary>
        /// Adds the look table to the layer preset menu, grouped into submenus by <see cref="ColorGradingLook.Group"/>.
        /// </summary>
        private void AddImageProcessColorGradingLookMenuItems(GenericMenu menu, string propertyPath, ImageProcessEffect effect)
        {
            for (int i = 0; i < ColorGradingLooks.Length; i++)
            {
                ColorGradingLook look = ColorGradingLooks[i];
                string path = look.Group + "/" + look.Label;
                menu.AddItem(new GUIContent(path), false, () => ApplyImageProcessColorGradingLook(propertyPath, effect, look));
            }
        }

        private void ApplyImageProcessColorGradingLook(string propertyPath, ImageProcessEffect effect, ColorGradingLook look)
        {
            ApplyImageProcessPreset(propertyPath, effect, (element, targetEffect) => ApplyImageProcessColorGradingLookPreset(element, targetEffect, look));
        }

        private static void ApplyImageProcessColorGradingLookPreset(SerializedProperty element, ImageProcessEffect effect, ColorGradingLook look)
        {
            // Resets the layer and writes the ColorGradingCustom defaults first, then the look.
            ApplyImageProcessDefaultPreset(element, effect);
            SetFloat(element, "intensity", look.Intensity);
            SetVector4(element, "parameters0", look.Lift);
            SetVector4(element, "parameters1", look.Gamma);
            SetVector4(element, "parameters2", look.Gain);

            PackColorGradingSixColor(
                look.Hue,
                look.Saturation,
                look.Value,
                look.LuminanceSaturation,
                out Vector4 parameters7,
                out Vector4 parameters8,
                out Vector4 parameters9,
                out Vector4 parameters10,
                out Vector4 parameters11,
                out Vector4 parameters12);

            SetVector4(element, "parameters7", parameters7);
            SetVector4(element, "parameters8", parameters8);
            SetVector4(element, "parameters9", parameters9);
            SetVector4(element, "parameters10", parameters10);
            SetVector4(element, "parameters11", parameters11);
            SetVector4(element, "parameters12", parameters12);
        }

        /// <summary>
        /// Packs the four six-colour modes into <c>parameters7</c>-<c>parameters12</c>.
        /// The shader reads them through <c>GetSixColorValue(mode, index)</c> with
        /// <c>valueIndex = mode * 6 + index</c>, i.e. indices 0..23 in order, so mode 0
        /// (hue) occupies parameters7.x .. parameters8.y, mode 1 (saturation)
        /// parameters8.z .. parameters9.w, mode 2 (value) parameters10.x .. parameters11.y
        /// and mode 3 (luminance/saturation) parameters11.z .. parameters12.w.
        /// </summary>
        private static void PackColorGradingSixColor(
            ColorGradingSixColor hue,
            ColorGradingSixColor saturation,
            ColorGradingSixColor value,
            ColorGradingSixColor luminanceSaturation,
            out Vector4 parameters7,
            out Vector4 parameters8,
            out Vector4 parameters9,
            out Vector4 parameters10,
            out Vector4 parameters11,
            out Vector4 parameters12)
        {
            parameters7 = new Vector4(hue.Red, hue.Yellow, hue.Green, hue.Cyan);
            parameters8 = new Vector4(hue.Blue, hue.Magenta, saturation.Red, saturation.Yellow);
            parameters9 = new Vector4(saturation.Green, saturation.Cyan, saturation.Blue, saturation.Magenta);
            parameters10 = new Vector4(value.Red, value.Yellow, value.Green, value.Cyan);
            parameters11 = new Vector4(value.Blue, value.Magenta, luminanceSaturation.Red, luminanceSaturation.Yellow);
            parameters12 = new Vector4(luminanceSaturation.Green, luminanceSaturation.Cyan, luminanceSaturation.Blue, luminanceSaturation.Magenta);
        }
    }
}
