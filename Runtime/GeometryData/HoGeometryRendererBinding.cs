using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.GeometryData
{
    /// <summary>Draw metadata transport: low16 is OB identity, high16 is the dedicated outline slot.</summary>
    public static class HoGeometryRendererBinding
    {
        private static readonly Dictionary<Renderer, uint> Values = new Dictionary<Renderer, uint>();
        public static bool SetObjectIdentity(Renderer renderer, uint identity) => Set(renderer, identity & 0xffffu, false);
        internal static bool SetOutlineSlot(Renderer renderer, uint slot) => Set(renderer, (slot & 0xffffu) << 16, true);
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
