using lilToon.URP.Extensions.CharacterSpecialization;
using UnityEditor;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    [InitializeOnLoad]
    internal static class HoCharacterReferenceFrameResourceLifecycle
    {
        static HoCharacterReferenceFrameResourceLifecycle()
        {
            AssemblyReloadEvents.beforeAssemblyReload += HoCharacterReferenceFrameBuffer.ReleaseResources;
            EditorApplication.quitting += HoCharacterReferenceFrameBuffer.ReleaseResources;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
                    HoCharacterReferenceFrameBuffer.ReleaseResources();
            };
        }
    }
}
