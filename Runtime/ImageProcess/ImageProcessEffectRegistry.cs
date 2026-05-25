namespace lilToon.URP.Extensions.PostProcessing
{
    internal static class ImageProcessEffectRegistry
    {
        public static string GetDefaultShaderName(ImageProcessEffect effect)
        {
            return ImageProcessEffectDescriptor.Get(effect).DefaultShaderName;
        }
    }
}
