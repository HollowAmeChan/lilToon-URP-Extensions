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
- 光照
- 中心色彩校正
- LED
- 天气
- 粒子
- 摄像头切换器
- 透明背景
- 胶片（暂时跳过：底层组合链路复杂）
- 电视（Tube，暂时跳过：底层组合链路复杂）
- VHS
- 显示器
- 视频游戏
- 光圈模糊
- 通道模糊
- RGB 分离
- 颗粒
- 暗角
- 像素化
- 帧率限制
- 湍流置换
- 镜头畸变
- 摄像机闪光

其中一部分是纯后处理 shader，一部分更像 Shoost 的场景叠加、UI 驱动或摄像机控制入口。我们在 URP 里会尽量保持它们的用户命名和图标入口一致，但底层实现不一定都是单个 fullscreen pass。

当前已经补上的具体效果包括 `VignetteCustom`、`Sharpen`、`RGBSplit`、`KawaseBlur`、`IrisBlur`、`ColorGradingCustom`、`LevelAdjustment`、`AutoWhiteBalance`、`Fisheye` / `LensDistortionCustom`、`Pixelize`、`Distortion`（湍流置换）和 `RGBChannelSeparator`。`LUTColorGrading` 已从公开实现中移除，第 4 个“调色”入口对齐 Shoost 的 `ColorGrading_Custom`：包含普通 `Lift / Gamma / Gain` 色轮、对数 `Shadows / Midtones / Highlights` 色轮，以及六色偏移的 `HueVsHue / HueVsSat / HueVsLum / LumVsSat` 模式。`DownScaleResolution` 只保留旧资产兼容，不再作为公开图层入口；新面板里应该用 `Pixelize`。`SharpenAfter` 也不再作为公开图层入口，用户侧统一用 `SharpenBefore`；当前普通 Shoost 滤镜默认已经作为最终滤镜后置执行，旧 `SharpenAfter` 只保留兼容。

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

图层列表的顺序会在当前 final stack 内部保留。主体数据、HDR 发光和 pre-Bloom 合成后续应移动到新的 subject effects / lighting feature；调色、CRT mask、final sharpen、VHS、颗粒、像素化这类最终滤镜则统一留在 Shoost final stack。

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
- blur pyramid 或多个临时 render target：`KawaseBlur`、`IrisBlur`、`RGBBlurV2`；`RGBBlurV2` 用户侧只暴露三个 RGB 模糊度，但运行时仍按 Shoost 的临时 RT blur + 原图合成思路处理；
- 多 pass 复古合成：`CRTEffects`、`Tube`、Retro Look Pro 派生 custom 效果。

保持 Volume 图层栈作为用户面对的排序界面，具体效果移植时再在 enum 槽背后添加专用执行代码。

如果目标是完全由 Shoost 控制画面风格，等等效 Shoost 图层实现后，最好关闭相机内置的 `Render Post Processing`。只有在明确需要过渡混用 URP Bloom、Tonemapping、FXAA 或 Color Adjustments 时才保留它。

## 当前对齐状态补记

- `GrainCustom / 颗粒`：已按 Shoost 对齐完成。当前实现使用 Shoost 解包里的 blue-noise/alpha 噪声图，shader 会读取 Alpha 通道噪声，并默认放在 `After URP Post Processing`，与 Shoost 最终画面颗粒叠加时机一致。状态标记为：完美对齐。
- `DitheringCustom / 视频游戏`：来源是 Shoost 的 `Custom/DitheringCustom`，原始 PPS v2 事件为 `BeforeStack`。用户侧面板暴露“模式（单色调/颜色）”“分辨率”“抖动类型（V1/V2/V3）”“网格线”，单色调模式使用“亮度阶调 + 阴影/中间调/高光”，颜色模式使用 RGB 三通道阶调和混合量。
- `DitheringCustom` 的三张抖动图来自 Shoost 解包工程的 `dithering_2x2_4_Steps_v2.png`、`dithering_2x2_4_Steps_v4.png`、`dithering_4x4_16_Steps.png`，在包内对应 `ShoostDitheringV1/V2/V3.png`。
- `DitheringCustom / 视频游戏`：已由实机核对确认完美对齐。当前实现已按 Shoost renderer 的 `_ResolutionX/_ResolutionY` 思路修正屏幕像素宽高归一化，并修正网格线为屏幕像素稳定宽度。
- `CRTEffects / 显示器`：已由实机核对确认完美对齐。来源是 Shoost 的 `Custom/CRTEffects`。用户侧只暴露“类型（RGB/RGB 单色/圆形/线条）”和“分辨率”，类型实际对应 Shoost UI 中 `_scanlineTexture` 列表的四张贴图：`crt_scanlines_A_v1.png`、`crt_scanlines_A_v2.png`、`crt_scanlines_D_v2.png`、`crt_scanlines_B.png`。shader 按 RenderDoc 中 `Hidden/CRTEffects` 第一 pass 的思路实现：以 FC `256x240` 为基础，并按 `当前屏幕宽高比 / (256/240)` 修正横向分辨率，再重复采样扫描线贴图，用 slot mask、shadow mask、brightness 和 glow 叠加到源图上。
- `VHS`：来源是 Shoost 的 `PostProcess_VHSValue` 组合滤镜。Shoost 用户侧以弱/中/强三档切换 profile，并联动 `RLProVHSEffect`、`RLProNoise2_Custom`、`RLProEdgeNoise`、`RGBBlurV2`、`Grain_Custom`、`Sharpen_Before`、`Tube`，扫描线子开关来自 `RLProTVEffect_Custom`。当前 URP 版压成一个用户滤镜和一个 fullscreen pass，暴露“类型、噪点强度、锐化、扫描线、大小”，默认作为 `After URP Post Processing` 的最终滤镜执行。已由实机核对确认完美对齐，状态标记为：完美对齐。
- `Tube / 电视`：暂时跳过。来源是 Shoost 的 `PostProcess_TVValue` 组合滤镜，用户侧 60/70/80/90 四档不是单个 `Custom/Tube` shader 的模式，而是 profile 组合：`FilmBreath_GateWeave`、`RGBBlur`、`LUTColorGrading`、`Tube`、`Sharpen_Before` 等层，60 年代还包含 `RLProJitter`。此前尝试把它压进单个 fullscreen pass，但 LUT、锐化、Tube/YIQ 漏色、年代 profile 和第三方包语义之间耦合较深，当前不继续对齐。状态标记为：暂时跳过。
- `Film / 胶片`：暂时跳过。Shoost 的 `AMS_AnimeFilm_60s/70s/80s/90s` 和 TV 的年代命名只共享 UI 名称，不共享滤镜语义；胶片入口需要单独处理 `LUTColorGrading`、`Grain_Custom`、`RLProOldFilm2_Custom` 等 profile 层，并且还要重新核对各层顺序、LUT 导入与 RenderDoc 汇编。状态标记为：暂时跳过。
- LUT 语义备忘：TV 组合的 `AMS_TV_60s/70s/80s/90s` 分别引用 `Monochrome Soft`、`Film Fuji v2`、`Film Fuji v2`、`Film Fuji v3`；胶片组合的 `AMS_AnimeFilm_60s/70s/80s/90s` 分别引用 `Monochrome Soft`、`Film Kodak v1`、`Film Kodak v2`、`Film Kodak v3`。这两组 60/70/80/90 只共享年代 UI 命名，不共享滤镜语义。RenderDoc 中 `Hidden/Custom/LUTColorGrading` 的 32x32 strip 是 B 通道切片、R 为横向、G 为纵向；LUT 纹理按非 sRGB 导入，由 shader 显式执行 sRGB/Linear 转换。该备忘仅保留给后续重启 Tube/胶片移植时参考。

## 图层系统重构方向

Shoost 的核心不是“某一个 URP pass”，而是一个可以叠很多层、每层再挂效果和混合模式的最终图层系统。后续更推荐把现在已经移植的 Shoost 滤镜逐步收敛成 `Shoost Final Stack`：默认在 URP 内置后处理之后执行，只处理最终画面或显式输入的图层 RT，不再承担角色边缘光、轮廓、投影这类需要主体数据的职责。

执行点建议不要粗暴统一成 `AfterRendering`。在 URP 里 `AfterRenderingPostProcessing` 更适合作为 Shoost 最终滤镜默认位置：它已经晚于 URP Bloom / Tonemapping / FXAA / Color Adjustments，但通常仍能稳定读写 camera color。`AfterRendering` 可以保留给最终覆盖、截图/导出、调试预览或明确需要在所有渲染之后执行的少数层。

移动执行点时必须同时看 HDR 语义。URP Bloom 只会响应它之前写进 HDR camera color 的高亮能量；Tonemapping 之后的画面通常已经被压到显示范围，很多“加亮”只是在 LDR 画面上叠白，不会再触发 Bloom。迁移时按意图分流：

- 需要给 Bloom 提供能量的效果：边缘光、光照、发光、镜头闪光、部分强 emissive/glow 合成，应放在 URP 内置后处理前，或放进自研 subject effects / lighting feature。
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

- `直接按 Shoost/RenderDoc 复刻`：主要依赖最终屏幕颜色的滤镜。包括 `SharpenBefore`、`AutoWhiteBalance`、`LevelAdjustment`、`ColorGradingCustom`、`VignetteCustom`、`Pixelize`、`Distortion`、`Fisheye / LensDistortionCustom`、`RGBSplit`、`RGBChannelSeparator`、`GrainCustom`、`DitheringCustom`、`CRTEffects`、`VHS`。这些可以继续走 fullscreen pass 或现有多 pass 调度。
- `按 Shoost 语义实现，但需要专用调度`：不是透明源问题，主要是运行时结构更复杂。包括 `KawaseBlur`、`IrisBlur`、`RGBBlurV2` 这类多 RT / blur pyramid，`MotionTrail`、`ChangeFrameRate` 这类历史 buffer，以及 `CustomMaterial` 的 Photoshop 式混合。
- `不要硬搬 Shoost，改成 URP 主体数据效果`：强依赖透明源、主体边界、alpha 或 Shoost 场景层级的效果。包括 `EdgeLight / 边缘光`、`Outline / 轮廓`、`DropShadow / 投影`，以及大概率需要重新设计的 `Glow / 发光`、`Lighting / 光照`、`TransparentBackground / 透明背景`、`Particle / 粒子`、`Weather / 天气`、`CameraSwitcher / 摄像头切换器`。这些不应该从最终 camera color 里猜 alpha，应使用明确的角色 mask、stencil、depth、normal 或单独的角色渲染目标。

建议的新数据契约：

- 普通画面滤镜只读 camera color。
- 主体相关滤镜读取角色 mask / stencil，并可选读取 depth、normal、motion 或单独角色 color。
- 背景、角色、前景的分层语义不要默认等同于 URP 最终颜色缓冲；需要分层时，应在渲染管线里显式产出 mask 或中间 RT。

三个高优先级重写项：

- `EdgeLight / 边缘光`：用角色 normal + view direction 做 rim，乘角色 mask/stencil 控制作用范围；需要时再叠加深度边缘，避免背景参与。
- `Outline / 轮廓`：用 depth + normal 的屏幕空间描边，最好配合角色 stencil 或 layer mask 限定对象；不要按 Shoost 的透明图片边缘膨胀硬搬。
- `DropShadow / 投影`：不要依赖最终图像 alpha 做偏移阴影。优先考虑基于角色 mask/depth 的投影，或基于 normal/depth 的二次打光/接触阴影式实现，让阴影和 URP 场景空间对齐。

边缘光的第一版设计见 [`ShoostEdgeLightDesign.md`](ShoostEdgeLightDesign.md)。结论是 lilToon 材质侧可以有“写入主体数据”的开关，但它只负责 opt-in 到 normal/mask RT；实际边缘光颜色、亮度、方向、模式和 pre-Bloom 合成都放在 Shoost EdgeLight 图层里处理。
