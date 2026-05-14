# Shoost 后处理源码来源地图

Shoost v0.16.3 看起来是以 Unity Post Processing Stack v2 作为后处理宿主框架，再整合多个外部后处理包，最后叠加一层 `Custom/*` 自定义效果、改版效果和预设。

重要提醒：Shoost 经 AssetRipper 解包得到的大多数 shader 都包含 `//DummyShaderTextExporter`。这些 shader 通常只适合参考名称、属性和材质引用，不要当作真实算法源码。Shoost 自定义 shader 目前应优先参考 `D:\Unity_Fork\Shoost_v0.16.3\RenderDocShaderDump` 中的 DXBC 反汇编，并结合 `D:\Unity_Fork\Shoost_v0.16.3\DecompileWorkFiles\Cpp2ILOutputs\ISIL` 里的 C# renderer 流程重写。

具体阅读流程见 `ShoostSourceReadingGuide.md`。

## 参考包获取方式

截至 2026-05-15 核对，当前移植 Shoost 后处理主要涉及 5 组参考资料。

可以直接从公开仓库拉取的有 3 组：

- `UnityPostProcessingV2`
  - 仓库：`https://github.com/Unity-Technologies/PostProcessing`
  - 本地：`ReferencePackages~/UnityPostProcessingV2/PostProcessing`
  - 用途：参考 PPS v2 框架、内置 Bloom、Color Grading、Vignette、Grain、Uber/FinalPass 等。
  - 注意：仓库已归档，适合读源码，不建议照搬运行时架构到 URP。
- `XPostProcessing`
  - 仓库：`https://github.com/QianMo/X-PostProcessing-Library`
  - 本地：`ReferencePackages~/XPostProcessing/X-PostProcessing-Library`
  - 用途：参考 Kawase/DualKawase、IrisBlur、Pixelize、RGBSplit/Glitch 等算法写法。
- `KinoPostprocessing`
  - 主仓库：`https://github.com/keijiro/Kino`
  - 旧版/单效果仓库示例：`https://github.com/keijiro/KinoGlitch`、`https://github.com/keijiro/KinoBloom`
  - 本地：`ReferencePackages~/KinoPostprocessing/Kino`、`KinoBloom`、`KinoBokeh`、`KinoGlitch`、`KinoTube`
  - 用途：参考 Kino Glitch、Tube、Bloom、Bokeh 一类效果的算法和命名习惯。
  - 注意：Shoost 里的 `Kino.Postprocessing.dll` 更像 PPS v2/旧版 Kino 系列整合，不一定完全对应当前 HDRP 版 `keijiro/Kino`。

需要你自己找资源包的有 1 组：

- `RetroLookPro`
  - 资源名：`Retro Look Pro`
  - 常见来源：Unity Asset Store / itch.io 的 `Limitless Development` 资源包。
  - 用途：参考 VHS、CRT、OldFilm、Noise、Bleed、TV、NTSC、Jitter 等复杂复古效果。
  - 注意：这是商业资源，不适合直接放进仓库。你可以本地安装/导入后只用来读源码和对照参数。

本地已有、不是外部可拉取仓库的有 1 组：

- `ShoostUnpack`
  - 来源：`D:\Unity_Fork\Shoost_v0.16.3` 下的 AssetRipper、Cpp2IL 和 RenderDoc 输出。
  - 用途：参考 Shoost 自己的参数、预设、renderer 调用顺序、uniform 名和实际编译后 shader 行为。
  - 注意：这组就是 Shoost 本体分析资料。遇到某个 shader/variant 在当前 dump 里缺失，优先提醒重新打开对应滤镜/模式抓帧。

如果只按“要你额外去找资源”来算，目前是 1 组：`RetroLookPro`。如果按“能直接 clone/拉取源码”来算，目前是 3 组：`UnityPostProcessingV2`、`XPostProcessing`、`KinoPostprocessing`。`ShoostUnpack` 已经在本地，不算外部资源。

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
