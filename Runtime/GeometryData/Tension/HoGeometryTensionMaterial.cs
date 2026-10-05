using UnityEngine;

namespace lilToon.URP.Extensions.GeometryData
{
    /// <summary>Explicit material API for scripts changing Tesion textures/toggles outside the lilToon Inspector.</summary>
    public static class HoGeometryTensionMaterial
    {
        public static void Synchronize(Material material)
        {
            if (material == null || !material.HasProperty("_HoTesionEnabled")) return;
            if (material.GetFloat("_HoTesionEnabled") > 0.5f) material.EnableKeyword("_HO_GD_TENSION");
            else material.DisableKeyword("_HO_GD_TENSION");
            material.SetVector("_HoTesionMaps", new Vector4(
                material.GetTexture("_HoTesionStretchBaseColor") != null ? 1 : 0,
                material.GetTexture("_HoTesionCompressionBaseColor") != null ? 1 : 0,
                material.GetTexture("_HoTesionStretchNormal") != null ? 1 : 0,
                material.GetTexture("_HoTesionCompressionNormal") != null ? 1 : 0));
        }
    }
}
