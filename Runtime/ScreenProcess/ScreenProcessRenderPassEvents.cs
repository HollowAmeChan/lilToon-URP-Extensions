using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal static class ScreenProcessRenderPassEvents
    {
        // RT products are linear HDR scene contributions. Keep their
        // consumer before transparents; the ordinary visual stack remains
        // in the post-processing domain below.
        public const RenderPassEvent RTLightingComposite = RenderPassEvent.BeforeRenderingTransparents;
        public const RenderPassEvent ScreenProcessStack = RenderPassEvent.AfterRenderingPostProcessing;
        public const RenderPassEvent ImageProcessFinalStack = (RenderPassEvent)((int)RenderPassEvent.AfterRenderingPostProcessing + 1);
    }
}
