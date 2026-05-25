# MaterialBuffer 与 GeometryBuffer 规划

> 这份文档从未来要做的效果反推两个 Buffer 应该承载什么。原则不是复刻当前 HoAOV MRT，而是保留能被多个系统消费的通用输入，删除没有真实消费者的垃圾通道。

---

## 0. 命名

后续统一使用：

```text
MaterialBuffer
GeometryBuffer
```

不用 `MBuffer/GBuffer` 作为新系统组分名，避免和 URP/Deferred 里的既有 GBuffer 语境混淆。

---

## 1. 设计原则

Buffer 里只放“跨系统复用的输入”，不放某个 feature 的运行时中间结果。

必须满足至少一个条件：

- 当前已有真实消费者。
- 下一阶段明确要接入的 feature 会消费。
- 它是多个未来效果共同需要的表面/几何语义。
- 它无法廉价从 URP 内置资源、renderer data 或材质常量重新获得。

不满足这些条件的，不默认输出。

成熟管线参考见 `04-HDRP_UE参考边界.md`。这里的候选项会参考 HDRP/UE 的 buffer、debug visualization 和 RDG 思路，但不直接照搬它们的 GBuffer layout。

---

## 2. 用户侧 UI 原则

MaterialBuffer / GeometryBuffer 的用户 UI 不能继续做成“全局大集合批量设置”。Buffer 是渲染数据层，不应该让用户在一个巨大面板里管理所有对象语义。

用户面对的主入口应是当前对象或当前集合：

- 选中一个角色、部件或 prefab 集合时，只展开这个对象/集合相关的 Buffer 标记。
- `HoMetadataBufferGroup` 这类集合组件是正确方向：按角色组、部件和已命名 object custom 位拖入对象。
- `HoMetadataBufferSubject` 只作为高级覆盖路径，用于 MaterialPropertyBlock 覆盖材质默认值。
- RendererFeature 面板只负责全局输出开关、render scale、pass event、debug view 和高级兼容项。
- RendererFeature 面板不列出全场景所有对象，不承担对象语义编辑器职责。

推荐交互：

```text
Renderer Data / MaterialBuffer+GeometryBuffer Feature
    只配置输出、调试和全局兼容策略

Scene Object / Character Root / Prefab Root
    使用 Group UI 展开当前对象或集合
    只编辑当前对象/集合的 CharacterId、PartId、FeatureFlags、已命名 bits

Material Inspector
    只编辑材质自身的 SurfaceColor、Thickness、Curvature、MaterialClass/Profile 等默认语义
```

如果需要“批量看全场景有哪些标记”，应做成只读诊断视图或搜索工具，而不是把它变成主要编辑入口。

---

## 3. 未来消费者

规划 Buffer 时要提前考虑这些方向：

- SSS / skin diffusion。
- 丁达尔效应。
- 大气散射。
- 体积雾。
- 水体与湿润表面。
- 屏幕空间角色合成。
- 风格化 outline / edge light / drop shadow。
- OIT 与透明合成。
- 场景 deferred，角色 forward 的混合路径。
- temporal / motion trail / history-based image process。

这些效果最常共享的是“表面本身是什么”和“屏幕空间几何关系”，而不是旧 HoAOV 的每个实验通道。

---

## 4. MaterialBuffer

MaterialBuffer 表达表面、材质、对象语义。

### 第一批建议保留

```text
MaterialBuffer.SurfaceColor / DiffuseColor
MaterialBuffer.SubsurfaceColor
MaterialBuffer.MaterialClass
MaterialBuffer.MaterialProfile
MaterialBuffer.Thickness
MaterialBuffer.Curvature
MaterialBuffer.TransmittanceHint
MaterialBuffer.ObjectId
MaterialBuffer.GroupId
MaterialBuffer.FeatureFlags
MaterialBuffer.CharacterPart
```

### SurfaceColor / DiffuseColor

旧 HoAOV 里的 `SssSource.rgb` 应改名理解为：

```text
MaterialBuffer.SurfaceColor.rgb
```

它不是 SSS 私有 source，而是通用表面散射颜色。SSS 会读它，未来丁达尔、大气、体积雾、水体透射、湿润表面和屏幕空间合成也可能读它。

如果需要单独表达 subsurface tint，可以再规划：

```text
MaterialBuffer.SubsurfaceColor
```

但不能为了 SSS 私有逻辑把它叫成 `SssSource`。

### Alpha / Weight

旧 `SssSource.a` 不能默认继承为 SSS weight。

只有当 alpha 表达通用材质语义时才进入 MaterialBuffer，例如：

- surface participation
- translucency hint
- density hint
- opacity coverage

如果 alpha 表达的是 SSS feature 的 runtime composite weight，则归 SSS 自己。

### MaterialClass

建议用粗分类服务多个系统：

```text
Skin
Hair
Cloth
Water
Glass
Metal
Foliage
Effect
```

不要把每个 effect 的开关都塞进 class。class 说明“它是什么”，feature flags 才说明“它参与什么”。

### MaterialProfile

profile 是跨 feature 的参数索引，不是某个 shader UI 的历史 enum。

可用于：

- SSS profile。
- fog interaction profile。
- water profile。
- atmospheric response profile。
- stylized material response profile。

### Thickness / Curvature

这两个是当前 SSS 已经需要、未来透射和散射也可能复用的通用材质/表面语义，可以优先保留。

---

## 5. GeometryBuffer

GeometryBuffer 表达屏幕空间几何关系。

### 第一批建议保留

```text
GeometryBuffer.Depth
GeometryBuffer.Normal
```

### 第二批候选

```text
GeometryBuffer.Motion
GeometryBuffer.Coverage
GeometryBuffer.SceneObjectClass
```

第二批只有当 temporal、体积重投影、角色/场景覆盖判定明确需要时再输出。

### 暂不默认输出

```text
GeometryBuffer.ViewNormal
GeometryBuffer.TangentNormal
```

视图法线不作为 Buffer 输出项，也不作为常驻 debug view。它可以由 `GeometryBuffer.Normal` 结合当前 view matrix 廉价派生；如果未来某个 feature 真需要观察它，应在该 feature 的局部调试里临时计算，而不是占用 AOV/GeometryBuffer 通用调试菜单。

切线法线目前没有明确 screen-space 消费者。未来如果有各向异性屏幕滤波、头发高光后处理、特殊材质局部方向滤波，再作为明确需求加入。

---

## 6. 删除或降级的旧 HoAOV 通道

### 整体遮罩

不默认输出“总 mask”。

真实消费者通常需要的是：

- object id
- group id
- part bit
- material class
- feature flag
- thickness / curvature

如果先输出一个整体遮罩，再让消费者 decode 或 remap，会多一层没有语义价值的中间层。

### 预留 Custom

没有命名、编码、范围和消费者的 custom 通道不输出。

允许存在：

- 已命名 object custom bits。
- 已命名 material custom channels。
- 当前 feature 私有 custom，但不能进入公共 Buffer。

### Tangent Normal

默认移出 HoAOV 输出。后续按真实需求进入 GeometryBuffer。

---

## 7. 候选项准入表

| 候选项 | 所属 | 当前状态 | 准入判断 |
| --- | --- | --- | --- |
| SurfaceColor / DiffuseColor | MaterialBuffer | 应补入 | SSS 和未来散射/雾/水体都可能消费 |
| MaterialClass | MaterialBuffer | 保留 | ScreenProcess/SSS/未来 profile 分派需要 |
| MaterialProfile | MaterialBuffer | 保留 | SSS 与未来材质响应 profile 需要 |
| Thickness | MaterialBuffer | 保留 | SSS/透射/薄物体散射需要 |
| Curvature | MaterialBuffer | 保留 | SSS/边缘散射/风格化边缘可能复用 |
| TransmittanceHint | MaterialBuffer | 保留 | SSS transmission radius 与未来透射/透光表面可复用 |
| ObjectId / GroupId | MaterialBuffer | 保留 | 角色合成与屏幕处理需要 |
| CharacterPart | MaterialBuffer | 保留已命名位 | 眼透/前发/角色局部合成需要 |
| FeatureFlags | MaterialBuffer | 保留已命名位 | 参与能力 gate |
| Depth | GeometryBuffer | 保留 | 所有屏幕空间效果基础 |
| Normal | GeometryBuffer | 保留 | SSS/outline/fog/deferred 需要 |
| Motion | GeometryBuffer | 候选 | temporal 需求明确后加入 |
| ViewNormal | GeometryBuffer | 不作为输出/调试项 | 可由 WorldNormal/Normal + view matrix 派生 |
| TangentNormal | GeometryBuffer | 删除默认 | 暂无真实消费者 |
| OverallMask | MaterialBuffer | 删除默认 | 语义太宽，消费者应读具体项 |
| ReservedCustom | 不进入公共 Buffer | 删除默认 | 无命名无消费者 |

---

## 8. 执行顺序

1. 盘点当前 HoAOV 每个 MRT 的真实消费者。
2. 把 `SssSource.rgb` 重命名规划为 `SurfaceColor/DiffuseColor`。
3. 删除或关闭 tangent normal 默认输出。
4. 删除整体遮罩默认输出，改成消费者直接读具体语义。
5. 删除未命名 reserved custom。
6. 重做用户 UI：RendererFeature 只做全局输出和 debug，对象/集合标记回到 `HoMetadataBufferGroup` 这类局部入口。
7. 更新 debug view，只显示仍存在的 Buffer 项。
8. 再迁 ScreenProcess / ImageProcess 消费侧。
9. 每次新增未来效果时，对照 HDRP/UE 参考文档复核它应该进入 Buffer、ScreenProcess、ImageProcess 还是 feature 私有资源。

---

## 9. 2026-05-25 执行记录

已先完成 HoAOV 运行时文件拆分，作为后续 MaterialBuffer / GeometryBuffer 收敛前的可维护性整理：

- 旧 `HoAovRendererFeature` 当时只保留 settings、pass enqueue、材质生命周期和 debug 按需加载决策；后续入口已迁为 `HoMetadataBufferRendererFeature`。
- `HoAovOutputPass` 承担 AOV/SSS source 输出与 RenderGraph 资源发布。
- `HoAovDebugPass` 承担 debug overlay，只有 debug mode 打开且 debug shader/material 可用时入队。
- `HoAovRenderTargets` / `HoAovRenderGraphResources` 集中管理兼容路径 RTHandle 与 RenderGraph texture handle。
- `HoAovDebug.shader` 已移动到 `Runtime/AOV/Shaders/Debug/`，保持 feature-local debug 归属，并同步更新显式 debug shader collection 路径。

这一步没有改动现有 MRT 语义布局；下一步再按真实消费者删除或重命名旧 HoAOV 通道。

## 10. 2026-05-25 执行记录：移除 TangentNormal 默认输出

已完成第一批旧 HoAOV 通道瘦身：`TangentNormal` 没有真实 ScreenProcess / SSS / CharacterSpecialization 消费者，已从默认 Buffer 输出中移除。

- `HoAovChannelMask.Default` 不再包含 `TangentNormal`，并从用户可选 debug mode 中移除切线法线视图。
- `HoAovOutputPass` / `HoAovRenderTargets` / `HoAovRenderGraphResources` 不再创建、清理、发布 `_lilHoAovTangentNormalTexture`。
- AOV fallback 与 clear shader 的 MRT 从 7 个缩到 6 个：`MaskId`、`NormalDepth`、`SurfaceData`、`Custom0_3`、`ObjectCustom0_3`、`ObjectCustom4_7`。
- `HoAovDebugPass` 和 `HoAovDebug.shader` 不再读取 TangentNormal；后续 debug 只显示仍存在的 Buffer 项。
- 新增 `HoAovAttachmentLayout` 固定剩余 MRT 索引，避免后续继续瘦身时在 `HoAovOutputPass` 中手写 attachment 数字。

保留的判断：如果未来出现各向异性屏幕滤波、头发屏幕空间高光或明确的切线方向后处理，再以新的 `GeometryBuffer` 候选项重新引入，而不是把旧 HoAOV 实验通道常驻输出。

## 11. 2026-05-25 执行记录：明确 surfaceData.a 语义

`surfaceData.a` 原名 `Utility` / 系统预留，但当前真实消费者是 SSS transmission radius multiplier。它不是无消费者 reserved custom，暂不删除；本步将运行时和 UI 语义改为 `TransmittanceHint`。

- `HoAovChannelMask.Utility` / `HoAovDebugMode.Utility` 改名为 `TransmittanceHint`，保持原 bit 和 debug mode 顺序。
- `HoAovSubject.utility` 改为 `transmittanceHint`，shader property 改为 `_HoAovTransmittanceHint`。
- fallback shader 继续写入 `surfaceData.a`，但变量名改成 transmittance hint，避免后续把它误判为无命名预留通道。
- ScreenProcess 规则源里的第 7 项同步改名为 `TransmittanceHint`；物理采样仍是 `surfaceData.a`。

## 12. 2026-05-25 执行记录：移除 ViewNormal 输出与调试项

`ViewNormal` 不再作为 AOV / GeometryBuffer 输出候选或常驻 debug view 暴露。视图空间法线可以从现有 world normal 结合 view matrix 派生，继续常驻输出/debug 开关只会扩大 Buffer 契约。

- `HoAovChannelMask.ViewNormal` 已删除，避免用户侧把它理解为独立输出通道。
- `HoAovDebugMode.ViewNormal` 同步删除；AOV debug 菜单只显示真实输出项或当前仍需观察的语义项。
- debug shader 删除 view-normal 派生分支，并把后续 mode 编号前移一位。
- bit 5 暂不复用，避免和旧序列化值或后续通道瘦身混在同一次改动里。

## 13. 2026-05-25 执行记录：启动 MetadataBuffer / GeometryBuffer 拆分

已确认 HoAOV 可以直接拆成两个独立 RendererFeature 方向推进，不再以旧 `HoAOV` 总资源上下文作为长期兼容层。

本次先落地 RenderGraph 资源出口拆分：

- 新增 `Runtime/MetadataBuffer/HoMetadataBufferRenderGraphResources.cs`，承载 mask/id、surfaceData、material custom、object custom 与当前 SSS surface/source 纹理。
- 新增 `Runtime/GeometryBuffer/HoGeometryBufferRenderGraphResources.cs`，承载 normalDepth 与独立 depth 纹理。
- `HoAovOutputPass` 仍暂时用一次 MRT 绘制填充现有 metadata attachments，但不再发布旧 `HoAovRenderGraphResources`；GeometryBuffer 输出已在独立 RendererFeature 中拆出。
- `HoAovDebugPass`、ScreenProcess、HoSSS、CharacterSpecialization 的 RenderGraph 路径已按真实输入改读 MetadataBuffer / GeometryBuffer 两个上下文。
- compatibility path 的 `HoAovRenderTargets` 暂时保留在 AOV 目录，作为旧非 RenderGraph 路径的 RTHandle 管理；拆两个 RendererFeature 时再跟随对应 feature 移出。

下一步优先级：

1. 把 `HoAovSettings` 剩余字段继续拆成 MetadataBuffer settings，RendererFeature 面板只保留 metadata 输出、pass event、debug。
2. 把 AOV debug view 改名为 Metadata/Geometry buffer debug view，并按 feature-local debug shader 策略继续拆。
3. 为材质原生 pass 补 `HoGeometryBuffer` LightMode，避免后续只靠 fallback material 输出几何。

## 14. 2026-05-25 执行记录：MetadataBuffer RendererFeature 入口落地

已把旧 HoAOV 用户入口迁到 MetadataBuffer：

- 新增 `Runtime/MetadataBuffer/HoMetadataBufferRendererFeature.cs`，作为 Renderer Data 里新的 MetadataBuffer 入口。
- 删除 `Runtime/AOV/HoAovRendererFeature.cs`，不再保留旧 HoAOV RendererFeature 类名。
- 新增 `Editor/MetadataBuffer/HoMetadataBufferRendererFeatureEditor.cs`，旧 AOV RendererFeature editor 同步删除。
- 当前 `HoMetadataBufferRendererFeature` 仍复用 `HoAovOutputPass` / `HoAovDebugPass` / `HoAovSettings`，因为 shader MRT layout 尚未拆成 Metadata-only 与 Geometry-only 两套 pass。
- GeometryBuffer 已有独立 RenderGraph resource context 和目录；下一步从独立 feature 补材质原生 `HoGeometryBuffer` LightMode，并继续瘦身 MetadataBuffer 的临时 MRT 输出。

这一步会打断旧 Renderer Data 中直接引用 `HoAovRendererFeature` 的资产；快速开发阶段接受这种破坏性迁移。

## 15. 2026-05-25 执行记录：GeometryBuffer RendererFeature 与输出 pass 落地

已新增独立 GeometryBuffer 用户入口与公开资源发布：

- 新增 `Runtime/GeometryBuffer/HoGeometryBufferRendererFeature.cs`，Renderer Data 中可单独添加 GeometryBuffer。
- 新增 `HoGeometryBufferPass`、`HoGeometryBufferSettings`、`HoGeometryBufferRenderTargets` 与 `HoGeometryBufferShaderConstants`。
- 新增 `Runtime/GeometryBuffer/Shaders/HoGeometryBufferFallback.shader`，fallback path 只输出 world normal + linear depth。
- 新增 `Editor/GeometryBuffer/HoGeometryBufferRendererFeatureEditor.cs`。
- `HoAovOutputPass` 不再发布 `HoGeometryBufferRenderGraphResources`；它仍临时绑定私有 normal/depth attachment 维持旧 metadata MRT shader target 索引，但消费者只能通过 GeometryBuffer feature 读取公开 normal/depth。
- `HoMetadataBufferRendererFeatureEditor` 提示用户需要单独添加 GeometryBuffer 才能供应 normal/depth 消费者。

后续仍要处理：

- `HoAovOutputPass` 需要改名/迁入 MetadataBuffer，并把旧 MRT shader target 索引拆成 metadata-only shader。
- 当前 GeometryBuffer 只对 fallback path 有专用 shader；未来材质原生输出应增加 `HoGeometryBuffer` LightMode。
- Debug shader 仍使用旧 HoAOV 命名，后续按 MetadataBuffer / GeometryBuffer feature-local debug 分拆。

## 16. 2026-05-25 执行记录：MetadataBuffer settings/pass 命名收敛

已把 MetadataBuffer 运行时入口继续从旧 HoAOV 类名中拆出：

- `HoAovOutputPass` 迁入 `Runtime/MetadataBuffer/HoMetadataBufferPass.cs`，RendererFeature 不再直接引用旧 AOV output pass 类。
- `HoAovDebugPass` 迁入 `Runtime/MetadataBuffer/HoMetadataBufferDebugPass.cs`，RenderGraph pass 名已统一到 `Ho-MetadataBuffer Debug`。
- 新增 `HoMetadataBufferSettings`，RendererFeature 面板字段改用 `passEvent`，旧 `HoAovSettings` 不再作为 MetadataBuffer 设置对象存在。
- `HoAovBufferTypes` 只保留当前仍被材质、RSUV、CharacterSpecialization 和 shader ABI 使用的旧 HoAOV 枚举/通道类型。
- `HoAovRenderTargets` 仍暂留在 AOV 目录，作为非 RenderGraph 路径的兼容 RTHandle 管理；后续拆 metadata-only shader target 时再迁到 MetadataBuffer 或删除。

这一步仍保留 `_lilHoAov*` shader property、`HoAOV` / `HoAOVSSS` LightMode 与 debug shader 文件名，原因是现有材质 pass 和消费者仍依赖这套 ABI。下一步再处理 shader target 拆分或 Metadata/Geometry feature-local debug shader 分拆。

## 17. 2026-05-25 执行记录：RenderTarget 管理从 AOV 拆出

已继续收敛非 RenderGraph 路径的 Buffer 目标管理：

- `HoAovRenderTargets` 迁入 `Runtime/MetadataBuffer/HoMetadataBufferRenderTargets.cs`，作为 MetadataBuffer 兼容 RTHandle 管理类。
- 新增 `Runtime/HoBufferFormatUtility.cs`，集中管理 mask、高精度颜色与 depth/stencil format fallback。
- `GeometryBuffer` 不再借用 AOV render-target 类的静态格式 helper，改读 `HoBufferFormatUtility`。
- `MetadataBuffer` RenderGraph 与 compatibility path 都改读 `HoMetadataBufferRenderTargets` / `HoBufferFormatUtility`。

这一步仍没有重命名 `_lilHoAov*` 纹理名；当前 ScreenProcess、SSS、CharacterSpecialization 与旧材质 pass 仍共用这套 shader ABI。后续拆 metadata-only shader target 或 feature-local debug shader 时再处理资源名与 shader 路径。

## 18. 2026-05-25 执行记录：fallback/format helper 回到 feature 目录

已修正上一步把 `HoBufferFormatUtility.cs` 放到 Runtime 根目录的问题：

- 删除根目录 `Runtime/HoBufferFormatUtility.cs`。
- 新增 `Runtime/MetadataBuffer/HoMetadataBufferFormatUtility.cs`，只服务 MetadataBuffer 的 mask / high precision / depth format fallback。
- 新增 `Runtime/GeometryBuffer/HoGeometryBufferFormatUtility.cs`，只服务 GeometryBuffer 的 high precision / depth format fallback。
- `MetadataBuffer` / `GeometryBuffer` 不再通过根目录共享 helper 耦合 fallback 细节；少量重复代码接受为 feature-local ownership 的代价。
- MetadataBuffer clear/fallback shader 迁到 `Runtime/MetadataBuffer/Shaders/Fallback/`，shader 名改为 `Hidden/lilToon/URP/MetadataBuffer/Clear` 与 `Hidden/lilToon/URP/MetadataBuffer/Fallback`。
- `HoMetadataBufferDebug.shader` 迁到 `Runtime/MetadataBuffer/Shaders/Debug/`，shader 名改为 `Hidden/lilToon/URP/MetadataBuffer/DebugView`，debug shader collection 显式路径同步更新。

这一步继续保留 `_lilHoAov*` 纹理/property ABI 和 `HoAOV` / `HoAOVSSS` 材质 LightMode；它们仍是当前材质 pass、ScreenProcess、SSS 与 CharacterSpecialization 的共享兼容层。

## 19. 2026-05-25 执行记录：MetadataBuffer debug mode 脱离 AOV 类型层

已把 MetadataBuffer debug-only 类型和 shader 参数继续迁回 feature 局部：

- `HoAovDebugMode` 从 `Runtime/AOV/HoAovBufferTypes.cs` 移除。
- 新增 `Runtime/MetadataBuffer/HoMetadataBufferDebugMode.cs`，只服务 MetadataBuffer debug UI / pass / settings。
- MetadataBuffer debug shader 参数从 `_HoAovDebugMode` / `_HoAovDebugDepthParams` 改为 `_HoMetadataBufferDebugMode` / `_HoMetadataBufferDebugDepthParams`。

继续补齐 GeometryBuffer 局部 debug：

- 新增 `Runtime/GeometryBuffer/HoGeometryBufferDebugMode.cs` 与 `Runtime/GeometryBuffer/HoGeometryBufferDebugPass.cs`。
- 新增 `Runtime/GeometryBuffer/Shaders/Debug/HoGeometryBufferDebug.shader`，只显示 GeometryBuffer 自己的 `Coverage`、`LinearDepth`、`WorldNormal`、`NormalValidity`。
- `HoGeometryBufferRendererFeature` 的 debug material 只在 debug mode 打开且当前 camera 类型允许时按需加载；debug shader 缺失不影响 normal/depth 输出。
- GeometryBuffer Inspector 增加 Debug Preview 区域，debug shader collection 生成器同步收集 `HoGeometryBufferDebug.shader`。
- Debug mode 最后一项从旧 `SSS Source Color` 命名收敛为 `Surface Color`，继续读取当前兼容 ABI 的 `_lilHoAovSssTexture`，后续 shader target 拆分时再改资源名。

这一步不改变主输出 MRT、`_lilHoAov*` 纹理/property ABI、`HoAOV` / `HoAOVSSS` 材质 LightMode，也不影响 ScreenProcess、SSS 或 CharacterSpecialization 的消费路径。

## 20. 2026-05-25 执行记录：GeometryBuffer sampling helper 迁出 AOV

已把只服务 normal/depth 解码的 shader helper 从旧 AOV 目录迁到 GeometryBuffer：

- `Runtime/AOV/Shaders/HoAOV/HoAovSampling.hlsl` 迁为 `Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl`。
- helper 函数改名为 `LilHoGeometryBufferCoverage`、`LilHoGeometryBufferLinearDepthOrFar`、`LilHoGeometryBufferEncodedNormalOrBlack`、`LilHoGeometryBufferWorldNormalOrZero` 等 GeometryBuffer 语义。
- MetadataBuffer debug shader、ScreenProcess 的 `EdgeLight` / `PostLighting` 改 include 新路径并调用新函数名。
- 删除空的 `Runtime/AOV/Shaders` 目录 meta；AOV 目录现在只剩对象/材质语义组件、旧材质 LightMode/纹理 ABI 常量和 attachment layout。

这一步仍保留 `_lilHoAovNormalDepthTexture` 纹理名，避免同时改动 ScreenProcess、SSS、CharacterSpecialization 的运行时采样 ABI。

## 21. 2026-05-25 执行记录：MetadataBuffer 类型与 attachment layout 迁出 AOV

已继续把只服务 MetadataBuffer 的类型边界从旧 AOV 类型层拆出：

- 新增 `HoMetadataBufferChannelMask`、`HoMetadataBufferCustomChannels` 与 `HoMetadataBufferObjectChannels`，`HoMetadataBufferSettings` 和对象标记组件改读新的 MetadataBuffer 类型。
- `HoAovChannelMask`、`HoAovCustomChannels`、`HoAovObjectChannels` 已从旧 `Runtime/AOV/HoAovBufferTypes.cs` 移除，避免后续继续把 MetadataBuffer 通道理解成 AOV 公共类型；该文件同步收窄并改名为 `HoAovRenderScale.cs`。
- `HoAovAttachmentLayout` 迁为 `HoMetadataBufferAttachmentLayout`，当前 MetadataBuffer MRT 索引仍保持不变，便于后续拆 metadata-only shader target。
- 旧 `HoAovRenderScale` 暂时单独保留给 CharacterSpecialization 的现有 render scale 参数；本步不把 CharacterSpecialization 混入 MetadataBuffer 类型迁移。
- 本步不改 `_lilHoAov*` shader texture/property ABI，也不改 `HoAOV` / `HoAOVSSS` LightMode；这些仍是当前材质 pass 与 ScreenProcess、SSS、CharacterSpecialization 的兼容层。

## 22. 2026-05-25 执行记录：删除 AOV 代码目录

已把旧 AOV 目录推进到删除状态：

- `HoAovSubject` / `HoAovGroup` 迁为 `HoMetadataBufferSubject` / `HoMetadataBufferGroup`，运行时与 Editor 入口移动到 `Runtime/MetadataBuffer/` 和 `Editor/MetadataBuffer/`。
- `HoAovShaderConstants` 合并进 `HoMetadataBufferShaderConstants`；MetadataBuffer、ScreenProcess、SSS 和 CharacterSpecialization 统一从 MetadataBuffer 命名空间读取当前共享 shader ABI 常量。
- `HoAovRenderScale` 改为 CharacterSpecialization 局部的 `HoCharacterRenderScale`，不再为了一个捕获分辨率 enum 保留 AOV 命名空间。
- 删除 `Runtime/AOV/`、`Editor/AOV/` 以及对应 `.meta` 文件，旧 AOV 不再作为代码目录或命名空间存在。
- 当前仍保留 `_lilHoAov*` 纹理名、`HoAOV` / `HoAOVSSS` LightMode，作为材质 pass / shader ABI 的后续单独迁移项；它们不再由 AOV 目录承载。

## 23. 2026-05-25 执行记录：补收用户可见 AOV 文案

删除 AOV 代码目录后复查 Runtime/Editor 残留，旧 `HoAov*` 类型、命名空间和代码目录没有回流。已补收两处用户可见旧口径：

- ScreenProcess rule mask Inspector 不再显示 `AOV 遮罩` / `AOV 规则` / `AOV 源`，改为规则遮罩、ScreenProcess 规则与 `MetadataBuffer 源`。
- HoSSS 设置面板和提示不再把源数据称作 HoAOV，改为 `MetadataBuffer` 的 SSS 专用通道与 `surfaceData`。

仍保留的 `HoAOV` / `HoAOVSSS` 只作为材质 LightMode 与 shader ABI 名存在，由 `HoMetadataBufferShaderConstants` 承载；这不是 AOV 代码目录或用户入口。

## 24. 2026-05-25 执行记录：GeometryBuffer normal/depth 公开 ABI 改名

已开始把 GeometryBuffer 的公开采样 ABI 从旧 AOV 纹理名中拆出：

- 新增 `_HoGeometryBufferNormalDepthTexture` / `_HoGeometryBufferDepthTexture` 作为 GeometryBuffer 公开 normal/depth 纹理名。
- `HoGeometryBufferPass` 的 RenderGraph 与兼容路径都改用新纹理名创建资源，并在输出后同时绑定旧 `_lilHoAovNormalDepthTexture` / `_lilHoAovDepthTexture` alias，避免尚未迁移的旧 shader 立即断裂。
- GeometryBuffer debug、MetadataBuffer debug、SSS、CharacterSpecialization、ScreenProcess 的 normal/depth shader 采样已改读 `_HoGeometryBufferNormalDepthTexture`。
- RenderGraph 消费点已改用 `HoGeometryBufferShaderConstants.NormalDepthTextureId`，不再通过 MetadataBuffer 常量表达 normal/depth 依赖。

本步没有改动 MetadataBuffer 的对象/材质语义纹理名，也没有移除 `HoAOV` / `HoAOVSSS` LightMode。下一步继续收口 MetadataBuffer 自身的 `_lilHoAovMaskIdTexture`、`_lilHoAovSurfaceDataTexture`、custom/object custom 与 SurfaceColor 纹理命名。
