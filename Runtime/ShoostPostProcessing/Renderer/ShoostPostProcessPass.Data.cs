using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
#pragma warning disable CS0618, CS0672

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed partial class ShoostPostProcessPass
    {
        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle originalTexture;
            public ShoostPostProcessLayer layer;
            public Material material;
            public int passIndex;
            public float radius;
            public float screenRatio;
            public TextureHandle blurredTexture;
            public Vector2 center;
            public float centerSize;
            public float smoothness;
            public float distance;
            public float angle;
            public float blurOffsetR;
            public float blurOffsetG;
            public float blurOffsetB;
            public bool enableRgbSplit;
            public TextureHandle frozenFrameTexture;
            public TextureHandle bloomTexture;
        }

        private sealed class ChangeFrameRateState
        {
            public RTHandle frozenTexture;
            public int width;
            public int height;
            public int volumeDepth;
            public int msaaSamples;
            public TextureDimension dimension;
            public GraphicsFormat graphicsFormat;
            public bool isValid;
            public int targetFrameRate;
            public double nextUpdateTime;

            public void Release()
            {
                frozenTexture?.Release();
                frozenTexture = null;
                isValid = false;
            }
        }

        private struct IrisBlurParameters
        {
            public int resolutionType;
            public Vector2Int customResolution;
            public float radius;
            public int downScale;
            public int iterations;
            public Vector2 center;
            public float centerSize;
            public float smoothness;
            public bool enableRgbSplit;
            public float blurRadiusR;
            public float blurRadiusG;
            public float blurRadiusB;
            public float distance;
            public float angleRadians;
        }

        private readonly HashSet<ShoostPostProcessEffect> warnedSemanticInputEffects = new HashSet<ShoostPostProcessEffect>();

    }
}
