namespace lilToon.URP.Extensions.PostProcessing
{
    public static class HoPostProcessEffectRegistry
    {
        public static string GetDefaultShaderName(HoPostProcessEffect effect)
        {
            switch (effect)
            {
                case HoPostProcessEffect.EdgeLight:
                case HoPostProcessEffect.Outline:
                case HoPostProcessEffect.DropShadow:
                case HoPostProcessEffect.CustomMaterial:
                default:
                    return HoPostProcessShaderConstants.DefaultLayerShaderName;
            }
        }
    }
}
