using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.GeometryData
{
    /// <summary>Draw metadata transport: low16 is OB identity, high16 is a shared GeometryData slot.</summary>
    public static class HoGeometryRendererBinding
    {
        private static readonly Dictionary<Renderer, uint> Values = new Dictionary<Renderer, uint>();
        private static readonly Dictionary<Renderer, ushort> Slots = new Dictionary<Renderer, ushort>();
        private static readonly Dictionary<object, Renderer> Owners = new Dictionary<object, Renderer>();
        private static readonly Stack<ushort> FreeSlots = new Stack<ushort>();
        private static int nextSlot = 1;
        public static int Revision { get; private set; }
        public static int SlotCapacity => nextSlot;
        public static bool SetObjectIdentity(Renderer renderer, uint identity) => Set(renderer, identity & 0xffffu, false);
        internal static uint Acquire(Renderer renderer, object owner)
        {
            if (renderer == null || owner == null) return 0;
            if (Owners.TryGetValue(owner, out var previous) && previous != renderer) Release(owner);
            if (!Slots.TryGetValue(renderer, out ushort slot))
            {
                if (FreeSlots.Count == 0 && nextSlot >= 65536) return 0;
                slot = FreeSlots.Count > 0 ? FreeSlots.Pop() : (ushort)nextSlot++;
                if (!Set(renderer, (uint)slot << 16, true)) { FreeSlots.Push(slot); return 0; }
                Slots.Add(renderer, slot);
                Revision++;
            }
            Owners[owner] = renderer;
            return slot;
        }
        internal static void Release(object owner)
        {
            if (owner == null || !Owners.TryGetValue(owner, out var renderer)) return;
            Owners.Remove(owner);
            foreach (var remaining in Owners.Values) if (ReferenceEquals(remaining, renderer)) return;
            if (Slots.TryGetValue(renderer, out ushort slot))
            {
                Slots.Remove(renderer); FreeSlots.Push(slot); Revision++;
                if (renderer != null) Set(renderer, 0, true); else Values.Remove(renderer);
            }
        }
        public static uint GetManagedValue(Renderer renderer) => renderer != null && Values.TryGetValue(renderer, out uint value) ? value : 0u;
        private static bool Set(Renderer renderer, uint value, bool high)
        {
            if (renderer == null) return false;
            uint previous = GetManagedValue(renderer);
            uint combined = high ? (previous & 0xffffu) | value : (previous & 0xffff0000u) | value;
            if (renderer is MeshRenderer mesh) mesh.SetShaderUserValue(combined);
            else if (renderer is SkinnedMeshRenderer skin) skin.SetShaderUserValue(combined);
            else return false;
            if (combined == 0) Values.Remove(renderer); else Values[renderer] = combined;
            return true;
        }
    }
}
