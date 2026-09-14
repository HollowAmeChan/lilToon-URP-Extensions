using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.CharacterBuffer
{
    /// <summary>
    /// 角色特化的身份来源：**部件表 + 选择表**（规划 §5.7 / §5.11）。
    /// <list type="bullet">
    /// <item>部件条目回答"这个 renderer 是哪个角色的哪个部件"，索引写进 RSUV（低 16 bit = 角色 8 + 槽位 8）；</item>
    /// <item>选择条目是具名的选区，取代 `custom0~3` 这类匿名通道；</item>
    /// <item>组件是**编辑器期与运行期共用的真值**，但表本身由 <see cref="HoCharacterBufferRegistry"/> 统一编译。</item>
    /// </list>
    /// 注意：RSUV **不会被序列化**（Unity 官方文档原话 "not serialized … resets when the object is reloaded"），
    /// 所以每次表重建（含 OnEnable / 域重载后）都要重新写一遍。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class HoCharacterBufferGroup : MonoBehaviour
    {
        private static readonly List<HoCharacterBufferGroup> ActiveGroups = new List<HoCharacterBufferGroup>();
        private static readonly Dictionary<Renderer, Assignment> Assignments = new Dictionary<Renderer, Assignment>();
        private static readonly HashSet<Renderer> WarnedRenderers = new HashSet<Renderer>();

        [InspectorName("优先级")]
        [Tooltip("同一个 Renderer 被多个 Group 命中时，优先级高者胜出；相同则离 Renderer 最近的组胜出。")]
        public int priority;

        [InspectorName("角色 ID (1-255)")]
        [Tooltip("角色 0 保留：ID 0 表示“未注册/未知”，与 RSUV 被重置后的值重合。")]
        [Range(1, 255)]
        public int characterId = 1;

        [InspectorName("角色级标签")]
        [Tooltip("放在角色表那一行，用于“整角色”语义（例如 CharacterFull），不必在每个部件行重复。")]
        public HoCharacterBufferPartTags characterTags = HoCharacterBufferPartTags.None;

        [InspectorName("部件")]
        public List<HoCharacterBufferPartEntry> parts = new List<HoCharacterBufferPartEntry>();

        [InspectorName("选择")]
        [Tooltip("具名的选区（规划 §5.11）。名字全局唯一；材质侧只能引用这里的名字。")]
        public List<HoCharacterBufferSelectionEntry> selections = new List<HoCharacterBufferSelectionEntry>();

        private readonly Dictionary<Renderer, int> localSlotByRenderer = new Dictionary<Renderer, int>();
        private readonly List<string> partNameCache = new List<string>();
        private readonly List<string> selectionNameCache = new List<string>();

        private void OnEnable()
        {
            if (!ActiveGroups.Contains(this))
            {
                ActiveGroups.Add(this);
            }

            HoCharacterBufferRegistry.Register(this);
        }

        private void OnDisable()
        {
            ActiveGroups.Remove(this);
            HoCharacterBufferRegistry.Unregister(this);
        }

        private void OnValidate()
        {
            characterId = Mathf.Clamp(characterId, 1, HoCharacterBufferPaletteLimits.MaxCharacters - 1);
            HoCharacterBufferRegistry.MarkDirty();
        }

        /// <summary>部件名列表（按槽位顺序）；注册表按这个顺序分配槽位号。</summary>
        public IReadOnlyList<string> GetPartNames()
        {
            partNameCache.Clear();
            for (int i = 0; i < parts.Count; i++)
            {
                HoCharacterBufferPartEntry part = parts[i];
                if (part == null || string.IsNullOrEmpty(part.name))
                {
                    continue;
                }

                if (partNameCache.Contains(part.name))
                {
                    // 角色内重名会让两个部件抢同一个槽位，必须点名而不是静默跳过。
                    Debug.LogWarning($"[Ho-CharacterBuffer] '{name}' 的部件名 '{part.name}' 重复，后一个被跳过。", this);
                    continue;
                }

                partNameCache.Add(part.name);
            }

            return partNameCache;
        }

        public IReadOnlyList<string> GetSelectionNames()
        {
            selectionNameCache.Clear();
            for (int i = 0; i < selections.Count; i++)
            {
                HoCharacterBufferSelectionEntry selection = selections[i];
                if (selection == null || string.IsNullOrEmpty(selection.name))
                {
                    continue;
                }

                selectionNameCache.Add(selection.name);
            }

            return selectionNameCache;
        }

        /// <summary>把第 <paramref name="slot"/> 个部件条目编成 palette 行（partId 由注册表填）。</summary>
        public HoCharacterPartData BuildPartRow(int slot, string partName)
        {
            HoCharacterBufferPartEntry entry = FindPart(partName);
            var row = new HoCharacterPartData
            {
                nameHash = HoCharacterBufferHash.ComputePart(characterId, partName),
                category = (uint)(entry != null ? entry.category : HoCharacterBufferPartCategory.Unspecified),
                tags = (uint)(entry != null ? entry.tags : HoCharacterBufferPartTags.None),
                thickness = entry != null ? entry.thickness : 1f,
                curvature = entry != null ? entry.curvature : 0f,
                transmittance = entry != null ? entry.transmittance : 0f,
                roughness = entry != null ? entry.roughness : 0.5f,
                metallic = entry != null ? entry.metallic : 0f,
                reflectance = entry != null ? entry.reflectance : 0.04f,
                plrStrength = entry != null ? entry.plrStrength : 1f,
                materialClass = (uint)Mathf.Max(0, entry != null ? entry.materialClass : 0),
                displayColor = entry != null ? (Vector4)entry.displayColor : new Vector4(0.75f, 0.75f, 0.75f, 1f)
            };

            return row;
        }

        public HoCharacterSelectionData BuildSelectionRow(int index, string selectionName)
        {
            HoCharacterBufferSelectionEntry entry = FindSelection(selectionName);
            return new HoCharacterSelectionData
            {
                nameHash = HoCharacterBufferHash.Compute(selectionName),
                tags = (uint)(entry != null ? entry.tags : HoCharacterBufferPartTags.None),
                displayColor = entry != null ? (Vector4)entry.displayColor : new Vector4(0.2f, 0.6f, 1f, 1f)
            };
        }

        /// <summary>把本组胜出的 renderer 的 RSUV 写成部件 ID。注册表在表重建后统一调用。</summary>
        public void ApplyIdentity()
        {
            localSlotByRenderer.Clear();
            IReadOnlyList<string> partNames = GetPartNames();
            for (int slot = 0; slot < partNames.Count; slot++)
            {
                HoCharacterBufferPartEntry entry = FindPart(partNames[slot]);
                if (entry == null)
                {
                    continue;
                }

                CollectRenderers(entry, renderer =>
                {
                    if (!localSlotByRenderer.ContainsKey(renderer))
                    {
                        localSlotByRenderer[renderer] = slot;
                    }
                });
            }

            foreach (KeyValuePair<Renderer, int> pair in localSlotByRenderer)
            {
                if (Assignments.TryGetValue(pair.Key, out Assignment assignment) && assignment.group != this)
                {
                    continue;
                }

                uint partId = HoCharacterBufferRegistry.MakePartId(characterId, pair.Value);
                if (!TrySetRendererUserValue(pair.Key, partId))
                {
                    WarnUnsupportedRenderer(pair.Key);
                }
            }
        }

        /// <summary>
        /// 跨组冲突解决：一个 renderer 只属于一个部件。优先级相同则取层级距离更近的组。
        /// 由注册表在重建表之前调用一次。
        /// </summary>
        public static void ResolveAssignments()
        {
            Assignments.Clear();
            for (int groupIndex = 0; groupIndex < ActiveGroups.Count; groupIndex++)
            {
                HoCharacterBufferGroup group = ActiveGroups[groupIndex];
                IReadOnlyList<string> partNames = group.GetPartNames();
                for (int slot = 0; slot < partNames.Count; slot++)
                {
                    HoCharacterBufferPartEntry entry = group.FindPart(partNames[slot]);
                    if (entry == null)
                    {
                        continue;
                    }

                    group.CollectRenderers(entry, renderer =>
                    {
                        int distance = group.GetHierarchyDistance(renderer.transform);
                        var candidate = new Assignment(group, slot, group.priority, distance);
                        if (!Assignments.TryGetValue(renderer, out Assignment existing) || candidate.IsHigherPriorityThan(existing))
                        {
                            Assignments[renderer] = candidate;
                        }
                    });
                }
            }
        }

        private void CollectRenderers(HoCharacterBufferPartEntry entry, System.Action<Renderer> visit)
        {
            if (entry?.renderers == null)
            {
                return;
            }

            for (int i = 0; i < entry.renderers.Length; i++)
            {
                UnityEngine.Object target = entry.renderers[i];
                if (target == null)
                {
                    continue;
                }

                if (target is Renderer renderer)
                {
                    visit(renderer);
                    if (entry.includeChildren)
                    {
                        VisitChildren(renderer.transform, visit);
                    }

                    continue;
                }

                if (target is GameObject gameObject)
                {
                    VisitChildren(gameObject.transform, visit);
                }
            }
        }

        private static void VisitChildren(Transform root, System.Action<Renderer> visit)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.TryGetComponent(out Renderer renderer))
                {
                    visit(renderer);
                }

                VisitChildren(child, visit);
            }
        }

        private HoCharacterBufferPartEntry FindPart(string partName)
        {
            if (parts == null)
            {
                return null;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null && parts[i].name == partName)
                {
                    return parts[i];
                }
            }

            return null;
        }

        private HoCharacterBufferSelectionEntry FindSelection(string selectionName)
        {
            if (selections == null)
            {
                return null;
            }

            for (int i = 0; i < selections.Count; i++)
            {
                if (selections[i] != null && selections[i].name == selectionName)
                {
                    return selections[i];
                }
            }

            return null;
        }

        private int GetHierarchyDistance(Transform target)
        {
            int distance = 0;
            Transform current = target;
            while (current != null)
            {
                if (current == transform)
                {
                    return distance;
                }

                current = current.parent;
                distance++;
            }

            return int.MaxValue;
        }

        private void WarnUnsupportedRenderer(Renderer renderer)
        {
            if (!WarnedRenderers.Add(renderer))
            {
                return;
            }

            Debug.LogWarning($"[Ho-CharacterBuffer] '{renderer.name}'（{renderer.GetType().Name}）拿不到 RSUV，" +
                             "这个部件不会写进 ID pass。角色部件请使用 MeshRenderer / SkinnedMeshRenderer。", renderer);
        }

        private static bool TrySetRendererUserValue(Renderer targetRenderer, uint value)
        {
            // RSUV 是**按具体类型**暴露的 API，不在 Renderer 基类上（Unity 官方文档逐个点名了 5 个类型）。
            // TODO(P1)：SpriteRenderer 走 SpriteRendererDataAccessExtensions.SetShaderUserValue，
            // SpriteShapeRenderer / TilemapRenderer 各自的 API 需在编辑器里核对命名空间后补全。
            switch (targetRenderer)
            {
                case MeshRenderer meshRenderer:
                    meshRenderer.SetShaderUserValue(value);
                    return true;
                case SkinnedMeshRenderer skinnedMeshRenderer:
                    skinnedMeshRenderer.SetShaderUserValue(value);
                    return true;
                default:
                    return false;
            }
        }

        private readonly struct Assignment
        {
            public readonly HoCharacterBufferGroup group;
            public readonly int slot;
            private readonly int priority;
            private readonly int distance;

            public Assignment(HoCharacterBufferGroup group, int slot, int priority, int distance)
            {
                this.group = group;
                this.slot = slot;
                this.priority = priority;
                this.distance = distance;
            }

            public bool IsHigherPriorityThan(Assignment other)
            {
                if (priority != other.priority)
                {
                    return priority > other.priority;
                }

                return distance < other.distance;
            }
        }
    }
}
