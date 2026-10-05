using lilToon.URP.Extensions.GeometryData;
using UnityEditor;

namespace lilToon.URP.Extensions.Editor.GeometryData
{
    [InitializeOnLoad]
    internal static class HoGeometryDataResourceLifecycle
    {
        static HoGeometryDataResourceLifecycle()
        {
            AssemblyReloadEvents.beforeAssemblyReload += HoGeometryObjectFrameBuffer.ReleaseResources;
            AssemblyReloadEvents.beforeAssemblyReload += HoOutlineDataRegistry.ReleaseResources;
            AssemblyReloadEvents.beforeAssemblyReload += HoTensionDataRegistry.ReleaseResources;
            EditorApplication.quitting += HoGeometryObjectFrameBuffer.ReleaseResources;
            EditorApplication.quitting += HoOutlineDataRegistry.ReleaseResources;
            EditorApplication.quitting += HoTensionDataRegistry.ReleaseResources;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
                {
                    HoGeometryObjectFrameBuffer.ReleaseResources();
                    HoOutlineDataRegistry.ReleaseResources();
                    HoTensionDataRegistry.ReleaseResources();
                }
            };
        }
    }
}
