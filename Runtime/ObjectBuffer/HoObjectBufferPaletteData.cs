using System.Runtime.InteropServices;
using UnityEngine;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    /// <summary>
    /// palette 的部件行。**CPU 与 HLSL 的布局必须逐字段一致**（见
    /// <c>Runtime/ObjectBuffer/Shaders/HoObjectBufferPalette.hlsl</c>）。
    /// 大小 64 B；4096 行 = 256 KB，全量上传（决策 11）。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HoObjectPartData
    {
        /// <summary>角色 8 bit + 槽位 8 bit，与像素里的 16 bit ID 同构。</summary>
        public uint partId;

        /// <summary>名字 hash（FNV-1a 32），供工具与 AOV manifest 对账用。</summary>
        public uint nameHash;

        /// <summary><see cref="HoObjectBufferPartTags"/> 的位掩码（角色语义，可多选）。</summary>
        public uint tags;

        /// <summary>保留：R3/R4 的 schema lane mask 之类会落到这里，现在恒 0，消费端不得依赖。</summary>
        public uint reserved;

        public float thickness;
        public float curvature;
        public float transmittance;
        public float roughness;
        public float metallic;
        public float reflectance;
        public float plrStrength;
        public uint materialClass;

        /// <summary>显示色（Nuke "color picker ID" 的语义），也是 debug 视图的颜色。</summary>
        public Vector4 displayColor;

        public const int Stride = 64;
    }

    /// <summary>
    /// 组行。两级表的第一级：像素里的 ID 是稀疏的 <c>角色 8 + 槽位 8</c>，
    /// 而部件表是稠密的 ≤4096 行，靠 <c>rowBase + slot</c> 定位（规划 §5.3）。
    /// 大小 16 B；256 行 = 4 KB。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HoObjectGroupData
    {
        /// <summary>该组第一行在部件表里的行号。</summary>
        public uint rowBase;

        /// <summary>该组已注册的槽位数（越界判断用它，而不是 clamp 行号）。</summary>
        public uint slotCount;

        /// <summary>保留：组级标签已撤（"整角色"由部件行的 <see cref="HoObjectBufferPartTags.CharacterFull"/> 表达），恒 0。</summary>
        public uint tags;

        public uint reserved;

        public const int Stride = 16;
    }

    /// <summary>
    /// 选择表的行。选择 ID 是**独立的 8 bit 空间**（≤256），与部件的 16 bit ID 无关（规划 §5.11）。
    /// 大小 32 B；256 行 = 8 KB。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HoObjectSelectionData
    {
        public uint selectionId;
        public uint nameHash;
        public uint tags;
        public uint reserved;
        public Vector4 displayColor;

        public const int Stride = 32;
    }

    /// <summary>palette 与选择表的容量上限（规划决策 2 / §5.11）。消费端与编辑器都要读，所以是 public。</summary>
    public static class HoObjectBufferPaletteLimits
    {
        /// <summary>部件行上限（注册校验预算，决策 2）。</summary>
        public const int MaxPartRows = 4096;

        /// <summary>每组的槽位上限（8 bit）。</summary>
        public const int MaxSlotsPerGroup = 256;

        /// <summary>组行上限（8 bit）。</summary>
        public const int MaxGroups = 256;

        /// <summary>选择上限（独立 8 bit 空间）。</summary>
        public const int MaxSelections = 256;

        /// <summary>palette 第 0 行永远是 unknown：RSUV 未写入/被重置时索引会变 0，必须看得见。</summary>
        public const int UnknownRow = 0;

        public const int UnknownSelectionId = 0;
    }
}
