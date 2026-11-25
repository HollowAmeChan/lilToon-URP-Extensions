using UnityEngine;
using UnityEngine.Rendering;
using lilToon.URP.Extensions.ObjectBuffer;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    // D3D12 requires every declared SRV even when the material's runtime gate
    // is off. This binding must exist without any RendererFeature or source.
    internal static class HoCharacterRimShaderState
    {
        internal static readonly GlobalKeyword HairReceiverKeyword = GlobalKeyword.Create("_HO_HAIR_RIM_AVAILABLE");
        static readonly int LightsId = Shader.PropertyToID("_HoRimLights");
        static readonly int ActiveId = Shader.PropertyToID("_HoRimActive");
        static readonly int CountId = Shader.PropertyToID("_HoRimLightCount");
        static GraphicsBuffer emptyLights;
        static bool shuttingDown;

        internal static GraphicsBuffer EmptyLights
        {
            get
            {
                if (shuttingDown || SystemInfo.graphicsShaderLevel < 45) return null;
                if (emptyLights == null || !emptyLights.IsValid())
                {
                    emptyLights?.Dispose();
                    emptyLights = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, HoCharacterRimGpuLight.Stride);
                    emptyLights.SetData(new HoCharacterRimGpuLight[1]);
                }
                return emptyLights;
            }
        }

        internal static void Reset()
        {
            Shader.SetKeyword(HairReceiverKeyword, false);
            Shader.SetGlobalFloat(ActiveId, 0);
            Shader.SetGlobalInt(CountId, 0);
            var buffer = EmptyLights;
            if (buffer != null) Shader.SetGlobalBuffer(LightsId, buffer);
        }

        static void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            Reset();
            // Publish renderer identities before URP culling captures per-object
            // data. Source direction and shadow geometry are still snapshotted
            // later, after the current constraint pose is available.
            if (HoCharacterRimLight.Active.Count != 0 && HoObjectBufferRegistry.SupportsStructuredBuffer)
                HoObjectBufferRegistry.EnsureBuilt();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Initialize()
        {
            shuttingDown = false;
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            Application.quitting -= Release;
            Application.quitting += Release;
            Reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeSceneLoad() => Reset();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorInitialize()
        {
            Initialize();
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Release;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Release;
            UnityEditor.EditorApplication.quitting -= Release;
            UnityEditor.EditorApplication.quitting += Release;
        }
#endif

        static void Release()
        {
            // Feature disposal can call Reset after the quit/reload callback.
            // Do not allocate another placeholder during shutdown.
            shuttingDown = true;
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            Shader.SetKeyword(HairReceiverKeyword, false);
            Shader.SetGlobalFloat(ActiveId, 0);
            Shader.SetGlobalInt(CountId, 0);
            emptyLights?.Dispose();
            emptyLights = null;
        }
    }
}
