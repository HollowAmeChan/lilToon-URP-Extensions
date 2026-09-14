using System;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        /// <summary>
        /// One 「渐变映射」 look for <see cref="ImageProcessEffect.GradientMap"/>.
        /// </summary>
        /// <remarks>
        /// The numbers follow <c>GradientMap.shader</c> and <c>ImageProcessGradientRampBaker</c>:
        /// <list type="bullet">
        /// <item><description><c>Input</c> indexes the ramp driver: 0 luma (Rec.709), 1 red, 2 green, 3 blue, 4 maximum, 5 average, 6 saturation.</description></item>
        /// <item><description><c>Window</c> is the input black/white point: black maps to the ramp start, white to its end.</description></item>
        /// <item><description><c>InterpolationSpace</c>: 0 display, 1 linear light, 2 Oklab - the space the ramp is baked in.</description></item>
        /// <item><description><c>Bands</c> 0/1 = off, N >= 2 posterises the ramp index into N levels.</description></item>
        /// <item><description><c>Reverse</c> flips the ramp direction; <c>Dither</c> is measured in 8-bit LSB.</description></item>
        /// <item><description><c>Ramp</c> is a Unity Gradient: up to 8 colour keys + 8 alpha keys, Blend or Fixed.</description></item>
        /// </list>
        /// This table is generated from .codex-research/gradient_map_sim/looks.js; edit the table
        /// there, run <c>node gen_csharp.js</c> and <c>node verify_csharp.js</c>.
        /// </remarks>
        private readonly struct GradientMapLook
        {
            public readonly string Group;
            public readonly string Label;
            public readonly float Intensity;
            public readonly float Input;
            public readonly Vector2 Window;
            public readonly float InterpolationSpace;
            public readonly float Bands;
            public readonly float Reverse;
            public readonly float Dither;
            public readonly ImageProcessBlendMode BlendMode;
            public readonly Gradient Ramp;

            public GradientMapLook(
                string group,
                string label,
                float intensity,
                float input,
                Vector2 window,
                float interpolationSpace,
                float bands,
                float reverse,
                float dither,
                ImageProcessBlendMode blendMode,
                Gradient ramp)
            {
                Group = group;
                Label = label;
                Intensity = intensity;
                Input = input;
                Window = window;
                InterpolationSpace = interpolationSpace;
                Bands = bands;
                Reverse = reverse;
                Dither = dither;
                BlendMode = blendMode;
                Ramp = ramp;
            }
        }

        /// <summary>Builds a look ramp from compact key arrays.</summary>
        private static Gradient MakeGradientMapRamp(GradientMode mode, GradientColorKey[] colorKeys, GradientAlphaKey[] alphaKeys)
        {
            var gradient = new Gradient { mode = mode };
            gradient.SetKeys(colorKeys, alphaKeys);
            return gradient;
        }

        private static readonly GradientMapLook[] GradientMapLooks =
        {
            // 【双色调】棕褐：经典棕褐色调：暗部偏暖褐、亮部奶油色。Oklab 插值让中间调不发灰。
            // 色标 #1c1108 #5a3a1e #b08a5c #f2e3c8
            new GradientMapLook(
                "双色调",
                "棕褐",
                1.0f,
                0.0f,
                new Vector2(0.0f, 1.0f),
                2.0f,
                0.0f,
                0.0f,
                0.5f,
                ImageProcessBlendMode.Normal,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.10980392f, 0.06666667f, 0.03137255f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.3529412f, 0.22745098f, 0.11764706f, 1.0f), 0.25f),
                        new GradientColorKey(new Color(0.6901961f, 0.5411765f, 0.36078432f, 1.0f), 0.75f),
                        new GradientColorKey(new Color(0.9490196f, 0.8901961f, 0.78431374f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
            // 【双色调】冷夜：夜戏感：整体压暗并偏蓝，亮部保留一点冷青色。
            // 色标 #05070f #101b33 #3a5c86 #c9dcef
            new GradientMapLook(
                "双色调",
                "冷夜",
                1.0f,
                0.0f,
                new Vector2(0.0f, 1.0f),
                2.0f,
                0.0f,
                0.0f,
                0.5f,
                ImageProcessBlendMode.Normal,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.019607844f, 0.02745098f, 0.05882353f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.0627451f, 0.105882354f, 0.2f, 1.0f), 0.3f),
                        new GradientColorKey(new Color(0.22745098f, 0.36078432f, 0.5254902f, 1.0f), 0.72f),
                        new GradientColorKey(new Color(0.7882353f, 0.8627451f, 0.9372549f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
            // 【双色调】青橙：青橙双色调：暗部青、亮部橙，是商业片最常见的冷暖对撞。
            // 色标 #08262b #186470 #c9814a #f7d9a8
            new GradientMapLook(
                "双色调",
                "青橙",
                1.0f,
                0.0f,
                new Vector2(0.0f, 1.0f),
                2.0f,
                0.0f,
                0.0f,
                0.5f,
                ImageProcessBlendMode.Normal,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.03137255f, 0.14901961f, 0.16862746f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.09411765f, 0.39215687f, 0.4392157f, 1.0f), 0.28f),
                        new GradientColorKey(new Color(0.7882353f, 0.5058824f, 0.2901961f, 1.0f), 0.7f),
                        new GradientColorKey(new Color(0.96862745f, 0.8509804f, 0.65882355f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
            // 【双色调】铜色：matplotlib copper 控制点的线性插值：黑→铜褐→浅铜。比棕褐更偏红铜的复古双色调。（色表：copper，matplotlib
            // （BSD 风格））
            // 色标 #000000 #2a1b11 #543522 #7d4f32 #a76943 #d18454 #fa9e64 #ffc77f
            new GradientMapLook(
                "双色调",
                "铜色",
                1.0f,
                0.0f,
                new Vector2(0.0f, 1.0f),
                0.0f,
                0.0f,
                0.0f,
                0.5f,
                ImageProcessBlendMode.Normal,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.0f, 0.0f, 0.0f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.16470584f, 0.10416f, 0.06633333f, 1.0f), 0.13333334f),
                        new GradientColorKey(new Color(0.3294117f, 0.20832f, 0.13266666f, 1.0f), 0.26666668f),
                        new GradientColorKey(new Color(0.48927325f, 0.30941647f, 0.19704902f, 1.0f), 0.39607844f),
                        new GradientColorKey(new Color(0.65397906f, 0.41357648f, 0.26338235f, 1.0f), 0.5294118f),
                        new GradientColorKey(new Color(0.81868494f, 0.5177365f, 0.3297157f, 1.0f), 0.6627451f),
                        new GradientColorKey(new Color(0.9785465f, 0.61883295f, 0.39409804f, 1.0f), 0.7921569f),
                        new GradientColorKey(new Color(1.0f, 0.7812f, 0.4975f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
            // 【胶片】高对比黑白：印片曲线式的黑白：暗部压死、亮部抬肩，对比比线性黑白强得多。
            // 色标 #050505 #262626 #e0e0e0 #ffffff
            new GradientMapLook(
                "胶片",
                "高对比黑白",
                1.0f,
                0.0f,
                new Vector2(0.0f, 1.0f),
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                ImageProcessBlendMode.Normal,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.019607844f, 0.019607844f, 0.019607844f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.14901961f, 0.14901961f, 0.14901961f, 1.0f), 0.22f),
                        new GradientColorKey(new Color(0.8784314f, 0.8784314f, 0.8784314f, 1.0f), 0.62f),
                        new GradientColorKey(new Color(1.0f, 1.0f, 1.0f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
            // 【胶片】褪色：褪色胶片：黑位抬到深灰、白位压到米白，中间调偏低。
            // 色标 #1a1712 #4b453c #c9c0b0 #ede7dc
            new GradientMapLook(
                "胶片",
                "褪色",
                1.0f,
                0.0f,
                new Vector2(0.0f, 1.0f),
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                ImageProcessBlendMode.Normal,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.101960786f, 0.09019608f, 0.07058824f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.29411766f, 0.27058825f, 0.23529412f, 1.0f), 0.3f),
                        new GradientColorKey(new Color(0.7882353f, 0.7529412f, 0.6901961f, 1.0f), 0.68f),
                        new GradientColorKey(new Color(0.92941177f, 0.90588236f, 0.8627451f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
            // 【胶片】漂白旁路：只走亮度：色标本身是灰阶，配合「明度」混合模式只改影调不改色彩，得到漂白旁路式的硬对比。
            // 色标 #000000 #4a4a4a #d6d6d6 #ffffff
            new GradientMapLook(
                "胶片",
                "漂白旁路",
                1.0f,
                0.0f,
                new Vector2(0.0f, 1.0f),
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                ImageProcessBlendMode.Luminosity,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.0f, 0.0f, 0.0f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.2901961f, 0.2901961f, 0.2901961f, 1.0f), 0.3f),
                        new GradientColorKey(new Color(0.8392157f, 0.8392157f, 0.8392157f, 1.0f), 0.7f),
                        new GradientColorKey(new Color(1.0f, 1.0f, 1.0f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
            // 【胶片】负片：反转的黑白：亮部走暗端、暗部走亮端，用来做负片风格的图形化处理。
            // 色标 #000000 #333333 #666666 #999999
            new GradientMapLook(
                "胶片",
                "负片",
                1.0f,
                0.0f,
                new Vector2(0.0f, 1.0f),
                0.0f,
                0.0f,
                1.0f,
                0.0f,
                ImageProcessBlendMode.Normal,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.0f, 0.0f, 0.0f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.2f, 0.2f, 0.2f, 1.0f), 0.33f),
                        new GradientColorKey(new Color(0.4f, 0.4f, 0.4f, 1.0f), 0.66f),
                        new GradientColorKey(new Color(0.6f, 0.6f, 0.6f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
            // 【风格】日落：紫罗兰暗部到橙金亮部的宽色标，Oklab 插值避免中间调变浑。
            // 色标 #241033 #7a2e63 #e0642f #ffe7a3
            new GradientMapLook(
                "风格",
                "日落",
                1.0f,
                0.0f,
                new Vector2(0.0f, 1.0f),
                2.0f,
                0.0f,
                0.0f,
                0.5f,
                ImageProcessBlendMode.Normal,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.14117648f, 0.0627451f, 0.2f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.47843137f, 0.18039216f, 0.3882353f, 1.0f), 0.32f),
                        new GradientColorKey(new Color(0.8784314f, 0.39215687f, 0.18431373f, 1.0f), 0.68f),
                        new GradientColorKey(new Color(1.0f, 0.90588236f, 0.6392157f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
            // 【风格】赛博霓虹：洋红与青的霓虹对撞，暗部近黑紫。
            // 色标 #06010f #3b0a6b #ff2e88 #21e6e6
            new GradientMapLook(
                "风格",
                "赛博霓虹",
                1.0f,
                0.0f,
                new Vector2(0.0f, 1.0f),
                2.0f,
                0.0f,
                0.0f,
                0.5f,
                ImageProcessBlendMode.Normal,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.023529412f, 0.003921569f, 0.05882353f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.23137255f, 0.039215688f, 0.41960785f, 1.0f), 0.3f),
                        new GradientColorKey(new Color(1.0f, 0.18039216f, 0.53333336f, 1.0f), 0.72f),
                        new GradientColorKey(new Color(0.12941177f, 0.9019608f, 0.9019608f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
            // 【风格】海报平涂：色阶数 6：输入被量化成 6 级再查表，得到平涂色块，配合青橙双色调使用。
            // 色标 #08262b #186470 #c9814a #f7d9a8
            new GradientMapLook(
                "风格",
                "海报平涂",
                1.0f,
                0.0f,
                new Vector2(0.0f, 1.0f),
                2.0f,
                6.0f,
                0.0f,
                0.0f,
                ImageProcessBlendMode.Normal,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.03137255f, 0.14901961f, 0.16862746f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.09411765f, 0.39215687f, 0.4392157f, 1.0f), 0.28f),
                        new GradientColorKey(new Color(0.7882353f, 0.5058824f, 0.2901961f, 1.0f), 0.7f),
                        new GradientColorKey(new Color(0.96862745f, 0.8509804f, 0.65882355f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
            // 【风格】红外假色：以红通道索引色标（输入 = 红）：红多的区域走亮端，做出假色/红外片的观感。这是风格化近似，不是红外胶片的色彩科学。
            // 色标 #101a12 #3f5a2a #c8443c #f6d8c4
            new GradientMapLook(
                "风格",
                "红外假色",
                1.0f,
                1.0f,
                new Vector2(0.0f, 1.0f),
                2.0f,
                0.0f,
                0.0f,
                0.5f,
                ImageProcessBlendMode.Normal,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.0627451f, 0.101960786f, 0.07058824f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.24705882f, 0.3529412f, 0.16470589f, 1.0f), 0.35f),
                        new GradientColorKey(new Color(0.78431374f, 0.26666668f, 0.23529412f, 1.0f), 0.7f),
                        new GradientColorKey(new Color(0.9647059f, 0.84705883f, 0.76862746f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
            // 【数据可视化】红外热图：热像仪的 ironbow 色表：黑→紫→橙→近白。用于夜视/热成像风格，也可以当高对比的暖冷对撞用。（色表：ironbow，MIT（
            // MickTheMechanic/FLIR-style-thermal-color-palettes））
            // 色标 #00000a #19007e #8d009d #d82764 #ef5f04 #fed308 #fff17b #fffff6
            new GradientMapLook(
                "数据可视化",
                "红外热图",
                1.0f,
                0.0f,
                new Vector2(0.0f, 1.0f),
                0.0f,
                0.0f,
                0.0f,
                0.5f,
                ImageProcessBlendMode.Normal,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.0f, 0.0f, 0.039215688f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.09803922f, 0.0f, 0.49411765f, 1.0f), 0.08796296f),
                        new GradientColorKey(new Color(0.5529412f, 0.0f, 0.6156863f, 1.0f), 0.24537037f),
                        new GradientColorKey(new Color(0.84705883f, 0.15294118f, 0.39215687f, 1.0f), 0.43287036f),
                        new GradientColorKey(new Color(0.9372549f, 0.37254903f, 0.015686274f, 1.0f), 0.5601852f),
                        new GradientColorKey(new Color(0.99607843f, 0.827451f, 0.031372547f, 1.0f), 0.8217593f),
                        new GradientColorKey(new Color(1.0f, 0.94509804f, 0.48235294f, 1.0f), 0.9212963f),
                        new GradientColorKey(new Color(1.0f, 1.0f, 0.9647059f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
            // 【数据可视化】光谱：Google turbo 伪彩：深蓝紫→青→黄→暗红。把亮度做成扫描/仪器感的彩色层次，适合科幻与科技界面。（色表：turbo，Apac
            // he-2.0（Copyright 2019 Google LLC，作者 Anton Mikhailov））
            // 色标 #30123b #424bb5 #37a8fa #2ff19b #92ff47 #f8be39 #e5470b #7a0403
            new GradientMapLook(
                "数据可视化",
                "光谱",
                1.0f,
                0.0f,
                new Vector2(0.0f, 1.0f),
                0.0f,
                0.0f,
                0.0f,
                0.5f,
                ImageProcessBlendMode.Normal,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.1882353f, 0.07058824f, 0.23137255f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.25882354f, 0.29411766f, 0.70980394f, 1.0f), 0.078431375f),
                        new GradientColorKey(new Color(0.21568628f, 0.65882355f, 0.98039216f, 1.0f), 0.21960784f),
                        new GradientColorKey(new Color(0.18431373f, 0.94509804f, 0.60784316f, 1.0f), 0.37254903f),
                        new GradientColorKey(new Color(0.57254905f, 1.0f, 0.2784314f, 1.0f), 0.47843137f),
                        new GradientColorKey(new Color(0.972549f, 0.74509805f, 0.22352941f, 1.0f), 0.65882355f),
                        new GradientColorKey(new Color(0.8980392f, 0.2784314f, 0.043137256f, 1.0f), 0.83137256f),
                        new GradientColorKey(new Color(0.47843137f, 0.015686274f, 0.011764706f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
            // 【数据可视化】岩浆：matplotlib inferno：黑→紫红→橙→淡黄，感知均匀的暖色伪彩，暗部层次比普通冷暖渐变清楚。（色表：inferno，mat
            // plotlib（BSD 风格））
            // 色标 #000004 #260c51 #6f196e #ba3655 #ed6925 #fcae12 #f4e156 #fcffa4
            new GradientMapLook(
                "数据可视化",
                "岩浆",
                1.0f,
                0.0f,
                new Vector2(0.0f, 1.0f),
                0.0f,
                0.0f,
                0.0f,
                0.5f,
                ImageProcessBlendMode.Normal,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.001462f, 0.000466f, 0.013866f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.149073f, 0.045468f, 0.317085f, 1.0f), 0.13725491f),
                        new GradientColorKey(new Color(0.434987f, 0.097069f, 0.432039f, 1.0f), 0.30980393f),
                        new GradientColorKey(new Color(0.729909f, 0.212759f, 0.333861f, 1.0f), 0.49803922f),
                        new GradientColorKey(new Color(0.929644f, 0.411479f, 0.145367f, 1.0f), 0.6666667f),
                        new GradientColorKey(new Color(0.987714f, 0.682807f, 0.072489f, 1.0f), 0.81960785f),
                        new GradientColorKey(new Color(0.954997f, 0.881569f, 0.337475f, 1.0f), 0.92156863f),
                        new GradientColorKey(new Color(0.988362f, 0.998364f, 0.644924f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
            // 【数据可视化】叶绿：matplotlib viridis：深紫→蓝绿→绿→黄，感知均匀且明度单调，暗部不会糊成一团。（色表：viridis，matplotl
            // ib（BSD 风格））
            // 色标 #440154 #46337f #2e6d8e #23a983 #56c667 #8ed645 #c2df23 #fde725
            new GradientMapLook(
                "数据可视化",
                "叶绿",
                1.0f,
                0.0f,
                new Vector2(0.0f, 1.0f),
                0.0f,
                0.0f,
                0.0f,
                0.5f,
                ImageProcessBlendMode.Normal,
                MakeGradientMapRamp(
                    GradientMode.Blend,
                    new[]
                    {
                        new GradientColorKey(new Color(0.267004f, 0.004874f, 0.329415f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.274128f, 0.199721f, 0.498911f, 1.0f), 0.14509805f),
                        new GradientColorKey(new Color(0.182256f, 0.426184f, 0.55712f, 1.0f), 0.3529412f),
                        new GradientColorKey(new Color(0.137339f, 0.662252f, 0.515571f, 1.0f), 0.6039216f),
                        new GradientColorKey(new Color(0.335885f, 0.777018f, 0.402049f, 1.0f), 0.7372549f),
                        new GradientColorKey(new Color(0.555484f, 0.840254f, 0.269281f, 1.0f), 0.83137256f),
                        new GradientColorKey(new Color(0.762373f, 0.876424f, 0.137064f, 1.0f), 0.9098039f),
                        new GradientColorKey(new Color(0.993248f, 0.906157f, 0.143936f, 1.0f), 1.0f)
                    },
                    new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) })),
        };

        /// <summary>
        /// Adds the look table to the layer preset menu, grouped into submenus by
        /// <see cref="GradientMapLook.Group"/>.
        /// </summary>
        private void AddImageProcessGradientMapLookMenuItems(GenericMenu menu, string propertyPath, ImageProcessEffect effect)
        {
            for (int i = 0; i < GradientMapLooks.Length; i++)
            {
                GradientMapLook look = GradientMapLooks[i];
                string path = look.Group + "/" + look.Label;
                menu.AddItem(new GUIContent(path), false, () => ApplyImageProcessGradientMapLook(propertyPath, effect, look));
            }
        }

        private void ApplyImageProcessGradientMapLook(string propertyPath, ImageProcessEffect effect, GradientMapLook look)
        {
            ApplyImageProcessPreset(propertyPath, effect, (element, targetEffect) => ApplyImageProcessGradientMapLookPreset(element, targetEffect, look));
        }

        private static void ApplyImageProcessGradientMapLookPreset(SerializedProperty element, ImageProcessEffect effect, GradientMapLook look)
        {
            // Resets the layer and writes the GradientMap defaults first, then the look.
            ApplyImageProcessDefaultPreset(element, effect);
            SetFloat(element, "intensity", look.Intensity);
            SetEnum(element, "blendMode", (int)look.BlendMode);
            SetVector4(element, "parameters0", new Vector4(look.Input, look.Window.x, look.Window.y, look.Dither));
            SetVector4(element, "parameters6", new Vector4(look.InterpolationSpace, look.Reverse, look.Bands, 0.0f));
            SetGradientMapRamp(element, look.Ramp);
        }
    }
}
