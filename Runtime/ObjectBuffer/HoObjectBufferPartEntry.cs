using System;
using UnityEngine;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    /// <summary>
    /// 部件条目：<b>只回答身份问题</b>——"这是谁、属于哪个角色、能不能被单独选中、debug 长什么样"。
    /// 名字必须**在所属角色内唯一**（它决定 palette 的槽位号，也就是像素里的 ID）。
    /// <para>
    /// **这里刻意不放材质数值**（thickness / curvature / roughness / metallic / reflectance /
    /// PLR strength / materialClass / transmittance）：这些是**材质自己填过一遍**的东西，
    /// 组件再存一份就会出现两个来源。它们的权威归属与写入路径见规划 §5.3 的"材质数值从哪里来"，
    /// 在定下来之前 palette 里那几栏恒为 0，消费端不得依赖。
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class HoObjectBufferPartEntry
    {
        [InspectorName("名字")]
        [Tooltip("角色内唯一。它决定槽位号 = 像素里 ID 的低字节；改名会改变 ID（跨帧稳定性由组件保证）。")]
        public string name = "Part";

        [InspectorName("类别")]
        [Tooltip("单值，回答“这是什么”（脸 / 前发 / 眼睛 …）。多归属语义（如整角色）请用标签位。")]
        public HoObjectBufferPartCategory category = HoObjectBufferPartCategory.Unspecified;

        [InspectorName("标签")]
        [Tooltip("位掩码。像 CharacterFull 这种“一个部件同时属于多个语义”的情况用标签最自然，消费端一次 & 即可查询。")]
        public HoObjectBufferPartTags tags = HoObjectBufferPartTags.None;

        [InspectorName("显示色")]
        [Tooltip("debug 与 Nuke color picker 用的颜色；像素里不存颜色，只存 ID。")]
        public Color displayColor = new Color(0.75f, 0.75f, 0.75f, 1f);

        [InspectorName("展开子级")]
        [Tooltip("拖入 GameObject 或预制件实例时，包含它下面的子级 Renderer；关闭时只使用物体自身的 Renderer。")]
        public bool includeChildren = true;

        [InspectorName("朝向覆盖")]
        [Tooltip("留空 = 用组上的「朝向参考系」。只有**会相对身体转动**的部件才需要填（头 / 脸 / 前发…）：" +
                 "头转到侧面而身体没动时，组那一份朝向对脸就不准了。三个轴向沿用组的配置，这里只换参考骨骼。" +
                 "它只影响朝向查询，不影响身份与覆盖率。")]
        public Transform faceBone;

        [InspectorName("渲染器")]
        [Tooltip("这个部件包含哪些 Renderer（拖 GameObject 或 Renderer 进来）。")]
        public UnityEngine.Object[] renderers;
    }

    /// <summary>
    /// 选择条目（规划 §5.11）：一个**具名的选区**，用来取代 `custom0~3` 这类匿名通道。
    /// <list type="bullet">
    /// <item>部件回答"这是谁"；选择回答"我想把哪一块单独拿出来调"——一块可以横跨多个部件，也可以是同一个部件里的一段遮罩；</item>
    /// <item>名字**全局唯一**，ID 由注册表按顺序分配（独立的 8 bit 空间）；材质侧只引用名字、不定义名字，所以改名不破资产。</item>
    /// </list>
    /// </summary>
    [Serializable]
    public sealed class HoObjectBufferSelectionEntry
    {
        [InspectorName("名字")]
        [Tooltip("全局唯一。材质里引用的是这个名字（例如“左袖口”）。")]
        public string name = "Selection";

        [InspectorName("标签")]
        public HoObjectBufferPartTags tags = HoObjectBufferPartTags.None;

        [InspectorName("显示色")]
        [Tooltip("debug 与 AOV manifest 用的颜色。")]
        public Color displayColor = new Color(0.2f, 0.6f, 1f, 1f);
    }
}
