# Debug Shader 按需编译策略

> 目标：普通用户不应因为安装或启用主功能而编译所有重 debug shader。Debug 是一等能力，但必须是显式启用的能力。

---

## 0. 问题

当前多个 feature 的 debug shader 或 debug 分支已经变重：

- AOV debug 需要 decode 多个 MRT。
- SSS debug 需要展示 source / diffusion / composite / profile。
- ShadowCast debug 需要 atlas / slice / receiver 视图。
- ScreenProcess AOV/rule debug 容易把 mask 规则和实际效果 shader 耦合。
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

- `Runtime/AOV/Shaders/HoAOV/HoAovDebug.shader`
- `Runtime/ShadowCast/Shaders/HoShadowCastDebug.shader`
- `Runtime/SubsurfaceScattering/HoSubsurfaceScattering.shader` 内部 debug 分支
- `Runtime/HoPostProcessing/Shaders/HoPost/HoPostAovMask.hlsl` 相关 debug 输出
- `Runtime/ShoostPostProcessing/Shaders/Shoost/AovComposite.shader` 迁出到 ScreenProcess 或删除

其中 AOV / ShadowCast 更适合独立 debug shader；ScreenProcess 的 AOV/rule mask debug 应从常规效果 shader 中降级为局部可选路径。
ImageProcess 不再提供 AOV mask debug 或 AOV composite debug。

---

## 7. 2026-05-24 执行记录

已对 ShadowCast debug shader 做第一批按需加载修正：

- `HoShadowCastRendererFeature` 不再在主渲染路径默认调用 debug shader 查找。
- 只有 `HoShadowCastController.debugMode != Off` 且 debug pass 即将入队时，才调用 `Shader.Find(HoShadowCastShaderConstants.DebugShaderName)` 并创建 debug material。
- debug shader 缺失仍只 warning once 并跳过 debug pass，不影响 ShadowCast 主 pass。

已继续对 AOV debug shader 做按需加载修正：

- `HoAovRendererFeature` 的主材质准备流程改为 `EnsureMaterials(includeDebug)`。
- 只有 `HoAovSettings.debugMode != Off` 且当前 camera 类型允许 debug 显示时，才调用 `Shader.Find(HoAovShaderConstants.DebugShaderName)` 并创建 debug material。
- debug 关闭时仍正常创建 clear / fallback 主功能材质，但不会查找或加载 `HoAovDebug.shader`。
- debug shader 缺失仍只 warning once，并且只影响 AOV debug pass，不影响 AOV output pass。

待处理：

- SSS debug 分支、ScreenProcess AOV/rule debug 还未迁到局部按需收集策略。
- 尚未增加 editor 菜单生成 debug shader collection。

---

## 8. 验收清单

- 普通用户不打开 debug 时，不会加载重 debug shader。
- 缺失 debug shader 不影响主 feature。
- Debug UI 能显示当前 feature 哪些 debug shader 未启用。
- 打开 debug profile 后，tile view 能显示短命名。
- 构建中 debug shader inclusion 可被用户明确控制。
