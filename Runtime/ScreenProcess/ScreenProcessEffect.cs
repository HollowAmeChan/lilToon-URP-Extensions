namespace lilToon.URP.Extensions.PostProcessing
{
    public enum ScreenProcessEffect
    {
        CustomMaterial = 0,
        EdgeLight = 1,
        Outline = 2,
        DropShadow = 3,
        DepthOfField = 4,
        PostLighting = 5,
        SkyTyndall = 6
    }

    public enum ScreenProcessBlendMode
    {
        Normal = 0,
        Add = 1,
        Screen = 2,
        Multiply = 3
    }
}
