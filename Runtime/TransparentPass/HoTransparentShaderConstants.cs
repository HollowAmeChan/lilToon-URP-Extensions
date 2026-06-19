using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.TransparentPass
{
    public static class HoTransparentShaderConstants
    {
        public const string ActiveName = "_HoTransparentActive";
        public const string BackfacePassName = "HoTransparentBackface";
        public const string FrontfacePassName = "HoTransparentFrontface";

        public static readonly int ActiveId = Shader.PropertyToID(ActiveName);
        public static readonly ShaderTagId BackfaceShaderTagId = new ShaderTagId(BackfacePassName);
        public static readonly ShaderTagId FrontfaceShaderTagId = new ShaderTagId(FrontfacePassName);
    }
}
