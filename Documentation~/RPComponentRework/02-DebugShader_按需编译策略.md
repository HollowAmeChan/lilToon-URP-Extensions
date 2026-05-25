# Debug Shader 按需编译策略

> 目标：普通用户不应因为安装或启用主功能而编译所有重 debug shader。Debug 是一等能力，但必须是显式启用的能力。

---

## 0. 问题

当前多个 feature 的 debug shader 或 debug 分支已经变重：

- AOV debug 需要 decode 多个 MRT。
- SSS debug 需要展示 source / diffusion / composite / profile。
- ShadowCast debug 需要 atlas / slice / receiver 视图。
- ScreenProcess rule mask debug 已由每个 layer 的 `debugRuleMask` / `_LayerRuleDebugOutput` 直出承担，不需要 RendererFeature 侧独立 debug shader。
- 旧 Shoost AOV composite / mask debug 是迁移对象，不作为 ImageProcess 新能力保留。

如果这些 shader 默认被引用、默认进变体收集、默认编译，会让普通用户项目付出不必要的导入和编译成本。

---

## 1. 总规则

默认包只保证主功能可用。

debug shader 必须满足：

- 用户显式打开 debug 功能后才查找、加载或收集。
- debug shader 缺失时主 feature 不能失败。
- runtime feature 不在 `Create()` 中强制创建所有 debug material。
- debug shader collection 不默认参与普通构建。
- debug-only keyword 不进入普通 effect shader 的默认变体爆炸路径。

---

## 2. Feature 侧职责

每个 feature 自己维护 debug 归属。
Debug shader、debug material、debug view descriptor、tile 短命名、shader collection 收集入口都必须放在 feature 自己的目录或 editor 子目录下。
公共 Debug UI 只能读取这些局部描述生成菜单、enum 和 tile 视图，不能把资源重新搬进一个中心 Debug 目录。

推荐结构：

```text
Runtime/<Feature>/Shaders/
Runtime/<Feature>/Shaders/Debug/
Runtime/<Feature>/<Feature>DebugMode.cs
Runtime/<Feature>/<Feature>DebugViewInfo.cs
Editor/<Feature>/Debug/<Feature>DebugShaderCollector.cs
```

不推荐结构：

```text
Runtime/Debug/Shaders/<Feature>Debug.shader
Runtime/Debug/<Feature>DebugViewInfo.cs
Editor/Debug/<Feature>DebugShaderCollector.cs
```

原因是 debug 也是 feature 的一部分。它可以被公共调试面板观察和编排，但 ownership 必须留在 feature 内部。

Feature 提供：

- debug view id。
- tile 短命名。
- shader 名。
- 是否重 shader。
- 是否需要 AOV / GeometryBuffer / atlas / history。
- 缺失 shader 时的降级消息。

公共 debug UI 只读取这些局部信息生成菜单和 tile，不重新定义资源归属。

2026-05-26 后，运行时契约落在 `Runtime/Debug/HoDebugViewInfo.cs` 与 `HoDebugViewRegistry`。各 feature 仍在自己的目录声明 `<Feature>DebugViewInfo.cs`；公共 registry 只汇总 view 元数据，供 Editor 工具、未来 Debug UI 和 tile view 读取，不拥有 shader、material 或 RenderTexture。

---

## 3. 编译与收集策略

可选方案按优先级：

### 方案 A：Debug Shader Collection 显式生成

Editor 菜单：

```text
lilToon URP Extensions/Debug/Generate Debug Shader Collection
```

只有用户执行后才把 debug shader 放入 collection。

### 方案 B：Debug Profile Asset

提供一个可选 asset：

```text
LilUrpDebugProfile.asset
```

用户把它放进项目并启用后，editor 工具收集对应 shader。

### 方案 C：Scripting Define

例如：

```text
LIL_URP_EXTENSIONS_ENABLE_DEBUG_SHADERS
```

只有 define 存在时，editor 才收集重 debug shader。

### 方案 D：Samples

把完整 debug shader collection 放进 sample，用户需要时导入。

---

## 4. Shader 写法约束

主效果 shader 中允许轻量 debug 分支，但不允许默认塞入重 debug 功能。

允许：

- 显示当前 pass 的简单 mask。
- 显示 alpha / coverage。
- 用已有采样结果 tint。

不允许默认内置：

- 遍历多张 AOV。
- atlas tile decode。
- 多 profile decode。
- 多模式大 switch。
- 为 debug 引入额外 texture 依赖。

重 debug 应独立 shader：

```text
Hidden/lilToon/URP/<Feature>/Debug/<ViewName>
```

---

## 5. Runtime 行为

推荐 lazy-load：

```text
if debug disabled:
    do not Shader.Find(debug shader)
    do not create debug material
    do not enqueue debug pass

if debug enabled:
    Shader.Find(debug shader)
    if missing: warn once and skip debug pass
```

禁止：

- 主 feature `Create()` 默认创建全部 debug materials。
- debug shader missing 导致主 feature 不工作。
- debug view 开关默认持久打开。

---

## 6. 第一批处理对象

优先处理：

- `Runtime/MetadataBuffer/Shaders/Debug/HoMetadataBufferDebug.shader`
- `Runtime/ShadowCast/Shaders/Debug/HoShadowCastDebug.shader`
- `Runtime/SubsurfaceScattering/HoSubsurfaceScattering.shader` 内部 debug 分支
- `Runtime/ScreenProcess/Shaders/ScreenProcess/ScreenProcessRuleMask.hlsl` 相关 layer-local debug 输出
- `Runtime/ImageProcess/Shaders/ImageProcess/AovComposite.shader` 迁出到 ScreenProcess 或删除

其中 MetadataBuffer / ShadowCast 更适合独立 debug shader；ScreenProcess 的 rule mask debug 已经是局部可选直出路径：用户在具体 layer 上打开 `debugRuleMask` 后，由该 layer 的效果 shader 通过 `_LayerRuleDebugOutput` 输出规则遮罩结果，不再作为 RendererFeature debug pass 或公共 tile view 的待拆项。
ImageProcess 不再提供 AOV mask debug 或 AOV composite debug。

---

## 7. 2026-05-24 执行记录

已对 ShadowCast debug shader 做第一批按需加载修正：

- `HoShadowCastRendererFeature` 不再在主渲染路径默认调用 debug shader 查找。
- 只有 ShadowCast frame config 的 `debugMode != Off` 且 debug pass 即将入队时，才调用 `Shader.Find(HoShadowCastShaderConstants.DebugShaderName)` 并创建 debug material；该 debug mode 来自 RendererFeature 设置，旧 controller override 已删除。
- debug shader 缺失仍只 warning once 并跳过 debug pass，不影响 ShadowCast 主 pass。
- `HoShadowCastDebug.shader` 已移动到 `Runtime/ShadowCast/Shaders/Debug/`，shader 名与 `.meta` GUID 保持不变，只调整资源归属。
- 2026-05-25 继续新增 `HoShadowCastDebugMaterial`，把 ShadowCast debug shader 查找、warning once、material 创建和释放收拢到 feature-local helper；RendererFeature 仍只在 `debugMode != Off` 时调用它。

已继续对 AOV debug shader 做按需加载修正：

- AOV/MetadataBuffer 主材质准备流程改为 `EnsureMaterials(includeDebug)`；2026-05-25 后用户入口类名已迁为 `HoMetadataBufferRendererFeature`。
- 只有 `HoMetadataBufferSettings.debugMode != Off` 且当前 camera 类型允许 debug 显示时，才调用 `Shader.Find(HoMetadataBufferShaderConstants.DebugShaderName)` 并创建 debug material。
- debug 关闭时仍正常创建 clear / fallback 主功能材质，但不会查找或加载 `HoMetadataBufferDebug.shader`。
- debug shader 缺失仍只 warning once，并且只影响 AOV debug pass，不影响 AOV output pass。
- 2026-05-25 继续把 `HoAovDebug.shader` 更名并迁到 `Runtime/MetadataBuffer/Shaders/Debug/HoMetadataBufferDebug.shader`，shader 名改为 `Hidden/lilToon/URP/MetadataBuffer/DebugView`；`LilUrpDebugShaderCollectionGenerator` 的显式收集路径同步更新到 MetadataBuffer feature-local debug 目录。
- 2026-05-25 继续把 `HoAovDebugMode` 迁为 `HoMetadataBufferDebugMode`，debug-only shader 参数改为 `_HoMetadataBufferDebugMode` / `_HoMetadataBufferDebugDepthParams`；旧 HoAOV 类型层不再承载 MetadataBuffer debug mode。

已补上显式生成 debug shader collection 的 Editor 入口：

- 新增 `Editor/Debug/LilUrpDebugShaderCollectionGenerator.cs`。
- 菜单路径为 `lilToon URP Extensions/Debug/Generate Debug Shader Collection`。
- 生成目标为宿主工程 `Assets/lilToon URP Extensions/Debug/LilUrpDebugShaders.shadervariants`，不把 collection 默认放入运行时包资源。
- 第一批只收集已经按需加载的独立重 debug shader：`HoMetadataBufferDebug.shader`、`HoGeometryBufferDebug.shader`、`HoShadowCastDebug.shader` 与 `HoSubsurfaceScatteringDebug.shader`。
- 生成器优先通过 package asset path 加载 shader，失败时才 fallback 到 `Shader.Find`，因此普通运行时仍不会因为安装包而主动查找 debug shader。
- 2026-05-26 生成器改为读取 `HoDebugViewRegistry.ShaderCollectionViews`，并按 shader name / asset path 去重；ScreenProcess 与 ImageProcess 这类非重 shader view 会登记到 view info，但不会进入 collection。

待处理：

- ScreenProcess rule mask debug 已作为 `ScreenProcessDebugViewInfo` 的非重 shader view 登记；它的执行入口就是 layer-local `debugRuleMask` 直出，不计划拆成独立 RendererFeature debug shader。
- 后续 debug profile / tile view 仍需基于 feature 局部 view info 接入。

已继续对 SSS debug 分支做按需编译拆分：

- `HoSubsurfaceScattering.shader` 移除内置 debug 输出大分支，主 shader 只保留 Source / Diffusion / Transmission / Composite 主功能 pass。
- 新增 `Runtime/SubsurfaceScattering/Shaders/Debug/HoSubsurfaceScatteringDebug.shader`，shader 名为 `Hidden/lilToon/URP/HoSubsurfaceScattering/DebugView`，负责 `Mask`、`Source`、`Diffusion`、`Transmission`、`TransmissionGate`、`CompositeWeight`、`ProfileId`、`Thickness`、`ProfileRadius`、`TransmissionDirection`、`TransmissionRim` 等 SSS debug view。
- `HoSubsurfaceScatteringRendererFeature` 只在 `debugMode != Off` 时才 `Shader.Find(HoSubsurfaceScatteringShaderConstants.DebugShaderName)` 并创建 debug material；缺失 debug shader 时只 warning once，主 SSS pass 继续运行。
- Debug 输出从 Composite pass 内联分支改为独立 `HoSubsurfaceScatteringDebugPass.cs`，RenderGraph 与 compatibility path 都只在 debug 开启时入队，且读取当前帧 SSS / transmission / AOV 资源后覆盖 camera color。
- 主合成参数从旧 `_lilHoSSSDebugParams` 拆为 `_lilHoSSSCompositeParams`，避免主 shader 继续携带 debug mode ABI。
- `LilUrpDebugShaderCollectionGenerator` 显式收集 SSS debug shader；普通用户不生成 collection 时不会因为启用主 SSS 功能而主动查找该 debug shader。

已补上 GeometryBuffer 局部 debug 模式：

- 新增 `HoGeometryBufferDebugMode`、`HoGeometryBufferDebugPass` 和 `Runtime/GeometryBuffer/Shaders/Debug/HoGeometryBufferDebug.shader`。
- GeometryBuffer debug shader 名为 `Hidden/lilToon/URP/GeometryBuffer/DebugView`，提供 `Coverage`、`LinearDepth`、`WorldNormal`、`NormalValidity` 视图。
- `HoGeometryBufferRendererFeature` 只在 `debugMode != Off` 且当前 camera 类型允许 debug 显示时才查找或创建 debug material；缺失 debug shader 只 warning once，不影响 GeometryBuffer 输出。
- `LilUrpDebugShaderCollectionGenerator` 显式收集 GeometryBuffer debug shader；普通用户不生成 collection 时不会因为启用 GeometryBuffer 主功能而主动查找该 debug shader。

2026-05-26 继续收窄 MetadataBuffer debug 归属：

- `HoMetadataBufferDebugMode` 删除 `LinearDepth`、`WorldNormal`、`Velocity`，MetadataBuffer debug 只显示自身 metadata、custom、RSUV 和 SurfaceColor。
- `HoMetadataBufferDebugPass` 不再读取 `HoGeometryBufferRenderGraphResources`，也不再向 debug shader 绑定 `_HoGeometryBufferNormalDepthTexture`。
- MetadataBuffer debug shader 删除 `HoGeometryBufferSampling.hlsl` include 与 `_HoMetadataBufferDebugDepthParams`，几何调试统一转到 GeometryBuffer debug view。

2026-05-26 继续补齐 feature-local debug view contract：

- 新增 `Runtime/Debug/HoDebugViewInfo.cs` / `HoDebugViewRegistry.cs`，公共层只聚合 view id、短名、mode value、shader 名、asset path、是否进入 shader collection 和缺失降级说明。
- MetadataBuffer、GeometryBuffer、ShadowCast、SSS、ScreenProcess、ImageProcess 分别新增 `<Feature>DebugViewInfo.cs`，view 列表归属仍留在 feature 目录。
- `LilUrpDebugShaderCollectionGenerator` 不再维护硬编码 debug shader 表，改读 registry 中标记为 `RequiresShaderCollection` 的 view，并对同一 shader 去重。
- ScreenProcess rule mask 和 ImageProcess layer chain 登记为无独立 shader 的轻量观察入口，公共 Debug UI / tile view 不因此接管 effect shader 或图像链资源。ScreenProcess rule mask 的实际排障方式是打开具体 layer 的 `debugRuleMask` 直出，而不是切换 RendererFeature 级 debug view。

---

## 8. 验收清单

- 普通用户不打开 debug 时，不会加载重 debug shader。
- 缺失 debug shader 不影响主 feature。
- Debug UI 能显示当前 feature 哪些 debug shader 未启用。
- 打开 debug profile 后，tile view 能显示短命名。
- 构建中 debug shader inclusion 可被用户明确控制。
