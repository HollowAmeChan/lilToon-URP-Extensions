using System;
using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.CharacterBuffer
{
    /// <summary>
    /// 部件表与选择表的**唯一真值**：
    /// <list type="bullet">
    /// <item>把各 <see cref="HoCharacterBufferGroup"/> 的条目编成两级 palette（角色行 + 部件行）；</item>
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
    public static class HoCharacterBufferRegistry
    {
        private static readonly List<HoCharacterBufferGroup> Groups = new List<HoCharacterBufferGroup>();
        private static readonly Dictionary<uint, int> PartRowByPartId = new Dictionary<uint, int>();
        private static readonly Dictionary<string, uint> PartIdByKey = new Dictionary<string, uint>(StringComparer.Ordinal);
        private static readonly Dictionary<string, uint> SelectionIdByName = new Dictionary<string, uint>(StringComparer.Ordinal);
        private static readonly List<string> PartNames = new List<string>();
        private static readonly List<string> SelectionNames = new List<string>();

        private static HoCharacterPartData[] partRows = Array.Empty<HoCharacterPartData>();
        private static HoCharacterData[] characterRows = Array.Empty<HoCharacterData>();
        private static HoCharacterSelectionData[] selectionRows = Array.Empty<HoCharacterSelectionData>();

        private static GraphicsBuffer partBuffer;
        private static GraphicsBuffer characterBuffer;
        private static GraphicsBuffer selectionBuffer;

        private static bool dirty = true;
        private static int version;
        private static bool warnedMissingGraphicsBufferSupport;

        /// <summary>每帧由 feature 读取：表内容变了才需要重传。</summary>
        public static int Version => version;

        public static int PartRowCount => partRows.Length;

        public static int SelectionCount => Mathf.Max(0, selectionRows.Length - 1);

        public static GraphicsBuffer PartBuffer => partBuffer;

        public static GraphicsBuffer CharacterBuffer => characterBuffer;

        public static GraphicsBuffer SelectionBuffer => selectionBuffer;

        /// <summary>平台是否支持 palette 需要的 StructuredBuffer（决策 8 的前置条件）。</summary>
        public static bool SupportsStructuredBuffer => SystemInfo.graphicsShaderLevel >= 45;

        public static void Register(HoCharacterBufferGroup group)
        {
            if (group == null || Groups.Contains(group))
            {
                return;
            }

            Groups.Add(group);
            MarkDirty();
        }

        public static void Unregister(HoCharacterBufferGroup group)
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
        public static uint GetPartId(int characterId, string partName)
        {
            EnsureBuilt();
            return PartIdByKey.TryGetValue(MakePartKey(characterId, partName), out uint partId) ? partId : 0u;
        }

        /// <summary>取部件 ID 在部件表里的行号；未注册返回 <see cref="HoCharacterBufferPaletteLimits.UnknownRow"/>。</summary>
        public static int GetPartRow(uint partId)
        {
            EnsureBuilt();
            return PartRowByPartId.TryGetValue(partId, out int row) ? row : HoCharacterBufferPaletteLimits.UnknownRow;
        }

        /// <summary>取选择名对应的 8 bit 选择 ID；未注册返回 0（= 无选择）。</summary>
        public static uint GetSelectionId(string selectionName)
        {
            EnsureBuilt();
            return SelectionIdByName.TryGetValue(selectionName ?? string.Empty, out uint id)
                ? id
                : HoCharacterBufferPaletteLimits.UnknownSelectionId;
        }

        /// <summary>部件行的只读视图（debug / AOV manifest / 编辑器用）。</summary>
        public static bool TryGetPartRowData(int row, out HoCharacterPartData data)
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

            // 角色行固定 256 行，按 characterId 直接索引；行 0 保留给 unknown。
            var characters = new HoCharacterData[HoCharacterBufferPaletteLimits.MaxCharacters];
            characters[0] = new HoCharacterData { rowBase = 0, slotCount = 0, tags = 0 };

            // 部件行 0 = unknown：RSUV 未被写入或因重载被重置时索引会变成 0，必须看得见。
            var parts = new List<HoCharacterPartData>(Mathf.Min(HoCharacterBufferPaletteLimits.MaxPartRows, 256))
            {
                new HoCharacterPartData
                {
                    partId = 0,
                    nameHash = 0,
                    category = (uint)HoCharacterBufferPartCategory.Unspecified,
                    tags = 0,
                    displayColor = new Vector4(1f, 0f, 1f, 1f)
                }
            };

            // 选择行 0 = 无选择。
            var selections = new List<HoCharacterSelectionData>
            {
                new HoCharacterSelectionData
                {
                    selectionId = 0,
                    nameHash = 0,
                    displayColor = new Vector4(0f, 0f, 0f, 1f)
                }
            };

            // 确定性顺序：先按 characterId，再按实例 ID，保证跨帧/跨机 ID 稳定（规划 §5.6）。
            var orderedGroups = new List<HoCharacterBufferGroup>(Groups);
            orderedGroups.Sort(CompareGroups);

            int selectionCount = 0;
            for (int groupIndex = 0; groupIndex < orderedGroups.Count; groupIndex++)
            {
                HoCharacterBufferGroup group = orderedGroups[groupIndex];
                int characterId = Mathf.Clamp(group.characterId, 0, HoCharacterBufferPaletteLimits.MaxCharacters - 1);
                if (characterId == 0)
                {
                    // 角色 0 保留：它的 partId 全部落在 0..255，与 "partId 0 = unknown" 冲突。
                    continue;
                }

                HoCharacterData character = characters[characterId];
                character.rowBase = (uint)parts.Count;
                character.slotCount = 0;
                character.tags = (uint)group.characterTags;

                IReadOnlyList<string> partNames = group.GetPartNames();
                for (int slot = 0; slot < partNames.Count; slot++)
                {
                    string partName = partNames[slot];
                    if (string.IsNullOrEmpty(partName))
                    {
                        continue;
                    }

                    if (parts.Count >= HoCharacterBufferPaletteLimits.MaxPartRows)
                    {
                        Debug.LogWarning($"[Ho-CharacterBuffer] 部件表已满（{HoCharacterBufferPaletteLimits.MaxPartRows} 行），'{partName}' 未注册。");
                        break;
                    }

                    if (slot >= HoCharacterBufferPaletteLimits.MaxSlotsPerCharacter)
                    {
                        Debug.LogWarning($"[Ho-CharacterBuffer] 角色 {characterId} 的槽位已满（{HoCharacterBufferPaletteLimits.MaxSlotsPerCharacter}），'{partName}' 未注册。");
                        break;
                    }

                    uint partId = MakePartId(characterId, slot);
                    HoCharacterPartData row = group.BuildPartRow(slot, partName);
                    row.partId = partId;

                    PartRowByPartId[partId] = parts.Count;
                    PartIdByKey[MakePartKey(characterId, partName)] = partId;
                    PartNames.Add(partName);
                    parts.Add(row);

                    character.slotCount = (uint)(slot + 1);
                }

                characters[characterId] = character;

                IReadOnlyList<string> groupSelections = group.GetSelectionNames();
                for (int i = 0; i < groupSelections.Count; i++)
                {
                    string selectionName = groupSelections[i];
                    if (string.IsNullOrEmpty(selectionName) || SelectionIdByName.ContainsKey(selectionName))
                    {
                        continue;
                    }

                    if (selectionCount + 1 >= HoCharacterBufferPaletteLimits.MaxSelections)
                    {
                        Debug.LogWarning($"[Ho-CharacterBuffer] 选择表已满（{HoCharacterBufferPaletteLimits.MaxSelections - 1} 个可用），'{selectionName}' 未注册。");
                        break;
                    }

                    selectionCount++;
                    SelectionIdByName[selectionName] = (uint)selectionCount;
                    SelectionNames.Add(selectionName);

                    HoCharacterSelectionData selectionRow = group.BuildSelectionRow(i, selectionName);
                    selectionRow.selectionId = (uint)selectionCount;
                    selections.Add(selectionRow);
                }
            }

            partRows = parts.ToArray();
            characterRows = characters;
            selectionRows = selections.ToArray();
        }

        private static void Upload()
        {
            if (!SupportsStructuredBuffer)
            {
                if (!warnedMissingGraphicsBufferSupport)
                {
                    warnedMissingGraphicsBufferSupport = true;
                    Debug.LogWarning("[Ho-CharacterBuffer] 平台不支持 StructuredBuffer（shader level < 4.5），palette 无法上传，" +
                                     "ID 解析会全部落到 unknown 行。");
                }

                return;
            }

            partBuffer = EnsureBuffer(partBuffer, partRows.Length, HoCharacterPartData.Stride, partRows);
            characterBuffer = EnsureBuffer(characterBuffer, characterRows.Length, HoCharacterData.Stride, characterRows);
            selectionBuffer = EnsureBuffer(selectionBuffer, selectionRows.Length, HoCharacterSelectionData.Stride, selectionRows);

            Shader.SetGlobalBuffer(HoCharacterBufferShaderConstants.PartBufferId, partBuffer);
            Shader.SetGlobalBuffer(HoCharacterBufferShaderConstants.CharacterBufferId, characterBuffer);
            Shader.SetGlobalBuffer(HoCharacterBufferShaderConstants.SelectionBufferId, selectionBuffer);
            Shader.SetGlobalFloat(HoCharacterBufferShaderConstants.PartCountId, partRows.Length);
            Shader.SetGlobalFloat(HoCharacterBufferShaderConstants.SelectionCountId, SelectionCount);

            // RSUV 不会被序列化（官方文档明确 "not serialized… resets when the object is reloaded"），
            // 所以每次表重建后都要把索引重新写回 renderer；写入前先解决跨组冲突（一个 renderer 只属一个部件）。
            HoCharacterBufferGroup.ResolveAssignments();
            for (int i = 0; i < Groups.Count; i++)
            {
                Groups[i].ApplyIdentity();
            }
        }

        private static GraphicsBuffer EnsureBuffer<T>(GraphicsBuffer buffer, int count, int stride, T[] source)
        {
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
            characterBuffer?.Dispose();
            selectionBuffer?.Dispose();
            partBuffer = null;
            characterBuffer = null;
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
            dirty = true;
        }

        public static uint MakePartId(int characterId, int slot)
        {
            return ((uint)(characterId & 0xFF) << 8) | (uint)(slot & 0xFF);
        }

        public static uint GetCharacterId(uint partId)
        {
            return partId >> 8;
        }

        public static uint GetSlotId(uint partId)
        {
            return partId & 0xFF;
        }

        private static string MakePartKey(int characterId, string partName)
        {
            return characterId.ToString() + "/" + (partName ?? string.Empty);
        }

        private static int CompareGroups(HoCharacterBufferGroup a, HoCharacterBufferGroup b)
        {
            int characterCompare = a.characterId.CompareTo(b.characterId);
            if (characterCompare != 0)
            {
                return characterCompare;
            }

            return a.GetInstanceID().CompareTo(b.GetInstanceID());
        }
    }
}
