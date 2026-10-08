using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.GeometryData;
using UnityEngine;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    public static partial class HoObjectBufferRegistry
    {
        // Membership is a palette property, not a second geometry pass or a material keyword.
        // A shared part is partitioned only where its renderers have different memberships.
        // The original named identity remains the public identity; aliases retain it in reserved.
        const int MembershipWords = 8;
        static readonly int MembershipId = Shader.PropertyToID("_HoOBSelectionMembership");
        static readonly int MembershipCountId = Shader.PropertyToID("_HoOBSelectionMembershipCount");
        static readonly Dictionary<Renderer, uint> SelectionBindings = new Dictionary<Renderer, uint>();
        static uint[] membershipWords = Array.Empty<uint>();
        static GraphicsBuffer membershipBuffer, placeholderMembership;

        sealed class Membership
        {
            public readonly uint[] bits = new uint[MembershipWords];
            public int fallbackGroup;
        }

        public static IReadOnlyList<string> GetSelectionNames()
        {
            EnsureBuilt();
            return SelectionNames.AsReadOnly();
        }

        /// <summary>Resolve an internal membership identity to the named part identity.</summary>
        public static uint GetCanonicalPartId(uint identity)
        {
            EnsureBuilt();
            if (!PartRowByPartId.TryGetValue(identity, out int row)) return identity;
            return partRows[row].reserved != 0 ? partRows[row].reserved : identity;
        }

        public static bool IsSelectionMember(uint identity, uint selectionId)
        {
            EnsureBuilt();
            if (selectionId == 0 || selectionId > SelectionCount || !PartRowByPartId.TryGetValue(identity, out int row)) return false;
            uint bit = selectionId - 1;
            return (membershipWords[row * MembershipWords + (int)(bit >> 5)] & (1u << (int)(bit & 31))) != 0;
        }

        static void ClearSelectionBindings()
        {
            foreach (var binding in SelectionBindings)
                if (binding.Key != null && (HoGeometryRendererBinding.GetManagedValue(binding.Key) & 0xffffu) == binding.Value)
                    HoGeometryRendererBinding.SetObjectIdentity(binding.Key, 0);
            SelectionBindings.Clear();
        }

        static void BuildSelectionMembership()
        {
            var members = new Dictionary<Renderer, Membership>();
            var ordered = new List<HoObjectBufferGroup>(Groups);
            ordered.Sort(CompareGroups);
            foreach (var group in ordered)
            {
                if (!IsGroupValid(group) || group.selections == null) continue;
                foreach (var entry in group.selections)
                {
                    if (entry == null || !SelectionIdByName.TryGetValue(entry.name ?? string.Empty, out uint id)) continue;
                    HoObjectBufferGroup.CollectRenderers(entry.renderers, entry.includeChildren, renderer =>
                    {
                        if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) return;
                        if (!members.TryGetValue(renderer, out var membership))
                        {
                            membership = new Membership { fallbackGroup = group.groupId };
                            members.Add(renderer, membership);
                        }
                        uint bit = id - 1;
                        membership.bits[(int)(bit >> 5)] |= 1u << (int)(bit & 31);
                    });
                }
            }

            var rows = new List<HoObjectPartData>[HoObjectBufferPaletteLimits.MaxGroups];
            var names = new List<string>[rows.Length];
            var bitsById = new Dictionary<uint, uint[]>();
            for (int group = 1; group < rows.Length; group++)
            {
                rows[group] = new List<HoObjectPartData>();
                names[group] = new List<string>();
                var descriptor = groupRows[group];
                for (int slot = 0; slot < descriptor.slotCount; slot++)
                {
                    int row = (int)descriptor.rowBase + slot;
                    rows[group].Add(partRows[row]);
                    names[group].Add(PartNames[row - 1]);
                }
            }

            // Sorting also makes shared signatures and capacity exhaustion deterministic.
            var renderers = new List<Renderer>(members.Keys);
            renderers.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
            var aliases = new Dictionary<string, uint>(StringComparer.Ordinal);
            int rowCount = partRows.Length, rejected = 0;
            foreach (var renderer in renderers)
            {
                var member = members[renderer];
                uint canonical = HoGeometryRendererBinding.GetManagedValue(renderer) & 0xffffu;
                bool hasPart = PartRowByPartId.TryGetValue(canonical, out int baseRow) && canonical != 0;
                int group = hasPart ? (int)GetGroupId(canonical) : member.fallbackGroup;
                string signature = group + "/" + (hasPart ? canonical : 0) + "/" + string.Join(",", member.bits);
                if (!aliases.TryGetValue(signature, out uint identity))
                {
                    if (rows[group].Count >= HoObjectBufferPaletteLimits.MaxSlotsPerGroup || rowCount >= HoObjectBufferPaletteLimits.MaxPartRows)
                    {
                        rejected++;
                        continue; // Fail closed: never mark all siblings of a shared part as selected.
                    }
                    identity = MakePartId(group, rows[group].Count);
                    var row = hasPart ? partRows[baseRow] : new HoObjectPartData { displayColor = new Vector4(.2f, .6f, 1, 1) };
                    row.partId = identity;
                    row.reserved = hasPart ? canonical : 0;
                    rows[group].Add(row);
                    names[group].Add(hasPart ? PartNames[baseRow - 1] : "选择组成员");
                    aliases.Add(signature, identity);
                    bitsById.Add(identity, member.bits);
                    rowCount++;
                }
                if (HoGeometryRendererBinding.SetObjectIdentity(renderer, identity)) SelectionBindings[renderer] = identity;
            }
            if (rejected > 0)
                Debug.LogWarning($"[Ho-ObjectBuffer] 选择组有 {rejected} 个 Renderer 超出身份槽位预算（每组 256 / 全局 4096 行），未加入遮罩。请拆分身份组或减少不同的成员组合。");

            var packed = new List<HoObjectPartData>(rowCount) { partRows[0] };
            membershipWords = new uint[rowCount * MembershipWords];
            PartRowByPartId.Clear();
            PartNames.Clear();
            for (int group = 1; group < rows.Length; group++)
            {
                groupRows[group].rowBase = (uint)packed.Count;
                groupRows[group].slotCount = (uint)rows[group].Count;
                foreach (var row in rows[group])
                {
                    if (bitsById.TryGetValue(row.partId, out var bits))
                        Array.Copy(bits, 0, membershipWords, packed.Count * MembershipWords, MembershipWords);
                    PartRowByPartId.Add(row.partId, packed.Count);
                    packed.Add(row);
                }
                PartNames.AddRange(names[group]);
            }
            partRows = packed.ToArray();
        }

        static void UploadSelectionMembership()
        {
            membershipBuffer = EnsureBuffer(membershipBuffer, membershipWords.Length, sizeof(uint), membershipWords);
            Shader.SetGlobalBuffer(MembershipId, membershipBuffer);
            Shader.SetGlobalFloat(MembershipCountId, partRows.Length);
        }

        static void ReleaseSelectionMembership()
        {
            membershipBuffer?.Dispose();
            membershipBuffer = null;
        }

        static void BindSelectionMembershipPlaceholder()
        {
            if (placeholderMembership == null)
            {
                placeholderMembership = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MembershipWords, sizeof(uint));
                placeholderMembership.SetData(new uint[MembershipWords]);
            }
            Shader.SetGlobalBuffer(MembershipId, placeholderMembership);
            Shader.SetGlobalFloat(MembershipCountId, 0);
        }
    }
}
