using System.Collections.Generic;
// HoFaceAxis 眼下还住在 MetadataBuffer 的命名空间里（面部朝向是那边的既有语义，直接复用同一个枚举，
// 免得两套轴向定义各自漂移）。P4 删掉 MetadataBuffer 时把它搬过来即可：枚举按 int 序列化，
// 只要成员顺序不变，迁移不会丢已有场景里的值。
using lilToon.URP.Extensions.MetadataBuffer;
using UnityEngine;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    /// <summary>
    /// 角色特化的身份来源：**部件表 + 选择表**（规划 §5.7 / §5.11）。
    /// <list type="bullet">
    /// <item>部件条目回答"这个 renderer 是哪个角色的哪个部件"，索引写进 RSUV（低 16 bit = 角色 8 + 槽位 8）；</item>
    /// <item>选择条目是具名的选区，取代 `custom0~3` 这类匿名通道；</item>
    /// <item>组件是**编辑器期与运行期共用的真值**，但表本身由 <see cref="HoObjectBufferRegistry"/> 统一编译。</item>
    /// </list>
    /// 注意：RSUV **不会被序列化**（Unity 官方文档原话 "not serialized … resets when the object is reloaded"），
    /// 所以每次表重建（含 OnEnable / 域重载后）都要重新写一遍。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class HoObjectBufferGroup : MonoBehaviour
    {
        private static readonly List<HoObjectBufferGroup> ActiveGroups = new List<HoObjectBufferGroup>();
        private static readonly Dictionary<Renderer, Assignment> Assignments = new Dictionary<Renderer, Assignment>();
        private static readonly HashSet<Renderer> WarnedRenderers = new HashSet<Renderer>();
        private static readonly List<HoObjectBufferConflict> Conflicts = new List<HoObjectBufferConflict>();

        /// <summary>
        /// 上次 <see cref="ResolveAssignments"/> 发现的所有重复指定（一个 Renderer 被多个条目命中）。
        /// 裁决是确定性的，但**必须让人看见**——拖父级展开子级时最容易撞上。
        /// </summary>
        public static IReadOnlyList<HoObjectBufferConflict> GetConflicts()
        {
            return Conflicts;
        }

        [InspectorName("优先级")]
        [Tooltip("同一个 Renderer 被多个 Group 命中时，优先级高者胜出；相同则离 Renderer 最近的组胜出。")]
        public int priority;

        [InspectorName("角色 ID (1-255)")]
        [Tooltip("角色 0 保留：ID 0 表示“未注册/未知”，与 RSUV 被重置后的值重合。")]
        [Range(1, 255)]
        public int characterId = 1;

        [InspectorName("角色级标签")]
        [Tooltip("放在角色表那一行，用于“整角色”语义（例如 CharacterFull），不必在每个部件行重复。")]
        public HoObjectBufferPartTags characterTags = HoObjectBufferPartTags.None;

        [InspectorName("部件")]
        public List<HoObjectBufferPartEntry> parts = new List<HoObjectBufferPartEntry>();

        [InspectorName("选择")]
        [Tooltip("具名的选区（规划 §5.11）。名字全局唯一；材质侧只能引用这里的名字。")]
        public List<HoObjectBufferSelectionEntry> selections = new List<HoObjectBufferSelectionEntry>();

        [InspectorName("面部朝向")]
        [Tooltip("确定角色面部朝向的 Transform——可以是骨骼，也可以是一个朝向正确的空物体。仅供各消费者系统读取（眼透相机角度修正、未来的 SDF 等）；留空表示未提供。以 Transform 的局部轴配合下方三个轴向设置来定义脸前/右/上。")]
        public Transform faceBone;

        [InspectorName("脸前轴")]
        [Tooltip("骨骼的哪个局部轴作为“脸前方”。默认 +Z（Unity 模型常见脸前约定）。若正面/侧面的衰减方向反了，换成 +Z / -Z 试试。")]
        public HoFaceAxis faceForwardAxis = HoFaceAxis.Forward;

        [InspectorName("右轴")]
        [Tooltip("骨骼的哪个局部轴作为“角色右侧（画面左侧）”。默认 +X。")]
        public HoFaceAxis faceRightAxis = HoFaceAxis.Right;

        [InspectorName("上轴")]
        [Tooltip("骨骼的哪个局部轴作为“角色上方”。默认 +Y。俯仰角按此轴分解，若俯视/仰视不生效请检查此项。")]
        public HoFaceAxis faceUpAxis = HoFaceAxis.Up;

        private readonly Dictionary<Renderer, int> localSlotByRenderer = new Dictionary<Renderer, int>();
        private readonly List<string> partNameCache = new List<string>();
        private readonly List<string> selectionNameCache = new List<string>();

        private void OnEnable()
        {
            if (!ActiveGroups.Contains(this))
            {
                ActiveGroups.Add(this);
            }

            HoObjectBufferRegistry.Register(this);
        }

        private void OnDisable()
        {
            ActiveGroups.Remove(this);
            HoObjectBufferRegistry.Unregister(this);
        }

        private void OnValidate()
        {
            characterId = Mathf.Clamp(characterId, 1, HoObjectBufferPaletteLimits.MaxCharacters - 1);
            HoObjectBufferRegistry.MarkDirty();
        }

        /// <summary>活动 group 列表（消费者系统按需遍历）。</summary>
        public static IReadOnlyList<HoObjectBufferGroup> GetActiveGroups()
        {
            return ActiveGroups;
        }

        /// <summary>强制重编译表（编辑器改了条目之后用）。</summary>
        public void Apply()
        {
            HoObjectBufferRegistry.MarkDirty();
            HoObjectBufferRegistry.EnsureBuilt();
        }

        /// <summary>
        /// 重新编译 palette 并把 RSUV 索引写回所有 renderer。
        /// RSUV **不会被序列化**，所以场景重载 / 域重载之后必须能手动刷新一次。
        /// </summary>
        public static void RefreshLoadedScenes()
        {
            HoObjectBufferRegistry.MarkDirty();
            HoObjectBufferRegistry.EnsureBuilt();
        }

        /// <summary>
        /// 提供角色世界朝向（供眼透相机角度修正、SDF 等消费者系统读取）。
        /// 以 <see cref="faceBone"/> 的局部轴按三个轴向配置换算成世界向量；
        /// 未设置朝向时返回 false。不负责相机相关计算，仅输出朝向参考数据。
        /// 与 <c>HoMetadataBufferGroup.TryGetWorldFacing</c> 同形——消费者从 MetadataBuffer 切过来时不用改调用方式。
        /// </summary>
        public bool TryGetWorldFacing(
            out Vector3 position,
            out Vector3 forward,
            out Vector3 right,
            out Vector3 up)
        {
            if (faceBone == null)
            {
                position = Vector3.zero;
                forward = Vector3.zero;
                right = Vector3.zero;
                up = Vector3.zero;
                return false;
            }

            position = faceBone.position;
            forward = GetLocalAxis(faceBone, faceForwardAxis).normalized;
            right = GetLocalAxis(faceBone, faceRightAxis).normalized;
            up = GetLocalAxis(faceBone, faceUpAxis).normalized;
            return true;
        }

        private static Vector3 GetLocalAxis(Transform reference, HoFaceAxis axis)
        {
            switch (axis)
            {
                case HoFaceAxis.Up:
                    return reference.up;
                case HoFaceAxis.Down:
                    return -reference.up;
                case HoFaceAxis.Right:
                    return reference.right;
                case HoFaceAxis.Left:
                    return -reference.right;
                case HoFaceAxis.Forward:
                    return reference.forward;
                default:
                    return -reference.forward;
            }
        }

        /// <summary>部件名列表（按槽位顺序）；注册表按这个顺序分配槽位号。</summary>
        public IReadOnlyList<string> GetPartNames()
        {
            partNameCache.Clear();
            for (int i = 0; i < parts.Count; i++)
            {
                HoObjectBufferPartEntry part = parts[i];
                if (part == null || string.IsNullOrEmpty(part.name))
                {
                    continue;
                }

                if (partNameCache.Contains(part.name))
                {
                    // 角色内重名会让两个部件抢同一个槽位，必须点名而不是静默跳过。
                    Debug.LogWarning($"[Ho-ObjectBuffer] '{name}' 的部件名 '{part.name}' 重复，后一个被跳过。", this);
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
                HoObjectBufferSelectionEntry selection = selections[i];
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
            HoObjectBufferPartEntry entry = FindPart(partName);
            return new HoCharacterPartData
            {
                nameHash = HoObjectBufferHash.ComputePart(characterId, partName),
                category = (uint)(entry != null ? entry.category : HoObjectBufferPartCategory.Unspecified),
                tags = (uint)(entry != null ? entry.tags : HoObjectBufferPartTags.None),
                // 材质数值（thickness / curvature / roughness / metallic / reflectance / plrStrength /
                // materialClass / transmittance）**不由组件提供**：它们在材质里已经填过一遍，
                // 权威归属与写入路径见规划 §5.3。定下来之前这里恒为 0，消费端不得依赖。
                thickness = 0f,
                curvature = 0f,
                transmittance = 0f,
                roughness = 0f,
                metallic = 0f,
                reflectance = 0f,
                plrStrength = 0f,
                materialClass = 0u,
                displayColor = entry != null ? (Vector4)entry.displayColor : new Vector4(0.75f, 0.75f, 0.75f, 1f)
            };
        }

        public HoCharacterSelectionData BuildSelectionRow(int index, string selectionName)
        {
            HoObjectBufferSelectionEntry entry = FindSelection(selectionName);
            return new HoCharacterSelectionData
            {
                nameHash = HoObjectBufferHash.Compute(selectionName),
                tags = (uint)(entry != null ? entry.tags : HoObjectBufferPartTags.None),
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
                HoObjectBufferPartEntry entry = FindPart(partNames[slot]);
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

                uint partId = HoObjectBufferRegistry.MakePartId(characterId, pair.Value);
                if (!TrySetRendererUserValue(pair.Key, partId))
                {
                    WarnUnsupportedRenderer(pair.Key);
                }
            }
        }

        /// <summary>
        /// 跨组冲突解决：一个 renderer 只属于一个部件。优先级相同则取层级距离更近的组；
        /// 同组内两条目命中同一个 renderer 时按**条目顺序**取前者。
        /// 由注册表在重建表之前调用一次；**所有重复都会记进 <see cref="Conflicts"/>**，不静默吞掉。
        /// </summary>
        public static void ResolveAssignments()
        {
            Assignments.Clear();
            Conflicts.Clear();
            for (int groupIndex = 0; groupIndex < ActiveGroups.Count; groupIndex++)
            {
                HoObjectBufferGroup group = ActiveGroups[groupIndex];
                // GetPartNames() 返回的是复用的缓存列表，而冲突记录会再次调它——
                // 这里先拷一份快照，避免"迭代中清空同一个 List"这类自己咬自己的 bug。
                var partNames = new List<string>(group.GetPartNames());
                for (int slot = 0; slot < partNames.Count; slot++)
                {
                    HoObjectBufferPartEntry entry = group.FindPart(partNames[slot]);
                    if (entry == null)
                    {
                        continue;
                    }

                    int currentSlot = slot;
                    string currentPartName = partNames[slot];
                    group.CollectRenderers(entry, renderer =>
                    {
                        int distance = group.GetHierarchyDistance(renderer.transform);
                        var candidate = new Assignment(group, currentSlot, group.priority, distance);
                        if (!Assignments.TryGetValue(renderer, out Assignment existing))
                        {
                            Assignments[renderer] = candidate;
                            return;
                        }

                        // 同一个部件自己命中两次（父级展开 + 显式子级）不算冲突。
                        if (existing.group == candidate.group && existing.slot == candidate.slot)
                        {
                            return;
                        }

                        if (candidate.IsHigherPriorityThan(existing))
                        {
                            Conflicts.Add(MakeConflict(renderer, candidate, existing));
                            Assignments[renderer] = candidate;
                        }
                        else
                        {
                            Conflicts.Add(MakeConflict(renderer, existing, candidate));
                        }
                    });
                }
            }
        }

        private static HoObjectBufferConflict MakeConflict(Renderer renderer, in Assignment winner, in Assignment loser)
        {
            string winnerName = GetPartNameAt(winner.group, winner.slot);
            string loserName = GetPartNameAt(loser.group, loser.slot);
            return new HoObjectBufferConflict(
                renderer,
                winner.group,
                winner.slot,
                winnerName,
                loser.group,
                loser.slot,
                loserName);
        }

        private static string GetPartNameAt(HoObjectBufferGroup group, int slot)
        {
            if (group == null)
            {
                return "(已销毁)";
            }

            var names = new List<string>(group.GetPartNames());
            return slot >= 0 && slot < names.Count ? names[slot] : $"(槽位 {slot})";
        }

        private void CollectRenderers(HoObjectBufferPartEntry entry, System.Action<Renderer> visit)
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

        private HoObjectBufferPartEntry FindPart(string partName)
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

        private HoObjectBufferSelectionEntry FindSelection(string selectionName)
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

            Debug.LogWarning($"[Ho-ObjectBuffer] '{renderer.name}'（{renderer.GetType().Name}）拿不到 RSUV，" +
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
            public readonly HoObjectBufferGroup group;
            public readonly int slot;
            private readonly int priority;
            private readonly int distance;

            public Assignment(HoObjectBufferGroup group, int slot, int priority, int distance)
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
