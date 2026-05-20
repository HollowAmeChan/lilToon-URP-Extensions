namespace lilToon.URP.Extensions.PostProcessing
{
    internal static class ShoostPostProcessEffectRegistry
    {
        public static string GetDefaultShaderName(ShoostPostProcessEffect effect)
        {
            return ShoostPostProcessEffectDescriptor.Get(effect).DefaultShaderName;
        }
    }
}
