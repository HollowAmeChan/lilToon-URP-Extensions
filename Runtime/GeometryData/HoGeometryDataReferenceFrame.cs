using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace lilToon.URP.Extensions.GeometryData
{
    // Local-axis choices. The producer has no camera or object-identity state.
    public enum HoGeometryAxis
    {
        [InspectorName("+Y (Up)")] Up = 0,
        [InspectorName("-Y (Down)")] Down = 1,
        [InspectorName("+X (Right)")] Right = 2,
        [InspectorName("-X (Left)")] Left = 3,
        [InspectorName("+Z (Forward)")] Forward = 4,
        [InspectorName("-Z (Backward)")] Backward = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HoGeometryFrameData : IEquatable<HoGeometryFrameData>
    {
        public Vector4 originValid;
        public Vector4 forward;
        public Vector4 right;
        public Vector4 up;
        public bool IsValid => originValid.w > 0.5f;
        public bool Equals(HoGeometryFrameData other) => originValid.Equals(other.originValid)
            && forward.Equals(other.forward) && right.Equals(other.right) && up.Equals(other.up);
    }

    /// <summary>Dynamic object/bone frame. Camera-relative evaluation belongs to its consumer.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Ho-GeometryData ReferenceFrame")]
    public sealed class HoGeometryDataReferenceFrame : MonoBehaviour
    {
        [InspectorName("方向来源（骨骼 Transform）")]
        [Tooltip("提供世界原点与方向，例如 Head 骨骼。它是方向来源，作用对象在下面的 Renderer 列表中指定。")]
        public Transform reference;
        [InspectorName("前轴")] public HoGeometryAxis forwardAxis = HoGeometryAxis.Forward;
        [InspectorName("右轴")] public HoGeometryAxis rightAxis = HoGeometryAxis.Right;
        [InspectorName("上轴")] public HoGeometryAxis upAxis = HoGeometryAxis.Up;
        [InspectorName("作用 Renderer")]
        [Tooltip("扁平列表，支持 SkinnedMeshRenderer 与 MeshRenderer，不需要别名或 OB 引用。空列表只尝试同对象上的 Renderer，不自动扩展整个角色。")]
        public List<Renderer> targetRenderers = new List<Renderer>();
        private void OnEnable() => HoGeometryReferenceFrameRegistry.Register(this);
        private void OnDisable() => HoGeometryReferenceFrameRegistry.Unregister(this);
        public bool TryGetFrame(out HoGeometryFrameData data)
        {
            data = default;
            if (!isActiveAndEnabled) return false;
            return Sample(reference, forwardAxis, rightAxis, upAxis, out data);
        }
        public IEnumerable<Renderer> GetTargetRenderers()
        {
            if (targetRenderers.Count == 0)
            {
                if (TryGetComponent<Renderer>(out var renderer)) yield return renderer;
                yield break;
            }
            foreach (var renderer in targetRenderers) if (renderer != null) yield return renderer;
        }

        public static bool Sample(Transform source, HoGeometryAxis forwardAxis,
            HoGeometryAxis rightAxis, HoGeometryAxis upAxis, out HoGeometryFrameData data)
        {
            data = default;
            if (source == null) return false;
            Vector3 position = source.position;
            data.originValid = new Vector4(position.x, position.y, position.z, 1);
            data.forward = Axis(source, forwardAxis).normalized;
            data.right = Axis(source, rightAxis).normalized;
            data.up = Axis(source, upAxis).normalized;
            return true;
        }

        private static Vector3 Axis(Transform source, HoGeometryAxis axis)
        {
            switch (axis)
            {
                case HoGeometryAxis.Up: return source.up;
                case HoGeometryAxis.Down: return -source.up;
                case HoGeometryAxis.Right: return source.right;
                case HoGeometryAxis.Left: return -source.right;
                case HoGeometryAxis.Forward: return source.forward;
                default: return -source.forward;
            }
        }
    }
}
