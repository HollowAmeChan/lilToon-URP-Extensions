using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEngine;

namespace lilToon.URP.Extensions.AttributeComposite
{
    /// <summary>
    /// 一条 lane 的来源合成方式（规划 §0.4）。**本轮只实现 `ObjectOnly`**：
    /// surface 侧要等 Ho-SurfaceBuffer 落地（R4b）才有生产者，其余四种先声明、不消费。
    /// </summary>
    public enum HoSemanticSourceMode
    {
        [InspectorName("只用物体位")]
        ObjectOnly = 0,
        [InspectorName("只用表面语义")]
        SurfaceOnly = 1,
        [InspectorName("并集")]
        Union = 2,
        [InspectorName("表面覆盖物体")]
        SurfaceOverride = 3,
        [InspectorName("交集")]
        Intersection = 4
    }

    /// <summary>
    /// schema 里的一条语义声明（规划 §0.3.6 / §3）。**SemanticId 是权威语义，LaneIndex 只是屏幕传输位**，
    /// 两者不许混用：像素里传的是 lane，名字解析出来的是 SemanticId。
    /// </summary>
    public sealed class HoSemanticEntry
    {
        /// <summary>稳定名字（ASCII，跨工程/跨仓对账用；也是消费者登记时用的键）。</summary>
        public string name = "Semantic";

        /// <summary>面板显示名（中文）。</summary>
        public string displayName = "语义";

        /// <summary>1..255 的具名语义；0 保留给"未写"。</summary>
        public int semanticId = 1;

        /// <summary>0..15 的传输位；每个 surface-writable 语义独占一个稳定 lane。</summary>
        public int laneIndex;

        public HoSemanticSourceMode sourceMode = HoSemanticSourceMode.ObjectOnly;

        /// <summary>object 侧的来源：部件行标签里的哪一位。`-1` = 该 lane 没有 object 来源。</summary>
        public int objectTagBit = -1;

        /// <summary>debug 视图用色。</summary>
        public Color debugColor = Color.white;
    }

    /// <summary>
    /// OB / SB / AC 三方共用的一份语义声明（规划 §0.3.6：OB/SB 都只读该声明，不自造 ID）。
    /// <para>
    /// **词表只有一份**：本轮所有 lane 都是"物体位"，所以直接由 <see cref="HoObjectBufferPartTags"/>
    /// 生成 —— SemanticId = 位序 + 1、LaneIndex = 位序、objectTagBit = 位序。
    /// 等 SB 落地、出现"材质可写"的语义时，再给这里加作者侧入口（那时 `name` 才是材质参数的键），
    /// 在那之前不要为它造第二套名字。
    /// </para>
    /// </summary>
    public static class HoSemanticSchema
    {
        /// <summary>传输位上限（规划 §0.3.6：最多 16 个 surface-writable 语义同时占 lane）。</summary>
        public const int MaxLanes = 16;

        /// <summary>本轮 SemanticResolve 一次写 4 张 RGBA8（= 8 条 lane，每张 2 条）。</summary>
        public const int ResolvedLaneCount = 8;

        private static readonly List<HoSemanticEntry> Entries = BuildDefault();

        private static readonly Dictionary<string, HoSemanticEntry> ByName = BuildNameLookup();
        private static readonly Dictionary<int, HoSemanticEntry> ById = BuildIdLookup();

        /// <summary>声明（按 lane 升序）。</summary>
        public static IReadOnlyList<HoSemanticEntry> Declarations => Entries;

        public static int LaneCount => Entries.Count;

        public static bool TryGetByName(string name, out HoSemanticEntry entry)
        {
            if (string.IsNullOrEmpty(name))
            {
                entry = null;
                return false;
            }

            return ByName.TryGetValue(name, out entry);
        }

        public static bool TryGetBySemanticId(int semanticId, out HoSemanticEntry entry)
        {
            return ById.TryGetValue(semanticId, out entry);
        }

        /// <summary>object 侧来源的位掩码（部件行 `tags` 的那 8 位同构）；没有 object 来源的 lane 记 0。</summary>
        public static uint ObjectTagMaskForLane(int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= Entries.Count)
            {
                return 0u;
            }

            int bit = Entries[laneIndex].objectTagBit;
            return bit >= 0 && bit < 32 ? 1u << bit : 0u;
        }

        /// <summary>一条声明的问题描述；全部合法时返回 null。解析不到 / 撞车必须可见（规划 §3）。</summary>
        public static string DescribeValidation()
        {
            if (Entries.Count == 0)
            {
                return "schema 是空的：没有任何语义声明。";
            }

            if (Entries.Count > MaxLanes)
            {
                return $"lane 超上限：声明了 {Entries.Count} 条，最多 {MaxLanes} 条。";
            }

            if (Entries.Count > ResolvedLaneCount)
            {
                return $"本轮 SemanticResolve 只写 {ResolvedLaneCount} 条 lane（4 张 RGBA8），" +
                       $"当前声明了 {Entries.Count} 条：多出来的 lane 没有产出（等 R4b 的 MRT 分批）。";
            }

            var seenIds = new HashSet<int>();
            var seenLanes = new HashSet<int>();
            for (int i = 0; i < Entries.Count; i++)
            {
                HoSemanticEntry entry = Entries[i];
                if (entry.semanticId <= 0 || entry.semanticId > 255)
                {
                    return $"'{entry.name}' 的 SemanticId {entry.semanticId} 越界（1..255，0 保留给未写）。";
                }

                if (entry.laneIndex < 0 || entry.laneIndex >= MaxLanes)
                {
                    return $"'{entry.name}' 的 LaneIndex {entry.laneIndex} 越界（0..{MaxLanes - 1}）。";
                }

                if (!seenIds.Add(entry.semanticId))
                {
                    return $"SemanticId {entry.semanticId} 被声明了两次。";
                }

                if (!seenLanes.Add(entry.laneIndex))
                {
                    return $"LaneIndex {entry.laneIndex} 被声明了两次。";
                }
            }

            return null;
        }

        private static List<HoSemanticEntry> BuildDefault()
        {
            var entries = new List<HoSemanticEntry>();
            foreach (HoObjectBufferPartTags tag in Enum.GetValues(typeof(HoObjectBufferPartTags)))
            {
                if (tag == HoObjectBufferPartTags.None)
                {
                    continue;
                }

                int bit = BitIndex(tag);
                if (bit < 0)
                {
                    continue;
                }

                string memberName = tag.ToString();
                entries.Add(new HoSemanticEntry
                {
                    name = memberName,
                    displayName = DisplayNameOf(tag, memberName),
                    semanticId = bit + 1,
                    laneIndex = bit,
                    sourceMode = HoSemanticSourceMode.ObjectOnly,
                    objectTagBit = bit,
                    debugColor = DebugColorOf(bit)
                });
            }

            return entries;
        }

        private static Dictionary<string, HoSemanticEntry> BuildNameLookup()
        {
            var map = new Dictionary<string, HoSemanticEntry>(StringComparer.Ordinal);
            for (int i = 0; i < Entries.Count; i++)
            {
                HoSemanticEntry entry = Entries[i];
                if (!string.IsNullOrEmpty(entry.name))
                {
                    map[entry.name] = entry;
                }
            }

            return map;
        }

        private static Dictionary<int, HoSemanticEntry> BuildIdLookup()
        {
            var map = new Dictionary<int, HoSemanticEntry>();
            for (int i = 0; i < Entries.Count; i++)
            {
                HoSemanticEntry entry = Entries[i];
                map[entry.semanticId] = entry;
            }

            return map;
        }

        private static int BitIndex(HoObjectBufferPartTags tag)
        {
            int value = (int)tag;
            for (int bit = 0; bit < 32; bit++)
            {
                if (value == 1 << bit)
                {
                    return bit;
                }
            }

            return -1;
        }

        private static string DisplayNameOf(HoObjectBufferPartTags tag, string fallback)
        {
            var field = typeof(HoObjectBufferPartTags).GetField(fallback);
            var attribute = field != null
                ? (InspectorNameAttribute)Attribute.GetCustomAttribute(field, typeof(InspectorNameAttribute))
                : null;
            return attribute != null ? attribute.displayName : fallback;
        }

        /// <summary>固定配色：与部件行默认色的语义一致（脸=暖、前发=紫、眼睛=青、人体=红…），跨会话稳定。</summary>
        private static Color DebugColorOf(int bit)
        {
            switch (bit)
            {
                case 0:
                    return new Color(1.0f, 1.0f, 1.0f, 1.0f);
                case 1:
                    return new Color(1.0f, 0.72f, 0.45f, 1.0f);
                case 2:
                    return new Color(0.72f, 0.45f, 1.0f, 1.0f);
                case 3:
                    return new Color(0.35f, 0.95f, 0.95f, 1.0f);
                case 4:
                    return new Color(0.45f, 0.95f, 0.45f, 1.0f);
                case 5:
                    return new Color(0.95f, 0.9f, 0.3f, 1.0f);
                case 6:
                    return new Color(0.95f, 0.35f, 0.35f, 1.0f);
                default:
                    return new Color(0.5f, 0.5f, 0.5f, 1.0f);
            }
        }
    }
}
