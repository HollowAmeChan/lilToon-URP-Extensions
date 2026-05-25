using lilToon.URP.Extensions.Debugging;

namespace lilToon.URP.Extensions.ImageProcess
{
    public static class ImageProcessDebugViewInfo
    {
        public static readonly HoDebugViewInfo[] Views =
        {
            new HoDebugViewInfo(
                "ImageProcess",
                "image-process.layer-chain",
                "Chain",
                0,
                string.Empty,
                string.Empty,
                false,
                "ImageProcess debug is observed through Frame Debugger pass names and layer isolation; it has no semantic input texture or standalone debug shader.")
        };
    }
}
