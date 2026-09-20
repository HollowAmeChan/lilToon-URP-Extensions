using System.Collections.Generic;
// HoFaceAxis 眼下还住在 MetadataBuffer 的命名空间里（面部朝向是那边的既有语义，直接复用同一个枚举，
// 免得两套轴向定义各自漂移）。P4 删掉 MetadataBuffer 时把它搬过来即可：枚举按 int 序列化，
// 只要成员顺序不变，迁移不会丢已有场景里的值。
using lilToon.URP.Extensions.MetadataBuffer;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Scripting.APIUpdating;

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
    [AddComponentMenu("Rendering/Ho-ObjectBuffer Group")]
    [MovedFrom(true, "lilToon.URP.Extensions.CharacterBuffer", null, "HoCharacterBufferGroup")]
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

        /// <summary>
        /// 组 ID = 像素里身份的高字节，同时**直接是组表的下标**（256 行）。它由
        /// <see cref="HoObjectBufferRegistry"/> **自动分配**并在编辑器期落盘：新组件默认 0（待分配），
        /// 注册表认领已经写好的号（旧场景手填的值、以及上次分配的结果都保持不动），
        /// 没号或撞车的补到最小可用号。手改它没有意义——两个组件抢同一个号时，注册表会给后来者换号。
        /// </summary>
        [HideInInspector]
        [FormerlySerializedAs("characterId")]
        public int groupId;

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
        // 本组上一次真正写出去的 renderer：下次重建时靠它做差集，把"不再属于本组"的索引收回来。
        private readonly HashSet<Renderer> lastWrittenRenderers = new HashSet<Renderer>();

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
            // 先把自己写出去的索引收回，再退注册：组件被删/被禁用之后，它定义的组就从表里消失了，
            // 而 renderer 上的索引还在——像素里那个 ID 查表落到 unknown 行，画面从"有身份"变成洋红，
            // 看起来就像"删掉的组换了个颜色还在"。索引是我们写的，职责也在这里。
            ClearIdentity();
            ActiveGroups.Remove(this);
            HoObjectBufferRegistry.Unregister(this);
        }

        private void OnValidate()
        {
            // 0 = 待分配（合法值：注册表下一帧给号并落盘）；上限是组表行数。
            groupId = Mathf.Clamp(groupId, 0, HoObjectBufferPaletteLimits.MaxGroups - 1);
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
        public HoObjectPartData BuildPartRow(int slot, string partName)
        {
            HoObjectBufferPartEntry entry = FindPart(partName);
            return new HoObjectPartData
            {
                nameHash = HoObjectBufferHash.ComputePart(groupId, partName),
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

        public HoObjectSelectionData BuildSelectionRow(int index, string selectionName)
        {
            HoObjectBufferSelectionEntry entry = FindSelection(selectionName);
            return new HoObjectSelectionData
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

            // 上次写过索引、这次不再覆盖的 renderer：把索引擦掉。改名 / 删条目 / 把物体拖走都会走到这里，
            // 不擦的话像素里留着一个表里已经没有的 ID —— 查表落到 unknown 行，画面从"有身份"变成洋红，
            // 看起来就像"删掉的东西换了个颜色还在"。索引是我们写的，就得由我们负责收回。
            foreach (Renderer previous in lastWrittenRenderers)
            {
                if (localSlotByRenderer.ContainsKey(previous) || IsOwnedByOtherGroup(previous))
                {
                    continue;
                }

                TrySetRendererUserValue(previous, 0u);
            }

            lastWrittenRenderers.Clear();

            int written = 0;
            int skippedOtherGroup = 0;
            foreach (KeyValuePair<Renderer, int> pair in localSlotByRenderer)
            {
                if (IsOwnedByOtherGroup(pair.Key))
                {
                    skippedOtherGroup++;
                    continue;
                }

                uint partId = HoObjectBufferRegistry.MakePartId(groupId, pair.Value);
                if (!TrySetRendererUserValue(pair.Key, partId))
                {
                    WarnUnsupportedRenderer(pair.Key);
                    continue;
                }

                lastWrittenRenderers.Add(pair.Key);

                // 诊断：把"写给了谁、写了什么 ID"打出来。画面是洋红（unknown 行）时，用它区分
                // "CPU 写错了对象/写了表外的值" 与 "写对了但 shader 读到无效 RSUV"。
                if (written < 5)
                {
                    Debug.Log($"[Ho-ObjectBuffer] RSUV 写入：组 {groupId} 槽 {pair.Value} => 0x{partId:X4} @ {pair.Key.name} ({pair.Key.GetType().Name})");
                }

                written++;
            }

            Debug.Log($"[Ho-ObjectBuffer] RSUV 汇总（组 {groupId} / {name}）：收集 renderer={localSlotByRenderer.Count} 写入={written} 被别组接管={skippedOtherGroup}");
        }

        /// <summary>
        /// 把本组写出去的 RSUV 归零：组件被禁用 / 被删除（它定义的组随之从表里消失），
        /// 或者本组整体失效（组 ID 撞车、组 ID = 0）。**已经被别的组接管的 renderer 不碰**，
        /// 否则会把别人的身份擦掉。
        /// </summary>
        internal void ClearIdentity()
        {
            if (parts != null)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    HoObjectBufferPartEntry entry = parts[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    CollectRenderers(entry, renderer =>
                    {
                        if (!IsOwnedByOtherGroup(renderer))
                        {
                            TrySetRendererUserValue(renderer, 0u);
                        }
                    });
                }
            }

            lastWrittenRenderers.Clear();
        }

        /// <summary>
        /// 这个 renderer 是不是已经被**别人**接管了。
        /// 必须用引用比较：组件被销毁时 Unity 重载的 <c>==</c> 会把"已销毁对象"判成与任何东西都不相等，
        /// 于是"我自己"也会被认成"别的组"，该擦的索引就擦不掉（这个坑在本仓库的贴图表缓存上已经踩过一次）。
        /// </summary>
        private bool IsOwnedByOtherGroup(Renderer renderer)
        {
            return Assignments.TryGetValue(renderer, out Assignment assignment)
                && !ReferenceEquals(assignment.group, this);
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
                if (!HoObjectBufferRegistry.IsGroupValid(group))
                {
                    continue;
                }
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
                        var candidate = new Assignment(group, currentSlot, distance);
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
                    // Inspector 允许直接拖 GameObject。根节点上的 MeshRenderer /
                    // SkinnedMeshRenderer 必须先收集；旧实现只遍历 child，会让正常
                    // 拖入的模型部件完全不写 RSUV，这就是场景调试只剩小块 unknown 的根因。
                    if (gameObject.TryGetComponent(out Renderer rootRenderer))
                    {
                        visit(rootRenderer);
                    }

                    if (entry.includeChildren)
                    {
                        VisitChildren(gameObject.transform, visit);
                    }
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
            private readonly int distance;

            public Assignment(HoObjectBufferGroup group, int slot, int distance)
            {
                this.group = group;
                this.slot = slot;
                this.distance = distance;
            }

            /// <summary>
            /// 裁决只用"离 Renderer 更近者胜"，距离相同再用组 ID 决出确定性结果（组 ID 由注册表分配，
            /// 所以这条比较是稳定的）。原来那套手填优先级已经撤掉：默认全 0 时它本来也没起作用，
            /// 真需要"更上层的组强行接管"时再加回来也不迟。
            /// </summary>
            public bool IsHigherPriorityThan(Assignment other)
            {
                if (distance != other.distance)
                {
                    return distance < other.distance;
                }

                int groupCompare = group != null ? group.groupId : 0;
                int otherCompare = other.group != null ? other.group.groupId : 0;
                return groupCompare < otherCompare;
            }
        }
    }
}
