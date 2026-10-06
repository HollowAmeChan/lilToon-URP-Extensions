using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.AttributeComposite
{
    /// <summary>Camera-scoped demand collection. Freeze after all consumers have queued their passes.</summary>
    public static class HoAttributeCompositeConsumerRegistry
    {
        private sealed class CameraRequests
        {
            public Camera camera;
            public long sequence;
            public int frame;
            public readonly Dictionary<int, HoAttributeCompositeConsumerDeclaration> consumers = new Dictionary<int, HoAttributeCompositeConsumerDeclaration>();
            public HoAttributeCompositeDemandSnapshot snapshot;
        }
        private static readonly Dictionary<int, CameraRequests> requests = new Dictionary<int, CameraRequests>();
        private static readonly Dictionary<string, HoAttributeCompositeConsumerDeclaration> legacy = new Dictionary<string, HoAttributeCompositeConsumerDeclaration>();
        private static long sequence;
        private static bool registered;
        public static HoAttributeCompositeDemandSnapshot LastSnapshot { get; private set; }

        static HoAttributeCompositeConsumerRegistry() { EnsureInitialized(); }
        public static void EnsureInitialized()
        {
            if (registered) return;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
            registered = true;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Shutdown(); sequence = 0;
            EnsureInitialized();
        }
        private static void Shutdown()
        {
            // Do not retain camera callbacks while editor/native rendering objects are torn down.
            if (registered)
            {
                RenderPipelineManager.beginCameraRendering -= BeginCamera;
                RenderPipelineManager.endCameraRendering -= EndCamera;
                registered = false;
            }
            requests.Clear(); legacy.Clear(); LastSnapshot = null;
        }
        #if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeEditor()
        {
            EnsureInitialized();
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            UnityEditor.EditorApplication.quitting -= Shutdown;
            UnityEditor.EditorApplication.quitting += Shutdown;
        }
        #endif
        private static void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != null) requests[camera.GetInstanceID()] = NewRequests(camera);
        }
        private static CameraRequests NewRequests(Camera camera) => new CameraRequests
        { camera = camera, sequence = ++sequence, frame = Time.frameCount };
        private static CameraRequests GetRequests(Camera camera)
        {
            EnsureInitialized();
            if (!requests.TryGetValue(camera.GetInstanceID(), out CameraRequests value))
                requests[camera.GetInstanceID()] = value = NewRequests(camera);
            return value;
        }

        /// <summary>RenderSingleCamera does not emit camera events. Explicitly delimit that render invocation.</summary>
        public static IDisposable BeginStandaloneCamera(Camera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            EnsureInitialized();
            return new StandaloneCameraScope(camera);
        }
        private sealed class StandaloneCameraScope : IDisposable
        {
            private readonly int cameraId;
            private readonly CameraRequests current;
            private readonly CameraRequests previous;
            private bool disposed;
            internal StandaloneCameraScope(Camera camera)
            {
                cameraId = camera.GetInstanceID();
                requests.TryGetValue(cameraId, out previous);
                requests[cameraId] = current = NewRequests(camera);
                HoAttributeCompositePass.ResetGlobalState();
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (!requests.TryGetValue(cameraId, out CameraRequests value) || !ReferenceEquals(value, current)) return;
                if (previous != null) requests[cameraId] = previous;
                else requests.Remove(cameraId);
            }
        }
        private static void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || !requests.ContainsKey(camera.GetInstanceID())) return;
            if (camera.cameraType == CameraType.Game || camera.cameraType == CameraType.SceneView) Freeze(camera);
            requests.Remove(camera.GetInstanceID());
        }
        public static void DeclareForCamera(Camera camera, UnityEngine.Object owner, string displayName,
            IEnumerable<HoACQueryDescriptor> queries, IEnumerable<string> semanticNames = null,
            HoACDemandResources resources = HoACDemandResources.None, uint attributeMask = 0, uint laneMask = 0)
        {
            if (camera == null || owner == null) return;
            CameraRequests current = GetRequests(camera);
            if (current.snapshot != null) throw new InvalidOperationException("AC demand is frozen; declare during AddRenderPasses/SetupRenderPasses.");
            current.consumers[owner.GetInstanceID()] = new HoAttributeCompositeConsumerDeclaration(owner.GetInstanceID(),
                displayName, queries, semanticNames, resources, attributeMask, laneMask);
        }
        public static void DeclareSemantics(Camera camera, UnityEngine.Object owner, string displayName, string[] names,
            HoACDemandResources resources = HoACDemandResources.None)
        {
            DeclareForCamera(camera, owner, displayName, HoAttributeCompositeConsumerDeclaration.ResolveNames(names), names, resources);
        }
        public static HoAttributeCompositeDemandSnapshot Freeze(Camera camera)
        {
            if (camera == null) return null;
            CameraRequests current = GetRequests(camera);
            if (current.snapshot == null)
            {
                var all = new List<HoAttributeCompositeConsumerDeclaration>(current.consumers.Values);
                all.AddRange(legacy.Values); // External unscoped callers conservatively apply to every camera.
                current.snapshot = new HoAttributeCompositeDemandSnapshot(camera, current.sequence, current.frame, all);
            }
            LastSnapshot = current.snapshot;
            return current.snapshot;
        }
        public static void Remove(UnityEngine.Object owner, Camera camera = null)
        {
            if (owner == null) return;
            int id = owner.GetInstanceID();
            if (camera != null)
            {
                if (requests.TryGetValue(camera.GetInstanceID(), out CameraRequests value) && value.snapshot == null) value.consumers.Remove(id);
            }
            else foreach (CameraRequests value in requests.Values)
                if (value.snapshot == null) value.consumers.Remove(id);
        }
        // Legacy API remains conservative. New consumers should use the scoped API.
        public static void Declare(string consumer, params string[] names)
        {
            if (!string.IsNullOrEmpty(consumer)) legacy[consumer] = new HoAttributeCompositeConsumerDeclaration(consumer, names);
        }
        public static void Remove(string consumer) { if (consumer != null) legacy.Remove(consumer); }
        public static IReadOnlyList<HoAttributeCompositeConsumerDeclaration> Declarations
        {
            get
            {
                var result = new List<HoAttributeCompositeConsumerDeclaration>();
                if (LastSnapshot != null) foreach (var item in LastSnapshot.Consumers) if (item.OwnerId != 0) result.Add(item);
                result.AddRange(legacy.Values);
                return result.AsReadOnly();
            }
        }
        public static int CountUnresolved()
        {
            int total = 0;
            foreach (var declaration in Declarations) total += declaration.UnresolvedNames.Length;
            return total;
        }
    }
}
