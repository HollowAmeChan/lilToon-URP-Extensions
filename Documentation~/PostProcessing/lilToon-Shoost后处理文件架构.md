# lilToon-Shoost 后处理结构

> 历史资料：本文记录旧 `ShoostStack` / `ShoostPostProcess` 阶段的结构，不作为当前 `ImageProcess` 使用说明。当前边界、用户顺序和验收口径以 `../RPComponentRework/RPComponentRework_验收文档.md` 为准。

本文只记录当前结构和后续新增效果的规则，不保留拆分过程记录。

## 设计理念

Shoost 后处理在 URP 中定位为 **Final Stack**。它消费当前 camera color，执行 Shoost 风格的最终图层、滤镜、混合、颗粒、CRT、VHS、像素化、最终调色和 LDR 后期发光。它不负责生产角色主体数据，也不应该从最终画面里反推角色边界、透明源或场景对象。

需要角色 mask、stencil、depth、normal、单独 subject color 或 pre-Bloom HDR 能量的效果，应放进 HoPost、Subject Effects、Lighting 等更早或数据语义更明确的管线，而不是塞进 Shoost Final Stack。`EdgeLight`、`Outline`、`DropShadow` 已按这个原则移出 Shoost；`Weather` 当前作为相机空间程序化粒子近似保留在 Shoost；`Glow` 是 Shoost/Kino 风格的 LDR final-stack bloom，不依赖 URP Bloom。

Shoost stack 使用 Volume profile 管理图层列表。用户面对的是一个后处理图层栈，当前 ImageProcess 执行顺序尊重用户在列表中的顺序，不再按旧 Shoost 固定 effect order 重排。`RemovedEffectSlot*` 只用于兼容旧资源，不生成 runtime layer。

编辑器设计以“图层栈”为核心：列表负责选择和排序意图，右侧或展开区域负责当前层参数；普通效果保持轻量、可扫读，多 pass 或状态型效果再暴露必要的高级参数。UI 命名应贴近 Shoost 用户侧语义，避免把内部 RenderGraph、RT、executor 等实现名暴露给普通用户。

## Runtime 目录

- `Runtime/ShoostPostProcessing/ShoostPostProcessRendererFeature.cs`：唯一公开 `ScriptableRendererFeature` 入口，只负责生命周期、Volume 判断、runtime layer 构建调度和 pass setup/enqueue。
- `Runtime/ShoostPostProcessing/ShoostPostProcessEffect.cs`：effect enum 与 blend mode enum。枚举值是序列化契约，不应随意重排。
- `Runtime/ShoostPostProcessing/ShoostPostProcessLayer.cs`：单个图层的序列化数据。
- `Runtime/ShoostPostProcessing/ShoostPostProcessStackSettings.cs`：renderer feature 级设置。
- `Runtime/ShoostPostProcessing/ShoostPostProcessStackVolume.cs`：Volume component，持有图层列表并判断 stack 是否 active。
- `Runtime/ShoostPostProcessing/ShoostPostProcessShaderConstants.cs`：shader property id 与固定 shader 名常量。
- `Runtime/ShoostPostProcessing/ShoostPostProcessEffectDescriptor.cs`：effect catalog，集中维护默认 shader、局部资源声明、removed slot 和执行类型。
- `Runtime/ShoostPostProcessing/ShoostPostProcessEffectRegistry.cs`：兼容门面，只转发 descriptor 的默认 shader 名。

## Renderer

- `Renderer/ShoostPostProcessRuntimeLayer.cs`：运行时图层容器，绑定 layer settings 与 material。
- `Renderer/ShoostPostProcessRuntimeLayerBuilder.cs`：从 Volume stack 构建 runtime layer，跳过 inactive、removed slot 和缺失材质的图层，并保留 Volume layer 列表顺序。
- `Renderer/ShoostPostProcessMaterialCache.cs`：解析 material override、shader override、默认 shader fallback，缓存 runtime material。
- `Renderer/ShoostPostProcessPass.cs`：主 render pass partial，保留 compatibility path 与 RenderGraph path 主循环、临时 RT 生命周期和 pass setup。
- `Renderer/ShoostPostProcessPass.Data.cs`：RenderGraph pass data、ChangeFrameRate state、Iris 参数等共享数据结构。
- `Renderer/ShoostPostProcessPass.Textures.cs`：HDR render texture descriptor 和 texture desc helper。

## EffectPipeline

- `Renderer/EffectPipeline/ShoostPostProcessPass.EffectDispatch.cs`：effect executor 注册表。每个 effect 注册 compatibility executor 与 RenderGraph executor，主循环只查表调用。
- `Renderer/EffectPipeline/ShoostPostProcessPass.EffectProperties.cs`：通用 shader property 写入、`LayerPropertyBlock` 和 effect-specific defaults 分派入口。

## Effects

`Renderer/Effects/` 只放实际效果层文件。命名统一为 `ShoostPostProcessPass.<Effect>.cs`，例如：

- `ShoostPostProcessPass.RGBBlurV2.cs`
- `ShoostPostProcessPass.Glow.cs`
- `ShoostPostProcessPass.LogoOverlay.cs`

简单 single-pass 效果可以只是薄 wrapper，统一复用 shared single-pass helper；多 pass 或有状态效果在自己的 effect 文件里放专用调度逻辑；effect-specific 默认值优先放回对应 effect 文件，再由 `EffectProperties` 统一分派。

## Descriptor 规则

`ShoostPostProcessEffectDescriptor` 是 effect metadata 的唯一事实来源。默认 shader、局部资源声明、removed slot 和执行类型都应从这里读取，不要在 runtime layer builder、editor 或 effect 文件中重新写一份 switch。

Descriptor 的执行类型只描述 runtime 形态：

- `SinglePass`：普通 fullscreen blit，可使用共享 single-pass helper。
- `MultiPass`：单帧内需要多个 pass 或临时纹理。
- `Stateful`：依赖 history、per-camera cache 或跨帧状态。
- `Removed`：旧资源兼容占位，不参与运行。

未知 effect 会回退为保留原始 enum 值的 single-pass descriptor，并使用默认 layer shader。这只是防御性 fallback，不是新增效果的注册方式。正式效果必须写入 `CreateCatalog()`，否则资源声明和执行分派都不具备明确语义。

## 新增 Shoost 效果流程

1. 在 `ShoostPostProcessEffect` enum 末尾追加新效果。不要插入已有枚举中间，不要复用 removed slot，除非明确是在做旧资源兼容迁移。
2. 评估图层数据。优先复用 `ShoostPostProcessLayer` 的 `color`、`texture`、`parameters0-12`、`blendMode`、`intensity`。只有语义长期稳定且通用字段确实不够时，才新增明确字段。
3. 在 editor 侧增加 UI。路径按现有 Shoost stack editor 结构放置，显示名、图标、默认值、折叠状态和条件显示应与用户侧语义一致。
4. 在 `ShoostPostProcessEffectDescriptor.CreateCatalog()` 注册 descriptor。普通 fullscreen blit 用 `SinglePass(effect)`；多 pass 用 `MultiPass(...)`；跨帧或持久状态用 `Stateful(...)`；旧资源空槽用 `Removed(...)`。默认 shader 不符合 `Hidden/lilToon-Shoost/URP/Shoost/<Effect>` 时，使用带 shader name 的重载。需要本地 ping-pong、history、original source 或 layer-supplied external texture 时，用 `ImageProcessResourceRequest` 声明；不要为 ImageProcess 新增 AOV / MaterialBuffer / GeometryBuffer / ShadowCast 输入。
5. 新增或更新 `Runtime/ShoostPostProcessing/Shaders/Shoost/<Effect>.shader`。shader 内部名称默认保持 `Hidden/lilToon-Shoost/URP/Shoost/<Effect>`。新增 property 时同时补 `ShoostPostProcessShaderConstants`，避免 magic string 散落在 effect 文件里。
6. 在 `Renderer/Effects/` 新增 `ShoostPostProcessPass.<Effect>.cs`。普通 single-pass 效果实现 `Apply<Effect>Layer(...)` 与 `Record<Effect>Layer(...)`，内部调用共享 helper；多 pass/stateful 效果在该文件中实现专用 `Apply...`、`Record...` 和 helper。
7. 在 `Renderer/EffectPipeline/ShoostPostProcessPass.EffectDispatch.cs` 注册 executor。普通 single-pass 使用 `RegisterSinglePassEffect`；多 pass 或 stateful 使用 `RegisterEffect`，同时提供 compatibility path 与 RenderGraph path。两条路径必须保持视觉行为一致。
8. 验证 enum 覆盖、descriptor 覆盖、executor 覆盖、effect 文件命名、Unity `.meta`、Unity import/compile，以及实际画面。单 pass 至少验证开关、强度、混合；多 pass/stateful 还要验证 resolution、history、camera 切换和 RenderGraph/compatibility 一致性。

## 当前约束

- 不改变 `ShoostPostProcessEffect` 既有枚举值。
- 不改变已有 shader property id、默认 shader 路径和 pass index 语义。
- compatibility path 与 RenderGraph path 的视觉行为必须保持一致。
- ImageProcess 不再支持 AOV mask composite；旧 `useAovMask` / `debugAovMask` 只作为迁移数据保留，运行时只提示迁往 ScreenProcess。
- removed slot 只作为旧资源兼容保留。

## 后续改进方向

- 让 descriptor 自动为 `SinglePass` 注册默认 executor，只让特殊效果显式注册，减少 `EffectDispatch` 中心表。
- 将 IrisBlur、RGBBlurV2、Glow、ApertureBokeh 的临时 RT 下沉到 effect state/cache。
- 将 ChangeFrameRate 的 per-camera history state 独立为 state cache。
- 把长期稳定的 effect-specific defaults 继续移动到对应 effect 文件。
