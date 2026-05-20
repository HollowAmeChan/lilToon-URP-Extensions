using lilToon.URP.Extensions.AOV;
using UnityEngine;
#pragma warning disable CS0618, CS0672

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed partial class ShoostPostProcessPass
    {
        private static void ApplyLayerProperties(ShoostPostProcessLayer layer, Material material)
        {
            LayerPropertyBlock properties = LayerPropertyBlock.FromLayer(layer);
            ApplyEffectPropertyDefaults(layer.effect, ref properties);

            material.SetFloat(ShoostPostProcessShaderConstants.IntensityId, layer.intensity);
            material.SetFloat(ShoostPostProcessShaderConstants.SharpnessId, properties.Sharpness);
            material.SetFloat(ShoostPostProcessShaderConstants.ModeId, layer.parameters0.x);
            material.SetFloat(ShoostPostProcessShaderConstants.AngleId, layer.parameters0.z * Mathf.Deg2Rad);
            material.SetFloat(ShoostPostProcessShaderConstants.LayerBlendModeId, (float)layer.blendMode);

            material.SetColor(ShoostPostProcessShaderConstants.LayerColorId, properties.Color);
            material.SetFloat(ShoostPostProcessShaderConstants.LayerTextureEnabledId, layer.texture != null ? 1.0f : 0.0f);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams0Id, properties.Params0);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams1Id, properties.Params1);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams2Id, properties.Params2);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams3Id, properties.Params3);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams4Id, properties.Params4);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams5Id, properties.Params5);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams6Id, properties.Params6);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams7Id, properties.Params7);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams8Id, properties.Params8);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams9Id, properties.Params9);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams10Id, properties.Params10);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams11Id, properties.Params11);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams12Id, properties.Params12);
            if (layer.texture != null)
            {
                material.SetTexture(ShoostPostProcessShaderConstants.LayerTextureId, layer.texture);
            }

            material.SetVector(
                ShoostPostProcessShaderConstants.LogoTextureEnabled0Id,
                new Vector4(
                    layer.logoTexture0 != null ? 1.0f : 0.0f,
                    layer.logoTexture1 != null ? 1.0f : 0.0f,
                    layer.logoTexture2 != null ? 1.0f : 0.0f,
                    layer.logoTexture3 != null ? 1.0f : 0.0f));
            material.SetVector(
                ShoostPostProcessShaderConstants.LogoTextureEnabled1Id,
                new Vector4(
                    layer.logoTexture4 != null ? 1.0f : 0.0f,
                    layer.logoTexture5 != null ? 1.0f : 0.0f,
                    layer.logoTexture6 != null ? 1.0f : 0.0f,
                    layer.logoTexture7 != null ? 1.0f : 0.0f));
            SetLogoTexture(material, 0, layer.logoTexture0);
            SetLogoTexture(material, 1, layer.logoTexture1);
            SetLogoTexture(material, 2, layer.logoTexture2);
            SetLogoTexture(material, 3, layer.logoTexture3);
            SetLogoTexture(material, 4, layer.logoTexture4);
            SetLogoTexture(material, 5, layer.logoTexture5);
            SetLogoTexture(material, 6, layer.logoTexture6);
            SetLogoTexture(material, 7, layer.logoTexture7);
        }

        private static void SetLogoTexture(Material material, int index, Texture texture)
        {
            if (texture == null)
            {
                return;
            }

            material.SetTexture(ShoostPostProcessShaderConstants.LogoTextureIds[index], texture);
        }

        private static void ApplyEffectPropertyDefaults(ShoostPostProcessEffect effect, ref LayerPropertyBlock properties)
        {
            switch (effect)
            {
                case ShoostPostProcessEffect.Fisheye:
                    ApplyFisheyePropertyDefaults(ref properties);
                    break;
                case ShoostPostProcessEffect.SharpenBefore:
                case ShoostPostProcessEffect.SharpenAfter:
                    ApplySharpenPropertyDefaults(ref properties);
                    break;
                case ShoostPostProcessEffect.SkyGodRays:
                    ApplySkyGodRaysPropertyDefaults(ref properties);
                    break;
                case ShoostPostProcessEffect.LogoOverlay:
                    ApplyLogoOverlayPropertyDefaults(ref properties);
                    break;
            }
        }

        private struct LayerPropertyBlock
        {
            public float Sharpness;
            public Color Color;
            public Vector4 Params0;
            public Vector4 Params1;
            public Vector4 Params2;
            public Vector4 Params3;
            public Vector4 Params4;
            public Vector4 Params5;
            public Vector4 Params6;
            public Vector4 Params7;
            public Vector4 Params8;
            public Vector4 Params9;
            public Vector4 Params10;
            public Vector4 Params11;
            public Vector4 Params12;

            public static LayerPropertyBlock FromLayer(ShoostPostProcessLayer layer)
            {
                return new LayerPropertyBlock
                {
                    Sharpness = layer.parameters0.x,
                    Color = layer.color,
                    Params0 = layer.parameters0,
                    Params1 = layer.parameters1,
                    Params2 = layer.parameters2,
                    Params3 = layer.parameters3,
                    Params4 = layer.parameters4,
                    Params5 = layer.parameters5,
                    Params6 = layer.parameters6,
                    Params7 = layer.parameters7,
                    Params8 = layer.parameters8,
                    Params9 = layer.parameters9,
                    Params10 = layer.parameters10,
                    Params11 = layer.parameters11,
                    Params12 = layer.parameters12
                };
            }
        }

        private static void ApplyShoostAovCompositeProperties(ShoostPostProcessLayer layer, Material material)
        {
            if (layer == null || material == null)
            {
                return;
            }

            material.SetFloat(ShoostPostProcessShaderConstants.LayerAovMaskEnabledId, layer.useAovMask ? 1.0f : 0.0f);
            material.SetFloat(ShoostPostProcessShaderConstants.LayerAovSourceId, (float)layer.aovSource);
            material.SetFloat(ShoostPostProcessShaderConstants.LayerAovModeId, (float)layer.aovMaskMode);
            material.SetVector(
                ShoostPostProcessShaderConstants.LayerAovParamsId,
                new Vector4(
                    Mathf.Max(0.0f, layer.aovThreshold),
                    0.0f,
                    layer.aovMatchValue,
                    layer.invertAovMask ? 1.0f : 0.0f));
            material.SetColor(ShoostPostProcessShaderConstants.LayerAovMatchColorId, layer.aovMatchColor);
            material.SetFloat(ShoostPostProcessShaderConstants.LayerAovDebugOutputId, layer.debugAovMask ? 1.0f : 0.0f);
            HoPostAovMaskRuntime.ApplyToMaterial(
                layer,
                material,
                ShoostPostProcessShaderConstants.LayerAovRuleCountId,
                ShoostPostProcessShaderConstants.LayerAovRuleData0Id,
                ShoostPostProcessShaderConstants.LayerAovRuleData1Id,
                ShoostPostProcessShaderConstants.LayerAovRuleData2Id,
                ShoostPostProcessShaderConstants.LayerAovRuleColorId);
        }

    }
}
