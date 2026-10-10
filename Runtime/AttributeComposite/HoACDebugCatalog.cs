using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEngine;

namespace lilToon.URP.Extensions.AttributeComposite
{
    /// <summary>Named targets shared by the color legend, mask preview and GPU palette.</summary>
    public static class HoACDebugCatalog
    {
        static int registryVersion = -1, groupNamesVersion;
        static readonly Dictionary<HoACDebugCategory, IReadOnlyList<Entry>> Entries = new Dictionary<HoACDebugCategory, IReadOnlyList<Entry>>();
        static readonly Dictionary<HoACDebugCategory, Vector4[]> Colors = new Dictionary<HoACDebugCategory, Vector4[]>();

        static void EnsureCache()
        {
            HoObjectBufferRegistry.EnsureBuilt();
            int names = 17;
            unchecked
            {
                foreach (var group in HoObjectBufferGroup.GetActiveGroups())
                    if (group != null) names = names * 31 + group.name.GetHashCode();
            }
            if (registryVersion == HoObjectBufferRegistry.Version && names == groupNamesVersion) return;
            registryVersion = HoObjectBufferRegistry.Version; groupNamesVersion = names;
            Entries.Clear(); Colors.Clear();
        }

        public readonly struct Entry
        {
            public readonly string Key, Label;
            public readonly Color Color;
            public readonly HoACQueryDescriptor Query;
            public Entry(string key, string label, Color color, HoACQueryDescriptor query)
            { Key = key; Label = label; Color = color; Query = query; }
        }

        public static IReadOnlyList<Entry> GetEntries(HoACDebugCategory category)
        {
            EnsureCache();
            if (Entries.TryGetValue(category, out var cached)) return cached;
            var result = new List<Entry>();
            var groupNames = new Dictionary<uint, string>();
            foreach (var group in HoObjectBufferGroup.GetActiveGroups())
                if (group != null) groupNames[(uint)group.groupId] = group.name;
            if (category == HoACDebugCategory.Semantics)
            {
                foreach (var semantic in HoSemanticSchema.Declarations)
                    result.Add(new Entry(semantic.name, semantic.displayName, semantic.debugColor,
                        HoACQueryDescriptor.Resolve(HoACQueryKind.Semantic, semantic.name, 0, HoACMaskDomain.Screen)));
            }
            else if (category == HoACDebugCategory.SelectionGroups)
            {
                foreach (string name in HoObjectBufferRegistry.GetSelectionNames())
                {
                    uint id = HoObjectBufferRegistry.GetSelectionId(name);
                    if (!HoObjectBufferRegistry.TryGetSelectionRowData(id, out var row)) continue;
                    result.Add(new Entry(name, name, row.displayColor,
                        HoACQueryDescriptor.Resolve(HoACQueryKind.SelectionGroup, name, 0, HoACMaskDomain.Screen)));
                }
            }
            else
            {
                var seen = new HashSet<uint>();
                for (int i = 1; i < HoObjectBufferRegistry.PartRowCount; i++)
                {
                    if (!HoObjectBufferRegistry.TryGetPartRowData(i, out var row) || row.reserved != 0) continue;
                    uint groupId = HoObjectBufferRegistry.GetGroupId(row.partId);
                    string groupName = groupNames.TryGetValue(groupId, out string name) ? name : "身份组 " + groupId;
                    if (category == HoACDebugCategory.IdentityGroups)
                    {
                        if (seen.Add(groupId)) result.Add(new Entry(groupId.ToString(), groupName, GroupColor(groupId),
                            HoACQueryDescriptor.Resolve(HoACQueryKind.Group, null, (int)groupId, HoACMaskDomain.Screen)));
                    }
                    else
                    {
                        HoObjectBufferRegistry.TryGetPartName(row.partId, out string partName);
                        // The key follows the named canonical part, never an alias allocated for selection membership.
                        string key = row.nameHash != 0 ? groupId + "/" + partName : groupId + "/@" + HoObjectBufferRegistry.GetSlotId(row.partId);
                        result.Add(new Entry(key, groupName + " · " + partName, row.displayColor,
                            HoACQueryDescriptor.Resolve(HoACQueryKind.Identity, null, (int)row.partId, HoACMaskDomain.Screen)));
                    }
                }
            }
            var view = result.AsReadOnly();
            Entries[category] = view;
            return view;
        }

        public static Color GroupColor(uint id) => Color.HSVToRGB(Mathf.Repeat(id * .61803398875f, 1f), .65f, .95f);

        public static HoACQueryDescriptor Resolve(HoACDebugCategory category, string key)
        {
            foreach (var entry in GetEntries(category)) if (string.Equals(entry.Key, key, StringComparison.Ordinal)) return entry.Query;
            return HoACQueryDescriptor.Resolve(HoACQueryKind.Identity, null, 0, HoACMaskDomain.Screen);
        }

        internal static HoACQueryDescriptor OverviewQuery(HoACDebugCategory category) => category == HoACDebugCategory.Semantics
            ? HoACQueryDescriptor.Resolve(HoACQueryKind.Semantic, HoSemanticSchema.Declarations[0].name, 0, HoACMaskDomain.Screen)
            : HoACQueryDescriptor.Resolve(HoACQueryKind.Group, null, 1, HoACMaskDomain.Screen);

        internal static Vector4[] BuildColors(HoACDebugCategory category)
        {
            EnsureCache();
            if (Colors.TryGetValue(category, out var cached)) return cached;
            var colors = new Vector4[256];
            if (category == HoACDebugCategory.IdentityGroups)
            {
                for (uint id = 1; id < 256; id++) colors[id] = GroupColor(id);
            }
            else if (category == HoACDebugCategory.SelectionGroups)
            {
                for (uint id = 1; id <= HoObjectBufferRegistry.SelectionCount; id++)
                    if (HoObjectBufferRegistry.TryGetSelectionRowData(id, out var row)) colors[id] = row.displayColor;
            }
            else if (category == HoACDebugCategory.Semantics)
            {
                foreach (var entry in HoSemanticSchema.Declarations) colors[entry.laneIndex] = entry.debugColor;
            }
            Colors[category] = colors;
            return colors;
        }
    }
}
