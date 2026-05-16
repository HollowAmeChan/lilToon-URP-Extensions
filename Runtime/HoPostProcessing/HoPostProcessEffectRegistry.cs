namespace lilToon.URP.Extensions.PostProcessing
{
    public static class HoPostProcessEffectRegistry
    {
        public static string GetDefaultShaderName(HoPostProcessEffect effect)
        {
            switch (effect)
            {
                case HoPostProcessEffect.EdgeLight:
                    return HoPostProcessShaderConstants.EdgeLightShaderName;
                case HoPostProcessEffect.Outline:
                    return HoPostProcessShaderConstants.OutlineShaderName;
                case HoPostProcessEffect.DropShadow:
                    return HoPostProcessShaderConstants.DropShadowShaderName;
                case HoPostProcessEffect.CustomMaterial:
                default:
                    return HoPostProcessShaderConstants.DefaultLayerShaderName;
            }
        }
    }
}
