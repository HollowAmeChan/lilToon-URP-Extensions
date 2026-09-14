using UnityEngine;

namespace lilToon.URP.Extensions.CharacterBuffer
{
    /// <summary>
    /// ID pass 的自建 MSAA 采样数。**与相机的 MSAA 设置解耦**：覆盖率是这个 feature 的产品功能，
    /// 相机把 AA 关掉时它也必须照常产出（见规划 §5.4 / 决策 7）。
    /// </summary>
    public enum HoCharacterBufferSampleCount
    {
        [InspectorName("2x")]
        Two = 2,
        [InspectorName("4x")]
        Four = 4
    }

    /// <summary>
    /// 每像素可容纳的选择层数。一张 RGBA8 按 Cryptomatte 的成对布局装 2 个选择
    /// （R=ID0, G=覆盖率0, B=ID1, A=覆盖率1），所以 4 个选择 = 两张图（见规划 §5.11）。
    /// </summary>
    public enum HoCharacterBufferSelectionLayers
    {
        [InspectorName("Off")]
        Off = 0,
        [InspectorName("2")]
        Two = 2,
        [InspectorName("4")]
        Four = 4
    }

    /// <summary>
    /// 选择层的遮罩来源。**这是跨仓协议里被冻结的枚举**：加来源只能加枚举值，
    /// 不允许改动通道布局或选择 ID 空间（规划 §5.11）。
    /// </summary>
    public enum HoCharacterBufferMaskSource
    {
        [InspectorName("None")]
        None = 0,
        [InspectorName("Texture R")]
        TextureR,
        [InspectorName("Texture G")]
        TextureG,
        [InspectorName("Texture B")]
        TextureB,
        [InspectorName("Texture A")]
        TextureA,
        [InspectorName("Vertex Color R")]
        VertexColorR,
        [InspectorName("Vertex Color G")]
        VertexColorG,
        [InspectorName("Vertex Color B")]
        VertexColorB,
        [InspectorName("Vertex Color A")]
        VertexColorA,
        [InspectorName("Custom Map 1 R")]
        CustomMap1R,
        [InspectorName("Custom Map 1 G")]
        CustomMap1G,
        [InspectorName("Custom Map 1 B")]
        CustomMap1B,
        [InspectorName("Custom Map 1 A")]
        CustomMap1A,
        [InspectorName("Constant")]
        Constant
    }

    /// <summary>
    /// 部件类别。取代旧设计里"8 个语义各占 1 bit"的写法：类别是单值（这是什么），
    /// 多归属语义用标签位表达（规划 §5.3 / 决策 9）。
    /// </summary>
    public enum HoCharacterBufferPartCategory
    {
        [InspectorName("Unspecified")]
        Unspecified = 0,
        [InspectorName("Body")]
        Body,
        [InspectorName("Face")]
        Face,
        [InspectorName("Front Hair")]
        FrontHair,
        [InspectorName("Back Hair")]
        BackHair,
        [InspectorName("Eye")]
        Eye,
        [InspectorName("Eye Reveal Area")]
        EyeRevealArea,
        [InspectorName("Eyebrow")]
        Eyebrow,
        [InspectorName("Accessory")]
        Accessory,
        [InspectorName("Cloth")]
        Cloth,
        [InspectorName("Effect")]
        Effect,
        [InspectorName("Other")]
        Other
    }

    /// <summary>
    /// 部件标签位。多归属语义（例如"该角色的任意部件"）走这里，消费端一次 `&` 即可查询。
    /// </summary>
    [System.Flags]
    public enum HoCharacterBufferPartTags
    {
        None = 0,
        [InspectorName("Character Full")]
        CharacterFull = 1 << 0,
        [InspectorName("Skin")]
        Skin = 1 << 1,
        [InspectorName("Opacity Tested")]
        OpacityTested = 1 << 2,
        [InspectorName("Transparent")]
        Transparent = 1 << 3
    }

    public enum HoCharacterBufferDebugMode
    {
        [InspectorName("Off")]
        Off = 0,
        [InspectorName("ID (Layer 0)")]
        Id0,
        [InspectorName("ID (Layer 1)")]
        Id1,
        [InspectorName("ID (Layer 2)")]
        Id2,
        [InspectorName("ID (Layer 3)")]
        Id3,
        [InspectorName("Coverage (Total)")]
        CoverageTotal,
        [InspectorName("Coverage (Layers)")]
        CoverageLayers,
        [InspectorName("Selection")]
        Selection,
        [InspectorName("Palette Row (Layer 0)")]
        PaletteRow,
        [InspectorName("Surface")]
        Surface,
        [InspectorName("Material 0")]
        Material0,
        [InspectorName("Valid")]
        Valid
    }
}
