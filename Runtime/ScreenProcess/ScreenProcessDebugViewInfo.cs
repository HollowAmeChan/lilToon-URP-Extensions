using lilToon.URP.Extensions.Debugging;

namespace lilToon.URP.Extensions.ScreenProcess
{
    public static class ScreenProcessDebugViewInfo
    {
        public static readonly HoDebugViewInfo[] Views =
        {
            new HoDebugViewInfo(
                "ScreenProcess",
                "screen-process.mask",
                "Mask",
                0,
                HoDebugViewRenderKind.None,
                string.Empty,
                string.Empty,
                false,
                "Mask debug uses each active ScreenProcess layer shader through _LayerMaskDebugOutput; no standalone debug shader is owned by the public debug UI.")
        };
    }
}
