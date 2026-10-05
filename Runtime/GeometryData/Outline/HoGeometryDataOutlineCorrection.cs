using System;
using UnityEngine;

namespace lilToon.URP.Extensions.GeometryData
{
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Ho-GeometryData OutlineCorrection")]
    public sealed class HoGeometryDataOutlineCorrection : MonoBehaviour
    {
        [InspectorName("目标渲染器")] public Renderer targetRenderer;
        [InspectorName("初始化时准备")] public bool prepareOnInitialize = true;
        [InspectorName("Play 前准备")] public bool prepareBeforePlay = true;
        [InspectorName("连接相同位置的顶点")]
        [Tooltip("仅连接位置、蒙皮权重和所有形态键位置变化一致的顶点。关闭可保留分离部件。")]
        public bool weldCoincidentVertices = true;
        [SerializeField, HideInInspector] private Mesh preparedMesh;
        [SerializeField, HideInInspector] private Vector4[] preparedData;
        [SerializeField, HideInInspector] private bool preparedWeld;
        [NonSerialized] private string status;
        [NonSerialized] private bool bindingDirty;
        public string Status => targetRenderer != null && targetRenderer.isPartOfStaticBatch
            ? "目标参加了静态合批，无法使用原 Mesh 顶点索引；描边回退材质法线。"
            : status ?? (HasData ? "已准备" : "未准备");
        public Vector4[] Data => HasData ? preparedData : null;
        public Mesh SourceMesh => targetRenderer is SkinnedMeshRenderer skin ? skin.sharedMesh
            : targetRenderer != null && targetRenderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
        public bool HasData => preparedData != null && preparedMesh != null && SourceMesh==preparedMesh
            && preparedData.Length==preparedMesh.vertexCount && preparedWeld==weldCoincidentVertices
            && targetRenderer != null && !targetRenderer.isPartOfStaticBatch;

        private void OnEnable()
        {
            if(targetRenderer==null)targetRenderer=GetComponent<Renderer>();
            if(prepareOnInitialize && !HasData)Prepare();
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
            if(targetRenderer==null)targetRenderer=GetComponent<Renderer>();
            try
            {
                Mesh mesh=SourceMesh;
                var data=HoGeometryDataOutlineCorrectionBuilder.Build(mesh,weldCoincidentVertices);
                preparedMesh=mesh;preparedWeld=weldCoincidentVertices;preparedData=data;
                status=$"已准备：{data.Length} 顶点";
                if(isActiveAndEnabled)HoOutlineDataRegistry.Register(this);else HoOutlineDataRegistry.MarkDirty();
                bindingDirty=false;
                return true;
            }
            catch(Exception exception)
            {
                status=exception.Message;return false;
            }
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
