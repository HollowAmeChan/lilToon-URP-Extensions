using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.Atmosphere
{
    /// <summary>Raw, unfiltered environment radiance. Declare UseTexture(Read) for every sampled handle.</summary>
    public sealed class HoAtmosphereRenderGraphResources : ContextItem
    {
        public TextureHandle radianceTexture = TextureHandle.nullHandle;
        // sin(yaw), cos(yaw), gain, valid. Rotation applies to lookup, not the stored cube.
        public Vector4 parameters;
        public Vector4 tint;
        // A=1 means a constant source; otherwise sample the cube.
        public Vector4 constantColor;
        public uint revision;
        public bool IsValid => radianceTexture.IsValid() && parameters.w > 0.5f;
        public override void Reset()
        {
            radianceTexture = TextureHandle.nullHandle;
            parameters = Vector4.zero;
            tint = Vector4.zero;
            constantColor = Vector4.zero;
            revision = 0;
        }
    }

    public static class HoAtmosphereBindings
    {
        // Borrowed compatibility snapshot published by the environment pass.
        // Consumers must read it at execution, after that pass, not during AddRenderPasses.
        internal static RTHandle compatibilityTexture;
        internal static Vector4 compatibilityParameters, compatibilityTint, compatibilityConstant;
        internal static uint compatibilityRevision;
        public static readonly int RadianceId = Shader.PropertyToID("_HoAtmosphereRadianceTexture");
        public static readonly int ParametersId = Shader.PropertyToID("_HoAtmosphereParameters");
        public static readonly int TintId = Shader.PropertyToID("_HoAtmosphereTint");
        public static readonly int ConstantId = Shader.PropertyToID("_HoAtmosphereConstantColor");

        public static void Bind(RasterCommandBuffer cmd, HoAtmosphereRenderGraphResources input)
        {
            cmd.SetGlobalTexture(RadianceId, input.radianceTexture);
            cmd.SetGlobalVector(ParametersId, input.parameters);
            cmd.SetGlobalVector(TintId, input.tint);
            cmd.SetGlobalVector(ConstantId, input.constantColor);
        }

        internal static void ResetGlobals()
        {
            compatibilityTexture = null; compatibilityParameters = compatibilityTint = compatibilityConstant = Vector4.zero; compatibilityRevision = 0;
            Shader.SetGlobalTexture(RadianceId, CoreUtils.blackCubeTexture);
            Shader.SetGlobalVector(ParametersId, Vector4.zero);
            Shader.SetGlobalVector(TintId, Vector4.zero);
            Shader.SetGlobalVector(ConstantId, Vector4.zero);
        }
    }
}
