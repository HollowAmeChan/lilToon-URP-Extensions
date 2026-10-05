using System;
using UnityEngine;

namespace lilToon.URP.Extensions.GeometryData
{
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Ho-GeometryData Tension")]
    public sealed class HoGeometryDataTension : MonoBehaviour
    {
        // The Editor supplies imported mesh access without changing the model's Read/Write flag.
        // Player builds use the serialized reference cache or a readable source mesh.
        public static Func<Mesh, Action<Mesh>, bool> EditorPrepareUnreadableMesh { get; set; }
        [InspectorName("目标渲染器")] public SkinnedMeshRenderer targetRenderer;
        [InspectorName("初始化时准备")] public bool prepareOnInitialize = true;
        [InspectorName("Play 前准备")] public bool prepareBeforePlay = true;
        [Min(0), InspectorName("边长权重")] public float edgeWeight = 1;
        [Min(0), InspectorName("面积权重")] public float areaWeight = 0.5f;
        [Min(0), InspectorName("角变化权重")] public float angularWeight = 1;
        [Range(0, 3), InspectorName("WS 加权平均次数")]
        [Tooltip("沿拓扑一环，以当前世界空间距离的倒数加权。0 关闭，最多 3 次；不跨独立模型寻找邻居。")]
        public int smoothingIterations = 1;
        [Range(0, 1), InspectorName("平均结果 Lerp")]
        [Tooltip("完成所有平均后，与原始数据混合。0 为原始数据，1 为完整平均结果。")]
        public float smoothingBlend = 1;
        [SerializeField, HideInInspector] private Mesh preparedMesh;
        [SerializeField, HideInInspector] private Vector4[] restPositions, restMetrics;
        [SerializeField, HideInInspector] private int[] triangles, neighborOffsets, neighbors, cornerOffsets, corners;
        [SerializeField, HideInInspector] private int referenceVersion;
        [SerializeField, HideInInspector] private float lengthEpsilon, areaEpsilon;
        [NonSerialized] private string status;
        [NonSerialized] private bool bindingDirty;
        internal HoTensionResources Resources;
        internal int ProducedFrame = -1;
        internal Vector3 ProducedWeights;
        internal int ProducedSmoothingIterations;
        internal float ProducedSmoothingBlend;
        public int ProductionCount { get; internal set; }
        public int LastProducedFrame => ProducedFrame;
        public int ReferenceVersion => referenceVersion;
        public int VertexCount => restPositions?.Length ?? 0;
        public int TriangleCount => restMetrics?.Length ?? 0;
        public Mesh SourceMesh => targetRenderer != null ? targetRenderer.sharedMesh : null;
        public bool HasData => preparedMesh != null && SourceMesh == preparedMesh && VertexCount == preparedMesh.vertexCount
            && TriangleCount > 0 && triangles?.Length == TriangleCount * 3 && neighbors != null && corners != null
            && neighborOffsets?.Length == VertexCount + 1 && cornerOffsets?.Length == VertexCount + 1;
        public string Status
        {
            get
            {
                if (!isActiveAndEnabled) return "组件停用。";
                if (targetRenderer == null || SourceMesh == null) return "未提供目标 SkinnedMeshRenderer / Mesh。";
                if (preparedMesh != null && !HasData) return "参考缓存与当前 Mesh 不一致，请重新准备。";
                if (HasData && targetRenderer.rootBone == null) return "当前 GPU 来源需要指定 rootBone。";
                return status ?? (HasData ? "参考状态已准备；等待 GPU 来源。" : "未准备参考状态。");
            }
        }
        internal Vector3 Weights => new Vector3(SafeWeight(edgeWeight), SafeWeight(areaWeight), SafeWeight(angularWeight));
        internal int SmoothingIterations => Mathf.Clamp(smoothingIterations, 0, 3);
        internal float SmoothingBlend => float.IsNaN(smoothingBlend) || float.IsInfinity(smoothingBlend) ? 0 : Mathf.Clamp01(smoothingBlend);
        private static float SafeWeight(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);

        private void OnEnable()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<SkinnedMeshRenderer>();
            HoGeometrySkinnedSource.Prepare(targetRenderer);
            if (prepareOnInitialize && !HasData) Prepare();
            HoTensionDataRegistry.Register(this);
            bindingDirty = false;
        }
        private void OnDisable() { HoTensionDataRegistry.Unregister(this); ReleaseGpu(); }
        private void OnValidate()
        {
            ProducedFrame = -1;
            bindingDirty = true; HoTensionDataRegistry.MarkDirty();
        }
        private void Update()
        {
            // Do not call native Renderer APIs from OnValidate (which may execute on the loading thread).
            if (!bindingDirty) return;
            bindingDirty = false; HoTensionDataRegistry.Register(this);
        }

        /// <summary>Explicitly rebuild topology and reference metrics from the source Mesh's rest pose.</summary>
        public bool Prepare()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<SkinnedMeshRenderer>();
            try
            {
                Mesh source = SourceMesh;
                HoTensionTopology prepared = null;
                if (source != null && !source.isReadable && EditorPrepareUnreadableMesh != null)
                {
                    if (!EditorPrepareUnreadableMesh(source, readable => prepared = HoTensionTopology.Build(readable)) || prepared == null)
                        throw new InvalidOperationException("未能读取导入 Mesh 的参考数据。");
                }
                else prepared = HoTensionTopology.Build(source);
                ReleaseGpu();
                preparedMesh = SourceMesh; restPositions = prepared.positions; restMetrics = prepared.metrics;
                triangles = prepared.triangles; neighborOffsets = prepared.neighborOffsets; neighbors = prepared.neighbors;
                cornerOffsets = prepared.cornerOffsets; corners = prepared.corners;
                lengthEpsilon = prepared.lengthEpsilon; areaEpsilon = prepared.areaEpsilon;
                referenceVersion++;
                HoGeometrySkinnedSource.Prepare(targetRenderer);
                status = $"已准备：{VertexCount} 顶点 / {TriangleCount} 三角面，参考版本 {referenceVersion}";
                if (isActiveAndEnabled) HoTensionDataRegistry.Register(this); else HoTensionDataRegistry.MarkDirty();
                bindingDirty = false;
                return true;
            }
            catch (Exception exception)
            {
                // An explicitly failed rebuild invalidates the previous reference, so it cannot be consumed silently.
                preparedMesh = null; ReleaseGpu(); status = exception.Message;
                if (isActiveAndEnabled) HoTensionDataRegistry.Register(this); else HoTensionDataRegistry.MarkDirty();
                return false;
            }
        }
        [ContextMenu("生成 / 更新张力参考状态")]
        private void PrepareContext() => Prepare();
        internal void SetStatus(string value) => status = value;
        internal HoTensionResources EnsureResources()
        {
            if (Resources == null)
                Resources = new HoTensionResources(restPositions, restMetrics, triangles, neighborOffsets, neighbors, cornerOffsets, corners, lengthEpsilon, areaEpsilon);
            return Resources;
        }
        internal void ReleaseGpu() { Resources?.Dispose(); Resources = null; ProducedFrame = -1; }
    }
}
