using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    internal sealed partial class HoCharacterSpecializationPass
    {
        private sealed class CompositePassData
        {
            public TextureHandle source;
            public TextureHandle metadataMaskIdTexture;
            public TextureHandle geometryNormalDepthTexture;
            public TextureHandle metadataObjectCustom0Texture;
            public TextureHandle metadataObjectCustom1Texture;
            public TextureHandle faceHairDiffuseSourceColorTexture;
            public TextureHandle faceHairDiffuseColorTexture;
            public TextureHandle faceHairDiffuseDepthTexture;
            public TextureHandle subjectOutlineSourceTexture;
            public TextureHandle subjectOutlineTexture;
            public TextureHandle enhancedOutlineSourceTexture;
            public TextureHandle enhancedOutlineTexture;
            public TextureHandle eyeColorTexture;
            public TextureHandle eyeDataTexture;
            public TextureHandle semanticMaskBlurredLowTexture;
            public TextureHandle semanticMaskBlurredHighTexture;
            public Vector4 semanticMaskOptions;
            public Material material;
            public Vector4 eyeRevealParams;
            public Vector4 eyeAngleParams;
            public Vector4 hairShadowParams;
            public Vector4 hairShadowParams1;
            public Vector4 hairShadowParams2;
            public Color hairShadowColor;
            public Vector4 faceHairDiffuseParams;
            public Vector4 faceHairDiffuseLevels;
            public Color faceHairDiffuseTintColor;
            public Vector4 faceHairDiffuseOptions;
            public Vector4 subjectOutlineParams;
            public Vector4 subjectOutlineLevels;
            public Color subjectOutlineColor;
            public Color subjectOutlineFogColor;
            public Vector4 subjectOutlineFogParams;
            public Vector4 subjectOutlineHeightFadeParams;
            public Vector4 subjectOutlineOptions;
            public Vector4 enhancedOutlineParams;
            public Color enhancedOutlineFogColor;
            public Vector4 enhancedOutlineFogParams;
            public Vector4 enhancedOutlineHeightFadeParams;
            public Vector4 enhancedOutlineOptions;
            public Vector4 options;
            public bool faceHairDiffuseReady;
            public bool subjectOutlineReady;
            public bool enhancedOutlineReady;
            public bool semanticMaskBlurReady;
            // 以下三个 bool 就是对应的"合成趟到底会不会采它"的门（见录制处注释）：
            // 它们只影响 UseTexture 声明与全局绑定，不影响任何 shader 常量。
            public bool eyeDataSampled;
            public bool semanticMaskBlurSampled;
            public bool faceHairDiffuseSourceColorSampled;
        }

        private sealed class FaceHairDiffuseSourcePassData
        {
            // 没有 source 字段：这趟从不用相机颜色（Frag 不采 _BlitTexture），
            // 画面也不再走 Blitter.BlitTexture 去绑它。
            public TextureHandle metadataObjectCustom0Texture;
            public TextureHandle metadataSurfaceColorTexture;
            public TextureHandle geometryNormalDepthTexture;
            public Material material;
        }

        private sealed class FaceHairDiffuseBlurPassData
        {
            public TextureHandle sourceColor;
            public TextureHandle sourceDepth;
            public TextureHandle destinationColor;
            public TextureHandle destinationDepth;
            public Material material;
            public Vector4 blurParams;
        }

        private sealed class SubjectOutlineSourcePassData
        {
            // 没有 source 字段：主体/增强轮廓的 source pass 也从不用相机颜色（同 F1/F2/F3）。
            public TextureHandle metadataObjectCustom0Texture;
            public TextureHandle metadataObjectCustom1Texture;
            public TextureHandle semanticMaskBlurredLowTexture;
            public TextureHandle semanticMaskBlurredHighTexture;
            public TextureHandle geometryDepthTexture;
            public Material material;
            public Vector4 sourceParams;
            public bool semanticMaskBlurReady;
            public bool useSemanticMaskAntiAliasing;
        }

        private sealed class SubjectOutlineBlurPassData
        {
            public TextureHandle source;
            public TextureHandle destination;
            public Material material;
            public Vector4 blurParams;
        }
    }
}
