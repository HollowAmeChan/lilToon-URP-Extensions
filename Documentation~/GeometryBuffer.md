# GeometryBuffer 公共契约

> GeometryBuffer 是 Ho-URP Extensions 的屏幕几何输入层。
>
> 它提供真实几何的法线、线性深度和有效覆盖判断，并额外提供与物理几何隔离的描边视觉 coverage。AO、GI、SSS、反射、角色特化和 ScreenProcess 都应按本文的语义消费这些资源。

## 1. 设计边界

GeometryBuffer 不是 URP `_CameraDepthTexture` 的别名，也不是完整 deferred GBuffer。它是由 Ho 管线主动重绘得到的、可复用的屏幕空间几何层：

```text
真实几何            -> NormalDepth / DepthTexture
视觉描边外扩壳      -> OutlineCoverageTexture
天空/无几何区域      -> SkyTexture（可选）
```

最重要的语义分离：

- `NormalDepth.a` 的 coverage 表示“这里有没有真实几何表面”；
- 描边外扩壳不是物理几何，不能写入 `NormalDepth.a`；
- `_HoGeometryBufferOutlineCoverageTexture` 只表示“这里有可见描边颜色”，不能被 AO/GI 当作表面；
- CameraColor 仍然是已经渲染的颜色源，GeometryBuffer 不替代它。

这套分离解决两个相反的问题：SSGI/GTAO 不会把描边当真实表面，DOF 等视觉后处理又可以识别并保护描边。

GeometryBuffer 的长期扩展模型是：**PhysicalGeometryBuffer 保持稳定，VisualSurfaceBuffer 按需增加**。后续增加描边深度、视觉法线或颜色时，不应覆盖物理通道，而应作为视觉表面扩展供特定消费者选择。

## 2. 生产顺序

入口：`Runtime/GeometryBuffer/HoGeometryBufferRendererFeature.cs`

默认时序：

```text
beginCameraRendering
  -> 重置 GeometryBuffer 全局状态
  -> Ho-GeometryBuffer Output（默认 BeforeRenderingOpaques）
       -> fallback/base GeometryBuffer pass
       -> lilToon HoGeometryBuffer pass
       -> lilToon HoGeometryBufferOutlineCoverage pass
  -> 可选 Ho-GeometryBuffer Sky（默认 AfterRenderingSkybox）
  -> 其他消费者读取全局 RT / RenderGraph TextureHandle
  -> 相机结束或 ScreenProcess 收尾时清理全局绑定
```

`HoGeometryBufferPass` 同时生产真实几何和描边 coverage：

1. 清空 `NormalDepthTexture` 和独立 depth attachment。
2. 使用 `LightMode = HoGeometryBuffer` 绘制基础几何；没有该 pass 的材质可走 fallback shader。
3. 清空 `OutlineCoverageTexture`，使用 `LightMode = HoGeometryBufferOutlineCoverage` 绘制外扩描边；该 pass 只写 R 通道 coverage，不写真实 geometry。
4. 发布全局纹理和 `_HoGeometryBufferValid`。

GeometryBuffer 的 `passEvent`、render queue、layer mask、render scale 都由 `HoGeometryBufferSettings` 控制。默认 `passEvent` 是 `BeforeRenderingOpaques`，默认 render scale 是 Full。

## 3. 输出资源

### 3.1 NormalDepth

全局名：`_HoGeometryBufferNormalDepthTexture`

默认优先格式：`R16G16B16A16_SFloat`。

| 通道 | 语义 | 无效值 |
| --- | --- | --- |
| `RGB` | 世界法线编码：`normal * 0.5 + 0.5` | 未定义；必须先检查 coverage/normal validity |
| `A` | 线性 eye depth，单位与相机空间深度一致 | `0` |

Coverage 不是额外的第五通道，而是从 alpha 派生：

```hlsl
half coverage = LilHoGeometryBufferCoverage(normalDepth);
// 等价于 step(0.0001, normalDepth.a)
```

公共采样函数位于 `Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl`：

- `LilHoGeometryBufferCoverage()`：判断真实几何是否覆盖像素；
- `LilHoGeometryBufferNormalValid()`：判断 coverage 和法线是否同时有效；
- `LilHoGeometryBufferLinearDepthOrFar()`：无 coverage 时返回调用方指定的远裁剪面；
- `LilHoGeometryBufferWorldNormalOrZero()`：无效法线返回零。

#### NormalDepth 的消费者规则

- AO/GI/SSS/PlanarReflection/CharacterSpecialization：必须以 coverage 作为几何有效性 gate。
- coverage 为 0 的像素不能进入 GI source、history、depth pyramid、AO 表面采样或物理遮挡判断。
- 不能把 `NormalDepth.a == 0` 直接当普通深度 0；需要使用 `LilHoGeometryBufferLinearDepthOrFar()` 或显式跳过。
- 描边像素即使在 CameraColor 中有黑色/彩色，也应保持 `NormalDepth` coverage 为 0。

### 3.2 独立 DepthTexture

全局名：`_HoGeometryBufferDepthTexture`

这是 GeometryBuffer pass 使用的独立 depth/stencil attachment，主要用于：

- GeometryBuffer 本身的深度测试；
- OutlineCoverage pass 的可见性测试；
- 需要硬 ZTest 的内部绘制流程。

它不是 `_CameraDepthTexture` 的别名，也不应该被当作可以直接采样的线性深度语义。需要在 shader 中消费深度语义时，优先读取 `NormalDepth.a`。

### 3.3 OutlineCoverageTexture

全局名：`_HoGeometryBufferOutlineCoverageTexture`

默认格式：`R8_UNorm`，不支持时回退到 `R8G8B8A8_UNorm`。

| 值 | 语义 |
| --- | --- |
| `0` | 当前像素没有可见 outline coverage |
| `1` | 当前像素由 lilToon 外扩描边 pass 覆盖 |

这个 RT 的设计目的不是让描边成为真实表面，而是让视觉后处理识别描边：

- DOF 可以保护描边中心像素，拒绝描边样本污染主体模糊；
- 后续可以在 blur 后重新合成描边；
- 调试系统可以直接验证描边 pass 是否被执行；
- SSGI/GTAO 不应读取它来建立物理几何。

OutlineCoverage pass 使用 `LightMode = HoGeometryBufferOutlineCoverage`，顶点路径启用 lilToon 的 `LIL_OUTLINE`，因此会执行 outline vertex expansion；fragment 只做 outline alpha/cutout/dissolve clipping，输出常量 coverage 1。

lilToon 的 `CustomShaderResources/URP/Default*Outline.lilblock` 模板直接声明这个 pass；`DefaultUsePassOutline*.lilblock` 通过 `UsePass` 引用它。`lilShaderContainerImporter` 只负责展开模板占位符，不承载这个 pass 的业务结构。修改模板后需要执行：

```text
Assets/lilToon/[Shader] Refresh shaders
```

否则已有生成 shader 仍没有新 pass，OutlineCoverageTexture 会是全黑。

### 3.4 SkyTexture

全局名：`_HoGeometryBufferSkyTexture`

仅在 `HoGeometryBufferSettings.enableSkyBuffer` 开启时生产。由 `HoGeometryBufferSkyPass` 在天空绘制之后从当前 camera color 捕获：

| 通道 | 语义 |
| --- | --- |
| `RGB` | 天空/背景 radiance |
| `A` | sky contribution / 可用性贡献 |

Sky capture 会读取 `NormalDepth` coverage：无真实几何的区域才贡献天空。`SkyTyndall` 等天空效果必须同时检查 `_HoGeometryBufferSkyTextureValid`。

### 3.5 Valid flags

- `_HoGeometryBufferValid`：GeometryBuffer 主 pass 是否在当前帧生产了真实资源；
- `_HoGeometryBufferSkyTextureValid`：SkyTexture 是否在当前帧生产。

GeometryBuffer 被禁用、当前 camera 不支持或资源被 reset 时，纹理会回退到 black texture，valid flag 会清零。消费者必须把 valid flag 和纹理有效性一起考虑。

## 4. lilToon 输入 pass

### 4.1 基础几何

lilToon 的各类基础/outline shader 需要提供：

```text
LightMode = HoGeometryBuffer
fragment  = fragGeometryBuffer
```

`fragGeometryBuffer()` 输出基础网格位置的世界法线和线性深度。该 pass 不定义 `LIL_OUTLINE`，因此不会执行外扩顶点变换。

没有该 pass 的普通材质可以使用 `Hidden/lilToon/URP/GeometryBuffer/Fallback`，但 fallback 只能提供基础几何，不能自动产生描边 coverage。

### 4.2 描边 coverage

带 lilToon 描边的 shader 额外提供：

```text
LightMode = HoGeometryBufferOutlineCoverage
fragment  = fragOutlineCoverage
define    LIL_OUTLINE
```

这个 pass 与基础 `HoGeometryBuffer` pass 有意分离。不能简单地给基础 GeometryBuffer pass 加 `LIL_OUTLINE`，因为那会把整颗 mesh 的基础几何也按 outline expansion 外扩，导致主体法线/深度错误。

## 5. RenderGraph 与兼容路径

### RenderGraph

`HoGeometryBufferRenderGraphResources` 每帧暴露：

- `normalDepthTexture`
- `depthTexture`
- `outlineCoverageTexture`
- `skyTexture`

生产 pass 使用 `SetGlobalTextureAfterPass` 发布全局资源；消费者通过 `frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>()` 获取 TextureHandle，并在实际读取时 `builder.UseTexture(..., AccessFlags.Read)`。

### Compatibility

兼容路径使用 `RTHandle`：

- 基础几何先写 `NormalDepthTexture + DepthTexture`；
- 描边 coverage 再绑定 `OutlineCoverageTexture + DepthTexture`，保留 depth 内容做 ZTest；
- pass 结束后设置对应 global texture；
- camera begin/reset 或 ScreenProcess 收尾时恢复 black texture 和 valid flag。

## 6. DebugTile 调试入口

GeometryBuffer DebugView 和 DebugTile 已注册：

| View ID | 作用 |
| --- | --- |
| `geometry.coverage` | 真实几何 coverage |
| `geometry.linear-depth` | NormalDepth alpha 的线性深度 |
| `geometry.world-normal` | 编码世界法线 |
| `geometry.normal-validity` | coverage + 法线有效性 |
| `geometry.outline-coverage` | 独立描边 coverage，预期描边区域为白色 |
| `geometry.sky-radiance` | SkyTexture RGB |
| `geometry.sky-contribution` | SkyTexture alpha |

排查描边时必须同时看：

1. `geometry.coverage`：描边应保持黑色/无真实几何 coverage；
2. `geometry.outline-coverage`：描边应为白色；
3. `geometry.linear-depth`：基础表面深度是否连续；
4. DOF 开关前后 CameraColor：确认描边保护是否生效。

## 7. 消费者契约

| 消费者 | 应读什么 | 不应读什么 |
| --- | --- | --- |
| GTAO | NormalDepth normal/depth/coverage | OutlineCoverage 当作几何 |
| Ho-SSGI | NormalDepth coverage、normal、depth | OutlineCoverage 作为 caster/receiver |
| SSS | NormalDepth 几何 + MetadataBuffer 语义 | 用描边 coverage 伪造物理表面 |
| PlanarReflection | NormalDepth 深度/法线与 MetadataBuffer mask | 把 outline mask 当反射平面 |
| CharacterSpecialization | NormalDepth + MetadataBuffer | 用 outline coverage 推断角色几何 |
| ScreenProcess DOF | NormalDepth 深度 + OutlineCoverage 保护视觉描边 | 把描边写入 NormalDepth |
| ScreenProcess Outline/EdgeLight | NormalDepth 几何；必要时 MetadataBuffer | 把 OutlineCoverage 当真实法线/深度 |

公共原则：

```text
NormalDepth = physical geometry truth
OutlineCoverage = visual outline truth
MetadataBuffer = object/material semantic truth
CameraColor = rendered color source
```

## 8. 常见错误

- 只打开 GeometryBuffer，但没有刷新 lilToon shader：NormalDepth 有数据，OutlineCoverage 全黑。
- 把 `_HoGeometryBufferDepthTexture` 当线性深度采样；应该使用 `NormalDepth.a`。
- 把 outline coverage 写入 `NormalDepth.a`，导致 AO/GI/SSS 把描边当真实表面。
- GeometryBuffer layer mask 或 render queue 没覆盖角色，导致基础几何和 outline coverage 都缺失。
- 只看 `geometry.coverage` 就判断描边是否输出；描边的正确检查是同时看 physical coverage 和 outline coverage。

## 9. 相关源码

- Producer：`Runtime/GeometryBuffer/HoGeometryBufferRendererFeature.cs`
- 主 pass：`Runtime/GeometryBuffer/HoGeometryBufferPass.cs`
- RT：`Runtime/GeometryBuffer/HoGeometryBufferRenderTargets.cs`
- RenderGraph 资源：`Runtime/GeometryBuffer/HoGeometryBufferRenderGraphResources.cs`
- 采样函数：`Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl`
- DebugTile 注册：`Runtime/GeometryBuffer/HoGeometryBufferDebugViewInfo.cs`
- Debug shader：`Runtime/GeometryBuffer/Shaders/Debug/HoGeometryBufferDebug.shader`
- lilToon outline 模板：`D:\Unity_Fork\lilToon\Assets\lilToon\CustomShaderResources\URP\Default*Outline.lilblock`
- lilToon UsePass 模板：`D:\Unity_Fork\lilToon\Assets\lilToon\CustomShaderResources\URP\DefaultUsePassOutline*.lilblock`
- coverage fragment：`D:\Unity_Fork\lilToon\Assets\lilToon\Shader\Includes\lil_pass_outline_coverage.hlsl`

## 10. VisualSurfaceBuffer 扩展与 RT 成本

### 10.1 推荐的数据分层

```text
PhysicalGeometryBuffer
  NormalDepth / physical coverage / DepthTexture
  -> AO / GI / SSS / 物理遮挡

VisualSurfaceBuffer
  OutlineCoverage / OutlineDepth / OutlineNormal / OutlineColor / KindFlags
  -> DOF / motion blur / visual occlusion / 后续视觉合成
```

消费者按需求选择：

| 模式 | 深度选择 | 适用消费者 |
| --- | --- | --- |
| `PhysicalOnly` | `NormalDepth.a` | GTAO、SSGI、SSS、物理遮挡 |
| `PhysicalPlusVisualDepth` | 描边 coverage 命中时使用 OutlineDepth，否则使用 NormalDepth | DOF、motion blur、视觉景深 |
| `VisualOcclusionOnly` | VisualSurface 只参与 ray blocking，不参与 radiance/normal/energy | 需要避免屏幕空间射线穿过描边的 GI/AO 变体 |
| `VisualComposite` | 读取 OutlineColor/coverage，在后处理后重新合成 | 描边、特殊视觉壳层、风格化后处理 |

### 10.2 为什么不能直接合并进 NormalDepth

把 OutlineDepth 写入 `NormalDepth.a` 会让所有现有消费者自动看到描边：

- SSGI 可能把描边当 caster、receiver 或 source；
- GTAO 会在描边壳上计算遮蔽；
- SSS/反射/角色特化会把描边误判成真实表面；
- 但这些效果仍然没有对应的物理法线、albedo、厚度和材质语义。

因此“补进主 GBuffer”应理解为建立一个可解析的 visual surface view，而不是改写 PhysicalGeometryBuffer。

### 10.3 RT 数量和带宽取舍

当前资源规模（全分辨率、无 MSAA 乘数）：

| 资源 | 常见格式 | 约每像素字节 | 1920×1080 近似显存 |
| --- | --- | ---: | ---: |
| `NormalDepth` | `R16G16B16A16_SFloat` | 8 | 15.8 MiB |
| `DepthTexture` | D24/D32 | 3-4 | 6.0-7.9 MiB |
| `OutlineCoverage` | `R8_UNorm` | 1 | 2.0 MiB |
| `SkyTexture` | `R16G16B16A16_SFloat` | 8 | 15.8 MiB |
| 未来 `OutlineDepth` | `R16_SFloat` | 2 | 4.0 MiB |

实际成本还会受到 render scale、MSAA、XR slice、RT 对齐和 RenderGraph 生命周期影响。当前 OutlineCoverage 是一张额外 R8 RT，成本相对可控；真正需要关注的是未来继续增加独立 RT 后的带宽和 pass attachment 切换。

### 10.4 推荐的优化路线

1. `OutlineCoverage` 保持独立 R8，作为廉价、易调试的视觉 mask。
2. 需要深度时优先新增一个 `OutlineSurface` 打包 RT，例如 `RG16_SFloat`：
   - `R = outline coverage`
   - `G = outline linear eye depth`
3. 需要法线/颜色时继续扩展同一个 visual surface contract，避免每个效果各自创建一套 RT。
4. 只有存在 Outline、DOF、motion blur 或明确的 visual surface 消费者时，才分配 VisualSurfaceBuffer；纯 AO/GI 场景不应为它付出成本。
5. DebugTile 必须为每个新增 visual channel 提供视图，否则 RT 成本无法在 Frame Debugger 之外被验证。

当前实现处于第 1 阶段：已经有独立 `OutlineCoverageTexture`。它目前随 GeometryBuffer 主资源一起创建，后续可以根据项目中的视觉消费者登记做懒分配。下一步的 `OutlineDepth` 应优先评估与 coverage 打包，而不是继续增加多张独立 attachment。
