using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    [AddComponentMenu("Rendering/Ho Capsule AO Receiver")]
    public sealed class HoCapsuleAOReceiver : MonoBehaviour
    {
        internal static readonly HashSet<HoCapsuleAOReceiver> Active = new HashSet<HoCapsuleAOReceiver>();
        private MeshRenderer cachedRenderer;
        private MeshFilter cachedFilter;
        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        internal bool TryGetSurface(Camera camera, out MeshRenderer renderer, out Mesh mesh)
        {
            renderer = cachedRenderer != null ? cachedRenderer : cachedRenderer = GetComponent<MeshRenderer>();
            var filter = cachedFilter != null ? cachedFilter : cachedFilter = GetComponent<MeshFilter>();
            mesh = filter != null ? filter.sharedMesh : null;
            return isActiveAndEnabled && renderer != null && renderer.enabled && mesh != null
                && gameObject.activeInHierarchy && (camera.cullingMask & (1 << gameObject.layer)) != 0;
        }
    }
}
