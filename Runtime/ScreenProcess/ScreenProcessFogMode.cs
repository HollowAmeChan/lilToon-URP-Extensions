namespace lilToon.URP.Extensions.PostProcessing
{
    /// <summary>Distance falloff curve of the DepthFog depth slot (layer <c>parameters0.y</c>).</summary>
    public enum ScreenProcessFogDepthMode
    {
        Linear = 0,
        Exponential = 1,
        ExponentialSquared = 2
    }

    /// <summary>
    /// Height curve of the DepthFog height slot (layer <c>parameters3.x</c>). One value carries both
    /// the shape and the direction: "window" fades out away from a height, "falloff" is the
    /// exponential ground-fog / cloud-sea curve.
    /// </summary>
    public enum ScreenProcessFogHeightMode
    {
        WindowBelow = 0,
        WindowAbove = 1,
        FalloffBelow = 2,
        FalloffAbove = 3
    }

    /// <summary>Which height the height slot uses (layer <c>parameters3.y</c>).</summary>
    public enum ScreenProcessFogHeightReference
    {
        World = 0,
        Camera = 1
    }

    /// <summary>How the fog treats sky pixels (layer <c>parameters5.y</c>).</summary>
    public enum ScreenProcessFogSkyMode
    {
        Skip = 0,
        Include = 1,
        Tint = 2
    }
}
