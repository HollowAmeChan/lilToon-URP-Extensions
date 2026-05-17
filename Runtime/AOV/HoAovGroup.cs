using System;
using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.AOV
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class HoAovGroup : MonoBehaviour
    {
        private static readonly List<HoAovGroup> ActiveGroups = new List<HoAovGroup>();
        private static readonly Dictionary<Renderer, Assignment> Assignments = new Dictionary<Renderer, Assignment>();
        private static readonly List<Renderer> AssignedRenderers = new List<Renderer>();
        private static readonly List<Renderer> RendererCache = new List<Renderer>();
        private static MaterialPropertyBlock clearPropertyBlock;

        [InspectorName("优先级")]
        [Tooltip("同一个 Renderer 被多个 HoAovGroup 命中时，优先级高者写入角色组 ID、部件 ID 和标记。优先级相同时，离 Renderer 最近的组优先。")]
        public int priority;

        [InspectorName("角色组 ID (CharacterId)")]
        [Range(0, 255)]
        public int characterId;

        [InspectorName("部件 ID (PartId)")]
        [Range(0, 255)]
        public int partId;

        [InspectorName("标记 (Flags)")]
        [Range(0, 255)]
        public int flags;

        [InspectorName("展开预制件")]
        [Tooltip("拖入 GameObject 或预制件实例时，包含它下面的子级 Renderer。关闭时只使用物体自身的 Renderer。")]
        public bool includeChildrenForListedObjects = true;

        [InspectorName("ObjectCustom0 主体")]
        public UnityEngine.Object[] objectCustom0;

        [InspectorName("ObjectCustom1 脸")]
        public UnityEngine.Object[] objectCustom1;

        [InspectorName("ObjectCustom2 前发")]
        public UnityEngine.Object[] objectCustom2;

        [InspectorName("ObjectCustom3 眼睛")]
        public UnityEngine.Object[] objectCustom3;

        [InspectorName("ObjectCustom4 眼透区域")]
        public UnityEngine.Object[] objectCustom4;

        [InspectorName("ObjectCustom5 配件")]
        public UnityEngine.Object[] objectCustom5;

        [InspectorName("ObjectCustom6 预留")]
        public UnityEngine.Object[] objectCustom6;

        [InspectorName("ObjectCustom7 预留")]
        public UnityEngine.Object[] objectCustom7;

        [InspectorName("仅写 ID")]
        [Tooltip("这些对象命中的 Renderer 只写入角色组 ID、部件 ID 和标记；ObjectCustom 位保持为 0。")]
        public UnityEngine.Object[] explicitRenderers;

        private readonly Dictionary<Renderer, byte> localMasks = new Dictionary<Renderer, byte>();
        private MaterialPropertyBlock propertyBlock;

        private void OnEnable()
        {
            if (!ActiveGroups.Contains(this))
            {
                ActiveGroups.Add(this);
            }

            RebuildAll();
        }

        private void OnDisable()
        {
            ActiveGroups.Remove(this);
            RebuildAll();
        }

        private void OnDestroy()
        {
            ActiveGroups.Remove(this);
            RebuildAll();
        }

        private void OnValidate()
        {
            characterId = Mathf.Clamp(characterId, 0, 255);
            partId = Mathf.Clamp(partId, 0, 255);
            flags = Mathf.Clamp(flags, 0, 255);
            if (isActiveAndEnabled && !ActiveGroups.Contains(this))
            {
                ActiveGroups.Add(this);
            }

            RebuildAll();
        }

        public static uint PackRendererUserValue(byte objectCustomMask, int characterId, int partId, int flags)
        {
            uint packed = objectCustomMask;
            packed |= ((uint)Mathf.Clamp(characterId, 0, 255)) << 8;
            packed |= ((uint)Mathf.Clamp(partId, 0, 255)) << 16;
            packed |= ((uint)Mathf.Clamp(flags, 0, 255)) << 24;
            return packed;
        }

        public void Apply()
        {
            RebuildAll();
        }

        private static void RebuildAll()
        {
            ClearAssignedRenderers();
            Assignments.Clear();

            for (int i = 0; i < ActiveGroups.Count; i++)
            {
                HoAovGroup group = ActiveGroups[i];
                if (group == null || !group.isActiveAndEnabled)
                {
                    continue;
                }

                group.CollectLocalMasks();
                group.AddAssignments();
            }

            foreach (KeyValuePair<Renderer, Assignment> pair in Assignments)
            {
                Renderer targetRenderer = pair.Key;
                if (targetRenderer == null)
                {
                    continue;
                }

                pair.Value.group.WriteRendererValue(targetRenderer, pair.Value.objectCustomMask);
                AssignedRenderers.Add(targetRenderer);
            }
        }

        private static void ClearAssignedRenderers()
        {
            for (int i = 0; i < AssignedRenderers.Count; i++)
            {
                Renderer targetRenderer = AssignedRenderers[i];
                if (targetRenderer == null)
                {
                    continue;
                }

                TrySetRendererUserValue(targetRenderer, 0);
                clearPropertyBlock ??= new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(clearPropertyBlock);
                clearPropertyBlock.SetFloat(HoAovShaderConstants.ObjectCustomMaskId, 0.0f);
                clearPropertyBlock.SetFloat(HoAovShaderConstants.GroupIdId, 0.0f);
                clearPropertyBlock.SetFloat(HoAovShaderConstants.ObjectIdId, 0.0f);
                clearPropertyBlock.SetFloat(HoAovShaderConstants.FlagsId, 0.0f);
                targetRenderer.SetPropertyBlock(clearPropertyBlock);
            }

            AssignedRenderers.Clear();
        }

        private void CollectLocalMasks()
        {
            localMasks.Clear();
            AddMaskEntries(objectCustom0, 0);
            AddMaskEntries(objectCustom1, 1);
            AddMaskEntries(objectCustom2, 2);
            AddMaskEntries(objectCustom3, 3);
            AddMaskEntries(objectCustom4, 4);
            AddMaskEntries(objectCustom5, 5);
            AddMaskEntries(objectCustom6, 6);
            AddMaskEntries(objectCustom7, 7);
            AddIdOnlyEntries(explicitRenderers);
        }

        private void AddMaskEntries(UnityEngine.Object[] entries, int bitIndex)
        {
            if (entries == null || bitIndex < 0 || bitIndex >= HoAovObjectChannels.DefaultCount)
            {
                return;
            }

            byte bit = (byte)(1 << bitIndex);
            for (int i = 0; i < entries.Length; i++)
            {
                AddObjectRenderers(entries[i], bit);
            }
        }

        private void AddIdOnlyEntries(UnityEngine.Object[] entries)
        {
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                AddObjectRenderers(entries[i], 0);
            }
        }

        private void AddObjectRenderers(UnityEngine.Object entry, byte bit)
        {
            if (entry == null)
            {
                return;
            }

            if (entry is Renderer directRenderer)
            {
                AddLocalMask(directRenderer, bit);
                return;
            }

            if (entry is GameObject gameObject)
            {
                if (includeChildrenForListedObjects)
                {
                    gameObject.GetComponentsInChildren(true, RendererCache);
                    for (int i = 0; i < RendererCache.Count; i++)
                    {
                        AddLocalMask(RendererCache[i], bit);
                    }

                    RendererCache.Clear();
                    return;
                }

                if (gameObject.TryGetComponent(out Renderer renderer))
                {
                    AddLocalMask(renderer, bit);
                }
            }
        }

        private void AddLocalMask(Renderer targetRenderer, byte bit)
        {
            if (targetRenderer == null)
            {
                return;
            }

            localMasks.TryGetValue(targetRenderer, out byte currentMask);
            localMasks[targetRenderer] = (byte)(currentMask | bit);
        }

        private void AddAssignments()
        {
            foreach (KeyValuePair<Renderer, byte> pair in localMasks)
            {
                AddAssignment(pair.Key, pair.Value);
            }
        }

        private void AddAssignment(Renderer targetRenderer, byte objectCustomMask)
        {
            if (targetRenderer == null)
            {
                return;
            }

            int distance = GetHierarchyDistance(targetRenderer.transform);
            Assignment candidate = new Assignment(this, objectCustomMask, priority, distance);
            if (!Assignments.TryGetValue(targetRenderer, out Assignment existing) || candidate.IsHigherPriorityThan(existing))
            {
                Assignments[targetRenderer] = candidate;
            }
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

        private void WriteRendererValue(Renderer targetRenderer, byte objectCustomMask)
        {
            uint packed = PackRendererUserValue(objectCustomMask, characterId, partId, flags);
            if (TrySetRendererUserValue(targetRenderer, packed))
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(HoAovShaderConstants.ObjectCustomMaskId, objectCustomMask);
            propertyBlock.SetFloat(HoAovShaderConstants.GroupIdId, characterId);
            propertyBlock.SetFloat(HoAovShaderConstants.ObjectIdId, partId);
            propertyBlock.SetFloat(HoAovShaderConstants.FlagsId, flags);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }

        private static bool TrySetRendererUserValue(Renderer targetRenderer, uint value)
        {
            if (targetRenderer is MeshRenderer meshRenderer)
            {
                meshRenderer.SetShaderUserValue(value);
                return true;
            }

            if (targetRenderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                skinnedMeshRenderer.SetShaderUserValue(value);
                return true;
            }

            return false;
        }

        private readonly struct Assignment
        {
            public readonly HoAovGroup group;
            public readonly byte objectCustomMask;
            private readonly int priority;
            private readonly int distance;

            public Assignment(HoAovGroup group, byte objectCustomMask, int priority, int distance)
            {
                this.group = group;
                this.objectCustomMask = objectCustomMask;
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
