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
            public TextureHandle eyeColorTexture;
            public TextureHandle eyeDataTexture;
            public Material material;
            public Vector4 eyeRevealParams;
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
            public Vector4 options;
            public bool faceHairDiffuseReady;
            public bool subjectOutlineReady;
        }

        private sealed class FaceHairDiffuseSourcePassData
        {
            public TextureHandle source;
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
            public TextureHandle source;
            public TextureHandle metadataObjectCustom0Texture;
            public TextureHandle geometryDepthTexture;
            public Material material;
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
