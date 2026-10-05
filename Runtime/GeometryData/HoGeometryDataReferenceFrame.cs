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

    [Serializable]
    public sealed class HoGeometryPartFrame
    {
        public string partName;
        public Transform reference;
    }

    /// <summary>Dynamic object/bone frame. Camera-relative evaluation belongs to its consumer.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Ho-GeometryData ReferenceFrame")]
    public sealed class HoGeometryDataReferenceFrame : MonoBehaviour
    {
        [InspectorName("参考朝向")]
        [Tooltip("提供世界原点与方向的骨骼或空物体。留空表示不提供参考系。")]
        public Transform reference;
        [InspectorName("前轴")] public HoGeometryAxis forwardAxis = HoGeometryAxis.Forward;
        [InspectorName("右轴")] public HoGeometryAxis rightAxis = HoGeometryAxis.Right;
        [InspectorName("上轴")] public HoGeometryAxis upAxis = HoGeometryAxis.Up;
        [InspectorName("部件参考系")]
        [Tooltip("按部件名提供独立参考；未指定参考的部件使用本组件的默认参考系。")]
        public List<HoGeometryPartFrame> parts = new List<HoGeometryPartFrame>();

        public bool TryGetFrame(out HoGeometryFrameData data) => TryGetFrame(null, out data);
        public bool TryGetFrame(string partName, out HoGeometryFrameData data)
        {
            data = default;
            if (!isActiveAndEnabled) return false;
            Transform source = reference;
            if (!string.IsNullOrEmpty(partName))
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    HoGeometryPartFrame part = parts[i];
                    if (part != null && part.partName == partName)
                    {
                        if (part.reference != null) source = part.reference;
                        break;
                    }
                }
            }
            return Sample(source, forwardAxis, rightAxis, upAxis, out data);
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
