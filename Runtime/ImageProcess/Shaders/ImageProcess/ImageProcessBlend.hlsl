#ifndef LIL_IMAGE_PROCESS_BLEND_INCLUDED
#define LIL_IMAGE_PROCESS_BLEND_INCLUDED

// The 24 Photoshop / PDF blend modes shared by every layer-based post-processing block:
// ImageProcess (Gradient, GradientMap, LayerBlit, Halftone) and ScreenProcess (Outline, EdgeLight,
// PostLighting, SkyTyndall, DepthFog). The file name is historical - this is the single blend table
// for the whole pipeline, and `.codex-research/shader-check/check_all.ps1` refuses to let any shader
// carry a local copy of it again.
//
// Mode numbering is the canonical one (`ApplyLayerBlend` below); ScreenProcess used to number
// Screen/Multiply the other way round (2/3), and was moved onto this table on purpose.
//
// The bodies below are the block those shaders used to carry individually, moved here verbatim
// (only the indentation changed), so the move cannot change a pixel:
// `.codex-research/shader-check/check_blend_include.js --baseline <dir>` proves token equality
// against the pre-refactor revision, and refuses to let anyone write a fourth local copy.

half3 ColorBurn(half3 baseColor, half3 layerColor)
{
    return max(1.0 - (1.0 - baseColor) / max(layerColor, 0.0001), 0.0);
}

half3 ColorDodge(half3 baseColor, half3 layerColor)
{
    return max(baseColor / max(1.0 - layerColor, 0.0001), 0.0);
}

half3 Overlay(half3 baseColor, half3 layerColor)
{
    return lerp(2.0 * baseColor * layerColor, 1.0 - 2.0 * (1.0 - baseColor) * (1.0 - layerColor), step(0.5, baseColor));
}

half3 SoftLight(half3 baseColor, half3 layerColor)
{
    half3 dark = baseColor - (1.0 - 2.0 * layerColor) * baseColor * (1.0 - baseColor);
    half3 light = baseColor + (2.0 * layerColor - 1.0) * (sqrt(saturate(baseColor)) - baseColor);
    return lerp(dark, light, step(0.5, layerColor));
}

half3 VividLight(half3 baseColor, half3 layerColor)
{
    half3 burn = ColorBurn(baseColor, 2.0 * layerColor);
    half3 dodge = ColorDodge(baseColor, 2.0 * (layerColor - 0.5));
    return lerp(burn, dodge, step(0.5, layerColor));
}

// Photoshop / PDF non-separable blend-mode luminance coefficients. The reference
// definitions of hue/saturation/color/luminosity use these weights, so they are kept
// as-is here rather than switched to Rec.709.
half Lum(half3 color)
{
    return dot(color, half3(0.3, 0.59, 0.11));
}

half Sat(half3 color)
{
    return max(color.r, max(color.g, color.b)) - min(color.r, min(color.g, color.b));
}

half3 ClipColor(half3 color)
{
    half l = Lum(color);
    half n = min(color.r, min(color.g, color.b));
    half x = max(color.r, max(color.g, color.b));

    if (n < 0.0)
    {
        color = l + ((color - l) * l) / max(l - n, 0.0001);
    }

    if (x > 1.0)
    {
        color = l + ((color - l) * (1.0 - l)) / max(x - l, 0.0001);
    }

    return saturate(color);
}

half3 SetLum(half3 color, half luminance)
{
    return ClipColor(color + (luminance - Lum(color)));
}

half3 SetSat(half3 color, half saturation)
{
    half cMin = min(color.r, min(color.g, color.b));
    half cMax = max(color.r, max(color.g, color.b));
    half delta = cMax - cMin;

    if (delta <= 0.0001)
    {
        return half3(0.0, 0.0, 0.0);
    }

    return saturate((color - cMin) * saturation / delta);
}

half3 ApplyLayerBlend(half3 baseColor, half3 layerColor, float blendMode)
{
    int mode = (int)round(blendMode);
    if (mode == 0) return layerColor;
    if (mode == 1) return max(baseColor + layerColor, 0.0);
    if (mode == 2) return baseColor * layerColor;
    if (mode == 3) return 1.0 - (1.0 - baseColor) * (1.0 - layerColor);
    if (mode == 4) return min(baseColor, layerColor);
    if (mode == 5) return ColorBurn(baseColor, layerColor);
    if (mode == 6) return max(baseColor + layerColor - 1.0, 0.0);
    if (mode == 7) return max(baseColor, layerColor);
    if (mode == 8) return ColorDodge(baseColor, layerColor);
    if (mode == 9) return Overlay(baseColor, layerColor);
    if (mode == 10) return SoftLight(baseColor, layerColor);
    if (mode == 11) return Overlay(layerColor, baseColor);
    if (mode == 12) return VividLight(baseColor, layerColor);
    if (mode == 13) return max(baseColor + 2.0 * layerColor - 1.0, 0.0);
    if (mode == 14) return lerp(min(baseColor, 2.0 * layerColor), max(baseColor, 2.0 * (layerColor - 0.5)), step(0.5, layerColor));
    if (mode == 15) return step(0.5, VividLight(baseColor, layerColor));
    if (mode == 16) return abs(baseColor - layerColor);
    if (mode == 17) return baseColor + layerColor - 2.0 * baseColor * layerColor;
    if (mode == 18) return max(baseColor - layerColor, 0.0);
    if (mode == 19) return max(baseColor / max(layerColor, 0.0001), 0.0);
    if (mode == 20) return SetLum(SetSat(layerColor, Sat(baseColor)), Lum(baseColor));
    if (mode == 21) return SetLum(SetSat(baseColor, Sat(layerColor)), Lum(baseColor));
    if (mode == 22) return SetLum(layerColor, Lum(baseColor));
    if (mode == 23) return SetLum(baseColor, Lum(layerColor));
    return layerColor;
}

// SkyTyndall 的 Screen 需要 HDR 版本：天空/神光的底色可能大于 1，普通 screen 会把这份能量压掉，
// 所以只对 LDR 部分做 screen，再把底色超出 1 的部分加回去（其它 23 个模式与 ApplyLayerBlend 一致）。
half3 ScreenHdr(half3 baseColor, half3 layerColor)
{
    half3 ldrBase = saturate(baseColor);
    half3 ldrLayer = saturate(layerColor);
    return 1.0 - (1.0 - ldrBase) * (1.0 - ldrLayer) + max(baseColor - 1.0, 0.0);
}

half3 ApplyLayerBlendHdr(half3 baseColor, half3 layerColor, float blendMode)
{
    if ((int)round(blendMode) == 3)
    {
        return ScreenHdr(baseColor, layerColor);
    }

    return ApplyLayerBlend(baseColor, layerColor, blendMode);
}

#endif
