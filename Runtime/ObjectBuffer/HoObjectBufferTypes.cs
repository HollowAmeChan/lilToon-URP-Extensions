using UnityEngine;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    /// <summary>
    /// ID pass 的自建 MSAA 采样数。**与相机的 MSAA 设置解耦**：覆盖率是这个 feature 的产品功能，
    /// 相机把 AA 关掉时它也必须照常产出（见规划 §5.4 / 决策 7）。
    /// </summary>
    public enum HoObjectBufferSampleCount
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
    public enum HoObjectBufferSelectionLayers
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
    public enum HoObjectBufferMaskSource
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
    /// **角色组分**：这个部件是角色的哪一块（单值，互斥）。
    /// <list type="bullet">
    /// <item>它本质上是**"角色的预置选区"**：角色一旦被当作角色来做，需要的语义就是固定那几条，所以预置成一张表，作者只做"归属"；场景那边多变，才用自由创建的**选区**（0.3.12）。</item>
    /// <item>**目前只有角色特化读它**，按「组 + 组分 + 覆盖率」取遮罩；组那一级表达「整角色」，组分表达「脸 / 前发 / 眼睛 / 眼透区 / 配件 / 人体」——这七条用这两级就够了（规划里 SSS 也会读分类/profile）。</item>
    /// <item>**AC 上线也不改这套分类**：AC 只是"怎么读、怎么合成"的通道，不改变"组分是什么"。</item>
    /// <item>这里**不放材质类语义**（皮肤 / 半透明 / 不透明测试…）：那些是表面语义，归 SB。加一条 = 给**角色语义**加一条，不是因为某个 feature 缺一位。</item>
    /// </list>
    /// </summary>
    public enum HoObjectBufferPartCategory
    {
        [InspectorName("未指定")]
        Unspecified = 0,
        [InspectorName("人体")]
        Body,
        [InspectorName("脸")]
        Face,
        [InspectorName("前发")]
        FrontHair,
        [InspectorName("后发")]
        BackHair,
        [InspectorName("眼睛")]
        Eye,
        [InspectorName("眼透区")]
        EyeRevealArea,
        [InspectorName("眉")]
        Eyebrow,
        [InspectorName("配件")]
        Accessory,
        [InspectorName("服装")]
        Cloth,
        [InspectorName("特效")]
        Effect,
        [InspectorName("其他")]
        Other
    }

    /// <summary>
    /// 部件标签位。多归属语义（例如"该组的任意部件"）走这里，消费端一次 `&` 即可查询。
    /// </summary>
    [System.Flags]
    public enum HoObjectBufferPartTags
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

    public enum HoObjectBufferDebugMode
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
        [InspectorName("Valid")]
        Valid
    }
}
