using System.Collections.Generic;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEngine;

namespace lilToon.URP.Extensions.RTShadow
{
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("Rendering/Ho-RTShadow Receiver")]
    public sealed class HoRTShadowReceiver : MonoBehaviour
    {
        internal static readonly List<HoRTShadowReceiver> Active = new();
        [InspectorName("接收组"), Tooltip("OB 组提供稳定表面身份；可以是角色或其他已登记的对象。")]
        public HoObjectBufferGroup objectGroup;
        [InspectorName("接收整组")] public bool receiveWholeGroup = true;
        [HideInInspector] public List<string> receiverParts = new();
        [InspectorName("接收全部请求灯")] public bool receiveAllLights = true;
        [InspectorName("接收灯")] public List<Light> lights = new();
        private void Reset() { objectGroup = GetComponentInParent<HoObjectBufferGroup>(); }
        private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        private void OnDisable() { Active.Remove(this); }
        internal static uint[] Capture(out int count, Light light = null)
        {
            HoObjectBufferRegistry.EnsureBuilt();
            var bits = new uint[2048]; count = 0;
            foreach (var declaration in Active)
            {
                if (declaration == null || !declaration.isActiveAndEnabled) continue;
                if (!declaration.receiveAllLights && (light == null || !declaration.lights.Contains(light))) continue;
                var group = declaration.objectGroup;
                if (group == null || !group.isActiveAndEnabled || !HoObjectBufferRegistry.IsGroupValid(group)) continue;
                foreach (string part in group.GetPartNames())
                {
                    if (!declaration.receiveWholeGroup && !declaration.receiverParts.Contains(part)) continue;
                    uint identity = HoObjectBufferRegistry.GetPartId(group.groupId, part);
                    if (identity == 0 || identity > ushort.MaxValue) continue;
                    uint flag = 1u << (int)(identity & 31); int index = (int)(identity >> 5);
                    if ((bits[index] & flag) == 0) { bits[index] |= flag; count++; }
                }
            }
            return bits;
        }
    }
}
