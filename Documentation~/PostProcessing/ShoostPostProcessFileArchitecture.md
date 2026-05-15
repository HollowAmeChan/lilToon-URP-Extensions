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

其他 `Filters/*.cs` 是已经迁出的较小滤镜或占位滤镜，例如锐化、白平衡、胶片、电视、颗粒、像素化、帧率限制等。即使暂时是占位，也先保留独立文件边界，后续补实现时直接进对应文件。注意：胶片和 Tube/电视目前明确标记为暂时跳过，因为它们不是单 shader 效果，而是多个 Shoost profile 层和第三方包语义组合出来的复杂入口。

## Runtime 侧

Shoost Runtime 当前仍放在：

- `Runtime/ShoostPostProcessing/`

主要文件职责：

- `ShoostPostProcessRendererFeature.cs`：Volume 读取、材质缓存、按插入点分组、Compatibility path 和 RenderGraph path 的 pass 调度。当前也包含 Kawase、Iris、RGBBlurV2、ChangeFrameRate 等特殊多 pass / 跨帧逻辑。
- `ShoostPostProcessEffect.cs`：用户侧滤镜 enum、混合模式 enum、插入位置 enum。
- `ShoostPostProcessLayer.cs`：单个滤镜图层的数据结构。当前仍使用 `parameters0` 到 `parameters12` 承载不同滤镜参数。
- `ShoostPostProcessEffectRegistry.cs`：enum 到 shader name、默认插入点的映射。
- `ShoostPostProcessShaderConstants.cs`：shader property id 集中表。
- `Shaders/Shoost/*.shader`：各 Shoost 滤镜实际 shader。文件名只保留效果名，例如 `VHS.shader`、`CRTEffects.shader`、`ColorGradingCustom.shader`；shader 内部命名保持 `Hidden/lilToon-Shoost/URP/Shoost/...`，这是 `Shader.Find()` 的运行时契约。

Runtime 先不强拆，避免在 RenderGraph 链路还没完全稳定前引入额外变量。等调色和基础滤镜链路稳定后，可以再把 `ShoostPostProcessRendererFeature.cs` 拆成 pass、属性写入、RenderGraph、多 pass blur、跨帧状态几个文件。

## 非 Shoost 滤镜

如果后面加入你自己的滤镜，建议按新的功能域单独建目录，例如：

- `Editor/PostProcessing/HoPost/`
- `Runtime/<YourFeatureName>/`，例如后续的 `Runtime/SubjectEffects/`

不要把自研滤镜塞进 `ShoostStack/Filters`，除非它确实是在复刻 Shoost 面板里的某个滤镜。这样可以保持两个层次清楚：

- `ShoostStack`：尽量对齐 Shoost 原 UI、图标顺序、参数命名和默认值。
- 自研后处理：按你自己的 RenderFeature / Volume 设计走，可以不受 Shoost 的单实例、固定顺序和图标面板限制。

## 主体数据效果

Shoost 中有一组效果依赖透明源或角色/前景图层：边缘光、轮廓、投影，以及部分发光、光照、透明背景、粒子、天气和摄像头切换器。它们在 Shoost 里可以利用图片/视频源 alpha、背景/角色/前景分层和 UI 场景对象；在 URP fullscreen post 中，这些信息通常已经丢失，所以不建议按 Shoost shader 或 profile 硬搬。

架构上建议把系统拆成两条线：`Shoost Final Stack` 只做最终图层和滤镜，默认在 URP 内置后处理之后运行；`lilToon Subject Effects` 作为新的 RendererFeature，负责读取 lilToon 输出的 subject normal/mask/depth/color 并实现边缘光、轮廓、投影等特调效果。这样 Shoost 移植可以保持“最终图层系统”的概念，主体效果也能按 URP 渲染数据正确实现。

拆分时要把 HDR/LDR 边界作为文件架构的一等概念。`BeforeRenderingPostProcessing` 阶段仍适合写入 HDR 能量并交给 Bloom/Tonemapping；`AfterRenderingPostProcessing` 更像最终显示空间处理，适合颗粒、扫描线、VHS、像素化和最终调色。不要把需要 Bloom 的层静默迁到 Shoost Final Stack，除非同时提供一个 pre-Bloom 输入或让该效果改由 `lilToon Subject Effects` 执行。

这类效果后续应单独建一个主体数据管线，而不是继续塞进普通 Shoost fullscreen pass：

- Runtime 侧需要能产出或读取角色 `stencil / mask / depth / normal`，必要时增加角色 color RT。
- Editor 侧可以沿用 Shoost 的用户入口名称和图标，但参数语义以 URP 实现为准。
- `EdgeLight` 应按 normal + view direction 的 rim light 写。
- `Outline` 应按 depth + normal 屏幕空间描边写，并用 stencil/layer mask 限定对象。
- `DropShadow` 应按角色 mask/depth 投影，或做 normal/depth 的二次打光/接触阴影近似。
- `TransparentBackground` 更接近相机 clear / 合成 / 导出设置，不应作为普通颜色后处理滤镜处理。
- `Particle`、`Weather`、`CameraSwitcher` 更接近场景对象或相机控制入口，应在需要时独立设计调度层。

边缘光的落地设计已单独拆到 [`ShoostEdgeLightDesign.md`](ShoostEdgeLightDesign.md)。Runtime 侧建议新增 `SubjectData` 模块，负责分配 `_lilShoostSubjectDataTexture` 并绘制 `LightMode = lilToonSubjectData` 的对象；边缘光本身优先放进新的 `lilToon Subject Effects` RendererFeature，在 `BeforeRenderingPostProcessing` 合成 HDR rim light。Shoost stack 可以保留同名入口作为参数参考或兼容层，但不应继续承担主体数据效果的实际执行。

## 新增 Shoost 滤镜流程

1. 在 `ShoostPostProcessEffect` enum 中补效果类型。
2. 在 `StackVolumeEditor.cs` 的图标顺序表、显示名、`DrawElement` 和高度计算里注册入口。
3. 新建或扩展 `Editor/PostProcessing/ShoostStack/Filters/<EffectName>.cs`，把 `Draw...Element`、`Ensure...Defaults`、条件显示 helper 放进去。
4. 在 `ShoostPostProcessEffectRegistry.cs` 中注册 shader 名和默认插入点。
5. 添加或更新 `Runtime/ShoostPostProcessing/Shaders/Shoost/<EffectName>.shader`。物理文件名保持短名，shader 内部 `Hidden/lilToon-Shoost/URP/Shoost/...` 名称保持稳定。
6. 如果不是单次 fullscreen blit，就在 Runtime pass 中补专用调度逻辑。

## 近期新增文件

- `Editor/PostProcessing/ShoostStack/Filters/DitheringCustom.cs`：视频游戏滤镜 UI。负责 Shoost 风格的模式、分辨率、抖动 V1/V2/V3、网格线、单色调三色、颜色模式 RGB 阶调等参数绘制，并根据抖动类型自动绑定对应纹理。
- `Runtime/ShoostPostProcessing/Shaders/Shoost/DitheringCustom.shader`：视频游戏滤镜的 URP fullscreen pass。当前实现把 Shoost 的低分辨率采样、抖动量化、单色调三色映射和颜色模式通道量化合并到一个 pass。
- `Runtime/ShoostPostProcessing/Textures/ShoostDitheringV1.png`、`ShoostDitheringV2.png`、`ShoostDitheringV3.png`：从 Shoost 解包资源复制来的三张抖动图，对应 UI 中的 V1/V2/V3。
- `GrainCustom / 颗粒` 已确认完美对齐：shader 读取 Shoost 噪声图 Alpha 通道，默认插入点为 `After URP Post Processing`。
- `Editor/PostProcessing/ShoostStack/Filters/CRTEffects.cs`：显示器滤镜 UI。只绘制 Shoost 用户侧的“类型”和“分辨率”，并按类型自动绑定扫描线贴图。
- `Runtime/ShoostPostProcessing/Shaders/Shoost/CRTEffects.shader`：显示器滤镜的 URP fullscreen pass。参考 RenderDoc 中 `Hidden/CRTEffects` 的扫描线合成 pass，实现降采样、扫描线贴图、slot/shadow mask、brightness 和 glow。
- `Runtime/ShoostPostProcessing/Textures/ShoostCRTScanlinesRGB.png`、`ShoostCRTScanlinesRGBMono.png`、`ShoostCRTScanlinesCircle.png`、`ShoostCRTScanlinesLine.png`：从 Shoost 解包资源复制来的四张显示器扫描线/荧光屏 mask。
- `Editor/PostProcessing/ShoostStack/Filters/VHS.cs`：VHS 滤镜 UI。负责 Shoost 用户侧的弱/中/强类型、噪点强度、锐化、扫描线开关和扫描线大小。
- `Runtime/ShoostPostProcessing/Shaders/Shoost/VHS.shader`：VHS 滤镜的 URP fullscreen pass。当前把 Shoost VHS profile 中的 RGB 模糊、Noise2/EdgeNoise 式噪点、轻微横向扰动、锐化和 TVEffect_Custom 式扫描线合并到一个 pass。已由实机核对确认完美对齐。
- `Editor/PostProcessing/ShoostStack/Filters/Tube.cs`：电视滤镜 UI。当前保留文件边界和已有试验代码，但 Tube/电视滤镜暂时跳过，不再继续对齐。原因是 Shoost 的 `PostProcess_TVValue` 是 profile 组合入口，涉及 `FilmBreath_GateWeave`、`RGBBlur`、`LUTColorGrading`、`Custom/Tube`、`Sharpen_Before`、`RLProJitter` 等多层语义。
- `Runtime/ShoostPostProcessing/Shaders/Shoost/Tube.shader`：Tube/电视滤镜的试验 fullscreen pass。当前不作为对齐完成实现看待，状态为暂时跳过。后续如重启，应优先拆清 Shoost profile 层顺序、Kino/Custom Tube 来源差异、LUT 导入与 RenderDoc 汇编，再决定是否继续单 pass 压缩或改成多 pass。
- `Film / 胶片`：暂时跳过。胶片年代入口应单独按 `AMS_AnimeFilm_*` profile 处理，不复用 TV 的 60/70/80/90 语义；需要重新核对 `LUTColorGrading`、`Grain_Custom`、`RLProOldFilm2_Custom` 等层。
- `EdgeLight / Outline / DropShadow`：保留 Shoost 用户入口，但实现策略改为 URP 主体数据效果。不要从最终颜色缓冲或后处理 alpha 里反推主体边界；后续应依赖角色 mask/stencil/depth/normal。
- `Glow / Lighting / TransparentBackground / Particle / Weather / CameraSwitcher`：暂不按普通 Shoost fullscreen pass 实现。需要先明确它们在 URP 中是画面后处理、主体特效、场景对象，还是相机/合成控制。
