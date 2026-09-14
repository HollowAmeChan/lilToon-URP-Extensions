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
        /// The numbers follow <c>GradientMap.shader</c>:
        /// <list type="bullet">
        /// <item><description><c>Input</c> indexes the ramp driver: 0 luma (Rec.709), 1 red, 2 green, 3 blue, 4 maximum, 5 average, 6 saturation.</description></item>
        /// <item><description><c>Window</c> is the input black/white point: black maps to stop 1, white to stop 4.</description></item>
        /// <item><description><c>InterpolationSpace</c>: 0 display, 1 linear light, 2 Oklab.</description></item>
        /// <item><description><c>Bands</c> 0/1 = off, N >= 2 posterises the ramp index into N levels.</description></item>
        /// <item><description><c>Reverse</c> flips the ramp direction; <c>Dither</c> is measured in 8-bit LSB.</description></item>
        /// <item><description><c>Positions</c> holds the four stop positions, <c>Color1</c>..<c>Color4</c> the four stop colours (rgba), stop 1 = black end.</description></item>
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
            public readonly Vector4 Positions;
            public readonly Vector4 Color1;
            public readonly Vector4 Color2;
            public readonly Vector4 Color3;
            public readonly Vector4 Color4;

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
                Vector4 positions,
                Vector4 color1,
                Vector4 color2,
                Vector4 color3,
                Vector4 color4)
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
                Positions = positions;
                Color1 = color1;
                Color2 = color2;
                Color3 = color3;
                Color4 = color4;
            }
        }

        private static readonly GradientMapLook[] GradientMapLooks =
        {
            // 【双色调】棕褐：经典棕褐色调：暗部偏暖褐、亮部奶油色。Oklab 插值让中间调不发灰。
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
                new Vector4(0.0f, 0.25f, 0.75f, 1.0f),
                new Vector4(0.10980392f, 0.06666667f, 0.03137255f, 1.0f), // #1c1108
                new Vector4(0.3529412f, 0.22745098f, 0.11764706f, 1.0f), // #5a3a1e
                new Vector4(0.6901961f, 0.5411765f, 0.36078432f, 1.0f), // #b08a5c
                new Vector4(0.9490196f, 0.8901961f, 0.78431374f, 1.0f) // #f2e3c8
            ),
            // 【双色调】冷夜：夜戏感：整体压暗并偏蓝，亮部保留一点冷青色。
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
                new Vector4(0.0f, 0.3f, 0.72f, 1.0f),
                new Vector4(0.019607844f, 0.02745098f, 0.05882353f, 1.0f), // #05070f
                new Vector4(0.0627451f, 0.105882354f, 0.2f, 1.0f), // #101b33
                new Vector4(0.22745098f, 0.36078432f, 0.5254902f, 1.0f), // #3a5c86
                new Vector4(0.7882353f, 0.8627451f, 0.9372549f, 1.0f) // #c9dcef
            ),
            // 【双色调】青橙：青橙双色调：暗部青、亮部橙，是商业片最常见的冷暖对撞。
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
                new Vector4(0.0f, 0.28f, 0.7f, 1.0f),
                new Vector4(0.03137255f, 0.14901961f, 0.16862746f, 1.0f), // #08262b
                new Vector4(0.09411765f, 0.39215687f, 0.4392157f, 1.0f), // #186470
                new Vector4(0.7882353f, 0.5058824f, 0.2901961f, 1.0f), // #c9814a
                new Vector4(0.96862745f, 0.8509804f, 0.65882355f, 1.0f) // #f7d9a8
            ),
            // 【双色调】铜色：matplotlib copper 控制点的线性插值：黑→铜褐→浅铜。比棕褐更偏红铜的复古双色调。（色表：copper，matplotlib
            // （BSD 风格））
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
                new Vector4(0.0f, 0.33333334f, 0.6666667f, 1.0f),
                new Vector4(0.0f, 0.0f, 0.0f, 1.0f), // #000000
                new Vector4(0.41176462f, 0.2604f, 0.16583334f, 1.0f), // #69422a
                new Vector4(0.82352924f, 0.5208f, 0.33166668f, 1.0f), // #d28555
                new Vector4(1.0f, 0.7812f, 0.4975f, 1.0f) // #ffc77f
            ),
            // 【胶片】高对比黑白：印片曲线式的黑白：暗部压死、亮部抬肩，对比比线性黑白强得多。
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
                new Vector4(0.0f, 0.22f, 0.62f, 1.0f),
                new Vector4(0.019607844f, 0.019607844f, 0.019607844f, 1.0f), // #050505
                new Vector4(0.14901961f, 0.14901961f, 0.14901961f, 1.0f), // #262626
                new Vector4(0.8784314f, 0.8784314f, 0.8784314f, 1.0f), // #e0e0e0
                new Vector4(1.0f, 1.0f, 1.0f, 1.0f) // #ffffff
            ),
            // 【胶片】褪色：褪色胶片：黑位抬到深灰、白位压到米白，中间调偏低。
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
                new Vector4(0.0f, 0.3f, 0.68f, 1.0f),
                new Vector4(0.101960786f, 0.09019608f, 0.07058824f, 1.0f), // #1a1712
                new Vector4(0.29411766f, 0.27058825f, 0.23529412f, 1.0f), // #4b453c
                new Vector4(0.7882353f, 0.7529412f, 0.6901961f, 1.0f), // #c9c0b0
                new Vector4(0.92941177f, 0.90588236f, 0.8627451f, 1.0f) // #ede7dc
            ),
            // 【胶片】漂白旁路：只走亮度：色标本身是灰阶，配合「明度」混合模式只改影调不改色彩，得到漂白旁路式的硬对比。
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
                new Vector4(0.0f, 0.3f, 0.7f, 1.0f),
                new Vector4(0.0f, 0.0f, 0.0f, 1.0f), // #000000
                new Vector4(0.2901961f, 0.2901961f, 0.2901961f, 1.0f), // #4a4a4a
                new Vector4(0.8392157f, 0.8392157f, 0.8392157f, 1.0f), // #d6d6d6
                new Vector4(1.0f, 1.0f, 1.0f, 1.0f) // #ffffff
            ),
            // 【胶片】负片：反转的黑白：亮部走暗端、暗部走亮端，用来做负片风格的图形化处理。
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
                new Vector4(0.0f, 0.33f, 0.66f, 1.0f),
                new Vector4(0.0f, 0.0f, 0.0f, 1.0f), // #000000
                new Vector4(0.2f, 0.2f, 0.2f, 1.0f), // #333333
                new Vector4(0.4f, 0.4f, 0.4f, 1.0f), // #666666
                new Vector4(0.6f, 0.6f, 0.6f, 1.0f) // #999999
            ),
            // 【风格】日落：紫罗兰暗部到橙金亮部的宽色标，Oklab 插值避免中间调变浑。
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
                new Vector4(0.0f, 0.32f, 0.68f, 1.0f),
                new Vector4(0.14117648f, 0.0627451f, 0.2f, 1.0f), // #241033
                new Vector4(0.47843137f, 0.18039216f, 0.3882353f, 1.0f), // #7a2e63
                new Vector4(0.8784314f, 0.39215687f, 0.18431373f, 1.0f), // #e0642f
                new Vector4(1.0f, 0.90588236f, 0.6392157f, 1.0f) // #ffe7a3
            ),
            // 【风格】赛博霓虹：洋红与青的霓虹对撞，暗部近黑紫。
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
                new Vector4(0.0f, 0.3f, 0.72f, 1.0f),
                new Vector4(0.023529412f, 0.003921569f, 0.05882353f, 1.0f), // #06010f
                new Vector4(0.23137255f, 0.039215688f, 0.41960785f, 1.0f), // #3b0a6b
                new Vector4(1.0f, 0.18039216f, 0.53333336f, 1.0f), // #ff2e88
                new Vector4(0.12941177f, 0.9019608f, 0.9019608f, 1.0f) // #21e6e6
            ),
            // 【风格】海报平涂：色阶数 6：输入被量化成 6 级再查表，得到平涂色块，配合青橙双色调使用。
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
                new Vector4(0.0f, 0.28f, 0.7f, 1.0f),
                new Vector4(0.03137255f, 0.14901961f, 0.16862746f, 1.0f), // #08262b
                new Vector4(0.09411765f, 0.39215687f, 0.4392157f, 1.0f), // #186470
                new Vector4(0.7882353f, 0.5058824f, 0.2901961f, 1.0f), // #c9814a
                new Vector4(0.96862745f, 0.8509804f, 0.65882355f, 1.0f) // #f7d9a8
            ),
            // 【风格】红外假色：以红通道索引色标（输入 = 红）：红多的区域走亮端，做出假色/红外片的观感。这是风格化近似，不是红外胶片的色彩科学。
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
                new Vector4(0.0f, 0.35f, 0.7f, 1.0f),
                new Vector4(0.0627451f, 0.101960786f, 0.07058824f, 1.0f), // #101a12
                new Vector4(0.24705882f, 0.3529412f, 0.16470589f, 1.0f), // #3f5a2a
                new Vector4(0.78431374f, 0.26666668f, 0.23529412f, 1.0f), // #c8443c
                new Vector4(0.9647059f, 0.84705883f, 0.76862746f, 1.0f) // #f6d8c4
            ),
            // 【数据可视化】红外热图：热像仪的 ironbow 色表：黑→紫→橙→近白。用于夜视/热成像风格，也可以当高对比的暖冷对撞用。（色表：ironbow，MIT（
            // MickTheMechanic/FLIR-style-thermal-color-palettes））
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
                new Vector4(0.0f, 0.33333334f, 0.6666667f, 1.0f),
                new Vector4(0.0f, 0.0f, 0.039215688f, 1.0f), // #00000a
                new Vector4(0.73333335f, 0.019607844f, 0.5764706f, 1.0f), // #bb0593
                new Vector4(0.972549f, 0.54901963f, 0.0f, 1.0f), // #f88c00
                new Vector4(1.0f, 1.0f, 0.9647059f, 1.0f) // #fffff6
            ),
            // 【数据可视化】光谱：Google turbo 伪彩：深蓝紫→青→黄→暗红。把亮度做成扫描/仪器感的彩色层次，适合科幻与科技界面。（色表：turbo，Apac
            // he-2.0（Copyright 2019 Google LLC，作者 Anton Mikhailov））
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
                new Vector4(0.0f, 0.33333334f, 0.6666667f, 1.0f),
                new Vector4(0.1882353f, 0.07058824f, 0.23137255f, 1.0f), // #30123b
                new Vector4(0.101960786f, 0.89411765f, 0.7137255f, 1.0f), // #1ae4b6
                new Vector4(0.98039216f, 0.7294118f, 0.22352941f, 1.0f), // #faba39
                new Vector4(0.47843137f, 0.015686275f, 0.011764706f, 1.0f) // #7a0403
            ),
            // 【数据可视化】岩浆：matplotlib inferno：黑→紫红→橙→淡黄，感知均匀的暖色伪彩，暗部层次比普通冷暖渐变清楚。（色表：inferno，mat
            // plotlib（BSD 风格））
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
                new Vector4(0.0f, 0.33333334f, 0.6666667f, 1.0f),
                new Vector4(0.001462f, 0.000466f, 0.013866f, 1.0f), // #000004
                new Vector4(0.472328f, 0.110547f, 0.428334f, 1.0f), // #781c6d
                new Vector4(0.929644f, 0.411479f, 0.145367f, 1.0f), // #ed6925
                new Vector4(0.988362f, 0.998364f, 0.644924f, 1.0f) // #fcffa4
            ),
            // 【数据可视化】叶绿：matplotlib viridis：深紫→蓝绿→绿→黄，感知均匀且明度单调，暗部不会糊成一团。（色表：viridis，matplotl
            // ib（BSD 风格））
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
                new Vector4(0.0f, 0.33333334f, 0.6666667f, 1.0f),
                new Vector4(0.267004f, 0.004874f, 0.329415f, 1.0f), // #440154
                new Vector4(0.190631f, 0.407061f, 0.556089f, 1.0f), // #31688e
                new Vector4(0.20803f, 0.718701f, 0.472873f, 1.0f), // #35b779
                new Vector4(0.993248f, 0.906157f, 0.143936f, 1.0f) // #fde725
            ),
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
            SetVector4(element, "parameters1", look.Positions);
            SetVector4(element, "parameters2", look.Color1);
            SetVector4(element, "parameters3", look.Color2);
            SetVector4(element, "parameters4", look.Color3);
            SetVector4(element, "parameters5", look.Color4);
            SetVector4(element, "parameters6", new Vector4(look.InterpolationSpace, look.Reverse, look.Bands, 0.0f));
        }
    }
}
