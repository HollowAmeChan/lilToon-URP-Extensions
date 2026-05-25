namespace lilToon.URP.Extensions.PostProcessing
{
    public static class ScreenProcessEffectRegistry
    {
        public static string GetDefaultShaderName(ScreenProcessEffect effect)
        {
            switch (effect)
            {
                case ScreenProcessEffect.EdgeLight:
                    return ScreenProcessShaderConstants.EdgeLightShaderName;
                case ScreenProcessEffect.Outline:
                    return ScreenProcessShaderConstants.OutlineShaderName;
                case ScreenProcessEffect.DropShadow:
                    return ScreenProcessShaderConstants.DropShadowShaderName;
                case ScreenProcessEffect.DepthOfField:
                    return ScreenProcessShaderConstants.DepthOfFieldShaderName;
                case ScreenProcessEffect.PostLighting:
                    return ScreenProcessShaderConstants.PostLightingShaderName;
                case ScreenProcessEffect.CustomMaterial:
                default:
                    return ScreenProcessShaderConstants.DefaultLayerShaderName;
            }
        }
    }
}
