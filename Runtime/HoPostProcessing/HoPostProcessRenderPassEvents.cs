using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal static class HoPostProcessRenderPassEvents
    {
        public const RenderPassEvent HoPostStack = RenderPassEvent.AfterRenderingPostProcessing;
        public const RenderPassEvent ShoostFinalStack = (RenderPassEvent)((int)RenderPassEvent.AfterRenderingPostProcessing + 1);
    }
}
