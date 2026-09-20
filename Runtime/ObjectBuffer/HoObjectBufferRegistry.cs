using System;
using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    /// <summary>
    /// 部件表与选择表的**唯一真值**：
    /// <list type="bullet">
    /// <item>把各 <see cref="HoObjectBufferGroup"/> 的条目编成两级 palette（组行 + 部件行）；</item>
    /// <item>分配 16 bit 部件 ID（角色 8 + 槽位 8）与 8 bit 选择 ID；</item>
    /// <item>上传 <see cref="GraphicsBuffer"/> 并发布全局。</item>
    /// </list>
    /// 规则（规划 §5.3 / §5.11）：像素里只有索引、属性永远在表里；两级是为了让稀疏的
    /// <c>角色&lt;&lt;8 | 槽位</c> 能定位到稠密行；越界一律回 unknown 行而**不是 clamp 行号**
    /// （clamp 会落到别的角色的行上，读出来看着合法其实是错的）。
    /// <para>
    /// public：消费端（ScreenProcess 规则、AOV 导出、调试与编辑器）需要按名字查 ID、按 ID 查行，
    /// 这是它们与本 feature 之间的只读接口。
    /// </para>
    /// </summary>
    public static class HoObjectBufferRegistry
    {
        private static readonly List<HoObjectBufferGroup> Groups = new List<HoObjectBufferGroup>();
        private static readonly HashSet<HoObjectBufferGroup> InvalidGroups = new HashSet<HoObjectBufferGroup>();
        private static readonly Dictionary<uint, int> PartRowByPartId = new Dictionary<uint, int>();
        private static readonly Dictionary<string, uint> PartIdByKey = new Dictionary<string, uint>(StringComparer.Ordinal);
        private static readonly Dictionary<string, uint> SelectionIdByName = new Dictionary<string, uint>(StringComparer.Ordinal);
        private static readonly List<string> PartNames = new List<string>();
        private static readonly List<string> SelectionNames = new List<string>();

        private static HoObjectPartData[] partRows = Array.Empty<HoObjectPartData>();
        private static HoObjectGroupData[] groupRows = Array.Empty<HoObjectGroupData>();
        private static HoObjectSelectionData[] selectionRows = Array.Empty<HoObjectSelectionData>();

        private static GraphicsBuffer partBuffer;
        private static GraphicsBuffer groupBuffer;
        private static GraphicsBuffer selectionBuffer;

        private static bool dirty = true;
        private static int version;
        private static bool warnedMissingGraphicsBufferSupport;

        /// <summary>每帧由 feature 读取：表内容变了才需要重传。</summary>
        public static int Version => version;

        public static int PartRowCount => partRows.Length;

        public static int SelectionCount => Mathf.Max(0, selectionRows.Length - 1);

        public static GraphicsBuffer PartBuffer => partBuffer;

        public static GraphicsBuffer GroupBuffer => groupBuffer;

        public static GraphicsBuffer SelectionBuffer => selectionBuffer;

        internal static bool IsGroupValid(HoObjectBufferGroup group)
        {
            return group != null && !InvalidGroups.Contains(group);
        }

        /// <summary>平台是否支持 palette 需要的 StructuredBuffer（决策 8 的前置条件）。</summary>
        public static bool SupportsStructuredBuffer => SystemInfo.graphicsShaderLevel >= 45;

        public static void Register(HoObjectBufferGroup group)
        {
            if (group == null || Groups.Contains(group))
            {
                return;
            }

            Groups.Add(group);
            MarkDirty();
        }

        public static void Unregister(HoObjectBufferGroup group)
        {
            if (group == null || !Groups.Remove(group))
            {
                return;
            }

            MarkDirty();
        }

        public static void MarkDirty()
        {
            dirty = true;
        }

        /// <summary>取某个角色/部件名的 16 bit 部件 ID；未注册返回 0（unknown）。</summary>
        public static uint GetPartId(int groupId, string partName)
        {
            EnsureBuilt();
            return PartIdByKey.TryGetValue(MakePartKey(groupId, partName), out uint partId) ? partId : 0u;
        }

        /// <summary>取部件 ID 在部件表里的行号；未注册返回 <see cref="HoObjectBufferPaletteLimits.UnknownRow"/>。</summary>
        public static int GetPartRow(uint partId)
        {
            EnsureBuilt();
            return PartRowByPartId.TryGetValue(partId, out int row) ? row : HoObjectBufferPaletteLimits.UnknownRow;
        }

        /// <summary>取选择名对应的 8 bit 选择 ID；未注册返回 0（= 无选择）。</summary>
        public static uint GetSelectionId(string selectionName)
        {
            EnsureBuilt();
            return SelectionIdByName.TryGetValue(selectionName ?? string.Empty, out uint id)
                ? id
                : HoObjectBufferPaletteLimits.UnknownSelectionId;
        }

        /// <summary>部件行的只读视图（debug / AOV manifest / 编辑器用）。</summary>
        public static bool TryGetPartRowData(int row, out HoObjectPartData data)
        {
            EnsureBuilt();
            if (row < 0 || row >= partRows.Length)
            {
                data = default;
                return false;
            }

            data = partRows[row];
            return true;
        }

        public static bool TryGetPartName(uint partId, out string name)
        {
            EnsureBuilt();
            int row = GetPartRow(partId);
            if (row > 0 && row - 1 < PartNames.Count)
            {
                name = PartNames[row - 1];
                return true;
            }

            name = string.Empty;
            return false;
        }

        public static bool TryGetSelectionName(uint selectionId, out string name)
        {
            EnsureBuilt();
            int index = (int)selectionId - 1;
            if (selectionId > 0 && index < SelectionNames.Count)
            {
                name = SelectionNames[index];
                return true;
            }

            name = string.Empty;
            return false;
        }

        /// <summary>按需重建表并（重新）上传。渲染线程每帧调用一次即可。</summary>
        public static void EnsureBuilt()
        {
            if (!dirty)
            {
                return;
            }

            dirty = false;
            Build();
            Upload();
            version++;
        }

        private static void Build()
        {
            PartRowByPartId.Clear();
            PartIdByKey.Clear();
            PartNames.Clear();
            SelectionIdByName.Clear();
            SelectionNames.Clear();
            InvalidGroups.Clear();

            // 组行固定 256 行，按 groupId 直接索引；行 0 保留给 unknown。
            var groups = new HoObjectGroupData[HoObjectBufferPaletteLimits.MaxGroups];
            groups[0] = new HoObjectGroupData { rowBase = 0, slotCount = 0, tags = 0 };

            // 部件行 0 = unknown：RSUV 未被写入或因重载被重置时索引会变成 0，必须看得见。
            var parts = new List<HoObjectPartData>(Mathf.Min(HoObjectBufferPaletteLimits.MaxPartRows, 256))
            {
                new HoObjectPartData
                {
                    partId = 0,
                    nameHash = 0,
                    category = (uint)HoObjectBufferPartCategory.Unspecified,
                    tags = 0,
                    displayColor = new Vector4(1f, 0f, 1f, 1f)
                }
            };

            // 选择行 0 = 无选择。
            var selections = new List<HoObjectSelectionData>
            {
                new HoObjectSelectionData
                {
                    selectionId = 0,
                    nameHash = 0,
                    displayColor = new Vector4(0f, 0f, 0f, 1f)
                }
            };

            // 确定性顺序：先按 groupId，再按实例 ID，保证跨帧/跨机 ID 稳定（规划 §5.6）。
            var orderedGroups = new List<HoObjectBufferGroup>(Groups);
            orderedGroups.Sort(CompareGroups);

            var groupIdCounts = new int[HoObjectBufferPaletteLimits.MaxGroups];
            for (int i = 0; i < orderedGroups.Count; i++)
            {
                HoObjectBufferGroup candidate = orderedGroups[i];
                if (candidate == null)
                {
                    continue;
                }

                int candidateId = Mathf.Clamp(candidate.groupId, 0, HoObjectBufferPaletteLimits.MaxGroups - 1);
                if (candidateId > 0)
                {
                    groupIdCounts[candidateId]++;
                }
            }

            for (int i = 0; i < orderedGroups.Count; i++)
            {
                HoObjectBufferGroup candidate = orderedGroups[i];
                if (candidate == null)
                {
                    continue;
                }

                int candidateId = Mathf.Clamp(candidate.groupId, 0, HoObjectBufferPaletteLimits.MaxGroups - 1);
                if (candidateId == 0 || groupIdCounts[candidateId] != 1)
                {
                    InvalidGroups.Add(candidate);
                }
            }

            for (int groupId = 1; groupId < groupIdCounts.Length; groupId++)
            {
                if (groupIdCounts[groupId] > 1)
                {
                    Debug.LogError($"[Ho-ObjectBuffer] 组 ID {groupId} 被 {groupIdCounts[groupId]} 个 HoObjectBufferGroup 重复使用。" +
                                   "冲突组已全部失效，请为每个组分配唯一 ID。");
                }
            }

            int selectionCount = 0;
            for (int groupIndex = 0; groupIndex < orderedGroups.Count; groupIndex++)
            {
                HoObjectBufferGroup group = orderedGroups[groupIndex];
                if (!IsGroupValid(group))
                {
                    continue;
                }
                int groupId = Mathf.Clamp(group.groupId, 0, HoObjectBufferPaletteLimits.MaxGroups - 1);
                if (groupId == 0)
                {
                    // 角色 0 保留：它的 partId 全部落在 0..255，与 "partId 0 = unknown" 冲突。
                    continue;
                }

                HoObjectGroupData groupData = groups[groupId];
                groupData.rowBase = (uint)parts.Count;
                groupData.slotCount = 0;
                groupData.tags = (uint)group.groupTags;

                // 快照：GetPartNames() 是复用缓存，循环体内又可能触发它被重填。
                var partNames = new List<string>(group.GetPartNames());
                for (int slot = 0; slot < partNames.Count; slot++)
                {
                    string partName = partNames[slot];
                    if (string.IsNullOrEmpty(partName))
                    {
                        continue;
                    }

                    if (parts.Count >= HoObjectBufferPaletteLimits.MaxPartRows)
                    {
                        Debug.LogWarning($"[Ho-ObjectBuffer] 部件表已满（{HoObjectBufferPaletteLimits.MaxPartRows} 行），'{partName}' 未注册。");
                        break;
                    }

                    if (slot >= HoObjectBufferPaletteLimits.MaxSlotsPerGroup)
                    {
                        Debug.LogWarning($"[Ho-ObjectBuffer] 组 {groupId} 的槽位已满（{HoObjectBufferPaletteLimits.MaxSlotsPerGroup}），'{partName}' 未注册。");
                        break;
                    }

                    uint partId = MakePartId(groupId, slot);
                    HoObjectPartData row = group.BuildPartRow(slot, partName);
                    row.partId = partId;

                    PartRowByPartId[partId] = parts.Count;
                    PartIdByKey[MakePartKey(groupId, partName)] = partId;
                    PartNames.Add(partName);
                    parts.Add(row);

                    groupData.slotCount = (uint)(slot + 1);
                }

                groups[groupId] = groupData;

                IReadOnlyList<string> groupSelections = group.GetSelectionNames();
                for (int i = 0; i < groupSelections.Count; i++)
                {
                    string selectionName = groupSelections[i];
                    if (string.IsNullOrEmpty(selectionName) || SelectionIdByName.ContainsKey(selectionName))
                    {
                        continue;
                    }

                    if (selectionCount + 1 >= HoObjectBufferPaletteLimits.MaxSelections)
                    {
                        Debug.LogWarning($"[Ho-ObjectBuffer] 选择表已满（{HoObjectBufferPaletteLimits.MaxSelections - 1} 个可用），'{selectionName}' 未注册。");
                        break;
                    }

                    selectionCount++;
                    SelectionIdByName[selectionName] = (uint)selectionCount;
                    SelectionNames.Add(selectionName);

                    HoObjectSelectionData selectionRow = group.BuildSelectionRow(i, selectionName);
                    selectionRow.selectionId = (uint)selectionCount;
                    selections.Add(selectionRow);
                }
            }

            partRows = parts.ToArray();
            groupRows = groups;
            selectionRows = selections.ToArray();
        }

        private static void Upload()
        {
            if (!SupportsStructuredBuffer)
            {
                if (!warnedMissingGraphicsBufferSupport)
                {
                    warnedMissingGraphicsBufferSupport = true;
                    Debug.LogWarning("[Ho-ObjectBuffer] 平台不支持 StructuredBuffer（shader level < 4.5），palette 无法上传，" +
                                     "ID 解析会全部落到 unknown 行。");
                }

                return;
            }

            partBuffer = EnsureBuffer(partBuffer, partRows.Length, HoObjectPartData.Stride, partRows);
            groupBuffer = EnsureBuffer(groupBuffer, groupRows.Length, HoObjectGroupData.Stride, groupRows);
            selectionBuffer = EnsureBuffer(selectionBuffer, selectionRows.Length, HoObjectSelectionData.Stride, selectionRows);

            Shader.SetGlobalBuffer(HoObjectBufferShaderConstants.PartBufferId, partBuffer);
            Shader.SetGlobalBuffer(HoObjectBufferShaderConstants.GroupBufferId, groupBuffer);
            Shader.SetGlobalBuffer(HoObjectBufferShaderConstants.SelectionBufferId, selectionBuffer);
            Shader.SetGlobalFloat(HoObjectBufferShaderConstants.PartCountId, partRows.Length);
            Shader.SetGlobalFloat(HoObjectBufferShaderConstants.SelectionCountId, SelectionCount);

            // RSUV 不会被序列化（官方文档明确 "not serialized… resets when the object is reloaded"），
            // 所以每次表重建后都要把索引重新写回 renderer；写入前先解决跨组冲突（一个 renderer 只属一个部件）。
            HoObjectBufferGroup.ResolveAssignments();
            for (int i = 0; i < Groups.Count; i++)
            {
                if (IsGroupValid(Groups[i]))
                {
                    Groups[i].ApplyIdentity();
                }
                else
                {
                    Groups[i].ClearIdentity();
                }
            }

            WarnAboutConflicts();

            // 诊断（重建时一次）：把表的前几行与组行的内容直接打出来，用于核对"像素里的 ID → 表行"是否对得上。
            // 洋红（unknown 行）有两种完全不同的原因：① 像素 ID 不在表里（残留 RSUV / 组没覆盖到）；
            // ② 表本身是空的或组行 slotCount=0。两者的修法完全不同，所以这里把表内容摆出来。
            var diag = new System.Text.StringBuilder();
            diag.Append($"[Ho-ObjectBuffer] palette：部件行={partRows.Length} 组行={groupRows.Length} 选择行={selectionRows.Length}");
            for (int i = 0; i < System.Math.Min(3, partRows.Length); i++)
            {
                diag.Append($" | 行{i} id=0x{partRows[i].partId:X4} color=({partRows[i].displayColor.x:0.##},{partRows[i].displayColor.y:0.##},{partRows[i].displayColor.z:0.##})");
            }

            for (int g = 0; g < groupRows.Length; g++)
            {
                if (groupRows[g].slotCount > 0u)
                {
                    diag.Append($" | 组{g} rowBase={groupRows[g].rowBase} slots={groupRows[g].slotCount} tags={groupRows[g].tags}");
                }
            }

            Debug.Log(diag.ToString());
        }

        private static int lastWarnedConflictCount = -1;

        /// <summary>
        /// 重复指定（一个 Renderer 被多个条目命中）**必须吵一次**：裁决本身是确定性的，
        /// 但静默裁决会让人以为"我明明拖进去了却没生效"。只在数量变化时打印，避免每次重建刷屏。
        /// </summary>
        private static void WarnAboutConflicts()
        {
            IReadOnlyList<HoObjectBufferConflict> conflicts = HoObjectBufferGroup.GetConflicts();
            if (conflicts.Count == lastWarnedConflictCount)
            {
                return;
            }

            lastWarnedConflictCount = conflicts.Count;
            if (conflicts.Count == 0)
            {
                return;
            }

            Debug.LogWarning($"[Ho-ObjectBuffer] {conflicts.Count} 个 Renderer 被多个部件条目同时命中" +
                             "（最常见的原因：拖了父级、展开子级之后与别的条目重叠）。已按「优先级 → 层级距离 → 条目顺序」裁决；" +
                             "逐条明细在各 HoObjectBufferGroup 的 Inspector 里。");
        }

        private static GraphicsBuffer EnsureBuffer<T>(GraphicsBuffer buffer, int count, int stride, T[] source)        {
            // 容量按 2 的幂增长，避免每加一个部件就重建缓冲。
            int capacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));
            if (buffer == null || buffer.count < capacity)
            {
                buffer?.Dispose();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, stride);
            }

            if (count > 0)
            {
                buffer.SetData(source, 0, 0, count);
            }

            return buffer;
        }

        public static void Release()
        {
            partBuffer?.Dispose();
            groupBuffer?.Dispose();
            selectionBuffer?.Dispose();
            partBuffer = null;
            groupBuffer = null;
            selectionBuffer = null;
        }

        /// <summary>
        /// 进入播放模式时丢掉旧的 GPU 缓冲并标脏（关闭 Domain Reload 时静态字段会跨播放存活，
        /// 缓冲区可能已经被释放）。**不清 Groups**：无 Domain Reload 时 OnEnable 不会重跑，
        /// 清了就再也注册不回来了；保留列表可以靠 EnsureBuilt 重新上传并重写 RSUV。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            Release();
            warnedMissingGraphicsBufferSupport = false;
            lastWarnedConflictCount = -1;
            dirty = true;
        }

        public static uint MakePartId(int groupId, int slot)
        {
            return ((uint)(groupId & 0xFF) << 8) | (uint)(slot & 0xFF);
        }

        public static uint GetGroupId(uint partId)
        {
            return partId >> 8;
        }

        public static uint GetSlotId(uint partId)
        {
            return partId & 0xFF;
        }

        private static string MakePartKey(int groupId, string partName)
        {
            return groupId.ToString() + "/" + (partName ?? string.Empty);
        }

        private static int CompareGroups(HoObjectBufferGroup a, HoObjectBufferGroup b)
        {
            int characterCompare = a.groupId.CompareTo(b.groupId);
            if (characterCompare != 0)
            {
                return characterCompare;
            }

            return a.GetInstanceID().CompareTo(b.GetInstanceID());
        }
    }
}
