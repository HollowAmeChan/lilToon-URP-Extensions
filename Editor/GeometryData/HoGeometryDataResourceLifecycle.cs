using lilToon.URP.Extensions.GeometryData;
using UnityEditor;

namespace lilToon.URP.Extensions.Editor.GeometryData
{
    [InitializeOnLoad]
    internal static class HoGeometryDataResourceLifecycle
    {
        static HoGeometryDataResourceLifecycle()
        {
            AssemblyReloadEvents.beforeAssemblyReload += HoOutlineDataRegistry.ReleaseResources;
            AssemblyReloadEvents.beforeAssemblyReload += HoTensionDataRegistry.ReleaseResources;
            EditorApplication.quitting += HoOutlineDataRegistry.ReleaseResources;
            EditorApplication.quitting += HoTensionDataRegistry.ReleaseResources;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
                {
                    HoOutlineDataRegistry.ReleaseResources();
                    HoTensionDataRegistry.ReleaseResources();
                }
            };
        }
    }
}
