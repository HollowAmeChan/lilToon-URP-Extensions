# RPComponentRework 验收文档

> 2026-05-26 收口版。本文是本轮 RPComponentRework 的唯一当前文档；旧 00-09 分布文档已删除。历史 `Documentation~/PostProcessing/` 资料只作为迁移参考，不作为当前使用说明。

## 0. 验收结论

本轮重构可以进入验收。Runtime / Editor / shader 主路径不再以旧 `HoAOV`、`HoPost`、`ShoostStack` 作为概念入口。

当前用户侧入口收敛为：

1. `ShadowCast`
2. `MetadataBuffer`
3. `GeometryBuffer`
4. `SSS`
5. `OIT`
6. `CharacterSpecialization`
7. `ScreenProcess`
8. `ImageProcess`
9. `DebugTile`

`ImageProcess` 是最终图像链；`ScreenProcess` 是语义屏幕处理层；Buffer 只提供跨 feature 复用的语义输入；ShadowCast 独立属于 Lighting/Shadow；DebugTile 只编排 feature-local debug view。

## 1. 当前 RendererFeature 顺序

Renderer Data 推荐顺序：

```text
1. Ho-ShadowCast
2. Ho-MetadataBuffer
3. Ho-GeometryBuffer
4. Ho-SubsurfaceScattering
5. Ho-WeightedOIT
6. Ho-CharacterSpecialization
7. Ho-ScreenProcess
8. Ho-ImageProcess
9. Ho-DebugTile
```

`Ho-DebugTile` 只在调试时放在最后覆盖画面。普通用户按上表添加 RendererFeature；高级兼容项只在需要排查旧 URP 非 RenderGraph 路径时调整。

## 2. 命名口径

当前命名如下：

| 当前名 | Renderer Data 显示名 | 作用 |
| --- | --- | --- |
| `ShadowCast` | `Ho-ShadowCast` | 额外投影光源 atlas 与 receiver 数据 |
| `MetadataBuffer` | `Ho-MetadataBuffer` | 材质、对象、mask、surface metadata |
| `GeometryBuffer` | `Ho-GeometryBuffer` | normal / depth 几何输入 |
| `SSS` | `Ho-SubsurfaceScattering` | 屏幕空间皮肤/材质散射 |
| `OIT` | `Ho-WeightedOIT` | 加权透明合成 |
| `CharacterSpecialization` | `Ho-CharacterSpecialization` | 眼透、前发、角色局部合成 |
| `ScreenProcess` | `Ho-ScreenProcess` | 读取 Buffer 的语义屏幕处理 |
| `ImageProcess` | `Ho-ImageProcess` | 最终图像处理链 |
| `DebugTile` | `Ho-DebugTile` | registry 驱动的自动 debug tile |

旧名只允许出现在历史资料或迁移说明中：

- `HoAOV` / `HoAov`
- `HoPost`
- `Shoost` / `ShoostStack`
- `_lilHoAov*`
- `HoAOVSSS`

新增代码、文档、UI 文案和 review 清单不再使用这些旧名作为当前概念名。

## 3. 组件边界

### ShadowCast

`ShadowCast` 是 Lighting/Shadow 组件，从 URP `visibleLights` 自动收集额外投影灯。它不把 Unity `Light` 组件的 shadow 开关作为收集条件；附加可见灯即使关闭 URP 内置阴影，也可由 `Ho-ShadowCast` 自己组织 shadow atlas。URP main light 仍跳过并交给 URP 内置主光阴影。

`Master Shadow Strength` 是 `Ho-ShadowCast` 的总强度：punctual 与 second directional 会分别再乘自己的强度项。Unity `Light.shadowStrength` 不参与该组件强度计算。

它负责：

- visible light 收集
- light GameObject layer 与 Rendering Layer 过滤
- caster GameObject layer 与 Rendering Layer 过滤
- atlas / block / slice 写入
- ShadowCast 自己的 debug view

它不写 MetadataBuffer / GeometryBuffer。未来若 ScreenProcess 需要 ShadowCast receiver 或 attenuation，必须作为 Lighting/Shadow 资源显式读取，不塞回 Buffer。

### Buffer 分工口径

`MetadataBuffer` 和 `GeometryBuffer` 是两套并列的语义输入，不是彼此的中间结果。可以把它们理解成角色/材质语义的 forward-style buffer 与屏幕几何的 deferred-style buffer：前者跟随材质 pass、render queue 和对象语义写入材质/对象/遮罩/基础色；后者提供可见几何表面的 normal/depth。

材质、对象、layer 与 RendererFeature 过滤共同决定一个 renderer 写入哪些 buffer。常见分工是：opaque/cutout 角色主体同时写 MetadataBuffer 与 GeometryBuffer；transparent/半透材质主要写 MetadataBuffer 的 surface metadata / SurfaceColor；不参与语义效果的对象不写或只走 fallback 最小信息。

生产阶段禁止交叉依赖：MetadataBuffer 不读取或反写 GeometryBuffer，GeometryBuffer 不读取或反写 MetadataBuffer。SSS、CharacterSpecialization、ScreenProcess 等后续 feature 可以同时消费两者，但结果不再塞回任何基础 Buffer。

### MetadataBuffer

`MetadataBuffer` 只表达可跨系统复用的材质、对象、mask、surface metadata。

当前保留：

- `Mask / Coverage`
- `ObjectId`
- `GroupId`
- `FeatureFlags`
- 已命名 object custom bits / character parts
- `MaterialClass`
- `Thickness`
- `Curvature`
- `TransmittanceHint`
- `SurfaceColor`
- `MBufferDepth`

`SurfaceColor` 当前语义是基础表面色 / diffuse albedo：材质 pass 输出已解析的 albedo；opaque/cutout 先写入，transparent 再按普通后置 alpha blend 合成到同一张 `SurfaceColor` RT。alpha 表示有效 subject 覆盖。它和 Thickness / Curvature 一样属于基础 MetadataBuffer 语义；只要对象参与 MetadataBuffer subject 写入，就不依赖 `_UseSSS`、`_SSSColor`、`_SSSMainStrength` 或 profile tint。SSS 只把它作为扩散源颜色读取，扩散颜色和合成权重由 SSS profile 在 SSS pass 内处理。

`MBufferDepth` 是 MetadataBuffer 自己的 depth 语义，用于 opaque/cutout 写 depth 后让 transparent 只做 ZTest 与 alpha blend。它与 `SurfaceColor` 的过滤、render scale、清除时机和 opaque 写入时机保持一致；它不是 GeometryBuffer depth，也不能由 GeometryBuffer depth 直接替代。当前 transparent 只贡献 `SurfaceColor` 的颜色与 coverage，不写入 `MBufferDepth`。
`DebugTile` 和 MetadataBuffer feature-local debug 都必须暴露 `MBufferDepth`；统一 view id 为 `metadata.mbuffer-depth`，tile 短名为 `MDep`。

不负责 normal、velocity / motion、ShadowCast attenuation、SSS composite weight、OIT accumulation / revealage 或 ImageProcess 临时图像结果。

对象语义入口在 `HoMetadataBufferGroup`、`HoMetadataBufferSubject` 和 material inspector，不在 RendererFeature 面板批量管理全场景对象。

### GeometryBuffer

`GeometryBuffer` 负责屏幕空间几何输入。

当前保留：

- `NormalDepth`
- 独立 `Depth`

暂不默认输出：

- `Motion / Velocity`
- `Coverage`
- `SceneObjectClass`
- `ViewNormal`
- `TangentNormal`

`ViewNormal` 可由 normal + view matrix 派生；`TangentNormal` 暂无真实 screen-space 消费者。

`GeometryBuffer` 的 depth 表达可见几何表面，更接近 deferred/GBuffer 的结果语义。它适合后续效果做遮挡、距离、normal/depth 判断，但不负责透明颜色合成，也不替代 MetadataBuffer 的 `MBufferDepth`。

### SSS

`surfaceColor` 输入按 base diffuse/albedo 解释；`surfaceData` 继续提供 thickness、curvature、profile/material id 和 transmittance hint。材质侧不再把 `_SSSColor` 预混入 `surfaceColor`，也不再只在 `_UseSSS` 开启时写入，避免把通用 diffuse buffer 变成 SSS 专用中间量。

SSS debug 中的 `Diffuse Input` 直接显示 MetadataBuffer `SurfaceColor`；`_lilHoSSSSourceTexture` 只是 SSS 内部降采样、遮罩和扩散链路的工作纹理，不作为新的材质语义来源。

`SSS` 消费 MetadataBuffer mask / surfaceData / surfaceColor 和 GeometryBuffer normalDepth。缺少依赖时跳过当前帧，并在 Feature Inspector runtime status 中暴露缺失项。

SSS 不拥有 MaterialBuffer，也不把 composite weight 反写到 Buffer。

### OIT

`OIT` 负责透明 accumulation / revealage / composite。它不生产 MetadataBuffer / GeometryBuffer，也不拥有 ShadowCast atlas。透明材质如需 ShadowCast receiver，仍走材质 receiver 语义。

### CharacterSpecialization

`CharacterSpecialization` 消费 MetadataBuffer maskId / objectCustom0 / objectCustom1 和 GeometryBuffer normalDepth。Face、FrontHair、Eye 等对象语义通过 `HoMetadataBufferGroup` 标记。

缺少依赖时跳过当前帧，并在 Feature Inspector runtime status 中暴露缺失项。

### ScreenProcess

`ScreenProcess` 是语义屏幕处理层。它按 active layer 聚合真实 Buffer 需求。

可能读取：

- MetadataBuffer mask / surfaceData / custom / objectCustom
- GeometryBuffer normalDepth / depth
- 后续明确声明的 ShadowCast Lighting/Shadow 资源

不默认读取：

- ImageProcess 中间图像
- OIT accumulation / revealage
- SSS composite weight
- 没有 layer 消费者的未来 channel

缺少当前 layer 需要的 Buffer 项时，shader 可降级执行，但 Volume Inspector 必须显示需要 / 可用 / 未使用状态。

### ImageProcess

`ImageProcess` 是最终 image-domain 图像链。它只读取 camera color / ImageChain。

它不读取 MetadataBuffer、GeometryBuffer、ShadowCast、SSS、OIT 或 CharacterSpecialization。需要语义输入的效果必须放到 ScreenProcess。

ImageProcess layer 顺序就是执行顺序，不恢复旧 AOV mask、semantic mask、`NeedsAovInput` 或 AOV composite 入口。

### DebugTile

Debug 系统采用 feature-local declaration + `HoDebugViewRegistry` + 自动 tile。

`Ho-DebugTile` 支持 `AllRegistered`，把当前可用的 MetadataBuffer、GeometryBuffer、ShadowCast 与 SSS view 自动排成 tile。它参考 `D:\Unity_Fork\HoUrp-Extensions` 的 `RenderCacheDebugRendererFeature` / `RenderCacheDebugTile` 路线，只读取 RenderGraph 资源并绘制总览，不接管各 feature 自己的 shader、material 或 render target。

ScreenProcess rule mask 不进入 `Ho-DebugTile`。具体 layer 已有 `debugRuleMask` / `_LayerRuleDebugOutput` 直出选项。

## 4. ScreenProcess Rule Mask Debug

ScreenProcess rule mask debug 不做 RendererFeature 侧独立 debug pass。

原因是 rule 本身已有直出选项：用户在具体 ScreenProcess layer 上开启规则遮罩调试后，由该 layer 的 shader 通过 `_LayerRuleDebugOutput` 输出命中结果。公共 debug UI 只把它作为 ScreenProcess 的轻量观察入口展示，不额外创建 shader、material 或 pass。

## 5. RenderGraph 边界

RenderGraph 是主线。

- ImageProcess、ShadowCast、MetadataBuffer、GeometryBuffer、ScreenProcess、CharacterSpecialization、SSS、OIT 进入 RDG 记录时释放 compatibility-only RTHandle / camera target 状态。
- 非 RG compatibility path 只作为 fallback，不影响 RDG 主线资源所有权。
- ImageProcess 不把 live camera attachment 当作普通长期纹理读取；layer 间通过 ImageChain 显式传递中间图像。
- Buffer 禁用或 camera reset 时公开 global texture / active 状态回到空状态。
- DebugTile 只读取 registry 声明的 feature-local RenderGraph 资源。

## 6. 验收结果

当前静态验收结果：

- Runtime / Editor / shader / asmdef / json 扫描：`HoAOV`、`HoAov`、`HoPost`、`ShoostStack`、`NeedsAovInput`、`_lilHoAov`、`HoAOVSSS` 无命中。
- 代码级 `TODO` / `FIXME` / `NotImplementedException` 未发现与本轮收口相关的阻塞项。
- `HoDebugTileRendererFeature`、`HoDebugViewRegistry`、`AllRegistered` 自动 tile 已落地。
- ScreenProcess rule mask debug 已按 layer-local `_LayerRuleDebugOutput` 路线落地。
- 当前仓库无 `.sln` / `.csproj`，未执行 C# 编译。

## 7. Unity 实机验收清单

进入 Unity 工程后按以下清单验收：

- Renderer Data 能按本文顺序添加 9 个 RendererFeature。
- 无 `HoShadowCastController` 场景组件也能从 URP visible lights 生成 ShadowCast 参与列表。
- 附加灯关闭 Unity/URP 内置 shadow 开关时，仍能被 `Ho-ShadowCast` 按可见灯、layer、Rendering Layer 和容量上限收集；URP main light 仍由 URP 自己处理。
- MetadataBuffer / GeometryBuffer 能输出对象语义和 normal / depth。
- SSS 在缺 MetadataBuffer / GeometryBuffer 时能显示缺失状态，资源齐全时正常 source / diffusion / composite。
- CharacterSpecialization 在缺 MetadataBuffer / GeometryBuffer 时能显示缺失状态，资源齐全时正常眼透、前发和局部合成。
- ScreenProcess Volume Inspector 能显示当前 layer 需要的 Buffer 项是否可用。
- ScreenProcess rule mask 的 `debugRuleMask` 能通过 `_LayerRuleDebugOutput` 直出命中结果。
- ImageProcess 不显示 AOV mask / semantic mask UI，也不读取 Buffer / ShadowCast 资源。
- `Ho-DebugTile` 放在最后时，`AllRegistered` 能显示 MetadataBuffer、GeometryBuffer、ShadowCast 与 SSS tiles。
- Frame Debugger / RenderDoc 中 pass 名称与本文组件顺序一致。

## 8. 后续边界

以下不是当前验收阻塞项，只能在有真实消费者后另开任务：

- `GeometryBuffer.Motion / Velocity`
- `MaterialBuffer.RoughnessLike / SmoothnessLike / EmissionHint`
- `ShadowCast` ScreenProcess receiver / attenuation
- temporal、wetness、deferred scene、体积重投影、motion trail 等扩展能力

新增 Buffer channel 必须先给出命名、编码范围、消费者和 debug 解释。没有真实消费者，不默认输出。

## 9. 最终摘要

本轮 RPComponentRework 已从旧 AOV / HoPost / Shoost 兼容迁移，收束为新的组件边界：

- Buffer 负责语义输入。
- ScreenProcess 负责语义屏幕效果。
- ImageProcess 负责最终图像链。
- ShadowCast 独立负责额外投影光源。
- Debug / tile 只编排 feature-local view。

当前主线可以验收。后续工作应以 Unity 实机验收或真实消费者驱动的新能力任务为入口，不再按旧兼容小项扩散。
