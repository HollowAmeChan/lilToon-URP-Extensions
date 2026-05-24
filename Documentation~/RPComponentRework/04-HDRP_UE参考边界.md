# HDRP / UE 参考边界

> 未来规划可以参考 HDRP、Unreal Engine 等成熟系统，但参考对象是它们的结构性经验，不是照搬复杂度、命名或完整实现。

---

## 0. 参考原则

成熟管线值得参考的是：

- buffer 按职责分层，而不是所有数据都进一个万能 AOV。
- pass 注入点决定可读写的 buffer，而不是 feature 想什么时候读就什么时候读。
- debug visualization 是正式工具，但 debug shader / debug variant 需要显式启用。
- RenderGraph / RDG 管理 transient resource 生命周期、依赖、pass culling 和校验。
- buffer visualization 面向材质和几何真实语义，而不是面向内部临时 RT。

不能照搬的是：

- HDRP/UE 的完整复杂度。
- 它们的物理材质模型。
- 它们的固定 GBuffer layout。
- 它们的全套 editor/debug 面板。
- 它们为了大型引擎生态存在的兼容层。

本仓库仍然是角色/NPR/URP 扩展优先，目标是薄、可调试、能继续开发。

---

## 1. 从 HDRP 借鉴什么

HDRP Custom Pass 的核心经验是：不同 injection point 能访问的 buffer 不一样，且每个 buffer 的读写权限也不同。

这对本仓库的启发：

- `MaterialBuffer` / `GeometryBuffer` 必须明确在哪个阶段成立。
- `ScreenProcess` 不能假设任何时候都能读完整 geometry/material 输入。
- `ImageProcess` 如果读 scene color 又写回 camera color，必须显式 copy 或使用临时 buffer。
- depth / normal / motion 的可用性和完整性要按 pass 时机判断。
- 透明、折射、水体、体积雾这类效果不能混在最终图像栈里随便读写。

HDRP 的 debug 也值得参考：

- Debugger 按 Material / Lighting / Rendering 等类别组织。
- Runtime debug shader 需要显式设置支持。
- Debug view 直接显示真实材质/光照/渲染属性。

这和当前策略一致：debug 是正式能力，但重 debug shader 必须按需启用。

---

## 2. 从 Unreal Engine 借鉴什么

UE 的 GBuffer/Buffer Visualization 值得参考的是“可视化目标就是材质和几何语义”。

典型可视化项包括：

- BaseColor / DiffuseColor
- SubsurfaceColor
- ShadingModel
- MaterialAO
- Metallic / Roughness / Specular
- Opacity
- SceneDepth
- WorldNormal / WorldTangent
- Velocity
- PreTonemapHDRColor / PostTonemapHDRColor

这说明未来规划 `MaterialBuffer` / `GeometryBuffer` 时，不应该只看当前 HoAOV 旧通道，还要看未来多个效果会共同消费哪些材质输入和几何输入。

UE 的 RDG 经验也直接适用：

- pass 声明资源依赖。
- transient resource 由图管理生命周期。
- unused pass/resource 可以被裁剪。
- debug/profiling 可以观察图结构和资源生命周期。

这支持我们当前对 ImageProcess 的底线：所有额外 RT 都必须走 RDG transient，不再由 effect 私自维护。

UE decal 的 DBuffer/GBuffer 分层也值得借鉴：有些材质修改应该在 base pass/lighting 前成为材质输入，有些只能后置 blend。对应到本仓库，就是区分：

- 进入 `MaterialBuffer` 的通用表面语义。
- 进入 `ScreenProcess` 的后置屏幕合成。
- 进入 `ImageProcess` 的最终图像效果。

---

## 3. 对 MaterialBuffer 的影响

参考 HDRP/UE 后，`MaterialBuffer` 不应只是旧 HoAOV 的 mask/custom 容器，而应承载未来效果会读的通用表面输入。

优先级更高的候选：

- `SurfaceColor` / `DiffuseColor`
- `SubsurfaceColor`
- `MaterialClass` / 类似 ShadingModel 的粗分类
- `MaterialProfile`
- `Thickness`
- `Curvature`
- `Opacity` / `Coverage`，仅当透明、SSS 或体积交互需要
- `MaterialAO`，仅当 screen-space lighting / fog / stylized lighting 需要
- `RoughnessLike` / `SmoothnessLike`，仅当场景 deferred 或 screen-space highlight 需要

继续删除或默认不输出：

- 没有消费者的整体 mask。
- 未命名 custom。
- 为单个 feature 临时准备的 runtime source。

`SssSource.rgb` 应正式去 SSS 私有化，规划为：

```text
MaterialBuffer.SurfaceColor.rgb
```

---

## 4. 对 GeometryBuffer 的影响

`GeometryBuffer` 应参考成熟管线里稳定的几何输入，而不是把所有几何派生量都塞进去。

优先级更高的候选：

- `Depth`
- `Normal`
- `Motion` / `Velocity`
- `Coverage`，仅当透明/角色覆盖/temporal 需要

暂不默认输出：

- `TangentNormal`
- `WorldTangent`
- 任意 reserved direction vector

`WorldTangent` 在 UE buffer visualization 中存在，但它是否进入本仓库的 `GeometryBuffer` 要看真实消费者。没有各向异性、头发屏幕空间高光、切线方向滤波等需求时，不输出。

---

## 5. 对 Debug 的影响

成熟系统都提供 buffer visualization，但这不意味着所有 debug shader 默认编译。

本仓库采用：

- feature 局部声明 debug view。
- 公共层生成菜单和 tile view。
- tile 显示短命名。
- 重 debug shader 必须显式启用。
- debug view 显示真实 buffer 语义，不显示已废弃或预留通道。

可以参考 UE 的 Buffer Overview 组织方式：

```text
SurfaceColor
SubsurfaceColor
MaterialClass
Thickness
Curvature
Depth
Normal
Velocity
PreImage
PostImage
```

但不要把这些目标都变成默认输出；debug target 只观察已经存在的真实资源。

---

## 6. 对 ImageProcess 的影响

HDRP/UE 都体现了一个原则：最终图像处理和场景/材质 buffer 处理不是一回事。

因此：

- `ImageProcess` 默认只处理图像链。
- 纯图像效果使用 ImageChain 双缓冲。
- 需要 pyramid/history/local ping-pong 的 effect 必须局部声明。
- 需要 MaterialBuffer/GeometryBuffer/mask/ShadowCast 的 effect 迁到 `ScreenProcess`。
- `ImageProcess` 不提供 semantic image pass 例外。

---

## 7. 参考资料

- Unity HDRP Custom Pass Injection Points  
  https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Custom-Pass-Injection-Points.html

- Unity HDRP Rendering Debugger  
  https://docs.unity.cn/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Render-Pipeline-Debug-Window.html

- Unreal Engine Render Dependency Graph  
  https://dev.epicgames.com/documentation/en-us/unreal-engine/render-dependency-graph-in-unreal-engine

- Unreal Engine Viewport Buffer Visualization  
  https://dev.epicgames.com/documentation/en-us/unreal-engine/viewport-modes-in-unreal-engine

- Unreal Engine Console Variables: Buffer Visualization targets  
  https://dev.epicgames.com/documentation/unreal-engine/unreal-engine-console-variables-reference

- Unreal Engine Decal Materials: DBuffer / GBuffer Decals  
  https://dev.epicgames.com/documentation/unreal-engine/decal-materials-in-unreal-engine
