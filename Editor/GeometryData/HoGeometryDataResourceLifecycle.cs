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
            EditorApplication.quitting += HoGeometryObjectFrameBuffer.ReleaseResources;
            EditorApplication.quitting += HoOutlineDataRegistry.ReleaseResources;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
                {
                    HoGeometryObjectFrameBuffer.ReleaseResources();
                    HoOutlineDataRegistry.ReleaseResources();
                }
            };
        }
    }
}
