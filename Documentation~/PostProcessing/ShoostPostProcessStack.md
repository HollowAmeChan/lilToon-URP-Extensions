# lilToon-Shoost 后处理图层栈

`ShoostPostProcessRendererFeature` 是在 URP 中重建 Shoost 后处理图层栈的入口。RendererFeature 只负责把 render pass 安装进管线；真正的图层列表放在 Volume component 里，这样就能像 HTrace 和 URP 后处理 override 一样由 Volume profile 控制。

当前版本有意先做成 Shoost 风格的效果槽位，而不是通用可排序层栈：

- 一个 renderer feature 安装后处理 pass，并提供 HTrace 风格的 `Use Volumes` 开关；
- 一个 `lilToon-Shoost Post Process Stack` Volume override 持有完整效果列表；
- 每种效果只保留一个实例，列表顺序固定，不做拖拽重排；
- 添加入口只显示当前未使用的效果；
- 标题栏只保留最常用的控制，名字、复制粘贴和每层齿轮菜单都不在这里展开；
- 更完整的交互约定写在 [`ShoostPostProcessEditorStyle.md`](ShoostPostProcessEditorStyle.md)。

## Shoost 的用户侧滤镜清单

下面这份是你现在要按 Shoost 面板去对齐的“真正给用户调的滤镜”分类，后面找参考包和图标都按这个口径走：

- 锐化
- 白平衡
- 色阶
- 调色
- 边缘光
- 轮廓
- 投影
- 渐变
- 发光
- ToonMap（URP 扩展：自管 Tonemapping）
- 光照
- 中心色彩校正
- 桑原（URP 扩展：Kuwahara 风格化）
- LED
- 天气
- 粒子
- 摄像头切换器
- 透明背景
- 胶片（可用近似版：按 Shoost 组合入口压成单 pass）
- 电视（Tube，暂时跳过：底层组合链路复杂）
- VHS
- 显示器
- 视频游戏
- 光圈模糊
- 通道模糊
- RGB 分离
- 光斑变焦（URP 扩展：Bokeh Zoom Blur）
- 颗粒
- 暗角
- 像素化
- 帧率限制
- 湍流置换
- 镜头畸变
- 摄像机闪光

其中一部分是纯后处理 shader，一部分更像 Shoost 的场景叠加、UI 驱动或摄像机控制入口。我们在 URP 里会尽量保持它们的用户命名和图标入口一致，但底层实现不一定都是单个 fullscreen pass。当前公开 UI 只显示适合 Shoost Final Stack 的入口；`天气` 已先按 Volume 驱动的相机空间程序化粒子实现雨 / 雪 / 烟雾近似版；`边缘光`、`轮廓`、`投影`、`LED`、`透明背景`、`摄像头切换器` 先隐藏，等主体数据、输入 RT、场景对象或相机合成边界重新确认后再决定是否恢复。

当前已经补上的公开效果包括 `VignetteCustom`、`Sharpen`、`RGBSplit`、`RGBChannelSeparator`、`IrisBlur`、`ColorGradingCustom`、`LevelAdjustment`、`AutoWhiteBalance`、`Fisheye` / `LensDistortionCustom`、`Pixelize`、`Distortion`（湍流置换）、`Glow`、`ToonMap`、`Kuwahara`、`BokehZoomBlur` 和 `ApertureBokeh`。`ToonMap` 不是 Shoost 原包滤镜，而是为了在 Shoost stack 内统一管理最终映射而加入的 URP 扩展项，当前提供 None / Neutral / ACES，其中 Neutral / ACES 直接复用 URP Tonemapping。`Kuwahara / 桑原` 同样是 URP 扩展滤镜，来源是 [`桑原滤镜研究.md`](桑原滤镜研究.md) 的方案整理，按 final stack fullscreen pass 实现基础桑原、色阶、Sobel 线稿和噪声组合，并提供可选高质量模式。`BokehZoomBlur / 光斑变焦` 来自 [`光板变焦滤镜研究.md`](光板变焦滤镜研究.md)，当前按纯屏幕空间高亮提取 + 径向光斑拖影实现，提供高成本质量档位但暂不依赖深度/法线。`ApertureBokeh / 光圈散景` 是独立的全局光圈虚焦近似：不使用深度，直接从最终画面的亮度和边缘提取焦外信号，再用圆形/多边形光圈核向各方向合并成真实摄影式 bokeh。旧的 `KawaseBlur` 和试验性的无方向光斑旧实现已整体摘除，shader、editor filter 和 runtime 调度都不再保留；旧 Volume 数据残留这些槽位时由编辑器自动清理。`LUTColorGrading` 已从公开实现中移除，第 4 个“调色”入口对齐 Shoost 的 `ColorGrading_Custom`：包含普通 `Lift / Gamma / Gain` 色轮、对数 `Shadows / Midtones / Highlights` 色轮，以及六色偏移的 `HueVsHue / HueVsSat / HueVsLum / LumVsSat` 模式。`DownScaleResolution` 只保留旧资产兼容，不再作为公开图层入口；新面板里应该用 `Pixelize`。`SharpenAfter` 也不再作为公开图层入口，用户侧统一用 `SharpenBefore`；当前普通 Shoost 滤镜默认已经作为最终滤镜后置执行，旧 `SharpenAfter` 只保留兼容。

## 设置

1. 把 `ShoostPostProcessRendererFeature` 加到 URP renderer data asset。
2. 保持 renderer feature 上的 `Use Volumes` 开启。
3. 在 Volume profile 里通过 `Add Override > Post-processing > lilToon-Shoost` 添加 `Post Process Stack`。
4. 启用这个 override，并在 `Layers` 列表里从添加菜单补齐需要的效果。
5. 烟雾测试可以先使用没有 override 的 `CustomMaterial`。它会解析到 `Hidden/lilToon-Shoost/URP/Shoost/PostProcessLayerBlit`。
6. 把 `Color` 改成非白色，或者修改 `Blend Mode`，确认图层确实在运行。

## 图层混合

Shoost 自己的 `BlendingModeChanger.BlendType` 里有一套 Photoshop 式混合模式，包括 `Darken`、`Add`、`Screen`、`SoftLight` 等。当前 `CustomMaterial` 默认解析到的 `PostProcessLayerBlit` 已经实现这套混合模式，所以可以先用它验证“加光源”和“压暗”这类图层叠加：

- 做加光：添加 `CustomMaterial` 图层，`混合模式` 选 `加亮`、`滤色`、`颜色减淡`、`叠加` 或 `柔光`，再用 `颜色` 和 `强度` 控制范围外的全局叠加；如果有光斑/渐变贴图，把它放到 `纹理`。
- 做压暗：添加 `CustomMaterial` 图层，`混合模式` 选 `变暗`、`正片叠底`、`颜色加深` 或 `线性加深`；如果只是暗角，优先使用 `暗角` 效果的 `压暗` 模式。
- 这里移植的是 Shoost 的图层混合思路，不等同于 `LightingGradientValue` / `FlareValue` 里那些场景对象、粒子、体积光和 RawImage 组合。那些属于 Shoost UI/场景叠加系统，后面要单独决定是否放进后处理栈。

## 顺序说明

Volume 里只有一个面向用户的大图层列表。Shoost stack 不再提供每层 `Injection Point`：所有 Shoost 图层都会进入 `AfterRenderingPostProcessing`，作为 URP 内置后处理之后的 final stack 执行。

补充一点：`ColorGradingCustom` 在当前 URP 移植里按最终显示空间调色处理，默认走 `After URP Post Processing`。如果后续需要 scene-linear / pre-Bloom 的调色，应把它拆到 HoPost 或新的 pre-post 管线里，而不是让 Shoost final stack 同时承担两套语义。`LevelAdjustment` 同样按最终重映射处理，默认走 `After URP Post Processing`。

重要事项：URP 内置后处理不会显示为 renderer data asset 里的 `ScriptableRendererFeature`。即使可见的 renderer feature 列表里只有 lilOIT、HTrace、Shoost 这些自定义功能，只要相机开启了 `Render Post Processing`，并且有 active 的 Volume override，URP 仍然会从 `UniversalRenderer` 内部注入自己的后处理 pass。

在 URP 17.3 中，内置后处理的大致顺序是：

- `ColorGradingLutPass` 位于 `BeforeRenderingPrePasses`；
- 自定义 pass 的 `BeforeRenderingPostProcessing` 组；
- URP `PostProcessPass` 位于 `AfterRenderingPostProcessing - 1`；
- 自定义 pass 的 `AfterRenderingPostProcessing` 组；
- 当 FXAA、最终缩放、TAA sharpening 或类似最终阶段功能需要运行时，URP `FinalPostProcessPass` 会接近 `AfterRendering - 1`。

这意味着 Shoost stack 统一运行在 URP 主后处理之后；在某些相机配置下，后面仍可能接着 URP 的 final post pass。需要 Bloom 响应或需要主体数据的效果后续应移动到 subject effects / lighting feature，而不是重新塞进 Shoost 图层插入点。

图层列表的顺序会在当前 final stack 内部保留。主体数据、HDR 发光和 pre-Bloom 合成后续应移动到新的 subject effects / lighting feature；调色、CRT mask、final sharpen、VHS、颗粒、像素化，以及已经明确为 LDR 纯后期 bloom 的 `Glow / 发光`，则统一留在 Shoost final stack。当前固定排序里 `BokehZoomBlur / 光斑变焦` 和 `ApertureBokeh / 光圈散景` 位于 RGB 分离/通道分离之后、`Glow / 发光` 之前，让它们产出的高亮拖影/散焦光斑还能继续进入 Shoost 内部发光；`Glow / 发光` 被放到后段，在天气、胶片、VHS、CRT、dither、模糊和 RGB 分离之后执行；`ToonMap` 紧跟 `Glow`，用于在 Shoost stack 内做最终 Neutral / ACES 映射；两者仍早于颗粒、暗角、像素化和帧率限制这类最终显示收尾效果。

从 Shoost v0.16.3 解包结果看，一共找到 59 个 `BeforeStack` 和 16 个 `AfterStack` 的 PPS v2 effect：

- 大多数 Shoost 自定义效果和 Retro Look Pro 效果都是 `PostProcessEvent.BeforeStack`；
- `CRTEffects`、`RGBChannelSeparator`、`Sharpen_After`、`RLProTVEffect_Custom` 是 `PostProcessEvent.AfterStack`；
- 很多 X-PostProcessing 的 blur / sharpen / glitch 效果也是 `PostProcessEvent.AfterStack`；
- 用户侧不再单独显示 `Sharpen_After`；`锐化` 作为普通 Shoost final stack 滤镜默认后置执行；
- blur pyramid 和 bloom-like 效果通常需要在最终颜色和细节叠加前运行；
- `MotionTrail`、`ChangeFrameRate` 这类历史帧效果需要 per-camera 持久 buffer，后面应作为同一个 Volume 列表背后的专用执行阶段处理。

如果源码包、Cpp2IL renderer 或 Shoost 预设引用了某个 shader / pass / variant，但当前 RenderDoc dump 里找不到，优先判断为当前抓帧没有覆盖对应开关或模式。记录缺口，然后让目标滤镜或模式开启后重新抓一帧。

## 移植流程

每个 Shoost 效果可以按这个顺序移植：

1. 选择对应的 `ShoostPostProcessEffect` enum 槽。
2. 读 AssetRipper 导出的 Shoost setting class，确定哪些 inspector 字段需要变成真实 typed setting。
3. 读 Cpp2IL ISIL renderer 输出，提取 `Shader.Find`、uniform 名、临时 render texture 分配、pass 顺序和 blit 顺序。
4. 读 RenderDoc `.dxbc.asm` dump，还原 shader 行为。
5. 在 `Hidden/lilToon-Shoost/URP/Shoost/...` 下实现对应 shader。
6. 如果单次 blit 不够，就为该 enum 槽补专用 C# 调度逻辑。

如果某个效果或 variant 在 C# 或参考包里出现，但当前 RenderDoc dump 里没有，优先认为这次抓帧没覆盖对应滤镜或模式。记录缺失项，并在目标开关打开后重新抓帧。

## 当前边界

现在的图层栈可以处理简单的单 pass 全屏效果和 custom material 实验。更复杂的 Shoost 效果还需要专用 runtime 代码：

- 历史 buffer：`MotionTrail`、`ChangeFrameRate`；
- 生成 lookup texture：`GrainCustom`；
- blur pyramid 或多个临时 render target：`IrisBlur`、`RGBBlurV2`、`Glow`；`RGBBlurV2` 用户侧只暴露三个 RGB 模糊度，但运行时仍按 Shoost 的临时 RT blur + 原图合成思路处理；
- 多 pass 复古合成：`CRTEffects`、`Tube`、Retro Look Pro 派生 custom 效果。

保持 Volume 图层栈作为用户面对的排序界面，具体效果移植时再在 enum 槽背后添加专用执行代码。

如果目标是完全由 Shoost 控制画面风格，等等效 Shoost 图层实现后，最好关闭相机内置的 `Render Post Processing`。只有在明确需要过渡混用 URP Bloom、Tonemapping、FXAA 或 Color Adjustments 时才保留它；如果只是需要最终 Tonemapping，优先在 Shoost stack 里使用 `ToonMap`，让 Glow、HDR 合成和最终映射顺序都留在同一个 Volume 面板里管理。

## 当前对齐状态补记

- `GrainCustom / 颗粒`：已按 Shoost 对齐完成。当前实现使用 Shoost 解包里的 blue-noise/alpha 噪声图，shader 会读取 Alpha 通道噪声，并默认放在 `After URP Post Processing`，与 Shoost 最终画面颗粒叠加时机一致。状态标记为：完美对齐。
- `DitheringCustom / 视频游戏`：来源是 Shoost 的 `Custom/DitheringCustom`，原始 PPS v2 事件为 `BeforeStack`。用户侧面板暴露“模式（单色调/颜色）”“分辨率”“抖动类型（V1/V2/V3）”“网格线”，单色调模式使用“亮度阶调 + 阴影/中间调/高光”，颜色模式使用 RGB 三通道阶调和混合量。
- `DitheringCustom` 的三张抖动图来自 Shoost 解包工程的 `dithering_2x2_4_Steps_v2.png`、`dithering_2x2_4_Steps_v4.png`、`dithering_4x4_16_Steps.png`，在包内对应 `ShoostDitheringV1/V2/V3.png`。
- `DitheringCustom / 视频游戏`：已由实机核对确认完美对齐。当前实现已按 Shoost renderer 的 `_ResolutionX/_ResolutionY` 思路修正屏幕像素宽高归一化，并修正网格线为屏幕像素稳定宽度。
- `CRTEffects / 显示器`：已由实机核对确认完美对齐。来源是 Shoost 的 `Custom/CRTEffects`。用户侧只暴露“类型（RGB/RGB 单色/圆形/线条）”和“分辨率”，类型实际对应 Shoost UI 中 `_scanlineTexture` 列表的四张贴图：`crt_scanlines_A_v1.png`、`crt_scanlines_A_v2.png`、`crt_scanlines_D_v2.png`、`crt_scanlines_B.png`。shader 按 RenderDoc 中 `Hidden/CRTEffects` 第一 pass 的思路实现：以 FC `256x240` 为基础，并按 `当前屏幕宽高比 / (256/240)` 修正横向分辨率，再重复采样扫描线贴图，用 slot mask、shadow mask、brightness 和 glow 叠加到源图上。
- `RGBChannelSeparator / RGB 通道分离`：已从旧实现上位到公开入口。它是直接消费最终 camera color 的单 pass 通道查看/分离滤镜，保留 Shoost 用户侧入口，不再放在“旧实现”栏。
- `VHS`：来源是 Shoost 的 `PostProcess_VHSValue` 组合滤镜。Shoost 用户侧以弱/中/强三档切换 profile，并联动 `RLProVHSEffect`、`RLProNoise2_Custom`、`RLProEdgeNoise`、`RGBBlurV2`、`Grain_Custom`、`Sharpen_Before`、`Tube`，扫描线子开关来自 `RLProTVEffect_Custom`。当前 URP 版压成一个用户滤镜和一个 fullscreen pass，暴露“类型、噪点强度、锐化、扫描线、大小”，默认作为 `After URP Post Processing` 的最终滤镜执行。已由实机核对确认完美对齐，状态标记为：完美对齐。
- `Gradient / 渐变`：已由实机核对确认完美对齐。来源是 Shoost 的 `GradientValue` / `Layer_GradientValue` 和 `Custom/AnimeComposition` 中的 Gradient Generator 参数。用户侧对齐 Shoost 面板的“类型、混合模式、颜色 1/颜色 2、反相、半径、柔和度、偏移 X/Y、角度、不透明度”；当前 URP 版作为 final stack 的单 pass fullscreen 效果执行。Shoost 的透明背景/排除背景开关属于透明源与图层合成语义，在 URP camera color 后处理里不参与。
- `CenterColorCorrection / 中心色彩校正`：来源是 Shoost `CenterColorCorrectionValue` 和 `Custom/AnimeComposition` 材质属性。当前 URP 版按纯 fullscreen 后处理实现，用户侧暴露“饱和度、色相、亮度、对比度、反相、半径、柔和度、中心位置 X/Y、不透明度”，其中“色相”是 URP 扩展参数，非 Shoost 原生项。默认值对齐当前面板截图：饱和度 `0.18`、色相 `0`、半径 `0.5`、柔和度 `0.5`、不透明度 `1`。shader 以中心圆形 mask 混合校正结果，按最终 LDR 画面处理。
- `Kuwahara / 桑原`：URP 扩展滤镜，不来自 Shoost 原包。参考 [`桑原滤镜研究.md`](桑原滤镜研究.md) 的“平衡 / 高质量”方案，在一个 fullscreen pass 中实现 Kuwahara 保边平滑、可选色阶、Sobel 线稿和噪声。用户侧暴露“质量、半径、色阶、线稿颜色、线稿强度、线稿阈值、噪声强度”，质量为“基础 / 平衡 / 高质量”；默认半径 `3`、平衡质量、色阶 `0`（关闭）、线稿强度 `0.25`、噪声 `0.05`。色阶开启后使用亮度量化再回乘原色比例，尽量保留色相；高质量模式会启用更多候选区域，成本更高但更适合强绘画感或截图。
- `BokehZoomBlur / 光斑变焦`：URP 扩展滤镜，不来自 Shoost 原包。参考 [`光板变焦滤镜研究.md`](光板变焦滤镜研究.md) 的 Bokeh Zoom Blur 方案，当前实现为单 pass fullscreen：先按曝光、阈值和 soft-knee 提取高亮，再沿屏幕中心径向采样形成方向性光斑变焦拖影，并支持叶片数、叶片曲率、旋转、色散、染色、亮度衰减、增益、相加/滤色/叠加/正常混合和“只显示光斑层”。质量档位为“快速 / 平衡 / 高质量”，分别对应 8 / 16 / 32 次采样；默认使用高质量、半径 `1`、光斑增益 `0.5`、阈值 `0`、阈值柔化 `0`、曝光 `1`、亮度衰减 `4`、叶片数 `0`、叶片曲率 `1`、色散 `1`、相加混合。固定排序位于 `Glow / 发光` 之前，方便用内部 Glow 继续扩散光斑。
- `ApertureBokeh / 光圈散景`：URP 扩展滤镜，不来自 Shoost 原包。它模拟全局焦外光圈成像，不读取深度，默认把整张画面当作同一焦外平面。实现结构参考 Unity DoF/Bokeh 的成熟管线，改为 prefilter/downsample、半分辨率 disk kernel bokeh blur、小 post filter、composite 四个 pass：先按亮度阈值、soft-knee 和局部边缘梯度提取焦外信号，再用圆形或叶片多边形 aperture kernel 从各方向采样合并，得到远景虚焦时一块块圆形光斑的感觉。主要参数为 `光圈大小`、`亮度阈值`、`阈值柔化`、`曝光`、`边缘提取`、`光斑硬度`、`叶片数 / 曲率 / 旋转`、`色散`、`光斑增益`、`叠加模式` 和 `只显示光斑层`。默认使用高质量、光圈大小 `1`、光斑增益 `4`、亮度阈值 `0.4`、阈值柔化 `0.2`、曝光 `1`、边缘提取 `0.35`、光斑硬度 `1`、叶片数 `0`、叶片曲率 `1`、叶片旋转 `0`、色散 `0.35`、相加混合，并关闭 `只显示光斑层`。固定排序同样位于 `Glow / 发光` 之前。
- `Glow / 发光`：已按 Shoost 的 `GlowValue` / Kino `Bloom_Custom` 对齐完成。当前 URP 版是不依赖 HDR 的纯后期 LDR bloom，多 pass 流程为阈值 soft-knee 预滤波、模糊金字塔、模式化方向采样和最终合成；用户侧对齐 Shoost 面板的“阈值、阈值平滑、半径、强度、饱和度、颜色、不透明度、发光类型”，三种类型为“正常 / 条纹 / 星芒”，星芒额外暴露数量和角度。当前默认阈值为 `0.9`，默认强度为 `2.0`，强度 UI 上限为 `12.0`。状态标记为：完美对齐。
- `ToonMap`：URP 扩展滤镜，不来自 Shoost 原包。用途是在关闭 URP 内置 Tonemapping 后，仍能把 Shoost stack 内保留的 HDR 颜色统一映射到最终显示范围。用户侧只暴露“模式”，包含 `None / Neutral / ACES`，默认 `ACES`；`None` 不改变颜色，`Neutral` 和 `ACES` 分别复用 URP 的 `NeutralTonemap` 和 `AcesTonemap(unity_to_ACES(...))`。固定排序紧跟 `Glow / 发光`，早于 Grain、Vignette、Pixelize 和 ChangeFrameRate。
- `Weather / 天气`：来源是 Shoost 的 `ParticleValue` 和 `Particle_Weather_Rain/Snow/Smoke` prefab，不是 PPS fullscreen shader。Unity `ParticleSystem` 场景粒子路线在 RendererFeature/Volume 调参时存在编辑器崩溃风险，当前 URP 版改为稳定的 fullscreen 程序化粒子 pass，但仍按相机空间 2D 合成层处理，并继续作为 Shoost final stack 图层跑在 `AfterRenderingPostProcessing`。HDR 颜色当前用于 Shoost 内部合成强度，不依赖 URP 内置 Bloom；后续如果需要发亮链路，应在 Shoost stack 内新增带 LUT 输入/可管理的泛光滤镜，而不是把 Weather 提前到 URP 后处理之前。用户侧按折叠组暴露“基础 / 假景深 / 粒子变化”：基础包含“粒子、HDR 颜色、发生率、不透明度、叠加模式”，假景深包含“焦距、虚化强度、虚化柔化、虚化曲线”，粒子变化包含“速度、数量、大小、随机、漂移、层次、上下不均、明暗变化”。默认发生率为 `1`，默认随机为 `0.35`，避免程序化格子粒子过早出现截断感。粒子为“雨 / 雪 / 烟雾 / 灰尘”。雨参考原 prefab 的 `Particle_Rain_Storm / S / M / L / Storm_L` 五层比例，雪参考 `Particle_Snow_BG / M / L` 加烟雾层，烟雾参考 `Particle_Smoke_BG / L` 两层软粒子。`灰尘` 是 URP 扩展模式，基于雪式漂浮粒子但加入细尘、中层颗粒、近景软斑和薄雾层。`焦距` 与虚化参数是 URP 扩展的假景深控制：每层程序化粒子分配伪深度，焦距前方的近层会按曲线变宽变软，远层保持较实；`层次` 控制伪深度分布宽度，`上下不均` 控制 Shoost 式上下质量分布。叠加模式当前提供“正常 / 加亮 / 滤色 / 柔光”，其中加亮路径保留 HDR 颜色强度。状态标记为：相机空间程序化粒子近似版。
- `Tube / 电视`：暂时跳过。来源是 Shoost 的 `PostProcess_TVValue` 组合滤镜，用户侧 60/70/80/90 四档不是单个 `Custom/Tube` shader 的模式，而是 profile 组合：`FilmBreath_GateWeave`、`RGBBlur`、`LUTColorGrading`、`Tube`、`Sharpen_Before` 等层，60 年代还包含 `RLProJitter`。此前尝试把它压进单个 fullscreen pass，但 LUT、锐化、Tube/YIQ 漏色、年代 profile 和第三方包语义之间耦合较深，当前不继续对齐。状态标记为：暂时跳过。
- `Film / 胶片`：来源是 Shoost 的 `PostProcess_FilmValue` 组合入口和 `AMS_AnimeFilm_60s/70s/80s/90s` profile。当前 URP 版已从旧的裸 `FilmBreath_GateWeave` 调试参数改成 Shoost 面板语义：模式、滤镜类型、滤镜强度、锐化、颗粒强度、颗粒大小、屏幕抖动量。运行时先压成一个 fullscreen pass，近似串联 LUTColorGrading、RGBBlur、FilmBreath/GateWeave、RLProOldFilm2_Custom 和 Grain_Custom，目标是稳定可用、不报错；仍未标记为完美对齐。
- `EdgeLight / 边缘光`、`Outline / 轮廓`、`DropShadow / 投影`：当前从 Shoost Final Stack 公开入口隐藏。它们需要主体 mask/stencil/depth/normal 或独立 subject RT，不应作为只消费 camera color 的普通图层添加。
- `LED`、`TransparentBackground / 透明背景`、`CameraSwitcher / 摄像头切换器`：当前从公开入口隐藏。它们更接近输入 RT、场景对象、相机或合成控制，对当前最终滤镜风格没有直接意义；后续如果 stack list 能明确控制输入 RT 或场景合成语义，再重新设计入口。
- LUT 语义备忘：TV 组合的 `AMS_TV_60s/70s/80s/90s` 分别引用 `Monochrome Soft`、`Film Fuji v2`、`Film Fuji v2`、`Film Fuji v3`；胶片组合的 `AMS_AnimeFilm_60s/70s/80s/90s` 分别引用 `Monochrome Soft`、`Film Kodak v1`、`Film Kodak v2`、`Film Kodak v3`。这两组 60/70/80/90 只共享年代 UI 命名，不共享滤镜语义。RenderDoc 中 `Hidden/Custom/LUTColorGrading` 的 32x32 strip 是 B 通道切片、R 为横向、G 为纵向；LUT 纹理按非 sRGB 导入，由 shader 显式执行 sRGB/Linear 转换。该备忘仅保留给后续重启 Tube/胶片移植时参考。

## 图层系统重构方向

Shoost 的核心不是“某一个 URP pass”，而是一个可以叠很多层、每层再挂效果和混合模式的最终图层系统。后续更推荐把现在已经移植的 Shoost 滤镜逐步收敛成 `Shoost Final Stack`：默认在 URP 内置后处理之后执行，只处理最终画面或显式输入的图层 RT，不再承担角色边缘光、轮廓、投影这类需要主体数据的职责。

执行点建议不要粗暴统一成 `AfterRendering`。在 URP 里 `AfterRenderingPostProcessing` 更适合作为 Shoost 最终滤镜默认位置：它已经晚于 URP Bloom / Tonemapping / FXAA / Color Adjustments，但通常仍能稳定读写 camera color。`AfterRendering` 可以保留给最终覆盖、截图/导出、调试预览或明确需要在所有渲染之后执行的少数层。

移动执行点时必须同时看 HDR 语义。URP Bloom 只会响应它之前写进 HDR camera color 的高亮能量；Tonemapping 之后的画面通常已经被压到显示范围，很多“加亮”只是在 LDR 画面上叠白，不会再触发 Bloom。迁移时按意图分流：

- 需要给 URP Bloom 提供 HDR 能量的效果：边缘光、光照、镜头闪光、部分强 emissive/glow 合成，应放在 URP 内置后处理前，或放进自研 subject effects / lighting feature。已实现的 `Glow / 发光` 例外：它是 Shoost/Kino 风格的 LDR final-stack bloom，自身负责扩散和合成，不再依赖 URP Bloom。
- 只改变最终观感的效果：颗粒、CRT scanline、VHS 噪声、像素化、最终色阶、最终暗角，可以放在 URP 后处理之后。
- 色彩类效果要明确是 HDR/scene-linear 调整还是 LDR/display-space 调整；同一个参数放在 Tonemapping 前后会有不同手感。
- 如果 Shoost Final Stack 需要在 URP 后处理之后继续用 HDR 数值，必须确保 renderer 的 camera color 仍是 HDR RT，并明确哪些层允许写超过 1 的值；否则默认按 LDR 最终图层看待。

新的分层边界建议如下：

- `lilToon / URP 渲染阶段`：正常输出 camera color、depth，以及 lilToon 自己的 subject normal/mask/depth/color 等可选 RT。
- `自研主体效果 RendererFeature`：消费 lilToon 输出的主体数据，实现边缘光、轮廓、投影、二次打光等需要角色边界的效果；需要 Bloom 的效果放在 URP 内置后处理前。
- `URP 内置后处理`：Bloom、Tonemapping、FXAA、Color Adjustments 等项目级画面处理。
- `Shoost Final Stack`：消费最终 camera color 和可选图层 RT，执行 Shoost 风格的图层、滤镜、混合、颗粒、CRT、VHS、像素化、色阶、最终调色等纯最终处理。

这样 Shoost 这一块可以被归类为“最终图层/滤镜系统”，而不是混杂承担渲染数据生产。Tube、胶片、VHS 这类 Shoost profile 组合以后也更适合在这个 final stack 里按图层组合恢复；边缘光、轮廓、投影则移动到新的 subject effects 管线，只在 UI 或参数命名上参考 Shoost。

## 透明源语义与重写边界

Shoost 原始工作流更像在处理一个可能带 alpha 的图片/视频源，而不是 URP 相机的最终不透明颜色缓冲。Shoost 场景里默认有背景、角色、前景三层，很多用户侧效果实际只作用于角色或前景，背景会被 alpha 或源图层边界自然排除。URP fullscreen post 看到的通常是已经合成后的 camera color，背景、角色和前景已经混在一起，alpha 也未必还保留可用语义。因此后续迁移分成三类：

- `直接按 Shoost/RenderDoc 复刻`：主要依赖最终屏幕颜色的滤镜。包括 `SharpenBefore`、`AutoWhiteBalance`、`LevelAdjustment`、`ColorGradingCustom`、`VignetteCustom`、`Pixelize`、`Distortion`、`Fisheye / LensDistortionCustom`、`RGBSplit`、`RGBChannelSeparator`、`GrainCustom`、`DitheringCustom`、`CRTEffects`、`VHS`。这些可以继续走 fullscreen pass 或现有多 pass 调度。`Kuwahara / 桑原`、`BokehZoomBlur / 光斑变焦` 和 `ApertureBokeh / 光圈散景` 是自研 URP 扩展，虽然同样只依赖最终屏幕颜色，但不标记为 Shoost 原包复刻。
- `按 Shoost 语义实现，但需要专用调度`：不是透明源问题，主要是运行时结构更复杂。包括 `IrisBlur`、`RGBBlurV2`、`Glow` 这类多 RT / blur pyramid，`MotionTrail`、`ChangeFrameRate` 这类历史 buffer，以及 `CustomMaterial` 的 Photoshop 式混合。
- `不要硬搬 Shoost，改成 URP 主体数据效果或明确的合成层`：强依赖透明源、主体边界、alpha 或 Shoost 场景层级的效果。包括 `EdgeLight / 边缘光`、`Outline / 轮廓`、`DropShadow / 投影`，以及大概率需要重新设计的 `Lighting / 光照`、`TransparentBackground / 透明背景`、`Particle / 粒子`、`CameraSwitcher / 摄像头切换器`。这些不应该从最终 camera color 里猜 alpha，应使用明确的角色 mask、stencil、depth、normal、单独的角色渲染目标或明确的场景合成对象。`Glow / 发光` 已按纯后期 LDR bloom 对齐，不再归入这一类；`Weather / 天气` 当前按相机空间程序化粒子处理。

建议的新数据契约：

- 普通画面滤镜只读 camera color。
- 主体相关滤镜读取角色 mask / stencil，并可选读取 depth、normal、motion 或单独角色 color。
- 背景、角色、前景的分层语义不要默认等同于 URP 最终颜色缓冲；需要分层时，应在渲染管线里显式产出 mask 或中间 RT。

三个高优先级重写项：

- `EdgeLight / 边缘光`：用角色 normal + view direction 做 rim，乘角色 mask/stencil 控制作用范围；需要时再叠加深度边缘，避免背景参与。
- `Outline / 轮廓`：用 depth + normal 的屏幕空间描边，最好配合角色 stencil 或 layer mask 限定对象；不要按 Shoost 的透明图片边缘膨胀硬搬。
- `DropShadow / 投影`：不要依赖最终图像 alpha 做偏移阴影。优先考虑基于角色 mask/depth 的投影，或基于 normal/depth 的二次打光/接触阴影式实现，让阴影和 URP 场景空间对齐。

边缘光的第一版设计见 [`ShoostEdgeLightDesign.md`](ShoostEdgeLightDesign.md)。结论是 lilToon 材质侧可以有“写入主体数据”的开关，但它只负责 opt-in 到 normal/mask RT；实际边缘光颜色、亮度、方向、模式和 pre-Bloom 合成都放在 Shoost EdgeLight 图层里处理。
