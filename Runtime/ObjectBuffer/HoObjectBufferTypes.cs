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
    /// **角色标签**：这个部件在角色语义上属于哪几类（**位掩码，可多选**）。
    /// <list type="bullet">
    /// <item>它本质上是**"角色的预置标签表"**：角色一旦被当作角色来做，需要的词就是固定那几条，所以预置成一张表，作者只做"打标签"；场景那边多变，才用自由创建的**选区**（规划 0.3.12）。</item>
    /// <item>**多选正是它存在的理由**："整角色 + 脸"这种同时成立的情况，单值枚举表达不了。它与 MetadataBuffer 的 `objectCustomMask` 完全对等；R3/R4 的 schema lane mask 只换机制、不换词表。</item>
    /// <item>**目前只有角色特化读它**，按「组 + 标签 + 覆盖率」取遮罩（规划里 SSS 也会读它）。</item>
    /// <item>**AC 上线也不改这套词表**：AC 只是"怎么读、怎么合成"的通道，不改变"标签是什么"。</item>
    /// <item>这里**不放材质类语义**（皮肤 / 半透明 / 不透明测试…）：那些是表面语义，归 SB。加一位 = 给**角色语义**加一条，不是因为某个 feature 缺一位。</item>
    /// <item>**这就是角色侧唯一的语义扩展点**：以后所有新的角色级开关都往这一张表里加（加一位 + 起个名字），
    /// 不要再新开字段或新开枚举 —— 一张表才好被 schema 接管、也才好被消费端一次遍历。</item>
    /// </list>
    /// </summary>
    [System.Flags]
    public enum HoObjectBufferPartTags
    {
        None = 0,
        /// <summary>整角色：该组的任意部件都算。组匹配也能表达，但显式一位更省事。</summary>
        [InspectorName("全角色")]
        CharacterFull = 1 << 0,
        [InspectorName("脸")]
        Face = 1 << 1,
        [InspectorName("前发")]
        FrontHair = 1 << 2,
        [InspectorName("眼睛")]
        Eye = 1 << 3,
        [InspectorName("眼透区")]
        EyeRevealArea = 1 << 4,
        [InspectorName("配件")]
        Accessory = 1 << 5,
        [InspectorName("人体")]
        Body = 1 << 6,
        /// <summary>预留：不是承诺，可被场景/后续语义拿走用。</summary>
        [InspectorName("预留")]
        Reserved = 1 << 7
    }

    /// <summary>调试视图：与 shader 里的 mode 数值一一对应，加新视图只能往后加（不要插在中间）。</summary>
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
        Valid,
        [InspectorName("Sample Count")]
        SampleCount
    }
}
