using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal static class ScreenProcessRenderPassEvents
    {
        public const RenderPassEvent ScreenProcessStack = RenderPassEvent.AfterRenderingPostProcessing;
        public const RenderPassEvent ImageProcessFinalStack = (RenderPassEvent)((int)RenderPassEvent.AfterRenderingPostProcessing + 1);
    }
}
