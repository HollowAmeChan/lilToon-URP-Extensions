using System;
using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.MetadataBuffer
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class HoMetadataBufferGroup : MonoBehaviour
    {
        private static readonly List<HoMetadataBufferGroup> ActiveGroups = new List<HoMetadataBufferGroup>();
        private static readonly Dictionary<Renderer, Assignment> Assignments = new Dictionary<Renderer, Assignment>();
        private static readonly List<Renderer> AssignedRenderers = new List<Renderer>();
        private static readonly List<Renderer> RendererCache = new List<Renderer>();
        private static readonly List<HoMetadataBufferGroup> GroupCache = new List<HoMetadataBufferGroup>();
        private static readonly List<HoMetadataBufferSubject> SubjectCache = new List<HoMetadataBufferSubject>();
        private static MaterialPropertyBlock clearPropertyBlock;

        [InspectorName("优先级")]
        [Tooltip("同一个 Renderer 被多个 HoMetadataBufferGroup 命中时，优先级高者写入角色组 ID、部件 ID 和标记。优先级相同时，离 Renderer 最近的组优先。")]
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

        [InspectorName("CharacterFull / 全角色")]
        public UnityEngine.Object[] objectCustom0;

        [InspectorName("脸")]
        public UnityEngine.Object[] objectCustom1;

        [InspectorName("前发")]
        public UnityEngine.Object[] objectCustom2;

        [InspectorName("眼睛")]
        public UnityEngine.Object[] objectCustom3;

        [InspectorName("眼透区域")]
        public UnityEngine.Object[] objectCustom4;

        [InspectorName("配件")]
        public UnityEngine.Object[] objectCustom5;

        [InspectorName("CharacterBody / 人体")]
        public UnityEngine.Object[] objectCustom6;

        [InspectorName("预留 7")]
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

        public static void RefreshLoadedScenes()
        {
            ClearSceneRenderers();
            ApplyActiveSubjects();
            RebuildActiveGroupList();
            RebuildAll();
        }

        private static void RebuildAll()
        {
            ClearAssignedRenderers();
            Assignments.Clear();

            for (int i = 0; i < ActiveGroups.Count; i++)
            {
                HoMetadataBufferGroup group = ActiveGroups[i];
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
                clearPropertyBlock.SetFloat(HoMetadataBufferShaderConstants.ObjectCustomMaskId, 0.0f);
                clearPropertyBlock.SetFloat(HoMetadataBufferShaderConstants.GroupIdId, 0.0f);
                clearPropertyBlock.SetFloat(HoMetadataBufferShaderConstants.ObjectIdId, 0.0f);
                clearPropertyBlock.SetFloat(HoMetadataBufferShaderConstants.FlagsId, 0.0f);
                clearPropertyBlock.SetFloat(HoMetadataBufferShaderConstants.RsuvAssignedId, 0.0f);
                targetRenderer.SetPropertyBlock(clearPropertyBlock);
            }

            AssignedRenderers.Clear();
        }

        private static void ClearSceneRenderers()
        {
            RendererCache.Clear();
            RendererCache.AddRange(UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            clearPropertyBlock ??= new MaterialPropertyBlock();

            for (int i = 0; i < RendererCache.Count; i++)
            {
                Renderer targetRenderer = RendererCache[i];
                if (targetRenderer == null || targetRenderer.gameObject.scene.IsValid() == false)
                {
                    continue;
                }

                TrySetRendererUserValue(targetRenderer, 0);
                targetRenderer.GetPropertyBlock(clearPropertyBlock);
                HoMetadataBufferSubject.ClearProperties(clearPropertyBlock);
                clearPropertyBlock.SetFloat(HoMetadataBufferShaderConstants.ObjectCustomMaskId, 0.0f);
                clearPropertyBlock.SetFloat(HoMetadataBufferShaderConstants.RsuvAssignedId, 0.0f);
                targetRenderer.SetPropertyBlock(clearPropertyBlock);
            }

            RendererCache.Clear();
            AssignedRenderers.Clear();
        }

        private static void ApplyActiveSubjects()
        {
            SubjectCache.Clear();
            SubjectCache.AddRange(UnityEngine.Object.FindObjectsByType<HoMetadataBufferSubject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            for (int i = 0; i < SubjectCache.Count; i++)
            {
                HoMetadataBufferSubject subject = SubjectCache[i];
                if (subject != null && subject.isActiveAndEnabled)
                {
                    subject.ApplyToRenderers();
                }
            }

            SubjectCache.Clear();
        }

        private static void RebuildActiveGroupList()
        {
            ActiveGroups.Clear();
            GroupCache.Clear();
            GroupCache.AddRange(UnityEngine.Object.FindObjectsByType<HoMetadataBufferGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            for (int i = 0; i < GroupCache.Count; i++)
            {
                HoMetadataBufferGroup group = GroupCache[i];
                if (group != null && group.isActiveAndEnabled)
                {
                    ActiveGroups.Add(group);
                }
            }

            GroupCache.Clear();
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
            if (entries == null || bitIndex < 0 || bitIndex >= HoMetadataBufferObjectChannels.DefaultCount)
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
            TrySetRendererUserValue(targetRenderer, packed);

            propertyBlock ??= new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(HoMetadataBufferShaderConstants.ObjectCustomMaskId, objectCustomMask);
            propertyBlock.SetFloat(HoMetadataBufferShaderConstants.GroupIdId, characterId);
            propertyBlock.SetFloat(HoMetadataBufferShaderConstants.ObjectIdId, partId);
            propertyBlock.SetFloat(HoMetadataBufferShaderConstants.FlagsId, flags);
            propertyBlock.SetFloat(HoMetadataBufferShaderConstants.RsuvAssignedId, 1.0f);
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
            public readonly HoMetadataBufferGroup group;
            public readonly byte objectCustomMask;
            private readonly int priority;
            private readonly int distance;

            public Assignment(HoMetadataBufferGroup group, byte objectCustomMask, int priority, int distance)
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
