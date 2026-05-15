# Shoost 后处理文件架构

这份文档记录 `lilToon-Shoost` 后处理移植侧的文件拆分方式。当前的 Shoost 滤镜只是归属于 `ShoostPostProcessRendererFeature` 的一组实现，不代表整个包只能做 Shoost。以后如果加入你自己的非 Shoost 滤镜，应该另开自己的 Runtime / Editor 目录，避免和 Shoost 复刻层混在一起。

## Editor 侧

Shoost 的 Volume Editor 放在：

- `Editor/PostProcessing/ShoostStack/StackVolumeEditor.cs`

它是 `ShoostPostProcessStackVolumeEditor` 的主入口，负责：

- `OnEnable` / `OnInspectorGUI`
- 图标开关栏
- 图层列表
- 效果排序
- 通用行绘制
- 通用 reset / setter
- `DrawElement` 对各滤镜绘制方法的分发

Shoost 的滤镜 UI 分部放在：

- `Editor/PostProcessing/ShoostStack/Filters/`

每个文件只用滤镜名命名，例如：

- `ColorGradingCustom.cs`：调色。包含普通色轮、对数色轮、六色偏移 UI、色轮纹理、trackball 绘制和调色默认值修正。
- `IrisBlur.cs`：光圈模糊。包含模糊大小、中心半径、柔和度、中心位置、RGB 分离开关等 UI。
- `KawaseBlur.cs`：Kawase/旧模糊入口。包含自定义分辨率判断、模糊半径、迭代等 UI。
- `RGBBlurV2.cs`：通道模糊。只暴露 R/G/B 三个模糊度参数。
- `RGBSplit.cs`：RGB 分离。包含径向/非径向模式的条件 UI，角度只在非径向模式显示。
- `LevelAdjustment.cs`：色阶。包含 RGB/单通道模式的条件 UI 和默认无效果参数。
- `VignetteCustom.cs`：暗角。包含压暗/染色模式的条件 UI，颜色只在染色模式显示。
- `Fisheye.cs`：Shoost 用户侧的“镜头畸变”。包含强度、缩放、柔和度、圆形黑边等 UI。
- `Distortion.cs`：湍流置换。包含速度、强度、纹理等 UI，并负责加载默认置换纹理。

其他 `Filters/*.cs` 是已经迁出的较小滤镜或占位滤镜，例如锐化、白平衡、胶片、电视、颗粒、像素化、帧率限制等。即使暂时是占位，也先保留独立文件边界，后续补实现时直接进对应文件。

## Runtime 侧

Shoost Runtime 当前仍放在：

- `Runtime/PostProcessing/`

主要文件职责：

- `ShoostPostProcessRendererFeature.cs`：Volume 读取、材质缓存、按插入点分组、Compatibility path 和 RenderGraph path 的 pass 调度。当前也包含 Kawase、Iris、RGBBlurV2、ChangeFrameRate 等特殊多 pass / 跨帧逻辑。
- `ShoostPostProcessEffect.cs`：用户侧滤镜 enum、混合模式 enum、插入位置 enum。
- `ShoostPostProcessLayer.cs`：单个滤镜图层的数据结构。当前仍使用 `parameters0` 到 `parameters12` 承载不同滤镜参数。
- `ShoostPostProcessEffectRegistry.cs`：enum 到 shader name、默认插入点的映射。
- `ShoostPostProcessShaderConstants.cs`：shader property id 集中表。
- `ShoostPostProcess*.shader`：各滤镜实际 shader。命名保持 `Hidden/lilToon-Shoost/URP/Shoost/...`。

Runtime 先不强拆，避免在 RenderGraph 链路还没完全稳定前引入额外变量。等调色和基础滤镜链路稳定后，可以再把 `ShoostPostProcessRendererFeature.cs` 拆成 pass、属性写入、RenderGraph、多 pass blur、跨帧状态几个文件。

## 非 Shoost 滤镜

如果后面加入你自己的滤镜，建议按新的功能域单独建目录，例如：

- `Editor/PostProcessing/HoPost/`
- `Runtime/PostProcessing/HoPost/`

不要把自研滤镜塞进 `ShoostStack/Filters`，除非它确实是在复刻 Shoost 面板里的某个滤镜。这样可以保持两个层次清楚：

- `ShoostStack`：尽量对齐 Shoost 原 UI、图标顺序、参数命名和默认值。
- 自研后处理：按你自己的 RenderFeature / Volume 设计走，可以不受 Shoost 的单实例、固定顺序和图标面板限制。

## 新增 Shoost 滤镜流程

1. 在 `ShoostPostProcessEffect` enum 中补效果类型。
2. 在 `StackVolumeEditor.cs` 的图标顺序表、显示名、`DrawElement` 和高度计算里注册入口。
3. 新建或扩展 `Editor/PostProcessing/ShoostStack/Filters/<EffectName>.cs`，把 `Draw...Element`、`Ensure...Defaults`、条件显示 helper 放进去。
4. 在 `ShoostPostProcessEffectRegistry.cs` 中注册 shader 名和默认插入点。
5. 添加或更新对应 shader。
6. 如果不是单次 fullscreen blit，就在 Runtime pass 中补专用调度逻辑。

## 近期新增文件

- `Editor/PostProcessing/ShoostStack/Filters/DitheringCustom.cs`：视频游戏滤镜 UI。负责 Shoost 风格的模式、分辨率、抖动 V1/V2/V3、网格线、单色调三色、颜色模式 RGB 阶调等参数绘制，并根据抖动类型自动绑定对应纹理。
- `Runtime/PostProcessing/ShoostPostProcessDitheringCustom.shader`：视频游戏滤镜的 URP fullscreen pass。当前实现把 Shoost 的低分辨率采样、抖动量化、单色调三色映射和颜色模式通道量化合并到一个 pass。
- `Runtime/PostProcessing/Textures/ShoostDitheringV1.png`、`ShoostDitheringV2.png`、`ShoostDitheringV3.png`：从 Shoost 解包资源复制来的三张抖动图，对应 UI 中的 V1/V2/V3。
- `GrainCustom / 颗粒` 已确认完美对齐：shader 读取 Shoost 噪声图 Alpha 通道，默认插入点为 `After URP Post Processing`。
- `Editor/PostProcessing/ShoostStack/Filters/CRTEffects.cs`：显示器滤镜 UI。只绘制 Shoost 用户侧的“类型”和“分辨率”，并按类型自动绑定扫描线贴图。
- `Runtime/PostProcessing/ShoostPostProcessCRTEffects.shader`：显示器滤镜的 URP fullscreen pass。参考 RenderDoc 中 `Hidden/CRTEffects` 的扫描线合成 pass，实现降采样、扫描线贴图、slot/shadow mask、brightness 和 glow。
- `Runtime/PostProcessing/Textures/ShoostCRTScanlinesRGB.png`、`ShoostCRTScanlinesRGBMono.png`、`ShoostCRTScanlinesCircle.png`、`ShoostCRTScanlinesLine.png`：从 Shoost 解包资源复制来的四张显示器扫描线/荧光屏 mask。
- `Editor/PostProcessing/ShoostStack/Filters/VHS.cs`：VHS 滤镜 UI。负责 Shoost 用户侧的弱/中/强类型、噪点强度、锐化、扫描线开关和扫描线大小。
- `Runtime/PostProcessing/ShoostPostProcessVHS.shader`：VHS 滤镜的 URP fullscreen pass。当前把 Shoost VHS profile 中的 RGB 模糊、Noise2/EdgeNoise 式噪点、轻微横向扰动、锐化和 TVEffect_Custom 式扫描线合并到一个 pass，状态仍为对齐中。
