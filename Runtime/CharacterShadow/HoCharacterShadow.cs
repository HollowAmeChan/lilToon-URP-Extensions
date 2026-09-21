using System.Collections.Generic;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEngine;

namespace lilToon.URP.Extensions.CharacterShadow
{
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Ho-CharacterShadow")]
    public sealed class HoCharacterShadow : MonoBehaviour
    {
        internal static readonly List<HoCharacterShadow> Active = new List<HoCharacterShadow>();

        [InspectorName("接收组"), Tooltip("接收高精度天光投影的 OB 组。CS 不改写对象身份或材质；一个 OB 组只能有一个 CS 组件。")]
        public HoObjectBufferGroup objectGroup;
        [InspectorName("接收部件"), Tooltip("留空接收整组；否则只接收这里列出的 OB 部件名（名字必须与组里的部件名一致）。")]
        public List<string> receiverParts = new List<string>();
        [InspectorName("包围盒锚点"), Tooltip("留空时用本物体。Center/Size 在该锚点的本地空间里解释，场景手柄直接编辑这个盒。")]
        public Transform boundsAnchor;
        [InspectorName("中心"), Tooltip("接收盒在锚点本地空间的中心。")]
        public Vector3 center = new Vector3(0, 1, 0);
        [InspectorName("尺寸"), Tooltip("接收盒在锚点本地空间的尺寸。必须覆盖角色的动作范围；自动计算是一次性工具，不会每帧跟随蒙皮。")]
        public Vector3 size = new Vector3(2, 2.5f, 2);
        [InspectorName("边缘回退"), Range(0, 0.25f), Tooltip("包围盒边缘回退普通天光投影的比例，用来软化盒边界的接缝。")]
        public float edgeBlend = 0.05f;

        public Transform Anchor => boundsAnchor != null ? boundsAnchor : transform;
        public Bounds LocalBounds => new Bounds(center, size);
        [System.NonSerialized] public string status = "等待 CS Renderer Feature";
        [System.NonSerialized] public int atlasSlice = -1;
        [System.NonSerialized] public float depthRange;
        [System.NonSerialized] public float worldUnitsPerTexel;

        private void Reset() { objectGroup = GetComponentInParent<HoObjectBufferGroup>(); }
        private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        private void OnDisable() { Active.Remove(this); atlasSlice = -1; status = "已停用"; }
        private void OnValidate()
        {
            size = Vector3.Max(size, Vector3.one * 0.01f);
            edgeBlend = Mathf.Clamp(edgeBlend, 0, 0.25f);
        }

        public void GetWorldCorners(Vector3[] corners)
        {
            Vector3 half = size * 0.5f;
            for (int i = 0; i < 8; i++)
                corners[i] = Anchor.TransformPoint(center + Vector3.Scale(half,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
        }

        private void OnDrawGizmosSelected()
        {
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = Anchor.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 0.85f, 1, 1);
            Gizmos.DrawWireCube(center, size);
            Gizmos.matrix = previous;
        }
    }
}
