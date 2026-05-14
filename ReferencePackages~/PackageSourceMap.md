# Shoost 后处理源码来源地图

Shoost v0.16.3 看起来是以 Unity Post Processing Stack v2 作为后处理宿主框架，再整合多个外部后处理包，最后叠加一层 `Custom/*` 自定义效果、改版效果和预设。

重要提醒：Shoost 经 AssetRipper 解包得到的大多数 shader 都包含 `//DummyShaderTextExporter`。这些 shader 通常只适合参考名称、属性和材质引用，不要当作真实算法源码。Shoost 自定义 shader 目前应优先参考 `D:\Unity_Fork\Shoost_v0.16.3\RenderDocShaderDump` 中的 DXBC 反汇编，并结合 `D:\Unity_Fork\Shoost_v0.16.3\DecompileWorkFiles\Cpp2ILOutputs\ISIL` 里的 C# renderer 流程重写。

具体阅读流程见 `ShoostSourceReadingGuide.md`。

## Unity Post Processing Stack v2

这一部分用于参考 PPS v2 标准效果和框架执行方式。

可能的源码/包名：

- `Unity-Technologies/PostProcessing`
- `com.unity.postprocessing`

Shoost 中的证据：

- `Unity.Postprocessing.Runtime.dll`
- `Assets/MonoBehaviour/PostProcessResources.asset`
- `PostProcessEffectSettings`
- `PostProcessEffectRenderer<T>`

适合参考的标准效果：

- Bloom
- Color Grading
- Vignette
- Grain
- Depth of Field
- Motion Blur
- Ambient Occlusion
- FXAA / SMAA / TAA

## Retro Look Pro

这一部分最适合参考 VHS、模拟电视、旧胶片、NTSC 和 CRT 风格。

可能的源码/包名：

- `Retro Look Pro`
- `LimitlessDev.RetroLookPro`

Shoost 中对应的效果：

- `RLProVHSEffect`
- `RLProVHSScanlines`
- `RLProVHSRewind`
- `RLProAnalogTVNoise`
- `RLProOldFilm`
- `RLProOldFilm2`
- `RLProBleed`
- `RLProJitter`
- `RLProCRTAperture`
- `RLProPhosphor`
- `RLProWarp`
- `RLProLowRes`
- `RLProGlitch1`
- `RLProGlitch2`
- `RLProGlitch3`
- `RLProPictureCorrection`
- `RLProCinematicBars`
- `RLProUltimateVignette`
- `RLProColormapPalette`
- `RLProNegative`
- `RLProArtefacts`
- `RLProCustomTexture`
- `RLPro_NTSC`
- `RLProNoise`
- `RLProNoise2`
- `RLProEdgeNoise`
- `RLProEdgeStretch`
- `RLProFisheye`
- `RLProTVEffect`

Shoost 中可能基于 Retro Look Pro 改出来的自定义版本：

- `RLProBleed_Custom`
- `RLProNoise2_Custom`
- `RLProOldFilm2_Custom`
- `RLProTVEffect_Custom`

## X-PostProcessing

这一部分适合参考各种模糊、像素化、锐化和 RGB 分离效果。

可能的源码/包名：

- `X-PostProcessing`
- `XPostProcessing`

Shoost 中对应的效果：

- `BokehBlur`
- `DirectionalBlur`
- `DualGaussianBlur`
- `DualKawaseBlur`
- `GaussianBlur`
- `GlitchRGBSplit`
- `IrisBlur`
- `IrisBlurV2`
- `KawaseBlur`
- `PixelizeCircle`
- `PixelizeHexagonGrid`
- `PixelizeLed`
- `RadialBlurV2`
- `SharpenV3`
- `TiltShiftBlurV2`

## Kino Postprocessing

这一部分适合参考一些比较紧凑的相机后处理实现，尤其是 Bloom、Bokeh、Glitch 和 Tube。

可能的源码/包名：

- `Kino`
- `Kino Postprocessing`
- `KinoBloom`
- `KinoBokeh`
- `KinoGlitch`
- `KinoTube`

Shoost 中对应的效果：

- `Kino/Bloom`
- `Kino/Bloom_Custom`
- `Kino/Bloom_Diffusion`
- `Kino/Bokeh`
- `Kino/AnalogGlitch`
- `Kino/DigitalGlitch`
- `Kino/Tube`

## Shoost 自定义层

Shoost 解包工程主要用于参考参数名、参数范围、菜单路径、预设和效果顺序。

比较像 Shoost 自己写、自己改写或重封装的效果：

- `AutoWhiteBalance`
- `ChangeFrameRate`
- `ColorGrading_Custom`
- `CRTEffects`
- `Distortion`
- `DitheringCustom`
- `DownScaleResolution`
- `FilmBreath_GateWeave`
- `Fisheye`
- `GateWeave`
- `Grain_Custom`
- `IrisBlur`
- `KawaseBlur`
- `LevelAdjustment`
- `LUTColorGrading`
- `MotionTrailEffect`
- `Pixelize`
- `RGBBlur`
- `RGBBlurV2`
- `RGBChannelSeparator`
- `RGBSplit`
- `Sharpen_Before`
- `Sharpen_After`
- `Tube`
- `Vignette_Custom`
- `LensDistortion_Custom`

有价值的 Shoost 预设文件：

- `AMS_VHS_*`
- `AMS_TV_*`
- `AMS_AnimeFilm_*`
- `AniMakeStudio_Finish_PostProcess_*`
- `AMS_01_Single_EffectsSET.asset`

## 重写优先级

第一批，简单全屏单 pass：

- `RGBSplit`
- `RGBChannelSeparator`
- `Pixelize`
- `Vignette_Custom`
- `Grain_Custom`
- `LUTColorGrading`
- `LevelAdjustment`
- `Sharpen_Before`
- `Sharpen_After`
- `DownScaleResolution`

第二批，需要临时纹理、多 RT 或 blur pyramid：

- `RGBBlurV2`
- `KawaseBlur`
- `IrisBlur`
- `DualKawaseBlur`
- `GaussianBlur`
- `BokehBlur`
- `Bloom_Custom`

第三批，时间相关或历史帧相关：

- `MotionTrailEffect`
- `ChangeFrameRate`
- `FilmBreath_GateWeave`
- `GateWeave`
- `RLProJitter`
- `RLProVHSRewind`

第四批，复合风格总成：

- `RLProVHSEffect`
- `RLProAnalogTVNoise`
- `RLProOldFilm`
- `RLProOldFilm2_Custom`
- `RLProBleed_Custom`
- `RLProTVEffect_Custom`
- `CRTEffects`
- `Tube`
