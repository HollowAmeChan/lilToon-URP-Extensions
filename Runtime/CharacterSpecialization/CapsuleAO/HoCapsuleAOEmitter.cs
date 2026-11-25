using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Ho Capsule AO Emitter")]
    public sealed class HoCapsuleAOEmitter : MonoBehaviour
    {
        internal static readonly HashSet<HoCapsuleAOEmitter> Active = new HashSet<HoCapsuleAOEmitter>();
        [InspectorName("中心")] public Vector3 center;
        [InspectorName("半径"), Min(.001f)] public float radius = .06f;
        [InspectorName("总长度"), Min(.002f)] public float height = .22f;
        [InspectorName("轴"), Tooltip("0=X，1=Y，2=Z；只定义几何，模式与强度由角色特化 Volume 控制。"), Range(0, 2)]
        public int direction = 2;

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        public bool TryGetWorldCapsule(out Vector3 a, out Vector3 b, out float worldRadius)
        {
            a = b = Vector3.zero; worldRadius = 0;
            if (!isActiveAndEnabled || !float.IsFinite(radius) || !float.IsFinite(height)
                || radius <= 0 || height <= 0 || direction < 0 || direction > 2) return false;
            Vector3 localAxis = direction == 0 ? Vector3.right : direction == 1 ? Vector3.up : Vector3.forward;
            Vector3 axis = transform.TransformVector(localAxis);
            float axisScale = axis.magnitude;
            float perpendicularScale = 0;
            for (int i = 0; i < 3; i++)
                if (i != direction)
                    perpendicularScale = Mathf.Max(perpendicularScale,
                        transform.TransformVector(i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward).magnitude);
            Vector3 c = transform.TransformPoint(center);
            worldRadius = radius * perpendicularScale;
            if (!Finite(c) || !Finite(axis) || !float.IsFinite(worldRadius)
                || !float.IsFinite(axisScale) || axisScale <= 1e-6f || worldRadius <= 1e-6f) return false;
            float halfSegment = Mathf.Max(0, height * axisScale * .5f - worldRadius);
            a = c - axis / axisScale * halfSegment;
            b = c + axis / axisScale * halfSegment;
            return Finite(a) && Finite(b);
        }

        internal static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);

        private void OnDrawGizmosSelected()
        {
            if (!TryGetWorldCapsule(out Vector3 a, out Vector3 b, out float r)) return;
            Color previous = Gizmos.color;
            Gizmos.color = new Color(.25f, .85f, 1f, .85f);
            Gizmos.DrawWireSphere(a, r); Gizmos.DrawWireSphere(b, r);
            Gizmos.DrawLine(a, b);
            Gizmos.color = previous;
        }
    }
}
