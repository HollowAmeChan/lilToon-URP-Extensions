# lilToon-Shoost 后处理图层栈

`ShoostPostProcessRendererFeature` 是在 URP 中重建 Shoost 后处理图层栈的入口。RendererFeature 只负责把 render pass 安装进管线；真正的图层列表放在 Volume component 里，这样就能像 HTrace 和 URP 后处理 override 一样由 Volume profile 控制。

当前版本有意先做成框架层：

- 一个 renderer feature 安装后处理 pass，并提供 HTrace 风格的 `Use Volumes` 开关；
- 一个 `lilToon-Shoost Post Process Stack` Volume override 持有完整后处理列表；
- 每个列表项都可以在 Volume profile 里启用、禁用、重排、复制或删除；
- 图层通过 ping-pong 全屏 blit 按顺序执行；
- RenderGraph 和非 RenderGraph 路径都已实现；
- 每个图层都会收到一组通用 shader 参数：
  - `_Intensity`
  - `_LayerBlendMode`
  - `_LayerColor`
  - `_LayerTexture`
  - `_LayerParams0`
  - `_LayerParams1`
  - `_LayerParams2`
  - `_LayerParams3`

## 设置

1. 把 `ShoostPostProcessRendererFeature` 加到 URP renderer data asset。
2. 保持 renderer feature 上的 `Use Volumes` 开启。
3. 在 Volume profile 里通过 `Add Override > Post-processing > lilToon-Shoost` 添加 `Post Process Stack`。
4. 启用这个 override，并在 `Layers` 列表里添加图层。
5. 烟雾测试可以先使用没有 override 的 `CustomMaterial`。它会解析到 `Hidden/lilToon-Shoost/URP/Shoost/PostProcessLayerBlit`。
6. 把 `Color` 改成非白色，或者修改 `Blend Mode`，确认图层确实在运行。

## 顺序说明

Volume 里只有一个面向用户的大图层列表，但运行时会把它拆成不同插入组。每个 layer 都有一个 `Injection Point` 字段：

- `Effect Default`：已知的 Shoost `BeforeStack` 效果映射到 `BeforeRenderingPostProcessing`，已知的 `AfterStack` 效果映射到 `AfterRenderingPostProcessing`；
- `Before URP Post Processing`：在 opaque / transparent 渲染后、URP Bloom / Tonemapping / Color Adjustments / Film Grain 等内置后处理前运行；
- `After URP Post Processing`：在 URP 主后处理栈之后运行，更接近 PPS v2 的 `AfterStack`，适合最终画面叠加类效果；
- `After Rendering`：更晚的逃生插入点，用于必须接近最终 blit 的效果。

重要事项：URP 内置后处理不会显示为 renderer data asset 里的 `ScriptableRendererFeature`。即使可见的 renderer feature 列表里只有 lilOIT、HTrace、Shoost 这些自定义功能，只要相机开启了 `Render Post Processing`，并且有 active 的 Volume override，URP 仍然会从 `UniversalRenderer` 内部注入自己的后处理 pass。

在 URP 17.3 中，内置后处理的大致顺序是：

- `ColorGradingLutPass` 位于 `BeforeRenderingPrePasses`；
- 自定义 pass 的 `BeforeRenderingPostProcessing` 组；
- URP `PostProcessPass` 位于 `AfterRenderingPostProcessing - 1`；
- 自定义 pass 的 `AfterRenderingPostProcessing` 组；
- 当 FXAA、最终缩放、TAA sharpening 或类似最终阶段功能需要运行时，URP `FinalPostProcessPass` 会接近 `AfterRendering - 1`。

这意味着设置为 `Before URP Post Processing` 的 Shoost 图层，后面仍然可能被 URP Bloom、Tonemapping、Color Adjustments、Film Grain、FXAA 等内置后处理再次修改。设置为 `After URP Post Processing` 的 Shoost 图层会在 URP 主后处理之后运行，但在某些相机配置下，后面仍可能接着 URP 的 final post pass。

图层列表的顺序会在每个插入组内部保留。也就是说，即使一个 `Before URP Post Processing` 图层在列表中排在 `After URP Post Processing` 图层后面，它运行时仍会先执行。这是有意设计的：它模拟 PPS v2 的 stack 分类，避免 color grading、tonemapping、CRT mask、final sharpen 这类效果被意外折进错误阶段。

从 Shoost v0.16.3 解包结果看，一共找到 59 个 `BeforeStack` 和 16 个 `AfterStack` 的 PPS v2 effect：

- 大多数 Shoost 自定义效果和 Retro Look Pro 效果都是 `PostProcessEvent.BeforeStack`；
- `CRTEffects`、`RGBChannelSeparator`、`Sharpen_After`、`RLProTVEffect_Custom` 是 `PostProcessEvent.AfterStack`；
- 很多 X-PostProcessing 的 blur / sharpen / glitch 效果也是 `PostProcessEvent.AfterStack`；
- `Sharpen_Before` 和 `Sharpen_After` 应该保持为两个独立效果，因为 Shoost 原本就把它们放在 stack 两侧；
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
- blur pyramid 或多个临时 render target：`KawaseBlur`、`IrisBlur`、`RGBBlurV2`；
- 多 pass 复古合成：`CRTEffects`、`Tube`、Retro Look Pro 派生 custom 效果。

保持 Volume 图层栈作为用户面对的排序界面，具体效果移植时再在 enum 槽背后添加专用执行代码。

如果目标是完全由 Shoost 控制画面风格，等等效 Shoost 图层实现后，最好关闭相机内置的 `Render Post Processing`。只有在明确需要过渡混用 URP Bloom、Tonemapping、FXAA 或 Color Adjustments 时才保留它。
