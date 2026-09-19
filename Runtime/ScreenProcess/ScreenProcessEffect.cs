using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    public enum ScreenProcessEffect
    {
        CustomMaterial = 0,
        EdgeLight = 1,
        Outline = 2,
        DropShadow = 3,
        DepthOfField = 4,
        PostLighting = 5,
        SkyTyndall = 6,
        DepthFog = 7
    }

    /// <summary>
    /// 图层混合模式。**编号与 ImageProcess 的共享混合表
    /// (`Runtime/ImageProcess/Shaders/ImageProcess/ImageProcessBlend.hlsl`) 逐位一致**，
    /// 面板上的中文标签也与 IP 面板的同名列表逐项对齐（由 sp_blend_sim 的检查钉住）；
    /// 序号即着色器里 <c>_LayerBlendMode</c> 的值。
    /// </summary>
    /// <remarks>
    /// 历史：ScreenProcess 以前只有 4 个模式，且 2/3 的含义与 ImageProcess 相反
    /// （旧 2=滤色、旧 3=正片叠底）。统一到这张表时没有做数据迁移 —— 所以旧资产里
    /// 值为 2/3 的图层，语义会跟着这张表变成 正片叠底/滤色。代码里一律用枚举名，
    /// 不受影响。
    /// </remarks>
    public enum ScreenProcessBlendMode
    {
        [InspectorName("正常")]
        Normal = 0,
        [InspectorName("相加")]
        Add = 1,
        [InspectorName("正片叠底")]
        Multiply = 2,
        [InspectorName("滤色")]
        Screen = 3,
        [InspectorName("变暗")]
        Darken = 4,
        [InspectorName("颜色加深")]
        ColorBurn = 5,
        [InspectorName("线性加深")]
        LinearBurn = 6,
        [InspectorName("变亮")]
        Lighten = 7,
        [InspectorName("颜色减淡")]
        ColorDodge = 8,
        [InspectorName("叠加")]
        Overlay = 9,
        [InspectorName("柔光")]
        SoftLight = 10,
        [InspectorName("强光")]
        HardLight = 11,
        [InspectorName("亮光")]
        VividLight = 12,
        [InspectorName("线性光")]
        LinearLight = 13,
        [InspectorName("点光")]
        PinLight = 14,
        [InspectorName("实色混合")]
        HardMix = 15,
        [InspectorName("差值")]
        Difference = 16,
        [InspectorName("排除")]
        Exclusion = 17,
        [InspectorName("减去")]
        Subtract = 18,
        [InspectorName("划分")]
        Divide = 19,
        [InspectorName("色相")]
        Hue = 20,
        [InspectorName("饱和度")]
        Saturation = 21,
        [InspectorName("颜色")]
        Color = 22,
        [InspectorName("明度")]
        Luminosity = 23
    }
}
