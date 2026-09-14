using System;
using UnityEngine;

namespace lilToon.URP.Extensions.CharacterBuffer
{
    /// <summary>
    /// 部件条目：取代旧设计里"给渲染器打勾"的 8 个固定布尔。
    /// 名字必须**在所属角色内唯一**（它决定 palette 的槽位号，也就是像素里的 ID）。
    /// </summary>
    [Serializable]
    public sealed class HoCharacterBufferPartEntry
    {
        [InspectorName("名字")]
        [Tooltip("角色内唯一。它决定槽位号 = 像素里 ID 的低字节；改名会改变 ID（跨帧稳定性由组件保证）。")]
        public string name = "Part";

        [InspectorName("类别")]
        [Tooltip("单值，回答“这是什么”。多归属语义（如整角色）请用标签位。")]
        public HoCharacterBufferPartCategory category = HoCharacterBufferPartCategory.Unspecified;

        [InspectorName("标签")]
        public HoCharacterBufferPartTags tags = HoCharacterBufferPartTags.None;

        [InspectorName("材质分类 (Material Class)")]
        public int materialClass;

        [InspectorName("厚度 (SSS)")]
        public float thickness = 1f;

        [InspectorName("曲率")]
        public float curvature;

        [InspectorName("透射提示")]
        public float transmittance;

        [InspectorName("粗糙度")]
        [Range(0f, 1f)]
        public float roughness = 0.5f;

        [InspectorName("金属度")]
        [Range(0f, 1f)]
        public float metallic;

        [InspectorName("反射率")]
        [Range(0f, 1f)]
        public float reflectance = 0.04f;

        [InspectorName("平面反射强度")]
        public float plrStrength = 1f;

        [InspectorName("显示色")]
        [Tooltip("debug 与 Nuke color picker 用的颜色；像素里不存颜色，只存 ID。")]
        public Color displayColor = new Color(0.75f, 0.75f, 0.75f, 1f);

        [InspectorName("展开子级")]
        public bool includeChildren = true;

        [InspectorName("渲染器")]
        public UnityEngine.Object[] renderers;
    }

    /// <summary>
    /// 选择条目（规划 §5.11）：具名的 `(选择 ID, 覆盖率)` 选区，取代 `custom0~3` 这类匿名通道。
    /// 名字**全局唯一**；ID 由注册表按顺序分配（独立的 8 bit 空间）。
    /// </summary>
    [Serializable]
    public sealed class HoCharacterBufferSelectionEntry
    {
        [InspectorName("名字")]
        public string name = "Selection";

        [InspectorName("标签")]
        public HoCharacterBufferPartTags tags = HoCharacterBufferPartTags.None;

        [InspectorName("显示色")]
        public Color displayColor = new Color(0.2f, 0.6f, 1f, 1f);
    }
}
