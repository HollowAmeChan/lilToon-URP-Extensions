using System;
using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.GeometryData
{
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Ho-GeometryData OutlineCorrection")]
    public sealed class HoGeometryDataOutlineCorrection : MonoBehaviour
    {
        [InspectorName("目标渲染器")] public List<Renderer> targetRenderers = new List<Renderer>();
        [InspectorName("初始化时准备")] public bool prepareOnInitialize = true;
        [InspectorName("Play 前准备")] public bool prepareBeforePlay = true;
        [InspectorName("连接相同位置的顶点")]
        [Tooltip("仅连接位置、蒙皮权重和所有形态键位置变化一致的顶点。关闭可保留分离部件。")]
        public bool weldCoincidentVertices = true;
        [Serializable]
        private sealed class PreparedMesh
        {
            public Mesh mesh;
            public Vector4[] data;
        }
        [SerializeField, HideInInspector] private List<PreparedMesh> preparedMeshes = new List<PreparedMesh>();
        [SerializeField, HideInInspector] private bool preparedWeld;
        [NonSerialized] private string status;
        [NonSerialized] private bool bindingDirty;
        public string Status => status ?? (HasData ? "已准备" : "未准备");
        public int TargetCount
        {
            get { int count = 0; foreach (var renderer in GetTargetRenderers()) count++; return count; }
        }
        public int PreparedRendererCount
        {
            get
            {
                int count = 0;
                foreach (var renderer in GetTargetRenderers()) if (GetPreparedData(renderer) != null) count++;
                return count;
            }
        }
        public bool HasData => PreparedRendererCount > 0;

        public IEnumerable<Renderer> GetTargetRenderers()
        {
            if (targetRenderers.Count == 0)
            {
                if (TryGetComponent<Renderer>(out var renderer)) yield return renderer;
                yield break;
            }
            var seen = new HashSet<Renderer>();
            foreach (var renderer in targetRenderers)
                if (renderer != null && seen.Add(renderer)) yield return renderer;
        }

        internal Vector4[] GetPreparedData(Renderer renderer)
        {
            if (renderer == null || renderer.isPartOfStaticBatch || preparedWeld != weldCoincidentVertices) return null;
            Mesh mesh = GetSourceMesh(renderer);
            if (mesh == null) return null;
            foreach (var prepared in preparedMeshes)
                if (prepared.mesh == mesh && prepared.data != null && prepared.data.Length == mesh.vertexCount)
                    return prepared.data;
            return null;
        }

        private static Mesh GetSourceMesh(Renderer renderer) => renderer is SkinnedMeshRenderer skin ? skin.sharedMesh
            : renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;

        private void OnEnable()
        {
            if(prepareOnInitialize && PreparedRendererCount != TargetCount)Prepare();
            HoOutlineDataRegistry.Register(this);
            bindingDirty=false;
        }
        private void OnDisable() => HoOutlineDataRegistry.Unregister(this);
        private void OnValidate()
        {
            bindingDirty=true;HoOutlineDataRegistry.MarkDirty();
        }
        private void Update()
        {
            // OnValidate may run on Unity's loading thread; the native Renderer binding belongs on the main thread.
            if(!bindingDirty)return;
            bindingDirty=false;HoOutlineDataRegistry.Register(this);
        }

        public bool Prepare()
        {
            var results = new List<PreparedMesh>();
            var meshes = new HashSet<Mesh>();
            int count = 0;
            bool success = true;
            string failure = null;
            foreach (var renderer in GetTargetRenderers())
            {
                count++;
                try
                {
                    if (renderer.isPartOfStaticBatch) throw new InvalidOperationException("目标参加了静态合批，无法使用原 Mesh 顶点索引。");
                    Mesh mesh = GetSourceMesh(renderer);
                    if (mesh == null) throw new InvalidOperationException("目标未提供 MeshRenderer / SkinnedMeshRenderer 的 Mesh。");
                    if (!meshes.Add(mesh)) continue;
                    results.Add(new PreparedMesh { mesh = mesh, data = HoGeometryDataOutlineCorrectionBuilder.Build(mesh, weldCoincidentVertices) });
                }
                catch (Exception exception)
                {
                    success = false;
                    if (failure == null) failure = $"{renderer.name}：{exception.Message}";
                }
            }
            preparedMeshes = results;
            preparedWeld = weldCoincidentVertices;
            status = count == 0 ? "未提供目标 Renderer。" : failure ?? $"已准备：{count} 个 Renderer";
            if(isActiveAndEnabled)HoOutlineDataRegistry.Register(this);else HoOutlineDataRegistry.MarkDirty();
            bindingDirty=false;
            return count > 0 && success;
        }
        [ContextMenu("生成或更新描边修正")]
        private void PrepareContext() => Prepare();

        public static void SelectMaterialSource(Material material, bool enabled)
        {
            if(material==null || !material.HasProperty("_OutlineVertexR2Width"))return;
            material.SetInt("_OutlineVertexR2Width",enabled ? 3 : 0);
            if(enabled)material.EnableKeyword("_HO_GD_OUTLINE");else material.DisableKeyword("_HO_GD_OUTLINE");
        }
    }
}
